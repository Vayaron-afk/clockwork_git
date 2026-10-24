namespace ClockworkGit.ViewModels;

/// <summary>Фрагмент редактора конфликтов: неизменённый текст или конфликт, требующий разрешения.</summary>
public abstract class MergeSegment : Observable
{
    /// <summary>Строки, которые этот фрагмент добавляет в сохраняемый файл.</summary>
    public abstract IEnumerable<string> ResultLines { get; }

    /// <summary>Форматирует строки версий для отображения: удаляет завершающий CR и раскрывает табуляцию.</summary>
    protected static string Show(IEnumerable<string> lines) => string.Join("\n", lines.Select(l => l.TrimEnd('\r').Replace("\t", "    ")));
}
