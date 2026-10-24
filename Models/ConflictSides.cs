namespace ClockworkGit.Models;

/// <summary>Три стадии конфликтующего файла в индексе: общий предок, наша и их версии; отсутствующая сторона равна null.</summary>
public sealed record ConflictSides(byte[]? Base, byte[]? Ours, byte[]? Theirs)
{
    /// <summary>Эвристика двоичного файла: в первых 8000 байтах хотя бы одной стадии есть нулевой байт.</summary>
    public bool IsBinary => new[] { Base, Ours, Theirs }.Any(b => b != null && b.AsSpan(0, Math.Min(b.Length, 8000)).Contains((byte)0));

    /// <summary>Обе версии Ours и Theirs доступны. При «изменение/удаление» одной нет; при «добавление/добавление» может отсутствовать только Base.</summary>
    public bool HasBothSides => Ours != null && Theirs != null;
}
