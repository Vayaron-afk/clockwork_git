namespace ClockworkGit.ViewModels;

/// <summary>Lines that merged cleanly; long runs are shown shortened.</summary>
public sealed class CommonSegment(List<string> lines) : MergeSegment
{
    const int Context = 3;

    public List<string> Lines { get; } = lines;
    public override IEnumerable<string> ResultLines => Lines;

    public string DisplayText => Lines.Count <= Context * 2 + 1
        ? Show(Lines)
        : Show(Lines.Take(Context)) + $"\n      ⋯ {Lines.Count - Context * 2} unchanged lines ⋯\n" + Show(Lines.TakeLast(Context));
}
