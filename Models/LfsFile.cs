namespace ClockworkGit.Models;

/// <summary>Запись из git lfs ls-files; <see cref="Downloaded"/> показывает, находится ли объект в локальном кэше.</summary>
public sealed record LfsFile(string Path, long Size, bool Downloaded, string Oid);
