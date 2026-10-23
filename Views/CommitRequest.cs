namespace ClockworkGit.Views;

/// <summary>What the commit dialog asked for.</summary>
public sealed record CommitRequest(string Message, bool Amend, bool StageAll, bool Push);
