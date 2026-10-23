namespace ClockworkGit.Models;

/// <summary>A filter=lfs pattern and its 1-based line in the root .gitattributes.</summary>
public sealed record LfsRule(string Pattern, int Line);
