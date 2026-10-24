namespace ClockworkGit.Models;

/// <summary>Один шаг плана интерактивного rebase.</summary>
public sealed record RebaseStep(string Hash, RebaseAction Action, string Message, string OriginalMessage);
