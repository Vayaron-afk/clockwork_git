using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using static ClockworkGit.Utils.MenuFactory;

namespace ClockworkGit.Views;

// Branches tree: building it, showing refs in the graph, branch/tag/stash actions.
public partial class MainWindow
{
    /// <summary>Формирует дерево веток по ссылкам, stash и восстановимым коммитам, восстанавливая сохранённое состояние дерева.</summary>
    void BuildBranchTree(List<StashInfo> stashes)
    {
        var shown = ShownRefs();
        var roots = new List<BranchNode>();

        // Creates a tree node and restores its saved expansion and graph-selection state.
        BranchNode Node(string key, NodeKind kind, string title, string detail = "", string? fullRef = null,
            string? shortName = null, string? remote = null, bool current = false)
        {
            var n = new BranchNode
            {
                Key = key, Kind = kind, Title = title, Detail = detail, FullRef = fullRef,
                ShortName = shortName, Remote = remote, IsCurrent = current,
            };
            n.InitState(fullRef != null && shown.Contains(fullRef), _expanded.Contains(key));
            n.PropertyChanged += OnBranchNodeChanged;
            return n;
        }

        // "feature/x/y" becomes folders feature/ → x/ → leaf y.
        // Adds missing folder nodes before appending the branch or tag leaf.
        void AddPath(BranchNode parent, string rel, Func<string, BranchNode> makeLeaf)
        {
            var parts = rel.Split('/');
            var cur = parent;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var key = cur.Key + "/" + parts[i];
                var folder = cur.Children.FirstOrDefault(c => c.Kind == NodeKind.Group && c.Key == key);
                if (folder == null)
                {
                    folder = Node(key, NodeKind.Group, parts[i] + "/");
                    cur.Children.Add(folder);
                }
                cur = folder;
            }
            cur.Children.Add(makeLeaf(parts[^1]));
        }

        var headTitle = _headHash == null ? "HEAD (no commits)" : $"HEAD ({_headHash[..8]})";
        roots.Add(Node("head", NodeKind.Head, headTitle, fullRef: _headHash == null ? null : "HEAD"));

        var locals = _refs.Where(r => r.Kind == RefKind.Local).ToList();
        var localGroup = Node("local", NodeKind.Group, $"Local Branches ({locals.Count})");
        foreach (var r in locals)
        {
            var detail = r.UpstreamShort.Length == 0 ? ""
                : r.IsGone ? $"→ {r.UpstreamShort} (gone)"
                : r.TrackShort.Length == 0 ? $"= {r.UpstreamRemote}"
                : $"{r.TrackShort} {r.UpstreamRemote}";
            AddPath(localGroup, r.ShortName, leaf => Node("local/" + r.ShortName, NodeKind.Local,
                (r.IsHead ? "▶ " : "") + leaf, detail, r.FullName, r.ShortName, current: r.IsHead));
        }
        roots.Add(localGroup);

        foreach (var remote in _remotes)
        {
            var remoteRefs = _refs.Where(r => r.Kind == RefKind.Remote && r.ShortName.StartsWith(remote + "/")).ToList();
            var group = Node("remote:" + remote, NodeKind.Group, $"{remote} ({remoteRefs.Count})");
            foreach (var r in remoteRefs)
                AddPath(group, r.ShortName[(remote.Length + 1)..], leaf => Node("remote:" + r.ShortName, NodeKind.Remote,
                    leaf, "", r.FullName, r.ShortName, remote));
            roots.Add(group);
        }

        var tags = _refs.Where(r => r.Kind == RefKind.Tag).ToList();
        if (tags.Count > 0)
        {
            var group = Node("tags", NodeKind.Group, $"Tags ({tags.Count})");
            foreach (var r in tags)
                group.Children.Add(Node("tag:" + r.ShortName, NodeKind.Tag, r.ShortName, "", r.FullName, r.ShortName));
            roots.Add(group);
        }

        if (stashes.Count > 0)
        {
            var group = Node("stashes", NodeKind.Group, $"Stashes ({stashes.Count})");
            foreach (var s in stashes)
                group.Children.Add(Node("stash:" + s.Ref, NodeKind.Stash, s.Message, s.Ref, shortName: s.Ref));
            roots.Add(group);
        }

        if (_recyclable.Count > 0)
        {
            var group = Node("recyclable", NodeKind.Group, $"Recyclable Commits ({_recyclable.Count})");
            foreach (var r in _recyclable)
            {
                var c = r.Commit;
                var detail = r.Reason.Length > 0 ? $"({r.Reason})" : "";
                // The hash doubles as the "ref": checking the node shows the commit's chain in the graph.
                group.Children.Add(Node("recyclable:" + c.Hash, NodeKind.Recyclable, $"{c.ShortHash} {c.Subject}", detail,
                    fullRef: c.Hash, shortName: c.Hash));
            }
            roots.Add(group);
        }

        BranchTree.ItemsSource = roots;
    }

    /// <summary>Сохраняет изменения раскрытия дерева веток и выбора ссылок графа.</summary>
    void OnBranchNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not BranchNode n || _repo == null) return;
        if (e.PropertyName == nameof(BranchNode.IsExpanded))
        {
            if (n.IsExpanded) _expanded.Add(n.Key);
            else _expanded.Remove(n.Key);
        }
        else if (e.PropertyName == nameof(BranchNode.IsChecked) && n.FullRef != null)
        {
            var shown = ShownRefs();
            if (n.IsChecked && !shown.Contains(n.FullRef)) shown.Add(n.FullRef);
            if (!n.IsChecked) shown.Remove(n.FullRef);
            _settings.Save();
            var select = _selectAfterReload ?? (GraphList.SelectedItem as CommitInfo)?.Hash;
            _selectAfterReload = null;
            _ = ReloadLog(select);
        }
    }

    /// <summary>Переключается на ветку при двойном щелчке или отображает восстановимый коммит на графе.</summary>
    async void OnBranchDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not BranchNode n) return;
        if (n is { Kind: NodeKind.Local or NodeKind.Remote, IsCurrent: false }) await CheckoutBranch(n);
        else if (n.Kind == NodeKind.Recyclable) await ShowRecyclableInGraph(n);
    }

    /// <summary>Добавляет историю восстановимого коммита в граф и выбирает этот коммит.</summary>
    async Task ShowRecyclableInGraph(BranchNode n)
    {
        if (n.IsChecked)
        {
            await ReloadLog(n.ShortName);
            return;
        }
        _selectAfterReload = n.ShortName;
        n.IsChecked = true; // reloads the log via OnBranchNodeChanged
    }

    /// <summary>Находит восстановимый коммит, представленный узлом дерева веток.</summary>
    RecyclableCommit? RecyclableOf(BranchNode n) => _recyclable.FirstOrDefault(r => r.Commit.Hash == n.ShortName);

    /// <summary>Создаёт контекстные действия для выбранной ветки, тега, stash или восстановимого коммита.</summary>
    void OnBranchContext(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not BranchNode n || _repo == null) return;
        BranchTree.SelectedItem = n;
        var cur = _currentBranch ?? "HEAD";
        var menu = new ContextMenu();
        var selected = SelectedRealCommits();

        switch (n.Kind)
        {
            case NodeKind.Local:
                if (!n.IsCurrent)
                {
                    menu.Items.Add(Item("Checkout", () => CheckoutBranch(n)));
                    menu.Items.Add(Item($"Merge into '{cur}'", () => Do(() => Exec("merge", n.ShortName!))));
                    menu.Items.Add(Item($"Rebase '{cur}' onto this", () => RebaseOnto(n.ShortName!)));
                }
                else
                {
                    menu.Items.Add(Item("Pull", () => Do(() => Exec("pull"))));
                    menu.Items.Add(Item("Push", () => Do(PushCore)));
                }
                if (selected.Count > 0)
                    menu.Items.Add(Item($"Cherry-pick {selected.Count} selected commit(s) here", () => CherryPick(selected, n.ShortName)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Rename…", () => RenameBranch(n.ShortName!)));
                menu.Items.Add(Item("Delete…", () => DeleteBranch(n.ShortName!), !n.IsCurrent));
                break;
            case NodeKind.Remote:
                menu.Items.Add(Item("Checkout (track)", () => CheckoutBranch(n)));
                menu.Items.Add(Item($"Merge into '{cur}'", () => Do(() => Exec("merge", n.ShortName!))));
                menu.Items.Add(Item($"Rebase '{cur}' onto this", () => RebaseOnto(n.ShortName!)));
                break;
            case NodeKind.Tag:
                menu.Items.Add(Item("Checkout (detached)", () => Do(() => Exec("switch", "--detach", n.FullRef!))));
                menu.Items.Add(Item("Delete tag…", async () =>
                {
                    if (await Dialogs.Confirm(this, "Delete tag", $"Delete local tag '{n.ShortName}'?", "Delete"))
                        await Do(() => Exec("tag", "-d", n.ShortName!));
                }));
                break;
            case NodeKind.Stash:
                menu.Items.Add(Item("Apply", () => Do(() => Exec("stash", "apply", n.ShortName!))));
                menu.Items.Add(Item("Pop", () => Do(() => Exec("stash", "pop", n.ShortName!))));
                menu.Items.Add(Item("Drop…", async () =>
                {
                    if (await Dialogs.Confirm(this, "Drop stash", $"Drop {n.ShortName} \"{n.Title}\"? It cannot be restored from here.", "Drop"))
                        await Do(() => Exec("stash", "drop", n.ShortName!));
                }));
                break;
            case NodeKind.Recyclable when RecyclableOf(n) is { } recyclable:
                var commit = recyclable.Commit;
                menu.Items.Add(Item("Show in graph", () => ShowRecyclableInGraph(n)));
                menu.Items.Add(Item("Create branch here…", async () =>
                {
                    var name = await Dialogs.Prompt(this, "Create branch", $"Branch name for {commit.ShortHash} (not checked out):");
                    if (!string.IsNullOrWhiteSpace(name)) await Do(() => Exec("branch", name.Trim(), commit.Hash));
                }));
                menu.Items.Add(Item($"Cherry-pick to '{cur}'", () => CherryPick([commit], null), _currentBranch != null));
                menu.Items.Add(Item($"Restore: reset '{cur}' to here…", async () =>
                {
                    if (await Dialogs.Confirm(this, "Restore commit",
                            $"Move '{cur}' to {commit.ShortHash} \"{commit.Subject}\"?\n\n" +
                            "Uses git reset --keep: local changes are kept, and git refuses if they would be overwritten. " +
                            "Commits currently on the branch after the common point become recyclable themselves.", "Reset"))
                        await Do(() => Exec("reset", "--keep", commit.Hash));
                }));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Copy hash", () => CopyText(commit.Hash)));
                menu.Open(BranchTree);
                e.Handled = true;
                return;
            default:
                return;
        }
        if (n.ShortName != null && n.Kind != NodeKind.Stash)
            menu.Items.Add(Item("Copy name", () => CopyText(n.ShortName)));
        menu.Open(BranchTree);
        e.Handled = true;
    }

    /// <summary>Переключается на локальную ветку или создаёт отслеживающую ветку из узла удалённой ветки.</summary>
    async Task CheckoutBranch(BranchNode n)
    {
        if (n.Kind == NodeKind.Local)
        {
            await Do(() => Exec("switch", n.ShortName!));
            return;
        }
        var local = n.ShortName![(n.Remote!.Length + 1)..];
        if (_refs.Any(r => r.Kind == RefKind.Local && r.ShortName == local))
            await Do(() => Exec("switch", local));
        else
            await Do(() => Exec("switch", "--track", n.ShortName!));
    }

    /// <summary>Запрашивает подтверждение и выполняет rebase текущей ветки на выбранную цель.</summary>
    async Task RebaseOnto(string target)
    {
        if (await Dialogs.Confirm(this, "Rebase", $"Rebase '{_currentBranch}' onto '{target}'? Commits of '{_currentBranch}' will be rewritten.", "Rebase"))
            await Do(() => Exec("rebase", target));
    }

    /// <summary>Запрашивает новое имя и переименовывает локальную ветку.</summary>
    async Task RenameBranch(string name)
    {
        var newName = await Dialogs.Prompt(this, "Rename branch", $"New name for '{name}':", name);
        if (!string.IsNullOrWhiteSpace(newName) && newName != name)
            await Do(() => Exec("branch", "-m", name, newName.Trim()));
    }

    /// <summary>Удаляет локальную ветку; принудительное удаление предлагается, только если Git сообщает о неслитых коммитах.</summary>
    async Task DeleteBranch(string name)
    {
        if (!await Dialogs.Confirm(this, "Delete branch", $"Delete local branch '{name}'?", "Delete")) return;
        await Do(async () =>
        {
            var r = await _repo!.Run("branch", "-d", name);
            if (r.Ok) return;
            if (!r.Error.Contains("not fully merged"))
                await Dialogs.ShowMessage(this, "git branch failed", r.Message);
            else if (await Dialogs.Confirm(this, "Delete branch",
                         $"'{name}' is not fully merged. Its unmerged commits will only be reachable via reflog. Force delete?", "Force delete"))
                await Exec("branch", "-D", name);
        });
    }
}
