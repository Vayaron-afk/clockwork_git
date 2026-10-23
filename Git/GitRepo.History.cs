namespace ClockworkGit.Git;

// Branches, refs, log, status and commit contents.
public sealed partial class GitRepo
{
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

    /// <summary>What a commit is compared against: its first parent, or the empty tree for a root commit.</summary>
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
}
