namespace ClockworkGit.Models;

/// <summary>Обнаруженная по метаданным операция Git. Clean означает отсутствие распознанной операции, а не отсутствие изменений файлов.</summary>
public enum RepoState { Clean, Merging, CherryPicking, Reverting, Rebasing }
