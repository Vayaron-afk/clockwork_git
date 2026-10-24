namespace ClockworkGit.Models;

/// <summary>Предупреждение перед коммитом; необязательный <see cref="FixPath"/> предлагает путь для добавления в индекс.</summary>
public sealed record CommitWarning(string Path, string Message, string? FixPath = null)
{
    /// <summary>Показывает, можно ли устранить предупреждение добавлением <see cref="FixPath"/> в индекс.</summary>
    public bool IsFixable => FixPath != null;
}
