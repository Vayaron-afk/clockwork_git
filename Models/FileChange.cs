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

    static readonly IBrush MyLock = new ImmutableSolidColorBrush(Color.Parse("#2f6b45"));
    static readonly IBrush TheirLock = new ImmutableSolidColorBrush(Color.Parse("#8a3030"));

    LfsLock? _lock;
    /// <summary>LFS server lock on this path, if any.</summary>
    public LfsLock? Lock
    {
        get => _lock;
        set
        {
            if (!Set(ref _lock, value)) return;
            Raise(nameof(HasLock));
            Raise(nameof(LockText));
            Raise(nameof(LockBrush));
            Raise(nameof(LockTip));
        }
    }

    public bool HasLock => Lock != null;
    public string LockText => Lock == null ? "" : "🔒 " + Lock.OwnerText;
    public IBrush LockBrush => Lock?.IsMine == true ? MyLock : TheirLock;
    public string? LockTip => Lock == null ? null : $"Locked by {Lock.OwnerText} {Lock.SinceText} (LFS lock #{Lock.Id})";

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
