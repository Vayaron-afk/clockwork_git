namespace ClockworkGit.Models;

/// <summary>Header row of the Files list when it is grouped by project; clicking it selects the group's files.</summary>
public sealed class FileGroup
{
    /// <summary>Project folder relative to the repository; empty for files at the repository root.</summary>
    public required string Root { get; init; }
    public required List<FileChange> Files { get; init; }

    public string Name => Root.Length == 0 ? "(repository root)" : Root[(Root.LastIndexOf('/') + 1)..];
    /// <summary>Full folder when the name alone does not show it.</summary>
    public string Location => Root.Contains('/') ? Root : "";
    public string CountText => Files.Count == 1 ? "1 file" : $"{Files.Count} files";
    public string Tip => $"{CountText} in {(Root.Length == 0 ? "the repository root" : Root)} — click to select them all";
}
