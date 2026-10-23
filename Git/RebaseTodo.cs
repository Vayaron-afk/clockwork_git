using System.Text;

namespace ClockworkGit.Git;

/// <summary>
/// Turns a rebase plan into a git-rebase-todo that runs without an editor:
/// reword and squash are expressed as <c>fixup</c> plus <c>exec git commit --amend -F file</c>,
/// so the final message of each group is exactly what the user typed.
/// </summary>
public static class RebaseTodo
{
    /// <summary>Validates the plan; null when it is fine, otherwise the reason it cannot run.</summary>
    public static string? Validate(IReadOnlyList<RebaseStep> oldestFirst)
    {
        var kept = oldestFirst.Where(s => s.Action != RebaseAction.Drop).ToList();
        if (kept.Count == 0) return "Every commit is dropped; use Reset instead.";
        if (kept[0].Action is RebaseAction.Squash or RebaseAction.Fixup)
            return "The oldest kept commit cannot be squashed or fixed up: there is no older commit in the rebase to meld it into.";
        return null;
    }

    /// <summary>True when the plan would recreate the same history.</summary>
    public static bool IsNoOp(IReadOnlyList<RebaseStep> oldestFirst, IReadOnlyList<string> originalOrderOldestFirst) =>
        oldestFirst.All(s => s.Action == RebaseAction.Pick && s.Message == s.OriginalMessage)
        && oldestFirst.Select(s => s.Hash).SequenceEqual(originalOrderOldestFirst);

    /// <summary>Builds the todo text; message files for reworded/squashed groups are written to <paramref name="workDir"/>.</summary>
    public static string Build(IReadOnlyList<RebaseStep> oldestFirst, string workDir)
    {
        if (Validate(oldestFirst) is { } error) throw new InvalidOperationException(error);
        Directory.CreateDirectory(workDir);

        var todo = new StringBuilder();
        var messages = new List<string>();
        var needsMessage = false;
        var fileNo = 0;

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
