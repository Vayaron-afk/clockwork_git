namespace ClockworkGit.Models;

/// <summary>Строка графа коммитов: линии входят сверху и продолжаются вниз.</summary>
public sealed class GraphRow(string hash, int node, string?[] before, string?[] after, int[] down)
{
    public string Hash { get; } = hash;
    public int Node { get; } = node;
    public string?[] Before { get; } = before;
    public string?[] After { get; } = after;
    public int[] Down { get; } = down;
    /// <summary>Показывает, представляет ли строка псевдокоммит рабочей копии.</summary>
    public bool IsWorkingTree => Hash == CommitInfo.WorkingTreeId;
}
