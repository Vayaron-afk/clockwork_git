namespace ClockworkGit.Models;

/// <summary>Заголовок группы в списке файлов, сгруппированных по проектам; щелчок выбирает все файлы группы.</summary>
public sealed class FileGroup
{
    /// <summary>Путь к папке проекта относительно репозитория; пустая строка для файлов в корне репозитория.</summary>
    public required string Root { get; init; }
    public required List<FileChange> Files { get; init; }

    /// <summary>Последний сегмент пути проекта или подпись для файлов в корне репозитория.</summary>
    public string Name => Root.Length == 0 ? "(repository root)" : Root[(Root.LastIndexOf('/') + 1)..];
    /// <summary>Полный путь к папке, если одного имени недостаточно.</summary>
    public string Location => Root.Contains('/') ? Root : "";
    /// <summary>Подпись с количеством файлов в заголовке группы.</summary>
    public string CountText => Files.Count == 1 ? "1 file" : $"{Files.Count} files";
    /// <summary>Подсказка о том, что выбор заголовка выделяет все файлы группы.</summary>
    public string Tip => $"{CountText} in {(Root.Length == 0 ? "the repository root" : Root)} — click to select them all";
}
