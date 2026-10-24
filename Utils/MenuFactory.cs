using Avalonia.Controls;

namespace ClockworkGit.Utils;

/// <summary>Создаёт единообразно настроенные пункты меню для синхронных и асинхронных действий.</summary>
public static class MenuFactory
{
    /// <summary>Пункт контекстного меню, запускающий асинхронное действие.</summary>
    public static MenuItem Item(string header, Func<Task> action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += async (_, _) => await action();
        return item;
    }

    /// <summary>Создаёт пункт меню, который выполняет синхронное действие при нажатии.</summary>
    public static MenuItem Item(string header, Action action, bool enabled = true) =>
        Item(header, () => { action(); return Task.CompletedTask; }, enabled);
}
