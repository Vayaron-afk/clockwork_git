namespace ClockworkGit.Models;

/// <summary>What interactive rebase does with a commit.</summary>
public enum RebaseAction
{
    Pick,
    /// <summary>Keep the changes, use an edited message.</summary>
    Reword,
    /// <summary>Meld into the older commit, keeping both messages.</summary>
    Squash,
    /// <summary>Meld into the older commit, discarding this message.</summary>
    Fixup,
    Drop,
}
