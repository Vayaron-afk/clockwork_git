namespace ClockworkGit.Models;

/// <summary>What a working-tree file is compared against.</summary>
public enum WorkTreeDiffMode
{
    /// <summary>HEAD ↔ working tree: everything not committed.</summary>
    All,
    /// <summary>Index ↔ working tree: what Stage would add.</summary>
    Unstaged,
    /// <summary>HEAD ↔ index: what Commit would record.</summary>
    Staged,
}
