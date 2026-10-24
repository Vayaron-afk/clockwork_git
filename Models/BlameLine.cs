using Avalonia.Media;

namespace ClockworkGit.Models;

/// <summary>Строка из git blame: автор последнего изменения и путь файла в том коммите.</summary>
public sealed class BlameLine
{
    public required string Sha { get; init; }
    public int LineNo { get; init; }
    public string Text { get; init; } = "";
    public string Author { get; init; } = "";
    public DateTimeOffset Date { get; init; }
    public string Summary { get; init; } = "";
    /// <summary>Путь файла в коммите <see cref="Sha"/>.</summary>
    public string Path { get; init; } = "";
    /// <summary>Хеш из поля previous вывода blame для перехода до изменения; null, если Git не сообщил предшествующую версию.</summary>
    public string? PreviousSha { get; init; }
    /// <summary>Путь из поля previous, относящийся к <see cref="PreviousSha"/>; null, если путь не сообщён.</summary>
    public string? PreviousPath { get; init; }

    /// <summary>Первая из последовательных строк одного коммита; только для неё показывается заголовок коммита.</summary>
    public bool IsBlockStart { get; set; }
    /// <summary>Цветовая шкала давности: более свежие изменения окрашены в тёплые тона.</summary>
    public IBrush AgeBrush { get; set; } = Brushes.Transparent;

    /// <summary>Показывает, что эта строка ещё не закоммичена.</summary>
    public bool IsUncommitted => Sha.All(c => c == '0');
    /// <summary>Подпись с коммитом и автором для первой строки блока blame.</summary>
    public string Header => !IsBlockStart ? "" : IsUncommitted ? "Not committed yet" : $"{Sha[..8]}  {Author}";
    /// <summary>Относительная дата коммита для первой строки блока blame.</summary>
    public string DateText => IsBlockStart && !IsUncommitted ? Format.RelativeDate(Date) : "";
    /// <summary>Подсказка с коммитом, автором, датой и сообщением либо с отметкой о незакоммиченном изменении.</summary>
    public string Tip => IsUncommitted ? "Local change, not committed" : $"{Sha[..10]} · {Author} · {Date.LocalDateTime:yyyy-MM-dd HH:mm}\n{Summary}";
}
