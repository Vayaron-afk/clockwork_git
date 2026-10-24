namespace ClockworkGit.ViewModels;

/// <summary>Строка вкладки «Правила» окна LFS.</summary>
public sealed record LfsRuleRow(string Pattern, int Line, string FilesText);
