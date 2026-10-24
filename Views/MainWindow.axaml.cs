using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ClockworkGit.Views;

/// <summary>
/// Главное окно в стиле SmartGit. Код разделён по областям:
/// Repositories, AutoFetch, Refresh, Branches, Details, Staging, Operations, Rebase, Conflicts, Locks и ContextMenus.
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
    /// <summary>Коммит, который нужно выбрать после перезагрузки графа при изменении флажка в дереве веток.</summary>
    string? _selectAfterReload;
    string? _currentBranch, _headHash;
    /// <summary>Файл, который нужно выбрать повторно после обновления списка файлов.</summary>
    string? _pendingFile;
    /// <summary>Сообщение удалённого командой «Отменить последний коммит» коммита, предлагаемое повторно в диалоге создания коммита.</summary>
    string? _undoneMessage;
    /// <summary>Коммит или рабочая копия, файлы которых отображаются.</summary>
    CommitInfo? _filesOwner;
    List<FileChange> _ownerFiles = new();
    /// <summary>Самые внешние каталоги проектов репозитория для группировки списка файлов.</summary>
    List<string> _projectRoots = new();
    /// <summary>Diff в нижней панели; его исходный текст используется для формирования патчей при частичном добавлении в индекс.</summary>
    DiffResult? _shownDiff;
    WorkTreeDiffMode _diffMode = WorkTreeDiffMode.Unstaged;
    /// <summary>Файл, для которого выбран режим diff; для другого файла режим выбирается заново автоматически.</summary>
    string? _diffModeFile;
    bool _busy, _resortingFiles, _updatingDiffMode;
    // Generation counters drop results of async loads that were superseded by a newer one.
    int _refreshGen, _logGen, _detailsGen, _diffGen;
    DateTime _lastRefresh;

    /// <summary>Настраивает привязки, восстанавливает сохранённые репозитории и подключает обработчики событий окна и списков.</summary>
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
        FilesList.SelectionChanged += (_, e) =>
        {
            if (_resortingFiles) return;
            if (e.AddedItems.OfType<FileGroup>().ToList() is { Count: > 0 } groups)
            {
                SelectGroups(groups);
                return;
            }
            ShowDiff(SingleSelectedFile());
        };
        FilesList.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is not FileChange f) return;
            if (f.IsConflict && _filesOwner?.IsWorkingTree == true) _ = OpenConflictEditor(f);
            else OpenCompare(f);
        };
        LfsOnly.IsCheckedChanged += (_, _) => RebindFiles();
        ByProject.IsCheckedChanged += (_, _) => OnByProjectChanged();
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
            _ = UpdateRepoSummaries();
            var last = _repos.FirstOrDefault(r => SamePath(r.Path, _settings.LastRepository)) ?? _repos.FirstOrDefault();
            if (last != null) ReposList.SelectedItem = last;
            StartAutoFetch();
        };
        // Like SmartGit: coming back to the window picks up changes made elsewhere.
        Activated += async (_, _) =>
        {
            if ((DateTime.Now - _lastRefresh).TotalSeconds <= 2) return;
            _ = UpdateRepoSummaries();
            if (_repo != null && !_busy) await RefreshAll();
        };
    }

    /// <summary>Сравнивает полные пути без учёта регистра после удаления завершающих разделителей.</summary>
    static bool SamePath(string? a, string? b) =>
        a != null && b != null && string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Возвращает выбранный файл, только если выбрана ровно одна строка.</summary>
    FileChange? SingleSelectedFile() => FilesList.SelectedItems?.Count == 1 ? FilesList.SelectedItem as FileChange : null;

    /// <summary>Обновляет сообщение в основной строке состояния.</summary>
    void SetStatus(string text) => StatusText.Text = text;

    /// <summary>Копирует обычный текст в системный буфер обмена, если платформа предоставляет такую возможность.</summary>
    async Task CopyText(string text)
    {
        if (Clipboard != null) await Clipboard.SetTextAsync(text);
    }

    /// <summary>Обрабатывает сочетания клавиш окна для обновления, создания коммита и частичного добавления изменений в индекс.</summary>
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
