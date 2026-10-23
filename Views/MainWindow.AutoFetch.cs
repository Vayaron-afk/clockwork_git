using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ClockworkGit.Views;

// Status of all repositories in the list and background fetch.
public partial class MainWindow
{
    /// <summary>Background fetches must never pop up a login window; a failure is shown in the tooltip instead.</summary>
    static readonly Dictionary<string, string> BackgroundEnv = new() { ["GCM_INTERACTIVE"] = "never" };

    DispatcherTimer? _fetchTimer;
    bool _fetchingRepos;

    void StartAutoFetch()
    {
        UpdateAutoFetchMenu();
        _fetchTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _fetchTimer.Tick += async (_, _) => await AutoFetchTick();
        _fetchTimer.Start();
    }

    async Task AutoFetchTick()
    {
        var minutes = _settings.AutoFetchMinutes;
        if (minutes <= 0) return;
        var due = _repos.Where(r => r.LastFetch == null || DateTime.Now - r.LastFetch >= TimeSpan.FromMinutes(minutes)).ToList();
        await FetchRepos(due);
    }

    /// <summary>Fetches one repository at a time, so a slow remote does not pile up processes.</summary>
    async Task FetchRepos(IReadOnlyList<RepoItem> repos)
    {
        if (_fetchingRepos) return;
        _fetchingRepos = true;
        try
        {
            foreach (var item in repos) await FetchRepo(item);
        }
        finally
        {
            _fetchingRepos = false;
        }
    }

    async Task FetchRepo(RepoItem item)
    {
        if (!Directory.Exists(item.Path)) return;
        var isCurrent = _repo != null && SamePath(_repo.Path, item.Path);
        if (isCurrent && _busy) return; // an operation holds the repository; the next tick retries

        var behindBefore = item.Summary?.Behind ?? 0;
        item.Fetching = true;
        var r = await GitRunner.RunWithEnvAsync(item.Path, BackgroundEnv, "fetch", "--all", "--prune", "--quiet");
        item.Fetching = false;
        item.LastFetch = DateTime.Now;
        item.FetchError = r.Ok ? null : r.LastLine ?? "git fetch failed";
        item.Summary = await RepoSummary.Read(item.Path);

        if (item.Summary is { Behind: var behind } summary && behind > behindBefore)
            SetStatus($"{item.Name}: {behind} new commit(s) on {summary.Upstream}");
        if (isCurrent && !_busy) await RefreshAll();
    }

    async Task UpdateRepoSummaries()
    {
        foreach (var item in _repos.ToList()) await UpdateRepoSummary(item);
    }

    async Task UpdateRepoSummary(RepoItem item)
    {
        item.Summary = await RepoSummary.Read(item.Path);
        if (item.Summary == null) item.Branch = Directory.Exists(item.Path) ? "not a repository" : "missing";
    }

    async void OnFetchAllRepos(object? sender, RoutedEventArgs e)
    {
        SetStatus("Fetching all repositories…");
        await FetchRepos(_repos.ToList());
        var failed = _repos.Count(r => r.FetchError != null);
        var incoming = _repos.Where(r => r.Summary is { Behind: > 0 }).Select(r => $"{r.Name} ↓{r.Summary!.Behind}").ToList();
        SetStatus((failed == 0 ? "Fetched all repositories" : $"Fetched; {failed} repositor{(failed == 1 ? "y" : "ies")} failed (see tooltips)")
                  + (incoming.Count > 0 ? " · incoming: " + string.Join(", ", incoming) : " · nothing new"));
    }

    void OnAutoFetchInterval(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !int.TryParse(tag, out var minutes)) return;
        _settings.AutoFetchMinutes = minutes;
        _settings.Save();
        UpdateAutoFetchMenu();
        SetStatus(minutes == 0 ? "Auto-fetch is off" : $"Auto-fetch every {minutes} minutes");
    }

    void UpdateAutoFetchMenu()
    {
        foreach (var item in AutoFetchMenu.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag as string == _settings.AutoFetchMinutes.ToString();
    }
}
