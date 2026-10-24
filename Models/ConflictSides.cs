namespace ClockworkGit.Models;

/// <summary>The three index stages of a conflicted file: common ancestor, ours, theirs (null when a side is missing).</summary>
public sealed record ConflictSides(byte[]? Base, byte[]? Ours, byte[]? Theirs)
{
    /// <summary>A side contains NUL bytes: resolve by picking a whole side instead of merging lines.</summary>
    public bool IsBinary => new[] { Base, Ours, Theirs }.Any(b => b != null && b.AsSpan(0, Math.Min(b.Length, 8000)).Contains((byte)0));

    /// <summary>Modify/delete and add/add conflicts lack a side; they can only be resolved by choosing one.</summary>
    public bool HasBothSides => Ours != null && Theirs != null;
}
