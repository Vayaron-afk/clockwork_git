using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.Models;

/// <summary>A branch/tag label shown next to a commit subject.</summary>
public sealed class RefBadge(string name, RefKind kind)
{
    static readonly IBrush HeadBrush = new ImmutableSolidColorBrush(Color.Parse("#3d8a3d"));
    static readonly IBrush LocalBrush = new ImmutableSolidColorBrush(Color.Parse("#2f6b45"));
    static readonly IBrush RemoteBrush = new ImmutableSolidColorBrush(Color.Parse("#3b4f7a"));
    static readonly IBrush TagBrush = new ImmutableSolidColorBrush(Color.Parse("#7a6526"));

    public string Name { get; } = kind == RefKind.Head ? "▶ " + name : name;
    public RefKind Kind { get; } = kind;

    public IBrush Background => Kind switch
    {
        RefKind.Head => HeadBrush,
        RefKind.Local => LocalBrush,
        RefKind.Remote => RemoteBrush,
        _ => TagBrush,
    };
}
