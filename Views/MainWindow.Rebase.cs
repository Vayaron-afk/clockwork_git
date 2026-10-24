using System.Text;

namespace ClockworkGit.Views;

// Validates and runs an interactive rebase of the current branch from a chosen commit.
public partial class MainWindow
{
    const int RebaseLimit = 500;

    /// <summary>Формирует и выполняет план интерактивного rebase, начиная с выбранного коммита.</summary>
    async Task InteractiveRebaseFrom(CommitInfo from)
    {
        var repo = _repo;
        if (repo == null || _busy) return;
        if (_currentBranch == null)
        {
            await Dialogs.ShowMessage(this, "Interactive rebase", "Check out a branch first: interactive rebase rewrites the current branch.");
            return;
        }
        if (!(await repo.Run("merge-base", "--is-ancestor", from.Hash, "HEAD")).Ok)
        {
            await Dialogs.ShowMessage(this, "Interactive rebase", $"{from.ShortHash} is not part of '{_currentBranch}'.");
            return;
        }

        var baseHash = from.Parents.FirstOrDefault();
        var range = baseHash == null ? "HEAD" : $"{baseHash}..HEAD";
        if ((await repo.Run("rev-list", "--merges", range)).Output.Trim().Length > 0)
        {
            await Dialogs.ShowMessage(this, "Interactive rebase",
                "The commits after this point include merge commits. Rebasing merges is not supported here.");
            return;
        }
        var commits = await repo.Log([range], RebaseLimit + 1, _remotes);
        if (commits.Count == 0) return;
        if (commits.Count > RebaseLimit)
        {
            await Dialogs.ShowMessage(this, "Interactive rebase", $"More than {RebaseLimit} commits; pick a newer starting commit.");
            return;
        }

        var messages = await Task.WhenAll(commits.Select(c => repo.CommitMessage(c.Hash)));
        var items = commits.Select((c, i) => new RebaseItem { Commit = c, OriginalMessage = messages[i], Message = messages[i] }).ToList();
        var pushedTo = (await repo.Run("branch", "-r", "--contains", from.Hash)).Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var warning = pushedTo.Length == 0 ? null
            : $"These commits are already on {string.Join(", ", pushedTo)}. Rewriting them needs a force push afterwards, " +
              "and anyone who based work on them will have to rebase too.";
        var header = $"Rebase {commits.Count} commit(s) of '{_currentBranch}' onto " +
                     (baseHash == null ? "the root (the first commit is included)" : $"{baseHash[..8]}");

        var steps = await new InteractiveRebaseWindow(items, header, warning).ShowDialog<List<RebaseStep>?>(this);
        if (steps == null) return;

        await Do(async () =>
        {
            // Kept until the rebase finishes: exec lines read the message files, also after a conflict stop.
            var work = Path.Combine(Path.GetTempPath(), "ClockworkGit", "rebase-" + Guid.NewGuid().ToString("N")[..8]);
            var todoPath = Path.Combine(work, "git-rebase-todo");
            var todo = RebaseTodo.Build(steps, work);
            await File.WriteAllTextAsync(todoPath, todo, new UTF8Encoding(false));

            var env = new Dictionary<string, string> { ["GIT_SEQUENCE_EDITOR"] = $"cp '{todoPath.Replace('\\', '/')}'" };
            string[] args = baseHash == null
                ? ["rebase", "-i", "--autostash", "--root"]
                : ["rebase", "-i", "--autostash", baseHash];
            SetStatus($"git rebase -i {(baseHash == null ? "--root" : baseHash[..8])} …");
            var r = await repo.RunWithEnv(env, args);
            if (r.Ok) SetStatus($"Rebased {steps.Count} commit(s) of '{_currentBranch}'");
            else
                await Dialogs.ShowMessage(this, "Rebase stopped", r.Message +
                    "\n\nResolve the conflicts, stage the files and press Continue in the banner, or Abort to return to the original state.");
        });
    }
}
