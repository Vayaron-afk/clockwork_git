using System.Text.Json.Serialization;

namespace ClockworkGit.Services;

/// <summary>Доступные ключи сортировки панели файлов; значения сохраняются в settings.json по имени.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FileSortColumn>))]
public enum FileSortColumn { Path, Name }
