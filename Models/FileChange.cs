using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.Models;

/// <summary>A changed file of a commit or of the working tree (then with index/worktree status).</summary>
public sealed class FileChange : Observable
{
    static readonly IBrush Modified = new ImmutableSolidColorBrush(Color.Parse("#6aa6ff"));
    static readonly IBrush Added = new ImmutableSolidColorBrush(Color.Parse("#5fbf5f"));
    static readonly IBrush Deleted = new ImmutableSolidColorBrush(Color.Parse("#e06060"));
    static readonly IBrush Untracked = new ImmutableSolidColorBrush(Color.Parse("#a0a0a0"));
    static readonly IBrush Conflict = new ImmutableSolidColorBrush(Color.Parse("#ff9e3d"));

    public required string RelPath { get; init; }
    public string? OldPath { get; init; }
    public char Status { get; init; }
    public char IndexStatus { get; init; } = ' ';
    public char WorkStatus { get; init; } = ' ';
    public bool IsWorkingTree { get; init; }
    public bool IsConflict { get; init; }

    bool _isLfs;
    /// <summary>The path falls under a filter=lfs rule (at the commit, or in the working tree).</summary>
    public bool IsLfs { get => _isLfs; set => Set(ref _isLfs, value); }

    public string Name => RelPath[(RelPath.LastIndexOf('/') + 1)..];
    public string Folder => RelPath.LastIndexOf('/') is var i and >= 0 ? RelPath[..i] : "";
    public string StatusLetter => Status.ToString();
    public bool IsUntracked => IndexStatus == '?';
    public bool IsStaged => IndexStatus != ' ' && IndexStatus != '?';

    public string State =>
        !IsWorkingTree ? ""
        : IsConflict ? "Conflict"
        : IsUntracked ? "Untracked"
        : IndexStatus != ' ' && WorkStatus == ' ' ? "Staged"
        : IndexStatus == ' ' ? "Modified"
        : "Partly staged";

    public string StatusText => Status switch
    {
        'M' => "Modified",
        'A' => "Added",
        'D' => "Deleted",
        'R' => "Renamed",
        'C' => "Copied",
        '?' => "Untracked",
        'U' => "Conflict",
        'T' => "Type changed",
        _ => Status.ToString(),
    };

    public IBrush StatusBrush => Status switch
    {
        'A' => Added,
        'D' => Deleted,
        '?' => Untracked,
        'U' => Conflict,
        _ => Modified,
    };
}
