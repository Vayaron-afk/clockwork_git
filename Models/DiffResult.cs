namespace ClockworkGit.Models;

/// <summary>A file diff: git's raw unified diff (empty for untracked files) and its side-by-side rows.</summary>
public sealed record DiffResult(string Raw, List<DiffRow> Rows);
