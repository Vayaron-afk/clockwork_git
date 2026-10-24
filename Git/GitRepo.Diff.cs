namespace ClockworkGit.Git;

/// <summary>Загружает текстовые различия и содержимое старой и новой версий изменённого файла.</summary>
public sealed partial class GitRepo
{
    /// <summary>
    /// Unified diff одного файла: коммит сравнивается с первым родителем, а для рабочей копии режим определяется
    /// параметром <paramref name="mode"/>. Значение <paramref name="context"/> &lt; 0 означает весь файл.
    /// </summary>
    public async Task<string> Diff(CommitInfo owner, FileChange f, int context = 3, bool ignoreWhitespace = false,
        WorkTreeDiffMode mode = WorkTreeDiffMode.All)
    {
        var args = new List<string> { "diff", "-M", "--no-ext-diff", "--histogram", context < 0 ? "-U999999" : $"-U{context}" };
        if (ignoreWhitespace) args.Add("--ignore-all-space");
        if (!owner.IsWorkingTree) { args.Add(BaseOf(owner)); args.Add(owner.Hash); }
        else if (mode == WorkTreeDiffMode.All) args.Add("HEAD");
        else if (mode == WorkTreeDiffMode.Staged) args.Add("--cached");
        // Unstaged: plain "git diff" compares the index with the working tree.
        args.Add("--");
        if (f.OldPath != null && !(owner.IsWorkingTree && mode == WorkTreeDiffMode.Unstaged)) args.Add(f.OldPath);
        args.Add(f.RelPath);
        return (await Run(args.ToArray())).Output;
    }

    /// <summary>Исходный diff и строки сравнения бок о бок для файла, в том числе ещё не отслеживаемого Git.</summary>
    public async Task<DiffResult> DiffRows(CommitInfo owner, FileChange f, bool hasHead, bool fullFile = false,
        bool ignoreWhitespace = false, WorkTreeDiffMode mode = WorkTreeDiffMode.All)
    {
        var max = fullFile ? DiffParser.FullFileMaxRows : DiffParser.DefaultMaxRows;
        if (owner.IsWorkingTree && (f.IsUntracked || (!hasHead && f.IndexStatus == 'A')))
            return new DiffResult("", await Task.Run(() => DiffParser.FromNewFile(FullPath(f.RelPath), max)));
        var diff = await Diff(owner, f, fullFile ? -1 : 3, ignoreWhitespace, mode);
        var rows = await Task.Run(() => DiffParser.SideBySide(diff, fullFile, max));
        // Unity YAML/asmdef: name the assets behind GUIDs. The index is only built when a diff mentions one.
        if (diff.Contains("guid:", StringComparison.OrdinalIgnoreCase))
            (await GuidIndex()).Annotate(rows);
        return new DiffResult(diff, rows);
    }

    /// <summary>Байты файла до и после изменения; для рабочей копии сравниваются HEAD и файл на диске независимо от режима текстового diff.</summary>
    /// <remarks>Чтение объектов Git применяет smudge-фильтры; при их ошибке возможен возврат указателя LFS. Отсутствующая или недоступная версия равна null.</remarks>
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

    /// <summary>Читает объект через cat-file --filters с применением smudge-фильтров; при ошибке возвращает необработанный объект через show, при повторной ошибке — null.</summary>
    public async Task<byte[]?> Blob(string rev, string path) =>
        await GitRunner.ReadBytesAsync(Path, "cat-file", "--filters", $"{rev}:{path}")
        ?? await GitRunner.ReadBytesAsync(Path, "show", $"{rev}:{path}");
}
