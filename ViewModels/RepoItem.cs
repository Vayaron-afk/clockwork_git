namespace ClockworkGit.ViewModels;

/// <summary>A repository in the Repositories list.</summary>
public sealed class RepoItem(string path) : Observable
{
    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));

    string? _branch;
    public string? Branch
    {
        get => _branch;
        set { if (Set(ref _branch, value)) Raise(nameof(Display)); }
    }

    public string Display => Branch == null ? Name : $"{Name} ({Branch})";
}
