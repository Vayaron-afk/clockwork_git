using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ClockworkGit.Views;

// Commit details, the Files list (sorting, LFS filter) and the bottom diff panel.
public partial class MainWindow
{
    /// <summary>Selected commits, newest first (graph order, not click order).</summary>
    List<CommitInfo> SelectedCommits()
    {
        var selected = GraphList.SelectedItems?.OfType<CommitInfo>().ToHashSet() ?? new HashSet<CommitInfo>();
        return _allCommits.Where(selected.Contains).ToList();
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
            _ownerFiles = new();
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

        var lfs = await repo.LfsAttributed(files.Select(f => f.RelPath).ToList(), c.IsWorkingTree ? null : c.Hash);
        if (gen != _detailsGen) return;
        foreach (var f in files) f.IsLfs = lfs.Contains(f.RelPath);

        _filesOwner = c;
        _ownerFiles = files;
        var shown = ShownFiles();
        FilesList.ItemsSource = shown;
        var pick = shown.FirstOrDefault(f => f.RelPath == _pendingFile) ?? shown.FirstOrDefault();
        _pendingFile = null;
        FilesList.SelectedItem = pick;
        if (pick == null) ShowDiff(null);
    }

    // ───────────── Files list: sort and filter ─────────────

    List<FileChange> SortFiles(IEnumerable<FileChange> files)
    {
        Func<FileChange, string> primary = _settings.FilesSort == FileSortColumn.Name ? f => f.Name : f => f.RelPath;
        var cmp = StringComparer.CurrentCultureIgnoreCase;
        var ordered = _settings.FilesSortDescending
            ? files.OrderByDescending(primary, cmp)
            : files.OrderBy(primary, cmp);
        return ordered.ThenBy(f => f.RelPath, cmp).ToList();
    }

    void UpdateSortHeaders()
    {
        var arrow = _settings.FilesSortDescending ? " ▼" : " ▲";
        SortByName.Content = "Name" + (_settings.FilesSort == FileSortColumn.Name ? arrow : "");
        SortByPath.Content = "Path" + (_settings.FilesSort == FileSortColumn.Path ? arrow : "");
    }

    void OnSortFiles(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<FileSortColumn>(tag, out var column)) return;
        if (_settings.FilesSort == column) _settings.FilesSortDescending = !_settings.FilesSortDescending;
        else
        {
            _settings.FilesSort = column;
            _settings.FilesSortDescending = false;
        }
        _settings.Save();
        UpdateSortHeaders();
        RebindFiles();
    }

    List<FileChange> ShownFiles() =>
        SortFiles(LfsOnly.IsChecked == true ? _ownerFiles.Where(f => f.IsLfs) : _ownerFiles);

    /// <summary>Re-applies sort and filter, keeping the selection (and its diff) when it is still visible.</summary>
    void RebindFiles()
    {
        if (FilesList.ItemsSource == null) return;
        var selected = FilesList.SelectedItems?.OfType<FileChange>().ToList() ?? new();
        var shown = ShownFiles();
        var kept = selected.Where(shown.Contains).ToList();
        _resortingFiles = true;
        try
        {
            FilesList.ItemsSource = shown;
            foreach (var f in kept) FilesList.SelectedItems?.Add(f);
        }
        finally
        {
            _resortingFiles = false;
        }
        if (kept.Count > 0) FilesList.ScrollIntoView(kept[0]);
        else if (selected.Count > 0 && shown.Count > 0) FilesList.SelectedItem = shown[0]; // selection got filtered out
        else if (selected.Count > 0) ShowDiff(null);
        if (kept.Count > 0 && kept.Count != selected.Count) ShowDiff(kept.Count == 1 ? kept[0] : null);
    }

    // ───────────── Diff panel ─────────────

    async void ShowDiff(FileChange? f)
    {
        var gen = ++_diffGen;
        var repo = _repo;
        var owner = _filesOwner;
        OpenCompareButton.IsEnabled = f != null && owner != null;
        SvgAsText.IsVisible = f != null && ImageFormats.IsTextImage(f.RelPath);
        if (f == null || repo == null || owner == null)
        {
            DiffTitle.Text = "Changes";
            DiffList.ItemsSource = null;
            DiffList.IsVisible = true;
            ImageView.IsVisible = false;
            ImageView.Clear();
            return;
        }

        DiffTitle.Text = $"Changes of {f.RelPath} ({f.StatusText})";
        if (ImageFormats.IsImage(f.RelPath) && !(SvgAsText.IsVisible && SvgAsText.IsChecked == true))
        {
            DiffList.IsVisible = false;
            ImageView.IsVisible = true;
            var (oldBytes, newBytes) = await repo.LoadVersions(owner, f, _headHash != null);
            if (gen != _diffGen) return;
            await ImageView.ShowAsync(oldBytes, newBytes, f.RelPath);
            return;
        }

        DiffList.IsVisible = true;
        ImageView.IsVisible = false;
        var rows = await repo.DiffRows(owner, f, _headHash != null);
        if (gen != _diffGen) return;
        DiffList.ItemsSource = rows;
    }

    void OnOpenCompare(object? sender, RoutedEventArgs e) => OpenCompare(SingleSelectedFile());

    void OpenCompare(FileChange? f)
    {
        if (f == null || _repo == null || _filesOwner == null) return;
        new FileCompareWindow(_repo, _filesOwner, f, _headHash != null, SvgAsText.IsChecked == true).Show(this);
    }

    void OnOpenLfs(object? sender, RoutedEventArgs e) => OpenLfs();

    void OpenLfs()
    {
        if (_repo == null) return;
        new LfsWindow(_repo, RefreshAll).Show(this);
    }
}
