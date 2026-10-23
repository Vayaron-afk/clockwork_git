using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.Git;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum RefKind { Head, Local, Remote, Tag }

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

/// <summary>One row of the commit graph: lanes entering from above, lanes leaving below.</summary>
public sealed class GraphRow(string hash, int node, string?[] before, string?[] after, int[] down)
{
    public string Hash { get; } = hash;
    public int Node { get; } = node;
    public string?[] Before { get; } = before;
    public string?[] After { get; } = after;
    public int[] Down { get; } = down;
    public bool IsWorkingTree => Hash == CommitInfo.WorkingTreeId;
}

public sealed class CommitInfo : Observable
{
    public const string WorkingTreeId = "WORKTREE";

    static readonly string[] AvatarColors =
        ["#c0392b", "#d35400", "#b7950b", "#27ae60", "#16a085", "#2980b9", "#8e44ad", "#c2185b", "#5d6d7e", "#00838f"];

    public required string Hash { get; init; }
    public string[] Parents { get; init; } = [];
    public string Author { get; init; } = "";
    public string Email { get; init; } = "";
    public DateTimeOffset Date { get; init; }
    public string Subject { get; init; } = "";
    public List<RefBadge> Refs { get; init; } = [];

    public GraphRow? Graph { get; set; }

    GraphRow? _shownGraph;
    public GraphRow? ShownGraph { get => _shownGraph; set => Set(ref _shownGraph, value); }

    public bool IsWorkingTree => Hash == WorkingTreeId;
    public bool HasRefs => Refs.Count > 0;
    public bool HasAvatar => !IsWorkingTree;
    public FontWeight SubjectWeight => IsWorkingTree ? FontWeight.Bold : FontWeight.Normal;
    public string ShortHash => IsWorkingTree ? "" : Hash[..Math.Min(8, Hash.Length)];
    public string DateText => IsWorkingTree ? "" : RelativeDate(Date);

    public string Initials
    {
        get
        {
            var words = Author.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length switch
            {
                0 => "?",
                1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
                _ => $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}",
            };
        }
    }

    public IBrush AvatarBrush
    {
        get
        {
            var h = 0;
            foreach (var ch in Email.ToLowerInvariant()) h = h * 31 + ch;
            return new ImmutableSolidColorBrush(Color.Parse(AvatarColors[(h & 0x7fffffff) % AvatarColors.Length]));
        }
    }

    public bool Matches(string filter) =>
        Subject.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Author.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Hash.StartsWith(filter, StringComparison.OrdinalIgnoreCase)
        || Refs.Any(r => r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

    public static CommitInfo WorkingTree(string? parent, int changes) => new()
    {
        Hash = WorkingTreeId,
        Parents = parent == null ? [] : [parent],
        Subject = changes == 0 ? "Working Tree/Index (clean)" : $"Working Tree/Index ({changes} changes)",
    };

    public static string RelativeDate(DateTimeOffset date)
    {
        var local = date.LocalDateTime;
        var today = DateTime.Today;
        if (local.Date == today) return local.ToString("HH:mm");
        if (local.Date == today.AddDays(-1)) return "Yesterday " + local.ToString("HH:mm");
        if ((today - local.Date).TotalDays < 7) return local.ToString("dddd HH:mm", CultureInfo.InvariantCulture);
        return local.ToString("dd.MM.yyyy HH:mm");
    }
}

public sealed record RefInfo(
    string FullName, string Hash, string Upstream, string UpstreamShort, string Track, bool IsHead, string UpstreamRemote)
{
    public RefKind Kind =>
        FullName.StartsWith("refs/heads/") ? RefKind.Local
        : FullName.StartsWith("refs/remotes/") ? RefKind.Remote
        : RefKind.Tag;

    public string ShortName =>
        FullName.StartsWith("refs/heads/") ? FullName["refs/heads/".Length..]
        : FullName.StartsWith("refs/remotes/") ? FullName["refs/remotes/".Length..]
        : FullName.StartsWith("refs/tags/") ? FullName["refs/tags/".Length..]
        : FullName;
}

public sealed record StashInfo(string Ref, string Message);

public enum RepoState { Clean, Merging, CherryPicking, Reverting, Rebasing }

public sealed class FileChange
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
