namespace ClockworkGit.ViewModels;

/// <summary>Строки, объединённые без конфликтов; длинные последовательности отображаются сокращённо.</summary>
public sealed class CommonSegment(List<string> lines) : MergeSegment
{
    const int Context = 3;

    /// <summary>Неизменённые исходные строки, представленные этим фрагментом.</summary>
    public List<string> Lines { get; } = lines;
    /// <summary>Возвращает неизменённые строки для записи в разрешённый файл.</summary>
    public override IEnumerable<string> ResultLines => Lines;

    /// <summary>Отображает фрагмент целиком, если он короткий, или краткий обзор его начала и конца.</summary>
    public string DisplayText => Lines.Count <= Context * 2 + 1
        ? Show(Lines)
        : Show(Lines.Take(Context)) + $"\n      ⋯ {Lines.Count - Context * 2} unchanged lines ⋯\n" + Show(Lines.TakeLast(Context));
}
