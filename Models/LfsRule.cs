namespace ClockworkGit.Models;

/// <summary>Шаблон filter=lfs и номер его строки (нумерация с единицы) в корневом .gitattributes.</summary>
public sealed record LfsRule(string Pattern, int Line);
