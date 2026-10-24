using Avalonia.Interactivity;

namespace ClockworkGit.Views;

// Running git commands and the toolbar/menu operations: remote, commit, stage, stash, paused operations.
public partial class MainWindow
{
    /// <summary>Запускает одну команду Git и показывает её вывод при ошибке. Используйте внутри <see cref="Do"/>.</summary>
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

    /// <summary>Не допускает повторного входа при _busy, отключает тулбар, показывает исключения действия и затем обновляет данные.</summary>
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

    /// <summary>Обычный push или push -u в первый удалённый репозиторий, если у ветки ещё нет upstream.</summary>
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
            SetStatus("git push …");
            var r = await _repo!.Run("push");
            if (r.Ok)
            {
                SetStatus(r.LastLine == null ? "Done: git push" : $"git push: {r.LastLine}");
                return;
            }
            // After a rebase or amend of pushed commits the remote rejects a plain push.
            var rejected = r.Error.Contains("[rejected]") || r.Error.Contains("non-fast-forward") || r.Error.Contains("fetch first");
            if (rejected && await Dialogs.Confirm(this, "Push rejected",
                    $"The remote branch {head.UpstreamShort} has commits that '{_currentBranch}' does not (usually after a rebase or amend).\n\n" +
                    "Force push with lease? It overwrites the remote branch, but refuses if someone else pushed there " +
                    "since your last fetch.\n\n" + r.Message, "Force push"))
                await Exec("push", "--force-with-lease");
            else if (!rejected)
                await Dialogs.ShowMessage(this, "git push failed", r.Message);
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

    /// <summary>Обрабатывает Pull и запускает <c>git pull</c> через общий обработчик операций.</summary>
    async void OnPull(object? sender, RoutedEventArgs e) => await Do(() => Exec("pull"));
    /// <summary>Обрабатывает Push и отправляет текущую ветку, при необходимости задавая upstream.</summary>
    async void OnPush(object? sender, RoutedEventArgs e) => await Do(PushCore);
    /// <summary>Получает данные со всех удалённых репозиториев и удаляет ссылки на удалённые ветки, которых больше нет.</summary>
    async void OnFetch(object? sender, RoutedEventArgs e) => await Do(() => Exec("fetch", "--all", "--prune"));

    /// <summary>Сначала выполняет pull и запускает push только в случае успеха.</summary>
    async void OnSync(object? sender, RoutedEventArgs e) => await Do(async () =>
    {
        if (await Exec("pull")) await PushCore();
    });

    // ───────────── Commit ─────────────

    /// <summary>Запускает создание коммита с панели инструментов или из меню.</summary>
    async void OnCommit(object? sender, RoutedEventArgs e) => await CommitFlow();

    /// <summary>Проверяет коммит, собирает его параметры, добавляет запрошенные файлы в индекс, создаёт коммит и при необходимости отправляет его.</summary>
    async Task CommitFlow()
    {
        var repo = _repo;
        if (repo == null || _busy) return;
        var staged = _workFiles.Count(f => f.IsStaged);
        SetStatus("Checking the commit…");
        var checkAll = CommitChecks.Run(repo, _workFiles, stageAll: true, _locks);
        var checkStaged = CommitChecks.Run(repo, _workFiles, stageAll: false, _locks);
        await Task.WhenAll(checkAll, checkStaged);
        SetStatus("");
        var request = await Dialogs.Commit(this, staged, _workFiles.Count, _undoneMessage, () => repo.CommitMessage("HEAD"),
            all => all ? checkAll.Result : checkStaged.Result);
        if (request == null) return;
        await Do(async () =>
        {
            if (request.StageAll && !await Exec("add", "-A")) return;
            if (request.StageFirst.Count > 0 && !await Exec(["add", "-A", "--", .. request.StageFirst])) return;
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

    /// <summary>Обрабатывает команду отмены последнего коммита и передаёт выполнение защищённому сценарию soft reset.</summary>
    async void OnUndoCommit(object? sender, RoutedEventArgs e) => await UndoLastCommit();

    /// <summary>Выполняет git reset --soft HEAD~1 и предупреждает, если коммит уже находится в удалённом репозитории.</summary>
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

    /// <summary>Возвращает выбранные файлы, только если панель файлов отображает рабочую копию.</summary>
    List<FileChange> SelectedWorkFiles() =>
        _filesOwner?.IsWorkingTree == true ? FilesList.SelectedItems?.OfType<FileChange>().ToList() ?? new() : new();

    /// <summary>Добавляет выбранные файлы рабочей копии в индекс.</summary>
    async void OnStage(object? sender, RoutedEventArgs e) => await Stage(SelectedWorkFiles());
    /// <summary>Удаляет выбранные файлы рабочей копии из индекса.</summary>
    async void OnUnstage(object? sender, RoutedEventArgs e) => await Unstage(SelectedWorkFiles());
    /// <summary>Запрашивает подтверждение и отменяет изменения выбранных файлов рабочей копии.</summary>
    async void OnDiscard(object? sender, RoutedEventArgs e) => await Discard(SelectedWorkFiles());

    /// <summary>Добавляет указанные файлы в индекс; пустой список означает git add -A для всего репозитория.</summary>
    Task Stage(List<FileChange> files) =>
        Do(() => files.Count == 0
            ? Exec("add", "-A")
            : Exec(["add", "-A", "--", .. files.Select(f => f.RelPath)]));

    /// <summary>Снимает изменения указанных файлов с индекса, сохраняя рабочую копию; пустой список означает все файлы.</summary>
    Task Unstage(List<FileChange> files) =>
        Do(() => _headHash == null
            ? Exec(["rm", "-r", "-q", "--cached", "--", .. files.Count == 0 ? ["."] : files.Select(f => f.RelPath)])
            : files.Count == 0
                ? Exec("reset", "-q")
                : Exec(["restore", "--staged", "--", .. files.Select(f => f.RelPath)]));

    /// <summary>Запрашивает подтверждение перед восстановлением отслеживаемых файлов и удалением выбранных неотслеживаемых файлов.</summary>
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

    /// <summary>Запрашивает необязательное сообщение и сохраняет отслеживаемые изменения в stash.</summary>
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

    /// <summary>Применяет самый новый stash, если существует хотя бы один.</summary>
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

    /// <summary>Продолжает текущую операцию merge, cherry-pick, revert или rebase.</summary>
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

    /// <summary>Запрашивает подтверждение и прерывает текущую операцию merge, cherry-pick, revert или rebase.</summary>
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
