namespace ClockworkGit.Models;

/// <summary>С чем сравнивается файл рабочей копии.</summary>
public enum WorkTreeDiffMode
{
    /// <summary>HEAD ↔ рабочая копия: все незакоммиченные изменения.</summary>
    All,
    /// <summary>Индекс ↔ рабочая копия: изменения, которые попадут в индекс при Stage.</summary>
    Unstaged,
    /// <summary>HEAD ↔ индекс: изменения, которые войдут в коммит.</summary>
    Staged,
}
