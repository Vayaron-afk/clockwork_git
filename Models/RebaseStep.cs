namespace ClockworkGit.Models;

/// <summary>One line of an interactive rebase plan.</summary>
public sealed record RebaseStep(string Hash, RebaseAction Action, string Message, string OriginalMessage);
