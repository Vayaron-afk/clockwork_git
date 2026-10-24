using Avalonia.Interactivity;

namespace ClockworkGit.Views;

// Refreshes repository data and updates the graph, status bar, and operation banner.
public partial class MainWindow
{
    /// <summary>Обновляет ссылки, stash, состояние рабочей копии, данные графа и корневые каталоги проектов открытого репозитория.</summary>
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
        var tProjects = repo.ProjectRoots();
        await Task.WhenAll(tRefs, tStashes, tStatus, tBranch, tHead, tRemotes, tRecyclable, tProjects);
        if (gen != _refreshGen || repo != _repo) return;

        _refs = tRefs.Result;
        _workFiles = tStatus.Result;
        _currentBranch = tBranch.Result;
        _headHash = tHead.Result;
        _remotes = tRemotes.Result;
        _recyclable = tRecyclable.Result;
        _projectRoots = tProjects.Result;

        BuildBranchTree(tStashes.Result);
        var repoItem = _repos.FirstOrDefault(r => SamePath(r.Path, repo.Path));
        if (repoItem != null) _ = UpdateRepoSummary(repoItem);
        UpdateStateBanner();
        UpdateStatusRight();
        await ReloadLog(selHash, selFile);
    }

    /// <summary>Обрабатывает команду обновления и заново загружает данные репозитория.</summary>
    async void OnRefresh(object? sender, RoutedEventArgs e) => await RefreshAll();

    /// <summary>Возвращает сохранённые ссылки графа; при первом обращении инициализирует их значениями HEAD и его upstream.</summary>
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

    /// <summary>Заново загружает коммиты графа для выбранных ссылок и восстанавливает выбранные коммит и файл.</summary>
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

    /// <summary>Фильтрует граф по тексту; линии скрываются, поскольку после фильтрации строки уже не соседствуют.</summary>
    void ApplyFilter(string? selectHash)
    {
        var filter = FilterBox.Text?.Trim() ?? "";
        var filtering = filter.Length > 0;
        foreach (var c in _allCommits) c.ShownGraph = filtering ? null : c.Graph;
        var rows = filtering ? _allCommits.Where(c => !c.IsWorkingTree && c.Matches(filter)).ToList() : _allCommits;
        GraphList.ItemsSource = rows;
        GraphList.SelectedItem = rows.FirstOrDefault(c => c.Hash == selectHash) ?? rows.FirstOrDefault();
    }

    /// <summary>Обновляет баннер с описанием приостановленной операции Git в репозитории.</summary>
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

    /// <summary>Отображает текущую ветку или отсоединённый HEAD, состояние upstream и количество блокировок LFS.</summary>
    void UpdateStatusRight()
    {
        var head = _refs.FirstOrDefault(r => r.IsHead);
        var text = _currentBranch ?? $"detached at {_headHash?[..8]}";
        if (head is { UpstreamShort.Length: > 0 })
            text += $"  →  {head.UpstreamShort}  {(head.TrackShort.Length == 0 ? "in sync" : head.TrackShort)}";
        StatusRight.Text = text + LocksSummary();
    }
}
