using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.Models;

/// <summary>One aligned line of a side-by-side diff; an empty line number means "no line on this side".</summary>
public sealed class DiffRow
{
    public static readonly IBrush ContextBg = Brushes.Transparent;
    public static readonly IBrush RemovedBg = new ImmutableSolidColorBrush(Color.Parse("#4b2226"));
    public static readonly IBrush AddedBg = new ImmutableSolidColorBrush(Color.Parse("#1f4526"));
    public static readonly IBrush EmptyBg = new ImmutableSolidColorBrush(Color.Parse("#26272a"));
    public static readonly IBrush HunkBg = new ImmutableSolidColorBrush(Color.Parse("#2b3442"));
    /// <summary>Background of the changed part inside a changed line.</summary>
    public static readonly IBrush RemovedHi = new ImmutableSolidColorBrush(Color.Parse("#8c3640"));
    public static readonly IBrush AddedHi = new ImmutableSolidColorBrush(Color.Parse("#2f7d3f"));

    public DiffRowKind Kind { get; init; }
    public string LeftNo { get; init; } = "";
    public string LeftText { get; init; } = "";
    public IBrush LeftBg { get; init; } = ContextBg;
    public string RightNo { get; init; } = "";
    public string RightText { get; init; } = "";
    public IBrush RightBg { get; init; } = ContextBg;
    public int LeftHiStart { get; init; }
    public int LeftHiLength { get; init; }
    public int RightHiStart { get; init; }
    public int RightHiLength { get; init; }

    public bool HasLeft => LeftNo.Length > 0;
    public bool HasRight => RightNo.Length > 0;

    public static DiffRow Message(string text) =>
        new() { Kind = DiffRowKind.Message, LeftText = text, LeftBg = HunkBg, RightBg = HunkBg };
}
