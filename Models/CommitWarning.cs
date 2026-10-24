namespace ClockworkGit.Models;

/// <summary>A problem found in what is about to be committed; <see cref="FixPath"/> is a file staging would fix.</summary>
public sealed record CommitWarning(string Path, string Message, string? FixPath = null)
{
    public bool IsFixable => FixPath != null;
}
