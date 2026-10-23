using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ClockworkGit.Views;

/// <summary>
/// The SmartGit-like main window. Code-behind is split by area:
/// Repositories, Refresh (log/graph/status), Branches, Details (files and diff), Operations, ContextMenus.
/// </summary>
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
    List<RecyclableCommit> _recyclable = new();
    /// <summary>Commit to select once the graph reloads after a branch-tree checkbox changed.</summary>
    string? _selectAfterReload;
    string? _currentBranch, _headHash;
    /// <summary>File to reselect after a refresh rebuilds the Files list.</summary>
    string? _pendingFile;
    /// <summary>Message of the commit removed by Undo Last Commit, offered again in the commit dialog.</summary>
    string? _undoneMessage;
    /// <summary>The commit (or working tree) whose files are listed.</summary>
    CommitInfo? _filesOwner;
    List<FileChange> _ownerFiles = new();
    /// <summary>Diff shown in the bottom panel; the raw text is what partial staging builds patches from.</summary>
    DiffResult? _shownDiff;
    WorkTreeDiffMode _diffMode = WorkTreeDiffMode.Unstaged;
    /// <summary>File the diff mode was chosen for; another file gets a fresh automatic choice.</summary>
    string? _diffModeFile;
    bool _busy, _resortingFiles, _updatingDiffMode;
    // Generation counters drop results of async loads that were superseded by a newer one.
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

        ReposList.SelectionChanged += async (_, _) =>
        {
            if (ReposList.SelectedItem is RepoItem r) await OpenRepo(r);
        };
        GraphList.SelectionChanged += (_, _) => LoadDetails();
        FilterBox.TextChanged += (_, _) => ApplyFilter((GraphList.SelectedItem as CommitInfo)?.Hash);
        FilesList.SelectionChanged += (_, _) =>
        {
            if (!_resortingFiles) ShowDiff(SingleSelectedFile());
        };
        FilesList.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is FileChange f) OpenCompare(f);
        };
        LfsOnly.IsCheckedChanged += (_, _) => RebindFiles();
        SvgAsText.IsCheckedChanged += (_, _) => ShowDiff(SingleSelectedFile());
        ModeUnstaged.IsCheckedChanged += (_, _) => OnDiffModeChosen(ModeUnstaged, WorkTreeDiffMode.Unstaged);
        ModeStaged.IsCheckedChanged += (_, _) => OnDiffModeChosen(ModeStaged, WorkTreeDiffMode.Staged);
        ModeAll.IsCheckedChanged += (_, _) => OnDiffModeChosen(ModeAll, WorkTreeDiffMode.All);
        DiffList.ContextRequested += OnDiffContext;

        GraphList.ContextRequested += OnGraphContext;
        BranchTree.ContextRequested += OnBranchContext;
        BranchTree.DoubleTapped += OnBranchDoubleTapped;
        FilesList.ContextRequested += OnFilesContext;
        ReposList.ContextRequested += OnRepoContext;

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);

        Opened += (_, _) =>
        {
            _ = UpdateRepoBranches();
            var last = _repos.FirstOrDefault(r => SamePath(r.Path, _settings.LastRepository)) ?? _repos.FirstOrDefault();
            if (last != null) ReposList.SelectedItem = last;
        };
        // Like SmartGit: coming back to the window picks up changes made elsewhere.
        Activated += async (_, _) =>
        {
            if (_repo != null && !_busy && (DateTime.Now - _lastRefresh).TotalSeconds > 2) await RefreshAll();
        };
    }

    static bool SamePath(string? a, string? b) =>
        a != null && b != null && string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    FileChange? SingleSelectedFile() => FilesList.SelectedItems?.Count == 1 ? FilesList.SelectedItem as FileChange : null;

    void SetStatus(string text) => StatusText.Text = text;

    async Task CopyText(string text)
    {
        if (Clipboard != null) await Clipboard.SetTextAsync(text);
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
        else if (e.Key is (Key.B or Key.L) && e.KeyModifiers == KeyModifiers.Control && StagePanel.IsVisible)
        {
            e.Handled = true;
            await ApplySelection(wholeBlocks: e.Key == Key.B, discard: false);
        }
    }
}
