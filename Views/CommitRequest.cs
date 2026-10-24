namespace ClockworkGit.Views;

/// <summary>What the commit dialog asked for; <see cref="StageFirst"/> are .meta files to add before committing.</summary>
public sealed record CommitRequest(string Message, bool Amend, bool StageAll, bool Push, IReadOnlyList<string> StageFirst);
