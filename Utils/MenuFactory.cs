using Avalonia.Controls;

namespace ClockworkGit.Utils;

public static class MenuFactory
{
    /// <summary>A context-menu item running an async action.</summary>
    public static MenuItem Item(string header, Func<Task> action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += async (_, _) => await action();
        return item;
    }

    public static MenuItem Item(string header, Action action, bool enabled = true) =>
        Item(header, () => { action(); return Task.CompletedTask; }, enabled);
}
