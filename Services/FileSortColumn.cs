using System.Text.Json.Serialization;

namespace ClockworkGit.Services;

/// <summary>Sort column of the Files panel; stored by name in settings.json.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FileSortColumn>))]
public enum FileSortColumn { Path, Name }
