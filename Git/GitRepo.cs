using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClockworkGit.Git;

public sealed class GitRepo
{
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    GitRepo(string path, string gitDir)
    {
        Path = path;
        GitDir = gitDir;
    }

    public string Path { get; }
    public string GitDir { get; }

    public static async Task<GitRepo?> Open(string path)
    {
        var r = await GitRunner.RunAsync(path, "rev-parse", "--absolute-git-dir");
        return r.Ok ? new GitRepo(path, r.Output.Trim()) : null;
    }

    public Task<GitResult> Run(params string[] args) => GitRunner.RunAsync(Path, args);

    public async Task<string?> CurrentBranch()
    {
        var r = await Run("symbolic-ref", "--short", "-q", "HEAD");
        return r.Ok ? r.Output.Trim() : null;
    }

    public async Task<string?> HeadHash()
    {
        var r = await Run("rev-parse", "-q", "--verify", "HEAD");
        return r.Ok ? r.Output.Trim() : null;
    }

    public async Task<List<string>> Remotes()
    {
        var r = await Run("remote");
        return r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    public async Task<List<CommitInfo>> Log(IEnumerable<string> refs, int limit, IReadOnlyCollection<string> remotes)
    {
        var args = new List<string>
        {
            "-c", "log.showSignature=false", "log", "--date-order", "-z", $"-n{limit}",
            "--format=%H%x1f%P%x1f%an%x1f%ae%x1f%at%x1f%D%x1f%s",
        };
        args.AddRange(refs);
        args.Add("--");
        var r = await Run(args.ToArray());
        var list = new List<CommitInfo>();
        if (!r.Ok) return list;

        foreach (var record in r.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = record.Split('\x1f');
            if (p.Length < 7) continue;
            list.Add(new CommitInfo
            {
                Hash = p[0].Trim(),
                Parents = p[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                Author = p[2],
                Email = p[3],
                Date = DateTimeOffset.FromUnixTimeSeconds(long.TryParse(p[4], out var t) ? t : 0),
                Refs = ParseDecorations(p[5], remotes),
                Subject = p[6],
            });
        }
        return list;
    }

    static List<RefBadge> ParseDecorations(string decorations, IReadOnlyCollection<string> remotes)
    {
        var result = new List<RefBadge>();
        foreach (var raw in decorations.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith("HEAD -> ")) result.Add(new RefBadge(raw[8..], RefKind.Head));
            else if (raw == "HEAD") result.Add(new RefBadge("HEAD", RefKind.Head));
            else if (raw.StartsWith("tag: ")) result.Add(new RefBadge(raw[5..], RefKind.Tag));
            else if (raw.EndsWith("/HEAD")) continue;
            else if (remotes.Any(rm => raw.StartsWith(rm + "/"))) result.Add(new RefBadge(raw, RefKind.Remote));
            else result.Add(new RefBadge(raw, RefKind.Local));
        }
        return result;
    }

    public async Task<List<RefInfo>> Refs()
    {
        var r = await Run("for-each-ref",
            "--format=%(refname)%1f%(objectname:short)%1f%(upstream)%1f%(upstream:short)%1f%(upstream:track)%1f%(HEAD)%1f%(upstream:remotename)",
            "refs/heads", "refs/remotes", "refs/tags");
        var list = new List<RefInfo>();
        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.TrimEnd('\r').Split('\x1f');
            if (p.Length < 7 || p[0].EndsWith("/HEAD")) continue;
            list.Add(new RefInfo(p[0], p[1], p[2], p[3], p[4], p[5] == "*", p[6]));
        }
        return list;
    }

    public async Task<List<StashInfo>> Stashes()
    {
        var r = await Run("stash", "list", "--format=%gd%x1f%s");
        return r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r').Split('\x1f'))
            .Where(p => p.Length == 2)
            .Select(p => new StashInfo(p[0], p[1]))
            .ToList();
    }

    public async Task<List<FileChange>> Status()
    {
        var r = await Run("status", "--porcelain=v1", "-z", "-uall");
        var list = new List<FileChange>();
        var t = r.Output.Split('\0');
        for (var i = 0; i < t.Length; i++)
        {
            var e = t[i];
            if (e.Length < 4) continue;
            char x = e[0], y = e[1];
            if (x == '!') continue;
            string? old = null;
            if ((x is 'R' or 'C' || y is 'R' or 'C') && i + 1 < t.Length) old = t[++i];
            var conflict = x == 'U' || y == 'U' || (x == 'A' && y == 'A') || (x == 'D' && y == 'D');
            list.Add(new FileChange
            {
                RelPath = e[3..],
                OldPath = old,
                IndexStatus = x,
                WorkStatus = y,
                IsConflict = conflict,
                IsWorkingTree = true,
                Status = conflict ? 'U' : x == '?' ? '?' : x != ' ' ? x : y,
            });
        }
        return list.OrderBy(f => f.RelPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string BaseOf(CommitInfo c) => c.Parents.Length > 0 ? c.Parents[0] : EmptyTree;

    public async Task<List<FileChange>> CommitFiles(CommitInfo c)
    {
        var r = await Run("diff", "--name-status", "-z", "-M", "--no-ext-diff", BaseOf(c), c.Hash);
        var list = new List<FileChange>();
        var t = r.Output.Split('\0');
        for (var i = 0; i < t.Length;)
        {
            var st = t[i];
            if (st.Length == 0) { i++; continue; }
            var s = st[0];
            if (s is 'R' or 'C')
            {
                if (i + 2 >= t.Length) break;
                list.Add(new FileChange { Status = s, OldPath = t[i + 1], RelPath = t[i + 2] });
                i += 3;
            }
            else
            {
                if (i + 1 >= t.Length) break;
                list.Add(new FileChange { Status = s, RelPath = t[i + 1] });
                i += 2;
            }
        }
        return list;
    }

    public async Task<string> CommitMessage(string rev)
    {
        var r = await Run("-c", "log.showSignature=false", "log", "-1", "--format=%B", rev);
        return r.Output.TrimEnd();
    }

    /// <summary>
    /// Unified diff of one file: commit against its first parent, or HEAD against the working tree.
    /// <paramref name="context"/> &lt; 0 means the whole file.
    /// </summary>
    public async Task<string> Diff(CommitInfo owner, FileChange f, int context = 3, bool ignoreWhitespace = false)
    {
        var args = new List<string> { "diff", "-M", "--no-ext-diff", "--histogram", context < 0 ? "-U999999" : $"-U{context}" };
        if (ignoreWhitespace) args.Add("--ignore-all-space");
        if (owner.IsWorkingTree) args.Add("HEAD");
        else { args.Add(BaseOf(owner)); args.Add(owner.Hash); }
        args.Add("--");
        if (f.OldPath != null) args.Add(f.OldPath);
        args.Add(f.RelPath);
        return (await Run(args.ToArray())).Output;
    }

    /// <summary>Side-by-side rows for one file, including files git does not track yet.</summary>
    public async Task<List<DiffRow>> DiffRows(CommitInfo owner, FileChange f, bool hasHead, bool fullFile = false, bool ignoreWhitespace = false)
    {
        var max = fullFile ? DiffParser.FullFileMaxRows : DiffParser.DefaultMaxRows;
        if (owner.IsWorkingTree && (f.IsUntracked || (!hasHead && f.IndexStatus == 'A')))
            return await Task.Run(() => DiffParser.FromNewFile(FullPath(f.RelPath), max));
        var diff = await Diff(owner, f, fullFile ? -1 : 3, ignoreWhitespace);
        return await Task.Run(() => DiffParser.SideBySide(diff, fullFile, max));
    }

    /// <summary>Raw bytes of the file before and after the change (LFS objects resolved); null where absent.</summary>
    public async Task<(byte[]? Old, byte[]? New)> LoadVersions(CommitInfo owner, FileChange f, bool hasHead)
    {
        byte[]? oldBytes = null, newBytes = null;
        if (owner.IsWorkingTree)
        {
            if (!f.IsUntracked && f.IndexStatus != 'A' && hasHead)
                oldBytes = await Blob("HEAD", f.OldPath ?? f.RelPath);
            var full = FullPath(f.RelPath);
            if (File.Exists(full)) newBytes = await File.ReadAllBytesAsync(full);
        }
        else
        {
            if (f.Status != 'A' && owner.Parents.Length > 0)
                oldBytes = await Blob(owner.Parents[0], f.OldPath ?? f.RelPath);
            if (f.Status != 'D')
                newBytes = await Blob(owner.Hash, f.RelPath);
        }
        return (oldBytes, newBytes);
    }

    /// <summary>File content at a revision, with LFS/smudge filters applied when possible.</summary>
    public async Task<byte[]?> Blob(string rev, string path) =>
        await GitRunner.ReadBytesAsync(Path, "cat-file", "--filters", $"{rev}:{path}")
        ?? await GitRunner.ReadBytesAsync(Path, "show", $"{rev}:{path}");

    public Task<GitResult> RunWithInput(string input, params string[] args) => GitRunner.RunWithInputAsync(Path, input, args);

    // ───────────── LFS ─────────────

    /// <summary>Paths whose filter attribute is "lfs", in the working tree or at <paramref name="source"/>.</summary>
    public async Task<HashSet<string>> LfsAttributed(IReadOnlyCollection<string> paths, string? source = null)
    {
        var set = new HashSet<string>();
        if (paths.Count == 0) return set;
        var args = new List<string> { "check-attr", "--stdin", "-z" };
        if (source != null) { args.Add("--source"); args.Add(source); }
        args.Add("filter");
        var r = await RunWithInput(string.Join('\0', paths) + '\0', args.ToArray());
        var t = r.Output.Split('\0');
        for (var i = 0; i + 2 < t.Length; i += 3)
            if (t[i + 2] == "lfs") set.Add(t[i]);
        return set;
    }

    public async Task<List<LfsFile>> LfsFiles(string rev = "HEAD")
    {
        var list = new List<LfsFile>();
        var r = await Run("lfs", "ls-files", "--json", rev);
        if (!r.Ok || r.Output.Length == 0) return list;
        using var doc = JsonDocument.Parse(r.Output);
        if (!doc.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array) return list;
        foreach (var f in files.EnumerateArray())
        {
            list.Add(new LfsFile(
                f.GetProperty("name").GetString() ?? "",
                f.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                f.TryGetProperty("downloaded", out var d) && d.GetBoolean(),
                f.TryGetProperty("oid", out var o) ? o.GetString() ?? "" : ""));
        }
        return list;
    }

    /// <summary>filter=lfs rules from the root .gitattributes.</summary>
    public List<LfsRule> LfsRules()
    {
        var file = System.IO.Path.Combine(Path, ".gitattributes");
        var rules = new List<LfsRule>();
        if (!File.Exists(file)) return rules;
        var lines = File.ReadAllLines(file);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Skip(1).Contains("filter=lfs")) rules.Add(new LfsRule(tokens[0], i + 1));
        }
        return rules;
    }

    /// <summary>gitattributes-style match: patterns without '/' match the file name anywhere.</summary>
    public static bool AttrPatternMatches(string pattern, string relPath)
    {
        var p = pattern.TrimStart('/');
        var target = p.Contains('/') ? relPath : relPath[(relPath.LastIndexOf('/') + 1)..];
        var rx = "^" + Regex.Escape(p)
            .Replace(@"\*\*", "\u0001").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]").Replace("\u0001", ".*") + "$";
        return Regex.IsMatch(target, rx);
    }

    public long LfsCacheBytes()
    {
        var dir = System.IO.Path.Combine(GitDir, "lfs", "objects");
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
            : 0;
    }

    /// <summary>Tracked files that match an LFS rule but are still stored as regular git blobs at HEAD.</summary>
    public async Task<List<string>> NotYetInLfs()
    {
        var all = (await Run("ls-files", "-z")).Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var attributed = await LfsAttributed(all);
        var inLfs = (await LfsFiles()).Select(f => f.Path).ToHashSet();
        return all.Where(p => attributed.Contains(p) && !inLfs.Contains(p)).ToList();
    }

    public RepoState GetState()
    {
        if (File.Exists(System.IO.Path.Combine(GitDir, "MERGE_HEAD"))) return RepoState.Merging;
        if (File.Exists(System.IO.Path.Combine(GitDir, "CHERRY_PICK_HEAD"))) return RepoState.CherryPicking;
        if (File.Exists(System.IO.Path.Combine(GitDir, "REVERT_HEAD"))) return RepoState.Reverting;
        if (Directory.Exists(System.IO.Path.Combine(GitDir, "rebase-merge"))
            || Directory.Exists(System.IO.Path.Combine(GitDir, "rebase-apply"))) return RepoState.Rebasing;
        return RepoState.Clean;
    }

    public string FullPath(string relPath) =>
        System.IO.Path.Combine(Path, relPath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public static string WriteTempMessage(string message)
    {
        var tmp = System.IO.Path.GetTempFileName();
        File.WriteAllText(tmp, message, new UTF8Encoding(false));
        return tmp;
    }
}
