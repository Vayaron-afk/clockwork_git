namespace ClockworkGit.Models;

/// <summary>Коммит, изменивший файл, и путь со статусом файла в этом коммите; при переименовании путь меняется.</summary>
public sealed record FileHistoryEntry(CommitInfo Commit, FileChange File)
{
    /// <summary>Подпись «переименован из…» или «скопирован из…», если Git обнаружил перенос; иначе пустая строка.</summary>
    public string RenameNote =>
        File.OldPath == null ? "" : $"{(File.Status == 'C' ? "copied" : "renamed")} from {File.OldPath}";
    /// <summary>Показывает, сообщил ли Git старый путь файла для этого изменения.</summary>
    public bool IsRename => File.OldPath != null;
}
