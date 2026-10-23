using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using static ClockworkGit.Utils.MenuFactory;

namespace ClockworkGit.Views;

/// <summary>Log of one file (following renames) and git blame with "blame before this change" navigation.</summary>
public partial class FileHistoryWindow : Window
{
    readonly GitRepo _repo = null!;
    readonly string _path = "";
    List<FileHistoryEntry> _history = new();
    /// <summary>Blame target: revision (null = working tree) and the file's path there.</summary>
    (string? Rev, string Path) _blameAt;
    readonly Stack<(string? Rev, string Path)> _blameBack = new();
    int _entryGen, _blameGen;

    public FileHistoryWindow()
    {
        InitializeComponent();
    }

    /// <param name="blameRev">Revision to blame at first; null blames the working tree file.</param>
    public FileHistoryWindow(GitRepo repo, string path, string? blameRev, bool openBlame) : this()
    {
        _repo = repo;
        _path = path;
        _blameAt = (blameRev, path);
        Title = $"[{Path.GetFileName(repo.Path.TrimEnd('\\', '/'))}/{path}] - File History";

        HistoryList.SelectionChanged += (_, _) => ShowEntry(HistoryList.SelectedItem as FileHistoryEntry);
        BlameList.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is BlameLine line) ShowInHistory(line.Sha);
        };
        BlameList.ContextRequested += OnBlameContext;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        }, RoutingStrategies.Tunnel);
        Opened += async (_, _) =>
        {
            if (openBlame) Tabs.SelectedIndex = 1;
            await Task.WhenAll(LoadHistory(), LoadBlame());
        };
    }

    // ───────────── History ─────────────

    async Task LoadHistory()
    {
        _history = await _repo.FileLog(_path);
        HistoryList.ItemsSource = _history;
        var renames = _history.Count(h => h.IsRename);
        HistoryHeader.Text = _history.Count == 0
            ? "No commits found (the file is not committed yet?)"
            : $"{_history.Count} commit(s) changed {Path.GetFileName(_path)}" + (renames > 0 ? $", renamed {renames} time(s)" : "");
        // Start at the blamed revision when there is one, otherwise at the newest change.
        HistoryList.SelectedItem = _history.FirstOrDefault(h => h.Commit.Hash == _blameAt.Rev) ?? _history.FirstOrDefault();
    }

    async void ShowEntry(FileHistoryEntry? entry)
    {
        var gen = ++_entryGen;
        BlameRevisionButton.IsEnabled = CompareButton.IsEnabled = entry != null;
        if (entry == null)
        {
            DetailHeader.Text = DetailMessage.Text = DiffTitle.Text = "";
            DiffList.ItemsSource = null;
            ImageView.Clear();
            return;
        }

        var c = entry.Commit;
        var lines = new List<string>
        {
            $"Commit {c.Hash[..10]}",
            $"by {c.Author} <{c.Email}>, {c.Date.LocalDateTime:yyyy-MM-dd HH:mm}",
        };
        if (entry.IsRename) lines.Add(entry.RenameNote);
        DetailHeader.Text = string.Join("\n", lines);
        DiffTitle.Text = $"{entry.File.RelPath} ({entry.File.StatusText})";
        BlameRevisionButton.IsEnabled = entry.File.Status != 'D';

        var message = _repo.CommitMessage(c.Hash);
        if (ImageFormats.IsImage(entry.File.RelPath))
        {
            DiffList.IsVisible = false;
            ImageView.IsVisible = true;
            var (oldBytes, newBytes) = await _repo.LoadVersions(c, entry.File, hasHead: true);
            if (gen != _entryGen) return;
            DetailMessage.Text = await message;
            await ImageView.ShowAsync(oldBytes, newBytes, entry.File.RelPath);
            return;
        }

        DiffList.IsVisible = true;
        ImageView.IsVisible = false;
        var diff = await _repo.DiffRows(c, entry.File, hasHead: true);
        var text = await message;
        if (gen != _entryGen) return;
        DetailMessage.Text = text;
        DiffList.ItemsSource = diff.Rows;
    }

    void ShowInHistory(string sha)
    {
        var entry = _history.FirstOrDefault(h => h.Commit.Hash == sha);
        if (entry == null)
        {
            StatusText.Text = sha.All(ch => ch == '0')
                ? "This line is not committed yet."
                : $"Commit {sha[..8]} is not in the loaded history of this file.";
            return;
        }
        Tabs.SelectedIndex = 0;
        HistoryList.SelectedItem = entry;
        HistoryList.ScrollIntoView(entry);
    }

    void OnBlameAtRevision(object? sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not FileHistoryEntry entry) return;
        Tabs.SelectedIndex = 1;
        BlameTo(entry.Commit.Hash, entry.File.RelPath);
    }

    void OnOpenCompare(object? sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is FileHistoryEntry entry)
            new FileCompareWindow(_repo, entry.Commit, entry.File, hasHead: true, svgAsText: false).Show(this);
    }

    // ───────────── Blame ─────────────

    async Task LoadBlame()
    {
        var gen = ++_blameGen;
        var (rev, path) = _blameAt;
        BlameBackButton.IsEnabled = _blameBack.Count > 0;
        BlameTitle.Text = $"{path}  at  {(rev == null ? "working tree" : rev[..8])}";
        if (ImageFormats.IsImage(path) && !ImageFormats.IsTextImage(path))
        {
            BlameList.ItemsSource = null;
            BlameTitle.Text += "  ·  blame is not available for images";
            StatusText.Text = "";
            return;
        }

        StatusText.Text = "Running git blame…";
        try
        {
            var lines = await _repo.Blame(path, rev);
            if (gen != _blameGen) return;
            BlameList.ItemsSource = lines;
            var commits = lines.Where(l => !l.IsUncommitted).Select(l => l.Sha).Distinct().Count();
            var local = lines.Count(l => l.IsUncommitted);
            StatusText.Text = $"{lines.Count} lines from {commits} commit(s)" + (local > 0 ? $", {local} not committed" : "");
        }
        catch (InvalidOperationException ex)
        {
            if (gen != _blameGen) return;
            BlameList.ItemsSource = null;
            StatusText.Text = "git blame failed.";
            await Dialogs.ShowMessage(this, "git blame failed", ex.Message);
        }
    }

    void BlameTo(string? rev, string path)
    {
        if (_blameAt == (rev, path)) return;
        _blameBack.Push(_blameAt);
        _blameAt = (rev, path);
        _ = LoadBlame();
    }

    void OnBlameBack(object? sender, RoutedEventArgs e)
    {
        if (_blameBack.Count == 0) return;
        _blameAt = _blameBack.Pop();
        _ = LoadBlame();
    }

    void OnBlameContext(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not BlameLine line) return;
        if (BlameList.SelectedItems?.Contains(line) != true) BlameList.SelectedItem = line;

        var menu = new ContextMenu();
        menu.Items.Add(Item("Show commit in History", () => ShowInHistory(line.Sha), !line.IsUncommitted));
        menu.Items.Add(Item("Blame before this change", () => BlameTo(line.PreviousSha, line.PreviousPath ?? line.Path),
            line.PreviousSha != null));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Copy commit hash", async () =>
        {
            if (Clipboard != null) await Clipboard.SetTextAsync(line.Sha);
        }, !line.IsUncommitted));
        menu.Items.Add(Item("Copy lines", async () =>
        {
            var text = BlameList.SelectedItems?.OfType<BlameLine>().OrderBy(l => l.LineNo).Select(l => l.Text) ?? [];
            if (Clipboard != null) await Clipboard.SetTextAsync(string.Join(Environment.NewLine, text));
        }));
        menu.Open(BlameList);
        e.Handled = true;
    }
}
