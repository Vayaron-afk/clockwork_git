namespace ClockworkGit.Models;

/// <summary>
/// A commit no ref points to any more but the reflog still keeps (after reset, amend, rebase or a deleted branch).
/// <see cref="Reason"/> is the reflog action that left it behind, e.g. "reset: moving to HEAD~1".
/// </summary>
public sealed record RecyclableCommit(CommitInfo Commit, string Reason);
