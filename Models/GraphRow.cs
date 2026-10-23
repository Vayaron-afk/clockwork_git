namespace ClockworkGit.Models;

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
