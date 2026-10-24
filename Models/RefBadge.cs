using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.Models;

/// <summary>Метка ветки или тега, отображаемая рядом с темой коммита.</summary>
public sealed class RefBadge(string name, RefKind kind)
{
    static readonly IBrush HeadBrush = new ImmutableSolidColorBrush(Color.Parse("#3d8a3d"));
    static readonly IBrush LocalBrush = new ImmutableSolidColorBrush(Color.Parse("#2f6b45"));
    static readonly IBrush RemoteBrush = new ImmutableSolidColorBrush(Color.Parse("#3b4f7a"));
    static readonly IBrush TagBrush = new ImmutableSolidColorBrush(Color.Parse("#7a6526"));
    static readonly IBrush RecyclableBrush = new ImmutableSolidColorBrush(Color.Parse("#5c5f66"));

    public string Name { get; } = kind == RefKind.Head ? "▶ " + name : name;
    /// <summary>Тип ссылки, представленной этой меткой.</summary>
    public RefKind Kind { get; } = kind;

    /// <summary>Цвет фона, соответствующий типу ссылки.</summary>
    public IBrush Background => Kind switch
    {
        RefKind.Head => HeadBrush,
        RefKind.Local => LocalBrush,
        RefKind.Remote => RemoteBrush,
        RefKind.Recyclable => RecyclableBrush,
        _ => TagBrush,
    };
}
