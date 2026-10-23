using System.Text.Json;

namespace ClockworkGit;

public sealed class Settings
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClockworkGit", "settings.json");

    public List<string> Repositories { get; set; } = new();
    public string? LastRepository { get; set; }
    /// <summary>Per repository: refs whose history is shown in the graph.</summary>
    public Dictionary<string, List<string>> ShownRefs { get; set; } = new();

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // Corrupt settings: start fresh.
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
