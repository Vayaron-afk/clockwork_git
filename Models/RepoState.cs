namespace ClockworkGit.Models;

/// <summary>An operation git has paused (usually on conflicts) and waits to continue or abort.</summary>
public enum RepoState { Clean, Merging, CherryPicking, Reverting, Rebasing }
