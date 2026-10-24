using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.ViewModels;

/// <summary>One conflict: both sides, their common ancestor and the chosen or typed resolution.</summary>
public sealed class ConflictSegment(int number, List<string> ours, List<string> bas, List<string> theirs,
    string oursLabel, string theirsLabel) : MergeSegment
{
    static readonly IBrush Open = new ImmutableSolidColorBrush(Color.Parse("#e0a040"));
    static readonly IBrush Done = new ImmutableSolidColorBrush(Color.Parse("#5fbf5f"));

    public int Number { get; } = number;
    public List<string> Ours { get; } = ours;
    public List<string> Base { get; } = bas;
    public List<string> Theirs { get; } = theirs;
    public string OursLabel { get; } = oursLabel;
    public string TheirsLabel { get; } = theirsLabel;

    public string Title => $"Conflict {Number}";
    public string OursText => Show(Ours);
    public string BaseText => Base.Count == 0 ? "(nothing)" : Show(Base);
    public string TheirsText => Show(Theirs);

    string? _resolution;
    /// <summary>The merged text; null while unresolved. Typing in the result box resolves the conflict.</summary>
    public string? Resolution
    {
        get => _resolution;
        set
        {
            if (!Set(ref _resolution, value)) return;
            Raise(nameof(IsResolved));
            Raise(nameof(Status));
            Raise(nameof(StatusBrush));
        }
    }

    public bool IsResolved => Resolution != null;
    public string Status => IsResolved ? "resolved" : "unresolved";
    public IBrush StatusBrush => IsResolved ? Done : Open;

    public void Take(string choice) => Resolution = choice switch
    {
        "ours" => Show(Ours),
        "theirs" => Show(Theirs),
        "ours+theirs" => Show(Ours.Concat(Theirs)),
        "theirs+ours" => Show(Theirs.Concat(Ours)),
        "base" => Show(Base),
        _ => "",
    };

    /// <summary>The resolution, or the conflict with diff3 markers when it is still open.</summary>
    public override IEnumerable<string> ResultLines =>
        Resolution != null
            ? Resolution.Length == 0 ? [] : Resolution.Replace("\r\n", "\n").Split('\n')
            : new[] { "<<<<<<< ours" }.Concat(Ours).Append("||||||| base").Concat(Base)
                .Append("=======").Concat(Theirs).Append(">>>>>>> theirs");
}
