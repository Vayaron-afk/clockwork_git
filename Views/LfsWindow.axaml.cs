using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ClockworkGit.Views;

/// <summary>Обзор Git LFS: файлы HEAD, правила корневого .gitattributes, блокировки, размер кэша и конвертация без переписывания истории.</summary>
public partial class LfsWindow : Window
{
    readonly GitRepo _repo = null!;
    readonly Func<Task> _changed = () => Task.CompletedTask;
    List<LfsFile> _files = new();
    List<string> _notInLfs = new();

    /// <summary>Инициализирует элементы XAML окна управления LFS.</summary>
    public LfsWindow()
    {
        InitializeComponent();
    }

    /// <summary>Создаёт окно управления LFS для репозитория и задаёт обратный вызов для обновления родительского представления.</summary>
    /// <param name="repo">Репозиторий, данные и объекты LFS которого отображаются.</param>
    /// <param name="changed">Вызывается после загрузки выбранных файлов, снятия блокировок и действий, изменяющих индекс.</param>
    public LfsWindow(GitRepo repo, Func<Task> changed) : this()
    {
        _repo = repo;
        _changed = changed;
        Title = $"Git LFS - {Path.GetFileName(repo.Path.TrimEnd('\\', '/'))}";

        FilesFilter.TextChanged += (_, _) => ApplyFilesFilter();
        OnlyMissing.IsCheckedChanged += (_, _) => ApplyFilesFilter();
        LfsFilesList.ContextRequested += OnFilesContext;
        Opened += async (_, _) =>
        {
            await LoadFiles();
            await RecalcCache();
            await LoadLocks();
        };
    }

    // ───────────── Locks ─────────────

    /// <summary>Загружает блокировки сервера и сообщает о состоянии подключения и владения на вкладке блокировок.</summary>
    async Task LoadLocks()
    {
        LocksStatus.Text = "Asking the LFS server…";
        var locks = await _repo.LfsLocks();
        LocksList.ItemsSource = locks?.OrderBy(l => l.Path, StringComparer.CurrentCultureIgnoreCase).ToList();
        LocksStatus.Text = locks == null ? "The LFS server did not answer (offline, signed out, or locking not supported)."
            : locks.Count == 0 ? "No locks."
            : $"{locks.Count} lock(s), {locks.Count(l => l.IsMine)} yours.";
    }

    /// <summary>Обновляет список блокировок сервера по запросу.</summary>
    async void OnRefreshLocks(object? sender, RoutedEventArgs e) => await LoadLocks();

    /// <summary>Снимает блокировки выбранных файлов; если какие-либо файлы принадлежат другому пользователю, сначала запрашивает подтверждение.</summary>
    async void OnUnlock(object? sender, RoutedEventArgs e)
    {
        var selected = LocksList.SelectedItems?.OfType<LfsLock>().ToList() ?? new();
        if (selected.Count == 0) return;
        var others = selected.Where(l => !l.IsMine).ToList();
        if (others.Count > 0 && !await Dialogs.Confirm(this, "Force unlock",
                $"{others.Count} of the selected lock(s) belong to someone else ({string.Join(", ", others.Select(l => l.Owner).Distinct())}). " +
                "Breaking them can make those people lose work. Unlock anyway?", "Force unlock"))
            return;

        UnlockButton.IsEnabled = false;
        try
        {
            foreach (var l in selected)
            {
                // By id: works even when the file is gone from the working tree.
                var r = l.IsMine ? await _repo.Run("lfs", "unlock", "--id", l.Id) : await _repo.Run("lfs", "unlock", "--force", "--id", l.Id);
                if (!r.Ok)
                {
                    await Dialogs.ShowMessage(this, "git lfs unlock failed", $"{l.Path}\n\n{r.Message}");
                    break;
                }
            }
        }
        finally
        {
            UnlockButton.IsEnabled = true;
        }
        await LoadLocks();
        await _changed();
    }

    // ───────────── Files ─────────────

    /// <summary>Загружает файлы Git LFS, применяет текущий фильтр и обновляет сведения о правилах.</summary>
    async Task LoadFiles()
    {
        FilesSummary.Text = "Loading…";
        _files = await _repo.LfsFiles();
        ApplyFilesFilter();
        LoadRules();
    }

    /// <summary>Фильтрует и сортирует LFS-файлы и обновляет итоговые значения на вкладке файлов.</summary>
    void ApplyFilesFilter()
    {
        var filter = FilesFilter.Text?.Trim() ?? "";
        IEnumerable<LfsFile> rows = _files;
        if (filter.Length > 0) rows = rows.Where(f => f.Path.Contains(filter, StringComparison.OrdinalIgnoreCase));
        if (OnlyMissing.IsChecked == true) rows = rows.Where(f => !f.Downloaded);
        var list = rows.OrderBy(f => f.Path, StringComparer.CurrentCultureIgnoreCase).ToList();
        LfsFilesList.ItemsSource = list.Select(f => new LfsFileRow(f)).ToList();

        var missing = _files.Count(f => !f.Downloaded);
        FilesSummary.Text = $"{list.Count} of {_files.Count} files · {Format.Size(list.Sum(f => f.Size))} shown · " +
                            $"total {Format.Size(_files.Sum(f => f.Size))} · {missing} not downloaded";
    }

    /// <summary>Повторно загружает список LFS-файлов.</summary>
    async void OnReloadFiles(object? sender, RoutedEventArgs e) => await LoadFiles();

    /// <summary>Создаёт действия загрузки, открытия в проводнике и копирования для выбранных LFS-файлов.</summary>
    void OnFilesContext(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is LfsFileRow clicked && LfsFilesList.SelectedItems?.Contains(clicked) != true)
            LfsFilesList.SelectedItem = clicked;
        var rows = LfsFilesList.SelectedItems?.OfType<LfsFileRow>().ToList() ?? new();
        if (rows.Count == 0) return;

        var menu = new ContextMenu();
        var missing = rows.Where(r => !r.File.Downloaded).ToList();
        if (missing.Count > 0)
            menu.Items.Add(MenuFactory.Item($"Download {missing.Count} file(s) (git lfs pull)", async () =>
            {
                FilesSummary.Text = "Downloading…";
                var r = await _repo.Run("lfs", "pull", "--include=" + string.Join(',', missing.Select(m => m.Path)), "--exclude=");
                if (!r.Ok) await Dialogs.ShowMessage(this, "git lfs pull failed", r.Message);
                await LoadFiles();
                await _changed();
            }));
        menu.Items.Add(MenuFactory.Item("Show in Explorer", () => Shell.ShowInExplorer(_repo.FullPath(rows[0].Path))));
        menu.Items.Add(MenuFactory.Item("Copy path", async () =>
        {
            if (Clipboard != null) await Clipboard.SetTextAsync(string.Join("\n", rows.Select(r => r.Path)));
        }));
        menu.Open(LfsFilesList);
        e.Handled = true;
    }

    // ───────────── Rules ─────────────

    /// <summary>Показывает правила корневого .gitattributes и приблизительное число соответствующих им LFS-файлов HEAD.</summary>
    void LoadRules()
    {
        RulesList.ItemsSource = _repo.LfsRules()
            .Select(r =>
            {
                var count = _files.Count(f => GitRepo.AttrPatternMatches(r.Pattern, f.Path));
                return new LfsRuleRow(r.Pattern, r.Line, count == 0 ? "—" : count.ToString());
            })
            .ToList();
    }

    /// <summary>Добавляет шаблон, введённый на вкладке правил, и обновляет данные после успеха.</summary>
    async void OnAddRule(object? sender, RoutedEventArgs e)
    {
        var pattern = NewRule.Text?.Trim() ?? "";
        if (pattern.Length == 0) return;
        var literal = pattern.IndexOfAny(['*', '?', '[']) < 0;
        if (await LfsActions.Track(this, _repo, pattern, literal))
        {
            NewRule.Text = "";
            await AfterChange();
        }
    }

    /// <summary>Удаляет выбранное правило отслеживания и обновляет данные после успеха.</summary>
    async void OnRemoveRule(object? sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not LfsRuleRow rule) return;
        if (await LfsActions.Untrack(this, _repo, rule.Pattern)) await AfterChange();
    }

    /// <summary>Уведомляет родительское окно об изменении состояния репозитория и заново загружает LFS-файлы.</summary>
    async Task AfterChange()
    {
        await _changed();
        await LoadFiles();
    }

    // ───────────── Maintenance ─────────────

    /// <summary>Вычисляет размер локального кэша LFS вне UI-потока и показывает его расположение.</summary>
    async Task RecalcCache()
    {
        CacheText.Text = "Calculating…";
        var bytes = await Task.Run(() => _repo.LfsCacheBytes());
        CacheText.Text = $"{Format.Size(bytes)} in {Path.Combine(_repo.GitDir, "lfs", "objects")}";
    }

    /// <summary>Повторно вычисляет размер локального кэша LFS по запросу.</summary>
    async void OnRecalcCache(object? sender, RoutedEventArgs e) => await RecalcCache();

    /// <summary>Находит отслеживаемые файлы, соответствующие правилам LFS, но всё ещё хранящиеся как обычные объекты Git.</summary>
    async void OnAnalyze(object? sender, RoutedEventArgs e)
    {
        AnalyzeButton.IsEnabled = false;
        MigrateText.Text = "Analyzing…";
        try
        {
            _notInLfs = await _repo.NotYetInLfs();
            var byExt = _notInLfs
                .GroupBy(p => Path.GetExtension(p).ToLowerInvariant())
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {g.Key}");
            MigrateText.Text = _notInLfs.Count == 0
                ? "Everything that matches the rules is already in LFS."
                : $"{_notInLfs.Count} files: {string.Join(", ", byExt)}";
            ConvertButton.IsEnabled = _notInLfs.Count > 0;
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
        }
    }

    /// <summary>Запрашивает подтверждение и добавляет проанализированные файлы в индекс как указатели LFS, используя их содержимое из рабочей копии.</summary>
    async void OnConvert(object? sender, RoutedEventArgs e)
    {
        if (_notInLfs.Count == 0) return;
        if (!await Dialogs.Confirm(this, "Convert to LFS",
                $"Stage {_notInLfs.Count} files as LFS pointers? Their current working-tree versions are staged, " +
                "so local edits to these files get staged too. Commit them in the main window afterwards.", "Stage"))
            return;
        ConvertButton.IsEnabled = false;
        MaintStatus.Text = "Staging…";
        if (await LfsActions.ConvertExisting(this, _repo, _notInLfs))
        {
            MaintStatus.Text = $"Staged {_notInLfs.Count} files. Commit them in the main window; Push uploads the objects to LFS.";
            _notInLfs = new();
            MigrateText.Text = "Converted files are staged. Analyze again after committing.";
        }
        else MaintStatus.Text = "Conversion failed.";
        await _changed();
    }

    /// <summary>Выполняет lfs fetch --all для объектов истории всех локально известных ссылок с сервера по умолчанию; рабочие файлы не обновляются.</summary>
    async void OnFetchAll(object? sender, RoutedEventArgs e)
    {
        FetchAllButton.IsEnabled = false;
        MaintStatus.Text = "Running git lfs fetch --all… this can take a while.";
        try
        {
            var r = await _repo.Run("lfs", "fetch", "--all");
            MaintStatus.Text = r.Ok ? $"Done. {r.LastLine}" : "git lfs fetch --all failed.";
            if (!r.Ok) await Dialogs.ShowMessage(this, "git lfs fetch failed", r.Message);
            await RecalcCache();
            await LoadFiles();
        }
        finally
        {
            FetchAllButton.IsEnabled = true;
        }
    }
}
