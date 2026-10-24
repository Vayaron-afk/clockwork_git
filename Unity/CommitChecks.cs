namespace ClockworkGit.Unity;

/// <summary>
/// Проверки перед созданием коммита: соответствие ресурсов Unity файлам .meta (файлы и папки в Assets/ каждого проекта),
/// известные чужие блокировки LFS и крупные файлы вне LFS. Используются статусы и содержимое рабочей копии,
/// поэтому результат является предупреждением, а не полной проверкой будущего дерева коммита.
/// </summary>
public static class CommitChecks
{
    public const long LargeFileBytes = 10L * 1024 * 1024;
    const string ProjectMarker = "ProjectSettings/ProjectVersion.txt";

    /// <summary>Проверяет будущий коммит на проблемы с .meta Unity, файлы, заблокированные другим пользователем LFS, и крупные файлы вне LFS.</summary>
    /// <param name="repo">Репозиторий для чтения отслеживаемых путей, атрибутов и файлов на диске.</param>
    /// <param name="workFiles">Снимок статусов изменённых файлов.</param>
    /// <param name="stageAll">Проверять все неконфликтные изменения; при false — только файлы с изменениями в индексе. Сам метод индекс не меняет.</param>
    /// <param name="locks">Последние известные блокировки по путям; null отключает предупреждения о блокировках. Возможность push зависит от проверки блокировок.</param>
    /// <returns>Предупреждения с необязательными путями .meta, которые можно дополнительно добавить в индекс.</returns>
    /// <remarks>Размеры и наличие файлов проверяются на диске, атрибуты LFS — в рабочей копии; частично добавленное содержимое индекса отдельно не читается.</remarks>
    public static async Task<List<CommitWarning>> Run(GitRepo repo, IReadOnlyList<FileChange> workFiles, bool stageAll,
        IReadOnlyDictionary<string, LfsLock>? locks = null)
    {
        var committing = workFiles.Where(f => !f.IsConflict && (stageAll || f.IsStaged))
            .GroupBy(f => f.RelPath).ToDictionary(g => g.Key, g => g.First());
        var warnings = new List<CommitWarning>();
        if (committing.Count == 0) return warnings;

        if (locks != null)
            foreach (var path in committing.Keys)
                if (locks.TryGetValue(path, out var l) && !l.IsMine)
                    warnings.Add(new(path, $"Locked in LFS by {l.Owner} {l.SinceText}: the server will reject pushing changes to it."));

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

        // Проверяем размер рабочей версии, даже если в индекс добавлена только часть изменений.
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

    /// <summary>Определяет, игнорирует ли Unity путь внутри Assets: скрытые имена, завершающий «~», каталоги cvs и файлы .tmp.</summary>
    static bool IsIgnoredByUnity(string relativeToAssets) =>
        relativeToAssets.Split('/').Any(part =>
            part.StartsWith('.') || part.EndsWith('~') || part.Equals("cvs", StringComparison.OrdinalIgnoreCase)
            || part.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
}
