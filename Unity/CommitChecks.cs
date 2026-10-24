namespace ClockworkGit.Unity;

/// <summary>
/// Checks run before a commit: Unity asset/.meta pairs (files and folders under each project's Assets/)
/// and large files that would go into git history instead of LFS.
/// </summary>
public static class CommitChecks
{
    public const long LargeFileBytes = 10L * 1024 * 1024;
    const string ProjectMarker = "ProjectSettings/ProjectVersion.txt";

    /// <param name="stageAll">Whether the commit stages every change (otherwise only staged files are committed).</param>
    public static async Task<List<CommitWarning>> Run(GitRepo repo, IReadOnlyList<FileChange> workFiles, bool stageAll)
    {
        var committing = workFiles.Where(f => !f.IsConflict && (stageAll || f.IsStaged))
            .GroupBy(f => f.RelPath).ToDictionary(g => g.Key, g => g.First());
        var warnings = new List<CommitWarning>();
        if (committing.Count == 0) return warnings;

        var tracked = (await repo.Run("ls-files", "-z")).Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var assetRoots = tracked.Where(p => p.EndsWith(ProjectMarker))
            .Select(p => p[..^ProjectMarker.Length] + "Assets/").ToList();

        bool Adds(FileChange f) => f.IndexStatus == 'A' || (stageAll && f.IsUntracked);
        bool Deletes(FileChange f) => f.IndexStatus == 'D' || (stageAll && f.WorkStatus == 'D');
        bool OnDisk(string path) => File.Exists(repo.FullPath(path)) || Directory.Exists(repo.FullPath(path));
        bool Committed(string path) => committing.ContainsKey(path);
        bool CommittedDeletion(string path) => committing.TryGetValue(path, out var c) && Deletes(c);
        var reportedFolders = new HashSet<string>();

        foreach (var f in committing.Values)
        {
            var root = assetRoots.FirstOrDefault(r => f.RelPath.StartsWith(r, StringComparison.Ordinal));
            if (root == null || IsIgnoredByUnity(f.RelPath[root.Length..])) continue;
            var isMeta = f.RelPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
            var path = f.RelPath;

            if (!isMeta && Adds(f))
            {
                var meta = path + ".meta";
                if (!File.Exists(repo.FullPath(meta)))
                    warnings.Add(new(path, "New asset without .meta: open the project in Unity so it creates one, otherwise its GUID will differ on every machine."));
                else if (!tracked.Contains(meta) && !Committed(meta))
                    warnings.Add(new(path, "Its .meta is on disk but not in the commit.", meta));

                // Every new folder needs its own .meta as well.
                var rel = path[root.Length..];
                for (var slash = rel.IndexOf('/'); slash >= 0; slash = rel.IndexOf('/', slash + 1))
                {
                    var folder = root + rel[..slash];
                    var folderMeta = folder + ".meta";
                    if (tracked.Contains(folderMeta) || Committed(folderMeta) || !reportedFolders.Add(folder)) continue;
                    warnings.Add(File.Exists(repo.FullPath(folderMeta))
                        ? new(folder + "/", "New folder's .meta is on disk but not in the commit.", folderMeta)
                        : new(folder + "/", "New folder without .meta: let Unity import it first."));
                }
            }
            else if (isMeta && Adds(f) && !OnDisk(path[..^5]))
                warnings.Add(new(path, "Orphan .meta: the asset next to it does not exist."));
            else if (!isMeta && Deletes(f) && tracked.Contains(path + ".meta") && !CommittedDeletion(path + ".meta"))
                warnings.Add(new(path, "Asset deleted but its .meta stays in the repository.",
                    File.Exists(repo.FullPath(path + ".meta")) ? null : path + ".meta"));
            else if (isMeta && Deletes(f) && OnDisk(path[..^5]))
                warnings.Add(new(path, ".meta deleted while the asset stays: Unity will assign a new GUID and every reference to it breaks."));
        }

        // Large files outside LFS bloat every clone forever.
        var big = committing.Values
            .Where(f => !Deletes(f) && File.Exists(repo.FullPath(f.RelPath)) && new FileInfo(repo.FullPath(f.RelPath)).Length > LargeFileBytes)
            .ToList();
        if (big.Count > 0)
        {
            var lfs = await repo.LfsAttributed(big.Select(f => f.RelPath).ToList());
            foreach (var f in big.Where(f => !lfs.Contains(f.RelPath)))
                warnings.Add(new(f.RelPath,
                    $"{Format.Size(new FileInfo(repo.FullPath(f.RelPath)).Length)} and not in LFS: it would stay in git history for good. Consider Track in LFS."));
        }
        return warnings;
    }

    /// <summary>Unity skips hidden names, names ending with '~', "cvs" folders and .tmp files.</summary>
    static bool IsIgnoredByUnity(string relativeToAssets) =>
        relativeToAssets.Split('/').Any(part =>
            part.StartsWith('.') || part.EndsWith('~') || part.Equals("cvs", StringComparison.OrdinalIgnoreCase)
            || part.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
}
