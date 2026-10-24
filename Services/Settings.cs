using System.Text.Json;

namespace ClockworkGit.Services;

/// <summary>Пользовательские настройки в %APPDATA%\ClockworkGit\settings.json.</summary>
public sealed class Settings
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClockworkGit", "settings.json");

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<string> Repositories { get; set; } = new();
    public string? LastRepository { get; set; }
    /// <summary>Для каждого репозитория: ссылки, история которых отображается на графе.</summary>
    public Dictionary<string, List<string>> ShownRefs { get; set; } = new();
    public FileSortColumn FilesSort { get; set; } = FileSortColumn.Path;
    public bool FilesSortDescending { get; set; }
    /// <summary>Репозитории, в которых список файлов сгруппирован по проектам (сайты, службы).</summary>
    public List<string> FilesByProject { get; set; } = new();
    /// <summary>Интервал фоновых попыток fetch в минутах; значение 0 отключает функцию.</summary>
    public int AutoFetchMinutes { get; set; } = 10;

    /// <summary>Загружает настройки из профиля пользователя и возвращает значения по умолчанию, если файл отсутствует или некорректен.</summary>
    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? new Settings();
        }
        catch
        {
            // Corrupt settings: start fresh.
        }
        return new Settings();
    }

    /// <summary>При необходимости создаёт каталог настроек и записывает текущие параметры в форматированном JSON.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
