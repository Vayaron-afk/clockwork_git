using Avalonia.Controls;
using static ClockworkGit.Utils.MenuFactory;

namespace ClockworkGit.Views;

// Context menus of the graph and of the Files list, with the history-rewriting commit actions.
public partial class MainWindow
{
    void OnGraphContext(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is CommitInfo clicked && GraphList.SelectedItems?.Contains(clicked) != true)
            GraphList.SelectedItem = clicked;
        var selected = SelectedCommits();
        if (selected.Count == 0 || _repo == null) return;

        var menu = new ContextMenu();
        if (selected.Count == 1 && selected[0].IsWorkingTree)
        {
            menu.Items.Add(Item("Commit…", CommitFlow));
            menu.Items.Add(Item("Stage all", () => Stage(new())));
            menu.Items.Add(Item("Unstage all", () => Unstage(new())));
            menu.Items.Add(Item("Discard all tracked changes…", async () =>
            {
                if (await Dialogs.Confirm(this, "Discard all", "Reset all tracked files to HEAD (git reset --hard)? Untracked files stay. This cannot be undone.", "Discard"))
                    await Do(() => Exec("reset", "--hard", "-q"));
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Undo Last Commit", UndoLastCommit));
            menu.Open(GraphList);
            e.Handled = true;
            return;
        }

        var commits = selected.Where(c => !c.IsWorkingTree).ToList();
        if (commits.Count == 0) return;
        var cur = _currentBranch ?? "HEAD";

        if (commits.Count == 1)
        {
            var c = commits[0];
            menu.Items.Add(Item("Checkout commit (detached)…", async () =>
            {
                if (await Dialogs.Confirm(this, "Checkout", $"Check out {c.ShortHash} as detached HEAD?", "Checkout"))
                    await Do(() => Exec("switch", "--detach", c.Hash));
            }));
            menu.Items.Add(Item("Create branch here…", async () =>
            {
                var name = await Dialogs.Prompt(this, "Create branch", $"Branch name (created at {c.ShortHash} and checked out):");
                if (!string.IsNullOrWhiteSpace(name)) await Do(() => Exec("switch", "-c", name.Trim(), c.Hash));
            }));
            menu.Items.Add(Item("Create tag here…", async () =>
            {
                var name = await Dialogs.Prompt(this, "Create tag", $"Tag name for {c.ShortHash}:");
                if (!string.IsNullOrWhiteSpace(name)) await Do(() => Exec("tag", name.Trim(), c.Hash));
            }));
            menu.Items.Add(new Separator());
        }

        var count = commits.Count == 1 ? "" : $" {commits.Count} commits";
        menu.Items.Add(Item($"Cherry-pick{count} to '{cur}'", () => CherryPick(commits, null), _currentBranch != null));
        var toBranch = new MenuItem { Header = $"Cherry-pick{count} to branch" };
        foreach (var b in _refs.Where(r => r.Kind == RefKind.Local && !r.IsHead))
            toBranch.Items.Add(Item(b.ShortName, () => CherryPick(commits, b.ShortName)));
        toBranch.IsEnabled = toBranch.Items.Count > 0;
        menu.Items.Add(toBranch);
        menu.Items.Add(Item($"Revert{count}", () => Revert(commits)));

        if (commits.Count == 1)
        {
            var c = commits[0];
            menu.Items.Add(new Separator());
            var reset = new MenuItem { Header = $"Reset '{cur}' to here" };
            reset.Items.Add(Item("Soft — keep index and working tree", () => Do(() => Exec("reset", "--soft", c.Hash))));
            reset.Items.Add(Item("Mixed — keep working tree", () => Do(() => Exec("reset", "--mixed", c.Hash))));
            reset.Items.Add(Item("Hard — discard all changes…", async () =>
            {
                if (await Dialogs.Confirm(this, "Hard reset",
                        $"Reset '{cur}' to {c.ShortHash} and discard all uncommitted changes? Commits after it remain only in reflog.", "Reset hard"))
                    await Do(() => Exec("reset", "--hard", c.Hash));
            }));
            menu.Items.Add(reset);
            menu.Items.Add(Item("Interactive rebase from here…", () => InteractiveRebaseFrom(c), _currentBranch != null));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Copy hash", () => CopyText(c.Hash)));
            menu.Items.Add(Item("Copy subject", () => CopyText(c.Subject)));
        }

        menu.Open(GraphList);
        e.Handled = true;
    }

    /// <summary>Applies commits oldest first, optionally switching to <paramref name="targetBranch"/> first.</summary>
    async Task CherryPick(List<CommitInfo> commits, string? targetBranch)
    {
        var ordered = Enumerable.Reverse(commits).ToList();
        await Do(async () =>
        {
            if (targetBranch != null && targetBranch != _currentBranch && !await Exec("switch", targetBranch)) return;
            foreach (var c in ordered)
            {
                var ok = c.Parents.Length > 1
                    ? await Exec("cherry-pick", "-m", "1", c.Hash)
                    : await Exec("cherry-pick", c.Hash);
                if (!ok) return;
            }
        });
    }

    /// <summary>Reverts commits newest first, so later changes are undone before the ones they build on.</summary>
    async Task Revert(List<CommitInfo> commits)
    {
        await Do(async () =>
        {
            foreach (var c in commits)
            {
                var ok = c.Parents.Length > 1
                    ? await Exec("revert", "--no-edit", "-m", "1", c.Hash)
                    : await Exec("revert", "--no-edit", c.Hash);
                if (!ok) return;
            }
        });
    }

    void OnFilesContext(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is FileChange clicked && FilesList.SelectedItems?.Contains(clicked) != true)
            FilesList.SelectedItem = clicked;
        var files = FilesList.SelectedItems?.OfType<FileChange>().ToList() ?? new();
        if (files.Count == 0 || _repo == null) return;
        var repo = _repo;
        var first = files[0];

        var menu = new ContextMenu();
        if (files.Count == 1)
        {
            menu.Items.Add(Item("Open in compare window", () => OpenCompare(first)));
            menu.Items.Add(Item("History…", () => OpenHistory(first, blame: false), !first.IsUntracked));
            menu.Items.Add(Item("Blame…", () => OpenHistory(first, blame: true), !first.IsUntracked && first.Status != 'D'));
            menu.Items.Add(new Separator());
        }
        if (_filesOwner?.IsWorkingTree == true)
        {
            menu.Items.Add(Item("Stage", () => Stage(files)));
            menu.Items.Add(Item("Unstage", () => Unstage(files)));
            menu.Items.Add(Item("Discard…", () => Discard(files)));
            var conflicts = files.Where(f => f.IsConflict).Select(f => f.RelPath).ToArray();
            if (conflicts.Length > 0)
            {
                menu.Items.Add(new Separator());
                if (files.Count == 1 && first.IsConflict)
                    menu.Items.Add(Item("Resolve in conflict editor…", () => OpenConflictEditor(first)));
                menu.Items.Add(Item("Resolve: take ours", () => TakeSide("--ours", conflicts)));
                menu.Items.Add(Item("Resolve: take theirs", () => TakeSide("--theirs", conflicts)));
                menu.Items.Add(Item("Mark resolved (stage)", () => Stage(files.Where(f => f.IsConflict).ToList())));
            }
            menu.Items.Add(new Separator());
        }
        if (files.Count == 1)
        {
            menu.Items.Add(LfsMenu(first));
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(Item("Open", () => Shell.OpenFile(repo.FullPath(first.RelPath))));
        menu.Items.Add(Item("Show in Explorer", () => Shell.ShowInExplorer(repo.FullPath(first.RelPath))));
        menu.Items.Add(Item("Copy path", () => CopyText(string.Join("\n", files.Select(f => f.RelPath)))));
        menu.Open(FilesList);
        e.Handled = true;
    }

    /// <summary>File log and blame; blame starts at the selected commit, or at the working tree file.</summary>
    void OpenHistory(FileChange f, bool blame)
    {
        if (_repo == null || _filesOwner == null) return;
        var rev = _filesOwner.IsWorkingTree ? null : _filesOwner.Hash;
        new FileHistoryWindow(_repo, f.RelPath, rev, blame).Show(this);
    }

    /// <summary>Resolves conflicts by taking one side entirely, then stages the result.</summary>
    Task TakeSide(string side, string[] paths) => Do(async () =>
    {
        if (await Exec(["checkout", side, "--", .. paths]))
            await Exec(["add", "--", .. paths]);
    });

    MenuItem LfsMenu(FileChange f)
    {
        var repo = _repo!;
        var lfs = new MenuItem { Header = "LFS" };
        if (!f.IsLfs)
        {
            var ext = LfsActions.ExtensionPattern(f.RelPath);
            if (ext.Length > 0)
                lfs.Items.Add(Item($"Track '{ext}' in LFS…", () => Do(() => LfsActions.Track(this, repo, ext, literal: false))));
            lfs.Items.Add(Item("Track this file in LFS…", () => Do(() => LfsActions.Track(this, repo, f.RelPath, literal: true))));
        }
        else
        {
            var rules = repo.LfsRules().Where(r => GitRepo.AttrPatternMatches(r.Pattern, f.RelPath)).ToList();
            foreach (var rule in rules)
                lfs.Items.Add(Item($"Untrack '{rule.Pattern}'…", () => Do(() => LfsActions.Untrack(this, repo, rule.Pattern))));
            if (rules.Count == 0)
                lfs.Items.Add(new MenuItem { Header = "Rule is not in the root .gitattributes", IsEnabled = false });
        }
        lfs.Items.Add(new Separator());
        AddLockItems(lfs, f);
        lfs.Items.Add(new Separator());
        lfs.Items.Add(Item("Open LFS window…", OpenLfs));
        return lfs;
    }
}
