using Avalonia;

namespace ClockworkGit;

/// <summary>Настраивает и запускает настольное приложение ClockworkGit.</summary>
internal static class Program
{
    /// <summary>Запускает настольное приложение Avalonia с указанными аргументами командной строки.</summary>
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Настраивает определение платформы Avalonia и трассировку событий.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
