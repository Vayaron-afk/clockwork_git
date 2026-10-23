namespace ClockworkGit.Models;

/// <summary>A commit that changed a file, with the file's path and status in that commit (paths change on renames).</summary>
public sealed record FileHistoryEntry(CommitInfo Commit, FileChange File)
{
    /// <summary>"renamed from …" / "copied from …" when git followed the file across a move, otherwise empty.</summary>
    public string RenameNote =>
        File.OldPath == null ? "" : $"{(File.Status == 'C' ? "copied" : "renamed")} from {File.OldPath}";
    public bool IsRename => File.OldPath != null;
}
