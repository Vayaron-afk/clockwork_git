using Avalonia.Controls;
using static ClockworkGit.Utils.MenuFactory;

namespace ClockworkGit.Views;

// Git LFS file locks: cached list from the server, badges in the Files list, lock/unlock actions.
public partial class MainWindow
{
    /// <summary>Server locks by path; asked on open, after fetch and after lock actions (each request takes ~1–2 s).</summary>
    Dictionary<string, LfsLock> _locks = new();

    async Task RefreshLocks()
    {
        var repo = _repo;
        if (repo == null || repo.LfsRules().Count == 0)
        {
            _locks = new();
            return;
        }
        var list = await repo.LfsLocks();
        if (repo != _repo || list == null) return; // keep the last known state when the server is unreachable
        _locks = list.GroupBy(l => l.Path).ToDictionary(g => g.Key, g => g.First());
        foreach (var f in _ownerFiles) f.Lock = _locks.GetValueOrDefault(f.RelPath);
        UpdateStatusRight();
    }

    /// <summary>"🔒 2 yours, 1 other" for the status bar; empty without locks.</summary>
    string LocksSummary()
    {
        if (_locks.Count == 0) return "";
        var mine = _locks.Values.Count(l => l.IsMine);
        var others = _locks.Count - mine;
        return $"   🔒 {mine} yours" + (others > 0 ? $", {others} other" : "");
    }

    void AddLockItems(MenuItem lfs, FileChange f)
    {
        var path = f.RelPath;
        if (f.Lock == null)
            lfs.Items.Add(Item("Lock file", () => LockAction(() => Exec("lfs", "lock", path)), f.IsLfs));
        else if (f.Lock.IsMine)
            lfs.Items.Add(Item("Unlock file", () => LockAction(() => Exec("lfs", "unlock", path))));
        else
            lfs.Items.Add(Item($"Force unlock (locked by {f.Lock.Owner})…", async () =>
            {
                if (await Dialogs.Confirm(this, "Force unlock",
                        $"{path} is locked by {f.Lock.Owner} {f.Lock.SinceText}. Breaking someone else's lock can make them lose work " +
                        "if they push changes to it. Unlock anyway?", "Force unlock"))
                    await LockAction(() => Exec("lfs", "unlock", "--force", path));
            }));
        lfs.Items.Add(Item("Refresh locks", async () =>
        {
            SetStatus("Asking the LFS server for locks…");
            await RefreshLocks();
            SetStatus(_locks.Count == 0 ? "No LFS locks" : $"{_locks.Count} LFS lock(s)");
        }));
    }

    /// <summary>Runs a lock/unlock command, then reloads the lock list before the usual refresh.</summary>
    Task LockAction(Func<Task<bool>> command) => Do(async () =>
    {
        if (await command()) await RefreshLocks();
    });
}
