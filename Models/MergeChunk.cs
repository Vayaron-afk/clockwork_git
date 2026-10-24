namespace ClockworkGit.Models;

/// <summary>
/// A piece of a three-way merge: either lines that merged cleanly (<see cref="Common"/>), or a conflict
/// with each side's lines. Lines keep a trailing '\r' when the file has one.
/// </summary>
public sealed record MergeChunk(List<string> Common, List<string>? Ours = null, List<string>? Base = null, List<string>? Theirs = null)
{
    public bool IsConflict => Ours != null;
}
