using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ClockworkGit;

/// <summary>Загружает ресурсы Avalonia и создаёт главное окно в настольном жизненном цикле приложения.</summary>
public partial class App : Application
{
    /// <summary>Загружает ресурсы XAML уровня приложения Avalonia.</summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Создаёт главное окно после инициализации Avalonia жизненного цикла приложения.</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
