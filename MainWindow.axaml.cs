using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ClockworkGit.Git;
using ClockworkGit.UI;
using SkiaSharp;

namespace ClockworkGit;

public partial class MainWindow : Window
{
    const int LogLimit = 2000;

    readonly Settings _settings = Settings.Load();
    readonly ObservableCollection<RepoItem> _repos = new();
    readonly HashSet<string> _expanded = new() { "head", "local" };

    GitRepo? _repo;
    List<CommitInfo> _allCommits = new();
    List<FileChange> _workFiles = new();
    List<RefInfo> _refs = new();
    List<string> _remotes = new();
    string? _currentBranch, _headHash, _pendingFile, _undoneMessage;
    CommitInfo? _filesOwner;
    bool _busy, _resortingFiles;
    int _refreshGen, _logGen, _detailsGen, _diffGen;
    DateTime _lastRefresh;

    public MainWindow()
    {
        InitializeComponent();

        ReposList.ItemsSource = _repos;
        foreach (var p in _settings.Repositories) _repos.Add(new RepoItem(p));
        var arg = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
        if (arg != null && Directory.Exists(arg))
        {
            var full = Path.GetFullPath(arg);
            AddRepository(full, select: false);
            _settings.LastRepository = full;
        }

        UpdateSortHeaders();

        var checker = ImageTools.CheckerBrush();
        ImgOldHost.Background = checker;
        ImgNewHost.Background = checker;
        ImgOverlayHost.Background = checker;

        ReposList.SelectionChanged += async (_, _) =>
        {
            if (ReposList.SelectedItem is RepoItem r) await OpenRepo(r);
        };
        GraphList.SelectionChanged += (_, _) => LoadDetails();
        FilesList.SelectionChanged += (_, _) =>
        {
            if (!_resortingFiles) ShowDiff(FilesList.SelectedItems?.Count == 1 ? FilesList.SelectedItem as FileChange : null);
        };
        FilterBox.TextChanged += (_, _) => ApplyFilter((GraphList.SelectedItem as CommitInfo)?.Hash);

        GraphList.ContextRequested += OnGraphContext;
        BranchTree.ContextRequested += OnBranchContext;
        BranchTree.DoubleTapped += OnBranchDoubleTapped;
        FilesList.ContextRequested += OnFilesContext;
        ReposList.ContextRequested += OnRepoContext;

        ImgSideBySide.IsCheckedChanged += (_, _) => UpdateImageMode();
        ImgOnion.IsCheckedChanged += (_, _) => UpdateImageMode();
        ImgDiffMode.IsCheckedChanged += (_, _) => UpdateImageMode();
        ImgActualSize.IsCheckedChanged += (_, _) => UpdateImageMode();

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);

        Opened += (_, _) =>
        {
            _ = UpdateRepoBranches();
            var last = _repos.FirstOrDefault(r => SamePath(r.Path, _settings.LastRepository)) ?? _repos.FirstOrDefault();
            if (last != null) ReposList.SelectedItem = last;
        };
        Activated += async (_, _) =>
        {
            if (_repo != null && !_busy && (DateTime.Now - _lastRefresh).TotalSeconds > 2) await RefreshAll();
        };
    }

    static bool SamePath(string? a, string? b) =>
        a != null && b != null && string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    // ───────────────────────── repositories ─────────────────────────

    void AddRepository(string path, bool select)
    {
        var item = _repos.FirstOrDefault(r => SamePath(r.Path, path));
        if (item == null)
        {
            item = new RepoItem(path);
            _repos.Add(item);
            _settings.Repositories.Add(path);
            _settings.Save();
            _ = UpdateRepoBranches();
        }
        if (select) ReposList.SelectedItem = item;
    }

    async Task UpdateRepoBranches()
    {
        foreach (var r in _repos.ToList())
        {
            var res = await GitRunner.RunAsync(r.Path, "symbolic-ref", "--short", "-q", "HEAD");
            r.Branch = res.Ok ? res.Output.Trim() : Directory.Exists(r.Path) ? "detached" : "missing";
        }
    }

    async Task OpenRepo(RepoItem item)
    {
        var repo = Directory.Exists(item.Path) ? await GitRepo.Open(item.Path) : null;
        if (repo == null)
        {
            await Dialogs.ShowMessage(this, "Repository", $"{item.Path} is not a git repository.");
            return;
        }
        _repo = repo;
        _settings.LastRepository = item.Path;
        _settings.Save();
        Title = $"{item.Name} - [{item.Path}] - ClockworkGit";
        _allCommits = new();
        GraphList.ItemsSource = null;
        FilesList.ItemsSource = null;
        ShowDiff(null);
        FilterBox.Text = "";
        await RefreshAll();
    }

    async void OnAddRepo(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select repository folder",
            AllowMultiple = false,
        });
        var path = picked.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        var top = await GitRunner.RunAsync(path, "rev-parse", "--show-toplevel");
        if (!top.Ok)
        {
            await Dialogs.ShowMessage(this, "Add Repository", $"{path} is not inside a git repository.");
            return;
        }
        AddRepository(Path.GetFullPath(top.Output.Trim()), select: true);
    }

    void OnRepoContext(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not RepoItem item) return;
        var menu = new ContextMenu();
        menu.Items.Add(Item("Open in Explorer", () => { OpenInExplorer(item.Path); return Task.CompletedTask; }));
        menu.Items.Add(Item("Remove from list", () =>
        {
            _repos.Remove(item);
            _settings.Repositories.RemoveAll(p => SamePath(p, item.Path));
            _settings.ShownRefs.Remove(item.Path);
            _settings.Save();
            return Task.CompletedTask;
        }));
        menu.Open(ReposList);
        e.Handled = true;
    }

    // ───────────────────────── refresh ─────────────────────────

    async Task RefreshAll()
    {
        var repo = _repo;
        if (repo == null) return;
        var gen = ++_refreshGen;
        _lastRefresh = DateTime.Now;
        var selHash = (GraphList.SelectedItem as CommitInfo)?.Hash;
        var selFile = (FilesList.SelectedItem as FileChange)?.RelPath;

        var tRefs = repo.Refs();
        var tStashes = repo.Stashes();
        var tStatus = repo.Status();
        var tBranch = repo.CurrentBranch();
        var tHead = repo.HeadHash();
        var tRemotes = repo.Remotes();
        await Task.WhenAll(tRefs, tStashes, tStatus, tBranch, tHead, tRemotes);
        if (gen != _refreshGen || repo != _repo) return;

        _refs = tRefs.Result;
        _workFiles = tStatus.Result;
        _currentBranch = tBranch.Result;
        _headHash = tHead.Result;
        _remotes = tRemotes.Result;

        BuildBranchTree(tStashes.Result);
        var repoItem = _repos.FirstOrDefault(r => SamePath(r.Path, repo.Path));
        if (repoItem != null) repoItem.Branch = _currentBranch ?? "detached";
        UpdateStateBanner();
        UpdateStatusRight();
        await ReloadLog(selHash, selFile);
    }

    List<string> ShownRefs()
    {
        var key = _repo!.Path;
        if (!_settings.ShownRefs.TryGetValue(key, out var list))
        {
            list = new List<string> { "HEAD" };
            var upstream = _refs.FirstOrDefault(r => r.IsHead)?.Upstream;
            if (!string.IsNullOrEmpty(upstream)) list.Add(upstream);
            _settings.ShownRefs[key] = list;
            _settings.Save();
        }
        return list;
    }

    async Task ReloadLog(string? selectHash = null, string? selectFile = null)
    {
        var repo = _repo;
        if (repo == null) return;
        var gen = ++_logGen;
        var refs = ShownRefs().Where(r => r == "HEAD" || _refs.Any(x => x.FullName == r)).ToList();
        if (refs.Count == 0) refs.Add("HEAD");

        var commits = _headHash == null && refs.All(r => r == "HEAD")
            ? new List<CommitInfo>()
            : await repo.Log(refs, LogLimit, _remotes);
        if (gen != _logGen || repo != _repo) return;

        var headLoaded = _headHash != null && commits.Any(c => c.Hash == _headHash);
        commits.Insert(0, CommitInfo.WorkingTree(headLoaded ? _headHash : null, _workFiles.Count));
        GraphBuilder.Build(commits);
        _allCommits = commits;
        _pendingFile = selectFile;
        ApplyFilter(selectHash);
    }

    void ApplyFilter(string? selectHash)
    {
        var filter = FilterBox.Text?.Trim() ?? "";
        var filtering = filter.Length > 0;
        foreach (var c in _allCommits) c.ShownGraph = filtering ? null : c.Graph;
        var rows = filtering ? _allCommits.Where(c => !c.IsWorkingTree && c.Matches(filter)).ToList() : _allCommits;
        GraphList.ItemsSource = rows;
        GraphList.SelectedItem = rows.FirstOrDefault(c => c.Hash == selectHash) ?? rows.FirstOrDefault();
    }

    void UpdateStateBanner()
    {
        var state = _repo?.GetState() ?? RepoState.Clean;
        StateBanner.IsVisible = state != RepoState.Clean;
        var conflicts = _workFiles.Count(f => f.IsConflict);
        var what = state switch
        {
            RepoState.Merging => "Merge in progress",
            RepoState.CherryPicking => "Cherry-pick in progress",
            RepoState.Reverting => "Revert in progress",
            RepoState.Rebasing => "Rebase in progress",
            _ => "",
        };
        StateText.Text = conflicts > 0
            ? $"{what}: {conflicts} conflicted file(s). Resolve, stage, then Continue."
            : $"{what}. Continue or Abort.";
    }

    void UpdateStatusRight()
    {
        var head = _refs.FirstOrDefault(r => r.IsHead);
        var text = _currentBranch ?? $"detached at {_headHash?[..8]}";
        if (head != null && head.UpstreamShort.Length > 0)
        {
            var track = head.Track.Trim('[', ']')
                .Replace("ahead ", "↑").Replace("behind ", "↓").Replace(",", "");
            text += $"  →  {head.UpstreamShort}  {(track.Length == 0 ? "in sync" : track)}";
        }
        StatusRight.Text = text;
    }

    void SetStatus(string text) => StatusText.Text = text;

    // ───────────────────────── branches tree ─────────────────────────

    void BuildBranchTree(List<StashInfo> stashes)
    {
        var shown = ShownRefs();
        var roots = new List<BranchNode>();

        BranchNode Node(string key, NodeKind kind, string title, string detail = "", string? fullRef = null,
            string? shortName = null, string? remote = null, RefInfo? info = null, bool current = false)
        {
            var n = new BranchNode
            {
                Key = key, Kind = kind, Title = title, Detail = detail, FullRef = fullRef,
                ShortName = shortName, Remote = remote, Ref = info, IsCurrent = current,
            };
            n.InitState(fullRef != null && shown.Contains(fullRef), _expanded.Contains(key));
            n.PropertyChanged += OnBranchNodeChanged;
            return n;
        }

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
                : r.Track == "[gone]" ? $"→ {r.UpstreamShort} (gone)"
                : r.Track.Length == 0 ? $"= {r.UpstreamRemote}"
                : $"{r.Track.Trim('[', ']').Replace("ahead ", "↑").Replace("behind ", "↓").Replace(",", "")} {r.UpstreamRemote}";
            AddPath(localGroup, r.ShortName, leaf => Node("local/" + r.ShortName, NodeKind.Local,
                (r.IsHead ? "▶ " : "") + leaf, detail, r.FullName, r.ShortName, info: r, current: r.IsHead));
        }
        roots.Add(localGroup);

        foreach (var remote in _remotes)
        {
            var remoteRefs = _refs.Where(r => r.Kind == RefKind.Remote && r.ShortName.StartsWith(remote + "/")).ToList();
            var group = Node("remote:" + remote, NodeKind.Group, $"{remote} ({remoteRefs.Count})");
            foreach (var r in remoteRefs)
                AddPath(group, r.ShortName[(remote.Length + 1)..], leaf => Node("remote:" + r.ShortName, NodeKind.Remote,
                    leaf, "", r.FullName, r.ShortName, remote, r));
            roots.Add(group);
        }

        var tags = _refs.Where(r => r.Kind == RefKind.Tag).ToList();
        if (tags.Count > 0)
        {
            var group = Node("tags", NodeKind.Group, $"Tags ({tags.Count})");
            foreach (var r in tags)
                group.Children.Add(Node("tag:" + r.ShortName, NodeKind.Tag, r.ShortName, "", r.FullName, r.ShortName, info: r));
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
            if (r.Error.Contains("not fully merged")
                && await Dialogs.Confirm(this, "Delete branch", $"'{name}' is not fully merged. Its unmerged commits will only be reachable via reflog. Force delete?", "Force delete"))
                await Exec("branch", "-D", name);
            else if (!r.Error.Contains("not fully merged"))
                await Dialogs.ShowMessage(this, "git branch failed", r.Error.Trim());
        });
    }

    // ───────────────────────── graph selection & details ─────────────────────────

    List<CommitInfo> SelectedCommits()
    {
        var selected = GraphList.SelectedItems?.OfType<CommitInfo>().ToHashSet() ?? new HashSet<CommitInfo>();
        return _allCommits.Where(selected.Contains).ToList(); // newest first, in graph order
    }

    List<CommitInfo> SelectedRealCommits() => SelectedCommits().Where(c => !c.IsWorkingTree).ToList();

    async void LoadDetails()
    {
        var gen = ++_detailsGen;
        var selected = SelectedCommits();
        var repo = _repo;
        if (selected.Count != 1 || repo == null)
        {
            _filesOwner = null;
            FilesList.ItemsSource = null;
            DetailAvatar.IsVisible = false;
            DetailHeader.Text = selected.Count > 1 ? $"{selected.Count} commits selected" : "";
            DetailMessage.Text = "";
            ShowDiff(null);
            return;
        }

        var c = selected[0];
        List<FileChange> files;
        if (c.IsWorkingTree)
        {
            files = _workFiles;
            DetailAvatar.IsVisible = false;
            var staged = files.Count(f => f.IsStaged);
            var untracked = files.Count(f => f.IsUntracked);
            DetailHeader.Text = files.Count == 0
                ? "Nothing to commit, working tree clean."
                : $"{files.Count} changed: {staged} staged, {files.Count - staged - untracked} unstaged, {untracked} untracked";
            DetailMessage.Text = "Working Tree / Index";
        }
        else
        {
            var tMessage = repo.CommitMessage(c.Hash);
            var tFiles = repo.CommitFiles(c);
            await Task.WhenAll(tMessage, tFiles);
            if (gen != _detailsGen) return;
            files = tFiles.Result;
            DetailAvatar.IsVisible = true;
            DetailAvatar.Background = c.AvatarBrush;
            DetailAvatarText.Text = c.Initials;
            var lines = new List<string>
            {
                $"Commit {c.Hash[..10]}",
                $"by {c.Author} <{c.Email}>, {c.Date.LocalDateTime:yyyy-MM-dd HH:mm}",
            };
            if (c.Parents.Length > 0) lines.Add("parent " + string.Join(", ", c.Parents.Select(p => p[..8])));
            if (c.Refs.Count > 0) lines.Add("refs " + string.Join(", ", c.Refs.Select(r => r.Name)));
            DetailHeader.Text = string.Join("\n", lines);
            DetailMessage.Text = tMessage.Result;
        }

        _filesOwner = c;
        var sorted = SortFiles(files);
        FilesList.ItemsSource = sorted;
        var pick = sorted.FirstOrDefault(f => f.RelPath == _pendingFile) ?? sorted.FirstOrDefault();
        _pendingFile = null;
        FilesList.SelectedItem = pick;
        if (pick == null) ShowDiff(null);
    }

    // ───────────────────────── files sorting ─────────────────────────

    List<FileChange> SortFiles(IEnumerable<FileChange> files)
    {
        var byName = _settings.FilesSort == "Name";
        Func<FileChange, string> primary = byName ? f => f.Name : f => f.RelPath;
        var cmp = StringComparer.CurrentCultureIgnoreCase;
        var ordered = _settings.FilesSortDescending
            ? files.OrderByDescending(primary, cmp)
            : files.OrderBy(primary, cmp);
        return ordered.ThenBy(f => f.RelPath, cmp).ToList();
    }

    void UpdateSortHeaders()
    {
        var arrow = _settings.FilesSortDescending ? " ▼" : " ▲";
        SortByName.Content = "Name" + (_settings.FilesSort == "Name" ? arrow : "");
        SortByPath.Content = "Path" + (_settings.FilesSort == "Path" ? arrow : "");
    }

    void OnSortFiles(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string column }) return;
        if (_settings.FilesSort == column) _settings.FilesSortDescending = !_settings.FilesSortDescending;
        else
        {
            _settings.FilesSort = column;
            _settings.FilesSortDescending = false;
        }
        _settings.Save();
        UpdateSortHeaders();

        if (FilesList.ItemsSource is not IEnumerable<FileChange> current) return;
        var selected = FilesList.SelectedItems?.OfType<FileChange>().ToList() ?? new();
        // Same files stay selected, so the diff panel must not be reloaded or cleared.
        _resortingFiles = true;
        try
        {
            FilesList.ItemsSource = SortFiles(current);
            foreach (var f in selected) FilesList.SelectedItems?.Add(f);
        }
        finally
        {
            _resortingFiles = false;
        }
        if (selected.Count > 0) FilesList.ScrollIntoView(selected[0]);
    }

    // ───────────────────────── diff ─────────────────────────

    async void ShowDiff(FileChange? f)
    {
        var gen = ++_diffGen;
        var repo = _repo;
        var owner = _filesOwner;
        if (f == null || repo == null || owner == null)
        {
            DiffTitle.Text = "Changes";
            DiffList.ItemsSource = null;
            DiffList.IsVisible = true;
            ImagePanel.IsVisible = false;
            return;
        }

        DiffTitle.Text = $"Changes of {f.RelPath} ({f.StatusText})";
        if (ImageTools.IsImage(f.RelPath))
        {
            await ShowImageDiff(repo, owner, f, gen);
            return;
        }

        DiffList.IsVisible = true;
        ImagePanel.IsVisible = false;
        List<DiffRow> rows;
        if (owner.IsWorkingTree && (f.IsUntracked || (_headHash == null && f.IndexStatus == 'A')))
        {
            rows = await Task.Run(() => ReadAsAdded(repo.FullPath(f.RelPath)));
        }
        else
        {
            var diff = owner.IsWorkingTree ? await repo.WorkingDiff(f) : await repo.CommitDiff(owner, f);
            rows = await Task.Run(() => DiffParser.SideBySide(diff));
        }
        if (gen != _diffGen) return;
        DiffList.ItemsSource = rows;
    }

    static List<DiffRow> ReadAsAdded(string path)
    {
        if (Directory.Exists(path)) return [DiffRow.Message("Directory")];
        if (!File.Exists(path)) return [DiffRow.Message("File not found")];
        var info = new FileInfo(path);
        if (info.Length > 4 * 1024 * 1024) return [DiffRow.Message($"File too large to show ({info.Length / 1024} KB)")];
        var bytes = File.ReadAllBytes(path);
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0)) return [DiffRow.Message("Binary file")];
        return DiffParser.AllAdded(System.Text.Encoding.UTF8.GetString(bytes));
    }

    async Task ShowImageDiff(GitRepo repo, CommitInfo owner, FileChange f, int gen)
    {
        DiffList.IsVisible = false;
        ImagePanel.IsVisible = true;
        ImgInfo.Text = "Loading…";
        SetImages(null, null, null);

        byte[]? oldBytes = null, newBytes = null;
        if (owner.IsWorkingTree)
        {
            if (!f.IsUntracked && f.IndexStatus != 'A' && _headHash != null)
                oldBytes = await repo.Blob("HEAD", f.OldPath ?? f.RelPath);
            var full = repo.FullPath(f.RelPath);
            if (File.Exists(full)) newBytes = await File.ReadAllBytesAsync(full);
        }
        else
        {
            if (f.Status != 'A' && owner.Parents.Length > 0)
                oldBytes = await repo.Blob(owner.Parents[0], f.OldPath ?? f.RelPath);
            if (f.Status != 'D')
                newBytes = await repo.Blob(owner.Hash, f.RelPath);
        }

        var path = f.RelPath;
        var (oldSk, newSk, diffSk, changed) = await Task.Run(() =>
        {
            var o = ImageTools.Decode(oldBytes, path);
            var n = ImageTools.Decode(newBytes, path);
            SKBitmap? d = null;
            double pct = -1;
            if (o != null && n != null) (d, pct) = ImageTools.Diff(o, n);
            return (o, n, d, pct);
        });

        if (gen != _diffGen)
        {
            oldSk?.Dispose(); newSk?.Dispose(); diffSk?.Dispose();
            return;
        }

        SetImages(
            oldSk == null ? null : ImageTools.ToAvalonia(oldSk),
            newSk == null ? null : ImageTools.ToAvalonia(newSk),
            diffSk == null ? null : ImageTools.ToAvalonia(diffSk));

        static string Describe(SKBitmap? b, byte[]? bytes) =>
            bytes == null ? "—" : b == null ? $"unsupported format, {bytes.Length / 1024.0:0.#} KB" : $"{b.Width}×{b.Height}, {bytes.Length / 1024.0:0.#} KB";

        var info = $"Old: {Describe(oldSk, oldBytes)}   New: {Describe(newSk, newBytes)}";
        if (changed >= 0) info += changed == 0 ? "   · pixels identical" : $"   · changed {changed:0.###}% of pixels";
        if (oldSk != null && newSk != null && (oldSk.Width != newSk.Width || oldSk.Height != newSk.Height)) info += "   · size differs";
        ImgInfo.Text = info;

        var small = Math.Max(oldSk?.Width ?? 0, newSk?.Width ?? 0) <= 256;
        foreach (var img in new[] { ImgOld, ImgNew, ImgOnionOld, ImgOnionNew, ImgDiffImage })
            RenderOptions.SetBitmapInterpolationMode(img, small ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);

        oldSk?.Dispose(); newSk?.Dispose(); diffSk?.Dispose();
        UpdateImageMode();
    }

    void SetImages(Bitmap? oldImage, Bitmap? newImage, Bitmap? diffImage)
    {
        var previous = new[] { ImgOld.Source, ImgNew.Source, ImgDiffImage.Source };
        ImgOld.Source = oldImage;
        ImgOnionOld.Source = oldImage;
        ImgNew.Source = newImage;
        ImgOnionNew.Source = newImage;
        ImgDiffImage.Source = diffImage;
        foreach (var p in previous) (p as IDisposable)?.Dispose();
    }

    void UpdateImageMode()
    {
        var side = ImgSideBySide.IsChecked == true;
        var onion = ImgOnion.IsChecked == true;
        var diff = ImgDiffMode.IsChecked == true;
        ImgSideGrid.IsVisible = side;
        ImgOverlayHost.IsVisible = onion;
        ImgDiffHost.IsVisible = diff;
        OnionSlider.IsVisible = onion;
        var stretch = ImgActualSize.IsChecked == true ? Stretch.None : Stretch.Uniform;
        foreach (var img in new[] { ImgOld, ImgNew, ImgOnionOld, ImgOnionNew, ImgDiffImage }) img.Stretch = stretch;
    }

    // ───────────────────────── operations ─────────────────────────

    async Task<bool> Exec(params string[] args)
    {
        var cmd = "git " + string.Join(' ', args);
        SetStatus(cmd + " …");
        var r = await _repo!.Run(args);
        if (!r.Ok)
        {
            SetStatus("Failed: " + cmd);
            await Dialogs.ShowMessage(this, $"git {args[0]} failed", (r.Error + "\n" + r.Output).Trim());
            return false;
        }
        var last = (r.Error + "\n" + r.Output).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        SetStatus(last == null ? "Done: " + cmd : $"{cmd}: {last}");
        return true;
    }

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
    async void OnRefresh(object? sender, RoutedEventArgs e) => await RefreshAll();
    void OnExit(object? sender, RoutedEventArgs e) => Close();
    void OnOpenExplorer(object? sender, RoutedEventArgs e) { if (_repo != null) OpenInExplorer(_repo.Path); }

    async void OnSync(object? sender, RoutedEventArgs e) => await Do(async () =>
    {
        if (await Exec("pull")) await PushCore();
    });

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

    async void OnUndoCommit(object? sender, RoutedEventArgs e)
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

    List<FileChange> SelectedWorkFiles() =>
        _filesOwner?.IsWorkingTree == true ? FilesList.SelectedItems?.OfType<FileChange>().ToList() ?? new() : new();

    async void OnStage(object? sender, RoutedEventArgs e) => await Stage(SelectedWorkFiles());
    async void OnUnstage(object? sender, RoutedEventArgs e) => await Unstage(SelectedWorkFiles());
    async void OnDiscard(object? sender, RoutedEventArgs e) => await Discard(SelectedWorkFiles());

    Task Stage(List<FileChange> files) =>
        Do(() => files.Count == 0
            ? Exec("add", "-A")
            : Exec(new[] { "add", "-A", "--" }.Concat(files.Select(f => f.RelPath)).ToArray()));

    Task Unstage(List<FileChange> files) =>
        Do(() => _headHash == null
            ? Exec(new[] { "rm", "-r", "-q", "--cached", "--" }.Concat(files.Count == 0 ? ["."] : files.Select(f => f.RelPath)).ToArray())
            : files.Count == 0
                ? Exec("reset", "-q")
                : Exec(new[] { "restore", "--staged", "--" }.Concat(files.Select(f => f.RelPath)).ToArray()));

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
                && !await Exec(new[] { "restore", "--source=HEAD", "--staged", "--worktree", "--" }.Concat(tracked.Select(f => f.RelPath)).ToArray()))
                return;
            var added = newFiles.Where(f => !f.IsUntracked).ToList();
            if (added.Count > 0)
                await _repo!.Run(new[] { "rm", "-q", "--cached", "-f", "--" }.Concat(added.Select(f => f.RelPath)).ToArray());
            foreach (var f in newFiles)
            {
                var full = _repo!.FullPath(f.RelPath);
                if (File.Exists(full)) File.Delete(full);
            }
        });
    }

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
        var state = _repo?.GetState() ?? RepoState.Clean;
        var op = state switch
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

    // ───────────────────────── graph context menu ─────────────────────────

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
            menu.Items.Add(Item("Undo Last Commit", () => { OnUndoCommit(null, new RoutedEventArgs()); return Task.CompletedTask; }));
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
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Copy hash", () => CopyText(c.Hash)));
            menu.Items.Add(Item("Copy subject", () => CopyText(c.Subject)));
        }

        menu.Open(GraphList);
        e.Handled = true;
    }

    async Task CherryPick(List<CommitInfo> commits, string? targetBranch)
    {
        var ordered = Enumerable.Reverse(commits).ToList(); // oldest first
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

    async Task Revert(List<CommitInfo> commits)
    {
        await Do(async () =>
        {
            foreach (var c in commits) // newest first
            {
                var ok = c.Parents.Length > 1
                    ? await Exec("revert", "--no-edit", "-m", "1", c.Hash)
                    : await Exec("revert", "--no-edit", c.Hash);
                if (!ok) return;
            }
        });
    }

    // ───────────────────────── files context menu ─────────────────────────

    void OnFilesContext(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is FileChange clicked && FilesList.SelectedItems?.Contains(clicked) != true)
            FilesList.SelectedItem = clicked;
        var files = FilesList.SelectedItems?.OfType<FileChange>().ToList() ?? new();
        if (files.Count == 0 || _repo == null) return;

        var menu = new ContextMenu();
        if (_filesOwner?.IsWorkingTree == true)
        {
            menu.Items.Add(Item("Stage", () => Stage(files)));
            menu.Items.Add(Item("Unstage", () => Unstage(files)));
            menu.Items.Add(Item("Discard…", () => Discard(files)));
            if (files.Any(f => f.IsConflict))
            {
                var paths = files.Where(f => f.IsConflict).Select(f => f.RelPath).ToArray();
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Resolve: take ours", () => Do(async () =>
                {
                    if (await Exec(new[] { "checkout", "--ours", "--" }.Concat(paths).ToArray()))
                        await Exec(new[] { "add", "--" }.Concat(paths).ToArray());
                })));
                menu.Items.Add(Item("Resolve: take theirs", () => Do(async () =>
                {
                    if (await Exec(new[] { "checkout", "--theirs", "--" }.Concat(paths).ToArray()))
                        await Exec(new[] { "add", "--" }.Concat(paths).ToArray());
                })));
                menu.Items.Add(Item("Mark resolved (stage)", () => Stage(files.Where(f => f.IsConflict).ToList())));
            }
            menu.Items.Add(new Separator());
        }
        var first = files[0];
        menu.Items.Add(Item("Open", () => { OpenFile(_repo.FullPath(first.RelPath)); return Task.CompletedTask; }));
        menu.Items.Add(Item("Show in Explorer", () => { ShowInExplorer(_repo.FullPath(first.RelPath)); return Task.CompletedTask; }));
        menu.Items.Add(Item("Copy path", () => CopyText(string.Join("\n", files.Select(f => f.RelPath)))));
        menu.Open(FilesList);
        e.Handled = true;
    }

    // ───────────────────────── helpers ─────────────────────────

    static MenuItem Item(string header, Func<Task> action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += async (_, _) => await action();
        return item;
    }

    async Task CopyText(string text)
    {
        if (Clipboard != null) await Clipboard.SetTextAsync(text);
    }

    static void OpenFile(string path)
    {
        if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    static void OpenInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });

    static void ShowInExplorer(string path)
    {
        if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));
        else OpenInExplorer(Path.GetDirectoryName(path)!);
    }

    async void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            await RefreshAll();
        }
        else if (e.Key == Key.K && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            await CommitFlow();
        }
    }
}
