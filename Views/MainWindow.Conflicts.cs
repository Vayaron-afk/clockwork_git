using Avalonia.Interactivity;

namespace ClockworkGit.Views;

// Opening the three-way conflict editor.
public partial class MainWindow
{
    /// <summary>What "ours" and "theirs" mean depends on the paused operation; in a rebase they are swapped.</summary>
    (string Ours, string Theirs) ConflictLabels() => (_repo?.GetState() ?? RepoState.Clean) switch
    {
        RepoState.Rebasing => ("Ours: branch being rebased onto", "Theirs: your commit being replayed"),
        RepoState.Merging => ($"Ours: {_currentBranch ?? "HEAD"}", "Theirs: merged branch"),
        RepoState.CherryPicking => ($"Ours: {_currentBranch ?? "HEAD"}", "Theirs: picked commit"),
        RepoState.Reverting => ($"Ours: {_currentBranch ?? "HEAD"}", "Theirs: revert of the commit"),
        _ => ($"Ours: {_currentBranch ?? "HEAD"}", "Theirs: incoming (stash or patch)"),
    };

    async Task OpenConflictEditor(FileChange f)
    {
        if (_repo == null || !f.IsConflict) return;
        var sides = await _repo.LoadConflict(f.RelPath);
        var (ours, theirs) = ConflictLabels();
        var saved = await new ConflictEditorWindow(_repo, f.RelPath, sides, ours, theirs).ShowDialog<bool?>(this);
        if (saved == true)
        {
            _pendingFile = f.RelPath;
            await RefreshAll();
            var left = _workFiles.Count(w => w.IsConflict);
            SetStatus(left == 0 ? "All conflicts resolved: press Continue in the banner" : $"{f.Name} saved; {left} conflicted file(s) left");
        }
    }

    async void OnResolveConflicts(object? sender, RoutedEventArgs e)
    {
        var first = _workFiles.FirstOrDefault(f => f.IsConflict);
        if (first == null)
        {
            await Dialogs.ShowMessage(this, "Resolve conflicts", "There are no conflicted files. Stage the result and press Continue.");
            return;
        }
        await OpenConflictEditor(first);
    }
}
