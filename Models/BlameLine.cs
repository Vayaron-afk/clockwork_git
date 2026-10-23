using Avalonia.Media;

namespace ClockworkGit.Models;

/// <summary>One line of git blame: who last changed it, and where that commit's version of the file came from.</summary>
public sealed class BlameLine
{
    public required string Sha { get; init; }
    public int LineNo { get; init; }
    public string Text { get; init; } = "";
    public string Author { get; init; } = "";
    public DateTimeOffset Date { get; init; }
    public string Summary { get; init; } = "";
    /// <summary>The file's path in <see cref="Sha"/>.</summary>
    public string Path { get; init; } = "";
    /// <summary>Parent commit and path before this change; null when the line was added in a root commit.</summary>
    public string? PreviousSha { get; init; }
    public string? PreviousPath { get; init; }

    /// <summary>First line of a run of lines from the same commit; only it shows the commit header.</summary>
    public bool IsBlockStart { get; set; }
    /// <summary>Heat colour: newer changes are warmer.</summary>
    public IBrush AgeBrush { get; set; } = Brushes.Transparent;

    public bool IsUncommitted => Sha.All(c => c == '0');
    public string Header => !IsBlockStart ? "" : IsUncommitted ? "Not committed yet" : $"{Sha[..8]}  {Author}";
    public string DateText => IsBlockStart && !IsUncommitted ? Format.RelativeDate(Date) : "";
    public string Tip => IsUncommitted ? "Local change, not committed" : $"{Sha[..10]} · {Author} · {Date.LocalDateTime:yyyy-MM-dd HH:mm}\n{Summary}";
}
