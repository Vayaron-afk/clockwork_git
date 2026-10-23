using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ClockworkGit.Views;

/// <summary>Git LFS overview: ls-files, rules from .gitattributes, cache and migration tools.</summary>
public partial class LfsWindow : Window
{
    readonly GitRepo _repo = null!;
    readonly Func<Task> _changed = () => Task.CompletedTask;
    List<LfsFile> _files = new();
    List<string> _notInLfs = new();

    public LfsWindow()
    {
        InitializeComponent();
    }

    /// <param name="changed">Called after an action that changed the index, so the main window refreshes.</param>
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
        };
    }

    // ───────────── Files ─────────────

    async Task LoadFiles()
    {
        FilesSummary.Text = "Loading…";
        _files = await _repo.LfsFiles();
        ApplyFilesFilter();
        LoadRules();
    }

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

    async void OnReloadFiles(object? sender, RoutedEventArgs e) => await LoadFiles();

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

    async void OnRemoveRule(object? sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not LfsRuleRow rule) return;
        if (await LfsActions.Untrack(this, _repo, rule.Pattern)) await AfterChange();
    }

    async Task AfterChange()
    {
        await _changed();
        await LoadFiles();
    }

    // ───────────── Maintenance ─────────────

    async Task RecalcCache()
    {
        CacheText.Text = "Calculating…";
        var bytes = await Task.Run(() => _repo.LfsCacheBytes());
        CacheText.Text = $"{Format.Size(bytes)} in {Path.Combine(_repo.GitDir, "lfs", "objects")}";
    }

    async void OnRecalcCache(object? sender, RoutedEventArgs e) => await RecalcCache();

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
