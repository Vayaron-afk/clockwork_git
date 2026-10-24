using System.Text;

namespace ClockworkGit.Git;

/// <summary>
/// Преобразует план rebase в файл git-rebase-todo, который выполняется без редактора:
/// reword выполняется через <c>pick</c>, squash и fixup — через <c>fixup</c>.
/// Изменённое сообщение группы задаётся последующим <c>exec git commit --amend --cleanup=strip -F file</c>;
/// сообщения обрезаются по краям и объединяются пустой строкой, после чего Git применяет cleanup=strip.
/// </summary>
public static class RebaseTodo
{
    /// <summary>Проверяет, что остаётся хотя бы один коммит и первый сохраняемый шаг не Squash/Fixup; возвращает причину нарушения или null.</summary>
    public static string? Validate(IReadOnlyList<RebaseStep> oldestFirst)
    {
        var kept = oldestFirst.Where(s => s.Action != RebaseAction.Drop).ToList();
        if (kept.Count == 0) return "Every commit is dropped; use Reset instead.";
        if (kept[0].Action is RebaseAction.Squash or RebaseAction.Fixup)
            return "The oldest kept commit cannot be squashed or fixed up: there is no older commit in the rebase to meld it into.";
        return null;
    }

    /// <summary>Возвращает true, если все шаги — Pick с исходными сообщениями, а порядок хешей не изменился.</summary>
    public static bool IsNoOp(IReadOnlyList<RebaseStep> oldestFirst, IReadOnlyList<string> originalOrderOldestFirst) =>
        oldestFirst.All(s => s.Action == RebaseAction.Pick && s.Message == s.OriginalMessage)
        && oldestFirst.Select(s => s.Hash).SequenceEqual(originalOrderOldestFirst);

    /// <summary>Формирует текст todo; файлы сообщений для групп reword/squash записываются в <paramref name="workDir"/>.</summary>
    public static string Build(IReadOnlyList<RebaseStep> oldestFirst, string workDir)
    {
        if (Validate(oldestFirst) is { } error) throw new InvalidOperationException(error);
        Directory.CreateDirectory(workDir);

        var todo = new StringBuilder();
        var messages = new List<string>();
        var needsMessage = false;
        var fileNo = 0;

        // Emits the amended message for the current squash/reword group, if it needs one.
        void FlushGroup()
        {
            if (needsMessage)
            {
                var file = Path.Combine(workDir, $"message-{++fileNo}.txt");
                File.WriteAllText(file, string.Join("\n\n", messages.Select(m => m.Trim())) + "\n", new UTF8Encoding(false));
                // exec runs through sh: forward slashes and single quotes keep Windows paths intact.
                todo.Append("exec git commit --amend --allow-empty --cleanup=strip -F '")
                    .Append(file.Replace('\\', '/').Replace("'", "'\\''")).Append("'\n");
            }
            messages.Clear();
            needsMessage = false;
        }

        foreach (var step in oldestFirst)
        {
            switch (step.Action)
            {
                case RebaseAction.Drop:
                    todo.Append($"drop {step.Hash}\n");
                    break;
                case RebaseAction.Pick or RebaseAction.Reword:
                    FlushGroup();
                    todo.Append($"pick {step.Hash}\n");
                    messages.Add(step.Message);
                    needsMessage = step.Action == RebaseAction.Reword || step.Message != step.OriginalMessage;
                    break;
                case RebaseAction.Squash:
                    todo.Append($"fixup {step.Hash}\n");
                    messages.Add(step.Message);
                    needsMessage = true;
                    break;
                case RebaseAction.Fixup:
                    todo.Append($"fixup {step.Hash}\n");
                    break;
            }
        }
        FlushGroup();
        return todo.ToString();
    }
}
