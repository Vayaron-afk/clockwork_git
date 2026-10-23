using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.Models;

/// <summary>A commit row of the graph; the first row is the working tree pseudo-commit.</summary>
public sealed class CommitInfo : Observable
{
    public const string WorkingTreeId = "WORKTREE";

    static readonly IBrush[] AvatarBrushes = new[]
    {
        "#c0392b", "#d35400", "#b7950b", "#27ae60", "#16a085", "#2980b9", "#8e44ad", "#c2185b", "#5d6d7e", "#00838f",
    }.Select(c => (IBrush)new ImmutableSolidColorBrush(Color.Parse(c))).ToArray();

    public required string Hash { get; init; }
    public string[] Parents { get; init; } = [];
    public string Author { get; init; } = "";
    public string Email { get; init; } = "";
    public DateTimeOffset Date { get; init; }
    public string Subject { get; init; } = "";
    public List<RefBadge> Refs { get; init; } = [];

    public GraphRow? Graph { get; set; }

    GraphRow? _shownGraph;
    /// <summary>The graph row while the list is unfiltered; null hides lanes for filtered results.</summary>
    public GraphRow? ShownGraph { get => _shownGraph; set => Set(ref _shownGraph, value); }

    public bool IsWorkingTree => Hash == WorkingTreeId;
    public bool HasRefs => Refs.Count > 0;
    public bool HasAvatar => !IsWorkingTree;
    public FontWeight SubjectWeight => IsWorkingTree ? FontWeight.Bold : FontWeight.Normal;
    public string ShortHash => IsWorkingTree ? "" : Hash[..Math.Min(8, Hash.Length)];
    public string DateText => IsWorkingTree ? "" : Format.RelativeDate(Date);

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

    /// <summary>Stable colour per author e-mail.</summary>
    public IBrush AvatarBrush
    {
        get
        {
            var h = 0;
            foreach (var ch in Email.ToLowerInvariant()) h = h * 31 + ch;
            return AvatarBrushes[(h & 0x7fffffff) % AvatarBrushes.Length];
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
}
