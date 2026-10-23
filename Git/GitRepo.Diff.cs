namespace ClockworkGit.Git;

// Text diffs and raw file versions of a change.
public sealed partial class GitRepo
{
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
}
