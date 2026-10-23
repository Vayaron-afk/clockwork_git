namespace ClockworkGit.ViewModels;

/// <summary>A row of the LFS window's Rules tab.</summary>
public sealed record LfsRuleRow(string Pattern, int Line, string FilesText);
