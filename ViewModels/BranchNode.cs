using System.Collections.ObjectModel;
using Avalonia.Media;

namespace ClockworkGit.ViewModels;

/// <summary>A node of the Branches tree: a group/folder, HEAD, a branch, a tag or a stash.</summary>
public sealed class BranchNode : Observable
{
    /// <summary>Stable id across refreshes, used to remember expanded nodes.</summary>
    public required string Key { get; init; }
    public required NodeKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    /// <summary>Full ref passed to git log (refs/heads/x, HEAD…); null for groups and stashes.</summary>
    public string? FullRef { get; init; }
    /// <summary>Name for git commands: "main", "origin/main", "v1.0", "stash@{0}".</summary>
    public string? ShortName { get; init; }
    public string? Remote { get; init; }
    public bool IsCurrent { get; init; }
    public ObservableCollection<BranchNode> Children { get; } = new();

    /// <summary>Only refs can be toggled into the graph.</summary>
    public bool CanCheck => FullRef != null;
    public FontWeight Weight => IsCurrent || Kind == NodeKind.Group && Key.IndexOf('/') < 0 ? FontWeight.SemiBold : FontWeight.Normal;

    bool _isChecked;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    /// <summary>Sets the initial state without raising change notifications.</summary>
    public void InitState(bool isChecked, bool isExpanded)
    {
        _isChecked = isChecked;
        _isExpanded = isExpanded;
    }
}
