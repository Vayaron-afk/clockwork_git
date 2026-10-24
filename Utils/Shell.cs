using System.Diagnostics;

namespace ClockworkGit.Utils;

/// <summary>Открытие файлов и папок в проводнике Windows или приложении по умолчанию.</summary>
public static class Shell
{
    /// <summary>Открывает существующий файл в зарегистрированном приложении Windows.</summary>
    public static void OpenFile(string path)
    {
        if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>Открывает указанную папку в проводнике Windows.</summary>
    public static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });

    /// <summary>Открывает проводник с выбранным файлом; если файл отсутствует, открывает содержащую его папку.</summary>
    public static void ShowInExplorer(string path)
    {
        if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));
        else OpenFolder(Path.GetDirectoryName(path)!);
    }
}
