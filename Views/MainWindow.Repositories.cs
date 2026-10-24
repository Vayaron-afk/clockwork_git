using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using static ClockworkGit.Utils.MenuFactory;

namespace ClockworkGit.Views;

// Repositories list: add, open, remove.
public partial class MainWindow
{
    void AddRepository(string path, bool select)
    {
        var item = _repos.FirstOrDefault(r => SamePath(r.Path, path));
        if (item == null)
        {
            item = new RepoItem(path);
            _repos.Add(item);
            _settings.Repositories.Add(path);
            _settings.Save();
            _ = UpdateRepoSummary(item);
        }
        if (select) ReposList.SelectedItem = item;
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
        _locks = new();
        await RefreshAll();
        _ = RefreshLocks(); // slow network call; badges appear when it returns
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
        menu.Items.Add(Item("Open in Explorer", () => Shell.OpenFolder(item.Path)));
        menu.Items.Add(Item("Remove from list", () =>
        {
            _repos.Remove(item);
            _settings.Repositories.RemoveAll(p => SamePath(p, item.Path));
            _settings.ShownRefs.Remove(item.Path);
            _settings.Save();
        }));
        menu.Open(ReposList);
        e.Handled = true;
    }

    void OnOpenExplorer(object? sender, RoutedEventArgs e)
    {
        if (_repo != null) Shell.OpenFolder(_repo.Path);
    }

    void OnExit(object? sender, RoutedEventArgs e) => Close();
}
