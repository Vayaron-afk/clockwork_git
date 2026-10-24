using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ClockworkGit.Unity;

/// <summary>
/// GUID ресурса Unity → путь к ресурсу, полученный из файлов .meta рабочей копии.
/// Дополняет строки diff файлов .mat/.prefab/.unity/.asset именами ресурсов, не изменяя исходный текст с GUID.
/// </summary>
public sealed partial class UnityGuidIndex
{
    static readonly Dictionary<string, string> BuiltIn = new()
    {
        ["0000000000000000e000000000000000"] = "Unity built-in (extra resources)",
        ["0000000000000000f000000000000000"] = "Unity built-in (default resources)",
        ["0000000000000000d000000000000000"] = "Unity built-in (editor resources)",
    };

    /// <summary>Формат «guid: abc…» в YAML и «GUID:abc…» в ссылках .asmdef.</summary>
    [GeneratedRegex(@"(?i)guid:\s*([0-9a-f]{32})")]
    private static partial Regex GuidRef();

    readonly IReadOnlyDictionary<string, string> _paths;

    /// <summary>Сохраняет переданный словарь путей; способ сравнения GUID определяется самим словарём.</summary>
    UnityGuidIndex(IReadOnlyDictionary<string, string> paths) => _paths = paths;

    public static UnityGuidIndex Empty { get; } = new(new Dictionary<string, string>());

    /// <summary>Количество GUID ресурсов, проиндексированных в рабочей копии.</summary>
    public int Count => _paths.Count;

    /// <summary>Создаёт словарь GUID, параллельно считывая отслеживаемые и неотслеживаемые файлы .meta.</summary>
    public static async Task<UnityGuidIndex> Build(GitRepo repo)
    {
        var r = await repo.Run("ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", "*.meta");
        var metas = r.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (metas.Length == 0) return Empty;

        var paths = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await Task.Run(() => Parallel.ForEach(metas, meta =>
        {
            try
            {
                // Ищем guid: среди первых четырёх строк .meta; обычно он находится во второй.
                foreach (var line in File.ReadLines(repo.FullPath(meta)).Take(4))
                {
                    if (!line.StartsWith("guid: ")) continue;
                    paths[line[6..].Trim()] = meta[..^".meta".Length];
                    break;
                }
            }
            catch (IOException)
            {
                // Deleted or locked meanwhile: skip.
            }
        }));
        return new UnityGuidIndex(new Dictionary<string, string>(paths, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Путь ресурса для GUID, встроенная метка или null, если ресурса нет в рабочей копии.</summary>
    public string? Resolve(string guid) =>
        BuiltIn.TryGetValue(guid, out var builtIn) ? builtIn
        : _paths.TryGetValue(guid, out var path) ? path
        : null;

    /// <summary>При непустом индексе заполняет примечания контекстных и изменённых строк, содержащих GUID.</summary>
    public void Annotate(IEnumerable<DiffRow> rows)
    {
        if (Count == 0) return;
        foreach (var row in rows)
        {
            if (row.Kind is DiffRowKind.Message or DiffRowKind.Hunk) continue;
            (row.LeftNote, row.LeftNoteTip) = Describe(row.LeftText);
            (row.RightNote, row.RightNoteTip) = Describe(row.RightText);
        }
    }

    /// <summary>Создаёт подпись имён ресурсов и подробную подсказку для ссылок GUID в строке diff.</summary>
    (string? Note, string? Tip) Describe(string text)
    {
        if (text.Length == 0) return (null, null);
        var matches = GuidRef().Matches(text);
        if (matches.Count == 0) return (null, null);
        var names = new List<string>();
        var tips = new List<string>();
        foreach (Match m in matches)
        {
            var guid = m.Groups[1].Value;
            var path = Resolve(guid);
            names.Add(path == null ? "missing asset" : BuiltIn.ContainsKey(guid) ? "built-in" : Path.GetFileName(path));
            tips.Add($"{guid} → {path ?? "not found in the working tree (deleted, or a broken reference)"}");
        }
        return ("→ " + string.Join(", ", names.Distinct()), string.Join("\n", tips));
    }
}
