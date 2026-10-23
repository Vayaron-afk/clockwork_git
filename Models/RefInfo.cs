namespace ClockworkGit.Models;

/// <summary>A ref from git for-each-ref; <see cref="Track"/> is e.g. "[ahead 2, behind 1]" or "[gone]".</summary>
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

    public bool IsGone => Track == "[gone]";

    /// <summary>"↑2 ↓1" for ahead/behind, empty when in sync.</summary>
    public string TrackShort =>
        IsGone ? "" : Track.Trim('[', ']').Replace("ahead ", "↑").Replace("behind ", "↓").Replace(",", "");
}
