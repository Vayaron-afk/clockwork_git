using Avalonia.Interactivity;

namespace ClockworkGit.Views;

// Running git commands and the toolbar/menu operations: remote, commit, stage, stash, paused operations.
public partial class MainWindow
{
    /// <summary>Runs one git command; shows its output on failure. Use inside <see cref="Do"/>.</summary>
    async Task<bool> Exec(params string[] args)
    {
        var cmd = "git " + string.Join(' ', args);
        SetStatus(cmd + " …");
        var r = await _repo!.Run(args);
        if (!r.Ok)
        {
            SetStatus("Failed: " + cmd);
            await Dialogs.ShowMessage(this, $"git {args[0]} failed", r.Message);
            return false;
        }
        SetStatus(r.LastLine == null ? "Done: " + cmd : $"{cmd}: {r.LastLine}");
        return true;
    }

    /// <summary>Runs a modifying action with the toolbar disabled, then refreshes everything.</summary>
    async Task Do(Func<Task> action)
    {
        if (_busy || _repo == null) return;
        _busy = true;
        Toolbar.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessage(this, "Error", ex.Message);
        }
        finally
        {
            _busy = false;
            Toolbar.IsEnabled = true;
            await RefreshAll();
        }
    }

    // ───────────── Remote ─────────────

    /// <summary>Plain push, or push -u to the first remote when the branch has no upstream yet.</summary>
    async Task PushCore()
    {
        if (_currentBranch == null)
        {
            await Dialogs.ShowMessage(this, "Push", "HEAD is detached; check out a branch first.");
            return;
        }
        var head = _refs.FirstOrDefault(r => r.IsHead);
        if (head is { Upstream.Length: > 0 })
        {
            await Exec("push");
            return;
        }
        var remote = _remotes.FirstOrDefault();
        if (remote == null)
        {
            await Dialogs.ShowMessage(this, "Push", "The repository has no remotes.");
            return;
        }
        await Exec("push", "-u", remote, _currentBranch);
    }

    async void OnPull(object? sender, RoutedEventArgs e) => await Do(() => Exec("pull"));
    async void OnPush(object? sender, RoutedEventArgs e) => await Do(PushCore);
    async void OnFetch(object? sender, RoutedEventArgs e) => await Do(() => Exec("fetch", "--all", "--prune"));

    async void OnSync(object? sender, RoutedEventArgs e) => await Do(async () =>
    {
        if (await Exec("pull")) await PushCore();
    });

    // ───────────── Commit ─────────────

    async void OnCommit(object? sender, RoutedEventArgs e) => await CommitFlow();

    async Task CommitFlow()
    {
        var repo = _repo;
        if (repo == null || _busy) return;
        var staged = _workFiles.Count(f => f.IsStaged);
        var request = await Dialogs.Commit(this, staged, _workFiles.Count, _undoneMessage, () => repo.CommitMessage("HEAD"));
        if (request == null) return;
        await Do(async () =>
        {
            if (request.StageAll && !await Exec("add", "-A")) return;
            var tmp = GitRepo.WriteTempMessage(request.Message);
            try
            {
                var args = new List<string> { "commit", "-F", tmp };
                if (request.Amend) args.Add("--amend");
                if (!await Exec(args.ToArray())) return;
            }
            finally
            {
                File.Delete(tmp);
            }
            _undoneMessage = null;
            if (request.Push) await PushCore();
        });
    }

    async void OnUndoCommit(object? sender, RoutedEventArgs e) => await UndoLastCommit();

    /// <summary>git reset --soft HEAD~1, warning when the commit is already on a remote.</summary>
    async Task UndoLastCommit()
    {
        var repo = _repo;
        if (repo == null || _headHash == null || _busy) return;
        var parents = await repo.Run("rev-list", "--parents", "-n1", "HEAD");
        if (parents.Output.Trim().Split(' ').Length < 2)
        {
            await Dialogs.ShowMessage(this, "Undo Last Commit", "HEAD is the root commit; there is nothing to undo to.");
            return;
        }
        var pushed = await repo.Run("branch", "-r", "--contains", "HEAD");
        if (pushed.Output.Trim().Length > 0
            && !await Dialogs.Confirm(this, "Undo Last Commit",
                $"The last commit is already on: {string.Join(", ", pushed.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))}.\n" +
                "Undoing it locally will require a force push to update the remote. Continue?", "Undo"))
            return;
        var message = await repo.CommitMessage("HEAD");
        await Do(async () =>
        {
            if (await Exec("reset", "--soft", "HEAD~1")) _undoneMessage = message;
        });
    }

    // ───────────── Stage / unstage / discard ─────────────

    List<FileChange> SelectedWorkFiles() =>
        _filesOwner?.IsWorkingTree == true ? FilesList.SelectedItems?.OfType<FileChange>().ToList() ?? new() : new();

    async void OnStage(object? sender, RoutedEventArgs e) => await Stage(SelectedWorkFiles());
    async void OnUnstage(object? sender, RoutedEventArgs e) => await Unstage(SelectedWorkFiles());
    async void OnDiscard(object? sender, RoutedEventArgs e) => await Discard(SelectedWorkFiles());

    /// <summary>Stages the given files, or everything when none are given.</summary>
    Task Stage(List<FileChange> files) =>
        Do(() => files.Count == 0
            ? Exec("add", "-A")
            : Exec(["add", "-A", "--", .. files.Select(f => f.RelPath)]));

    /// <summary>Unstages the given files, or everything when none are given.</summary>
    Task Unstage(List<FileChange> files) =>
        Do(() => _headHash == null
            ? Exec(["rm", "-r", "-q", "--cached", "--", .. files.Count == 0 ? ["."] : files.Select(f => f.RelPath)])
            : files.Count == 0
                ? Exec("reset", "-q")
                : Exec(["restore", "--staged", "--", .. files.Select(f => f.RelPath)]));

    async Task Discard(List<FileChange> files)
    {
        if (files.Count == 0)
        {
            await Dialogs.ShowMessage(this, "Discard", "Select the working tree files to discard.");
            return;
        }
        var newFiles = files.Where(f => f.IsUntracked || f.IndexStatus == 'A').ToList();
        var tracked = files.Except(newFiles).ToList();
        var text = $"Discard changes in {files.Count} file(s)? This cannot be undone.";
        if (newFiles.Count > 0) text += $"\n\n{newFiles.Count} new file(s) will be deleted from disk:\n" + string.Join("\n", newFiles.Take(15).Select(f => "  " + f.RelPath));
        if (!await Dialogs.Confirm(this, "Discard", text, "Discard")) return;

        await Do(async () =>
        {
            if (tracked.Count > 0
                && !await Exec(["restore", "--source=HEAD", "--staged", "--worktree", "--", .. tracked.Select(f => f.RelPath)]))
                return;
            var added = newFiles.Where(f => !f.IsUntracked).ToList();
            if (added.Count > 0)
                await _repo!.Run(["rm", "-q", "--cached", "-f", "--", .. added.Select(f => f.RelPath)]);
            foreach (var f in newFiles)
            {
                var full = _repo!.FullPath(f.RelPath);
                if (File.Exists(full)) File.Delete(full);
            }
        });
    }

    // ───────────── Stash ─────────────

    async void OnSaveStash(object? sender, RoutedEventArgs e)
    {
        if (_repo == null || _busy) return;
        if (_workFiles.Count(f => !f.IsUntracked) == 0)
        {
            await Dialogs.ShowMessage(this, "Save Stash", "No tracked changes to stash.");
            return;
        }
        var message = await Dialogs.Prompt(this, "Save Stash", "Stash message (optional):");
        if (message == null) return;
        await Do(() => message.Trim().Length == 0 ? Exec("stash", "push") : Exec("stash", "push", "-m", message.Trim()));
    }

    async void OnApplyStash(object? sender, RoutedEventArgs e)
    {
        if (_repo == null || _busy) return;
        var stashes = await _repo.Stashes();
        if (stashes.Count == 0)
        {
            await Dialogs.ShowMessage(this, "Apply Stash", "There are no stashes.");
            return;
        }
        await Do(() => Exec("stash", "apply", stashes[0].Ref));
    }

    // ───────────── Paused merge / cherry-pick / revert / rebase ─────────────

    async void OnStateContinue(object? sender, RoutedEventArgs e)
    {
        var state = _repo?.GetState() ?? RepoState.Clean;
        await Do(() => state switch
        {
            RepoState.Merging => Exec("commit", "--no-edit"),
            RepoState.CherryPicking => Exec("cherry-pick", "--continue"),
            RepoState.Reverting => Exec("revert", "--continue"),
            RepoState.Rebasing => Exec("rebase", "--continue"),
            _ => Task.FromResult(true),
        });
    }

    async void OnStateAbort(object? sender, RoutedEventArgs e)
    {
        var op = (_repo?.GetState() ?? RepoState.Clean) switch
        {
            RepoState.Merging => "merge",
            RepoState.CherryPicking => "cherry-pick",
            RepoState.Reverting => "revert",
            RepoState.Rebasing => "rebase",
            _ => null,
        };
        if (op == null) return;
        if (await Dialogs.Confirm(this, "Abort", $"Abort the {op}? Changes made by it will be thrown away.", "Abort"))
            await Do(() => Exec(op, "--abort"));
    }
}
