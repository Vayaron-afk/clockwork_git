using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using static ClockworkGit.Utils.MenuFactory;

namespace ClockworkGit.Views;

// Branches tree: building it, showing refs in the graph, branch/tag/stash actions.
public partial class MainWindow
{
    void BuildBranchTree(List<StashInfo> stashes)
    {
        var shown = ShownRefs();
        var roots = new List<BranchNode>();

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

        BranchTree.ItemsSource = roots;
    }

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
            _ = ReloadLog((GraphList.SelectedItem as CommitInfo)?.Hash);
        }
    }

    async void OnBranchDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is BranchNode { Kind: NodeKind.Local or NodeKind.Remote, IsCurrent: false } n)
            await CheckoutBranch(n);
    }

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
            default:
                return;
        }
        if (n.ShortName != null && n.Kind != NodeKind.Stash)
            menu.Items.Add(Item("Copy name", () => CopyText(n.ShortName)));
        menu.Open(BranchTree);
        e.Handled = true;
    }

    /// <summary>Local branch: switch; remote branch: switch to its local twin, creating a tracking branch if needed.</summary>
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

    async Task RebaseOnto(string target)
    {
        if (await Dialogs.Confirm(this, "Rebase", $"Rebase '{_currentBranch}' onto '{target}'? Commits of '{_currentBranch}' will be rewritten.", "Rebase"))
            await Do(() => Exec("rebase", target));
    }

    async Task RenameBranch(string name)
    {
        var newName = await Dialogs.Prompt(this, "Rename branch", $"New name for '{name}':", name);
        if (!string.IsNullOrWhiteSpace(newName) && newName != name)
            await Do(() => Exec("branch", "-m", name, newName.Trim()));
    }

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
