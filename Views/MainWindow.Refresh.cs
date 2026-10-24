using Avalonia.Interactivity;

namespace ClockworkGit.Views;

// Reloading refs, status and the commit graph; status bar and state banner.
public partial class MainWindow
{
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
        var tRecyclable = repo.RecyclableCommits();
        await Task.WhenAll(tRefs, tStashes, tStatus, tBranch, tHead, tRemotes, tRecyclable);
        if (gen != _refreshGen || repo != _repo) return;

        _refs = tRefs.Result;
        _workFiles = tStatus.Result;
        _currentBranch = tBranch.Result;
        _headHash = tHead.Result;
        _remotes = tRemotes.Result;
        _recyclable = tRecyclable.Result;

        BuildBranchTree(tStashes.Result);
        var repoItem = _repos.FirstOrDefault(r => SamePath(r.Path, repo.Path));
        if (repoItem != null) _ = UpdateRepoSummary(repoItem);
        UpdateStateBanner();
        UpdateStatusRight();
        await ReloadLog(selHash, selFile);
    }

    async void OnRefresh(object? sender, RoutedEventArgs e) => await RefreshAll();

    /// <summary>Refs whose history the graph shows; first time: HEAD and its upstream.</summary>
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
        // Refs that no longer exist are skipped; checked recyclable commits are passed by hash.
        var refs = ShownRefs()
            .Where(r => r == "HEAD" || _refs.Any(x => x.FullName == r) || _recyclable.Any(c => c.Commit.Hash == r))
            .ToList();
        if (refs.Count == 0) refs.Add("HEAD");

        var commits = _headHash == null && refs.All(r => r == "HEAD")
            ? new List<CommitInfo>()
            : await repo.Log(refs, LogLimit, _remotes);
        if (gen != _logGen || repo != _repo) return;

        var recyclable = _recyclable.Select(c => c.Commit.Hash).ToHashSet();
        foreach (var c in commits.Where(c => recyclable.Contains(c.Hash)))
            c.Refs.Add(new RefBadge("♻ recyclable", RefKind.Recyclable));

        var headLoaded = _headHash != null && commits.Any(c => c.Hash == _headHash);
        commits.Insert(0, CommitInfo.WorkingTree(headLoaded ? _headHash : null, _workFiles.Count));
        GraphBuilder.Build(commits);
        _allCommits = commits;
        _pendingFile = selectFile;
        ApplyFilter(selectHash);
    }

    /// <summary>Filters the graph by text; lanes are hidden while filtering since rows are no longer adjacent.</summary>
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
        if (head is { UpstreamShort.Length: > 0 })
            text += $"  →  {head.UpstreamShort}  {(head.TrackShort.Length == 0 ? "in sync" : head.TrackShort)}";
        StatusRight.Text = text + LocksSummary();
    }
}
