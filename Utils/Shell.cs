using System.Diagnostics;

namespace ClockworkGit.Utils;

/// <summary>Opening files and folders with Windows Explorer / the default application.</summary>
public static class Shell
{
    public static void OpenFile(string path)
    {
        if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });

    /// <summary>Opens Explorer with the file selected; falls back to its folder when the file is gone.</summary>
    public static void ShowInExplorer(string path)
    {
        if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));
        else OpenFolder(Path.GetDirectoryName(path)!);
    }
}
