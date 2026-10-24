namespace ClockworkGit.Git;

/// <summary>Код завершения, стандартный вывод и поток ошибок одного процесса Git.</summary>
public sealed record GitResult(int ExitCode, string Output, string Error)
{
    /// <summary>Показывает, завершился ли Git успешно.</summary>
    public bool Ok => ExitCode == 0;

    /// <summary>Потоки stderr и stdout вместе для диалогов ошибок (Git выводит ход выполнения и подсказки в stderr).</summary>
    public string Message => (Error + "\n" + Output).Trim();

    /// <summary>Последняя непустая строка объединённого вывода для строки состояния или null, если вывода нет.</summary>
    public string? LastLine =>
        (Error + "\n" + Output).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
}
