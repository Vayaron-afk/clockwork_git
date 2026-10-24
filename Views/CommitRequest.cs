namespace ClockworkGit.Views;

/// <summary>Параметры диалога коммита, включая добавление в индекс файлов для исправления перед коммитом.</summary>
public sealed record CommitRequest(string Message, bool Amend, bool StageAll, bool Push, IReadOnlyList<string> StageFirst);
