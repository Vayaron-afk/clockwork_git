using System.Collections.ObjectModel;
using Avalonia.Media;
using ClockworkGit.Git;

namespace ClockworkGit.UI;

public sealed class RepoItem(string path) : Observable
{
    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));

    string? _branch;
    public string? Branch
    {
        get => _branch;
        set { if (Set(ref _branch, value)) Raise(nameof(Display)); }
    }

    public string Display => Branch == null ? Name : $"{Name} ({Branch})";
}

public enum NodeKind { Group, Head, Local, Remote, Tag, Stash }

public sealed class BranchNode : Observable
{
    public required string Key { get; init; }
    public required NodeKind Kind { get; init; }
    public string Title { get; set; } = "";
    public string Detail { get; init; } = "";
    /// <summary>Full ref passed to git log (refs/heads/x, HEAD…); null for groups and stashes.</summary>
    public string? FullRef { get; init; }
    public string? ShortName { get; init; }
    public string? Remote { get; init; }
    public RefInfo? Ref { get; init; }
    public bool IsCurrent { get; init; }
    public ObservableCollection<BranchNode> Children { get; } = new();

    public bool CanCheck => FullRef != null;
    public FontWeight Weight => IsCurrent || Kind == NodeKind.Group && Key.IndexOf('/') < 0 ? FontWeight.SemiBold : FontWeight.Normal;

    bool _isChecked;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    public void InitState(bool isChecked, bool isExpanded)
    {
        _isChecked = isChecked;
        _isExpanded = isExpanded;
    }
}
