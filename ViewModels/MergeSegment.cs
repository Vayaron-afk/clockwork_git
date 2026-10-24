namespace ClockworkGit.ViewModels;

/// <summary>A piece of the conflict editor: unchanged text or a conflict to resolve.</summary>
public abstract class MergeSegment : Observable
{
    /// <summary>Lines this segment contributes to the saved file.</summary>
    public abstract IEnumerable<string> ResultLines { get; }

    protected static string Show(IEnumerable<string> lines) => string.Join("\n", lines.Select(l => l.TrimEnd('\r').Replace("\t", "    ")));
}
