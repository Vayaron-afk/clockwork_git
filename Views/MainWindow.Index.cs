using Avalonia.Interactivity;

namespace ClockworkGit.Views;

public partial class MainWindow
{
    /// <summary>Показывает конкретную блокировку и удаляет её только после подтверждения и повторных проверок.</summary>
    async void OnUnlockIndex(object? sender, RoutedEventArgs e) => await Do(async () =>
    {
        var repo = _repo!;
        var info = await repo.InspectIndexLockAsync();
        if (info == null)
        {
            await Dialogs.ShowMessage(this, "Unlock index", "There is no index.lock for this repository.");
            return;
        }
        if (!await Dialogs.Confirm(this, "Remove leftover index.lock?",
                $"{info.FullPath}\n\nLast modified: {info.LastWriteUtc.ToLocalTime():g}\nSize: {info.Length:N0} bytes\n\n"
                + "Use this only after Git commands in other clients have finished. "
                + "Only this lock file will be removed; the index and working files are preserved.", "Remove lock")) return;
        await repo.RemoveIndexLockAsync(info);
        SetStatus("Leftover index.lock removed; index and working files preserved.");
    });
}
