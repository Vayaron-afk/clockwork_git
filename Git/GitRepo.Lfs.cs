using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClockworkGit.Git;

/// <summary>Получает атрибуты Git LFS, отслеживаемые объекты, данные локального кэша, блокировки и правила.</summary>
public sealed partial class GitRepo
{
    /// <summary>Пути, для которых атрибут фильтра равен «lfs», в рабочей копии или источнике <paramref name="source"/>.</summary>
    public async Task<HashSet<string>> LfsAttributed(IReadOnlyCollection<string> paths, string? source = null)
    {
        var set = new HashSet<string>();
        if (paths.Count == 0) return set;
        var args = new List<string> { "check-attr", "--stdin", "-z" };
        if (source != null) { args.Add("--source"); args.Add(source); }
        args.Add("filter");
        var r = await RunWithInput(string.Join('\0', paths) + '\0', args.ToArray());
        var t = r.Output.Split('\0');
        for (var i = 0; i + 2 < t.Length; i += 3)
            if (t[i + 2] == "lfs") set.Add(t[i]);
        return set;
    }

    /// <summary>Возвращает LFS-файлы дерева указанной ревизии (по умолчанию HEAD), размеры и наличие объектов в локальном хранилище LFS.</summary>
    /// <remarks>Индекс и рабочая копия не сканируются. При неуспешной команде возвращается пустой список; некорректный JSON вызывает исключение.</remarks>
    public async Task<List<LfsFile>> LfsFiles(string rev = "HEAD")
    {
        var list = new List<LfsFile>();
        var r = await Run("lfs", "ls-files", "--json", rev);
        if (!r.Ok || r.Output.Length == 0) return list;
        using var doc = JsonDocument.Parse(r.Output);
        if (!doc.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array) return list;
        foreach (var f in files.EnumerateArray())
        {
            list.Add(new LfsFile(
                f.GetProperty("name").GetString() ?? "",
                f.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                f.TryGetProperty("downloaded", out var d) && d.GetBoolean(),
                f.TryGetProperty("oid", out var o) ? o.GetString() ?? "" : ""));
        }
        return list;
    }

    /// <summary>Читает строки с отдельным токеном filter=lfs из корневого .gitattributes, сохраняя шаблон и номер строки.</summary>
    /// <remarks>Упрощённый разбор по пробелам: кавычки, макросы, вложенные файлы атрибутов и приоритеты правил не обрабатываются.</remarks>
    public List<LfsRule> LfsRules()
    {
        var file = System.IO.Path.Combine(Path, ".gitattributes");
        var rules = new List<LfsRule>();
        if (!File.Exists(file)) return rules;
        var lines = File.ReadAllLines(file);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Skip(1).Contains("filter=lfs")) rules.Add(new LfsRule(tokens[0], i + 1));
        }
        return rules;
    }

    /// <summary>Приближённо сопоставляет шаблоны с *, ** и ?; шаблон без «/» проверяется по имени файла в любом каталоге.</summary>
    /// <remarks>Не реализует полную семантику gitattributes, включая классы символов и экранирование; для фактических атрибутов используйте <see cref="LfsAttributed"/>.</remarks>
    public static bool AttrPatternMatches(string pattern, string relPath)
    {
        var p = pattern.TrimStart('/');
        var target = p.Contains('/') ? relPath : relPath[(relPath.LastIndexOf('/') + 1)..];
        var rx = "^" + Regex.Escape(p)
            .Replace(@"\*\*", "\u0001").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]").Replace("\u0001", ".*") + "$";
        return Regex.IsMatch(target, rx);
    }

    /// <summary>Суммирует размеры файлов в GitDir/lfs/objects; возвращает 0, если каталога нет. Настройка lfs.storage не учитывается.</summary>
    public long LfsCacheBytes()
    {
        var dir = System.IO.Path.Combine(GitDir, "lfs", "objects");
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
            : 0;
    }

    /// <summary>
    /// Запрашивает блокировки через locks --verify --json, при неуспехе пробует locks --json без сведений о владении.
    /// При резервном запросе IsMine=false у всех записей; null означает, что получить список не удалось.
    /// Графический вход через GCM отключён; некорректный JSON вызывает исключение.
    /// </summary>
    public async Task<List<LfsLock>?> LfsLocks()
    {
        var r = await RunWithEnv(GitRunner.NoLoginPrompt, "lfs", "locks", "--verify", "--json");
        if (r.Ok && r.Output.TrimStart().StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(r.Output);
            return ParseLocks(doc.RootElement, "ours", mine: true).Concat(ParseLocks(doc.RootElement, "theirs", mine: false)).ToList();
        }
        // Servers without lock verification still list locks, only without ownership.
        r = await RunWithEnv(GitRunner.NoLoginPrompt, "lfs", "locks", "--json");
        if (!r.Ok || !r.Output.TrimStart().StartsWith('[')) return null;
        using var plain = JsonDocument.Parse(r.Output);
        return plain.RootElement.EnumerateArray().Select(e => ParseLock(e, mine: false)).ToList();
    }

    /// <summary>Читает одну группу владения из JSON-ответа команды <c>git lfs locks --verify</c>.</summary>
    static IEnumerable<LfsLock> ParseLocks(JsonElement root, string group, bool mine) =>
        root.TryGetProperty(group, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(e => ParseLock(e, mine)).ToList()
            : [];

    /// <summary>Преобразует JSON-объект одной блокировки LFS в модель блокировки приложения.</summary>
    static LfsLock ParseLock(JsonElement e, bool mine) => new(
        e.TryGetProperty("id", out var id) ? id.ToString() : "",
        e.TryGetProperty("path", out var path) ? path.GetString() ?? "" : "",
        e.TryGetProperty("owner", out var owner) && owner.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
        e.TryGetProperty("locked_at", out var at) && DateTimeOffset.TryParse(at.GetString(), out var when) ? when : DateTimeOffset.MinValue,
        mine);

    /// <summary>Отслеживаемые файлы, подходящие под правило LFS, но всё ещё хранящиеся в HEAD как обычные объекты Git.</summary>
    public async Task<List<string>> NotYetInLfs()
    {
        var all = (await Run("ls-files", "-z")).Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var attributed = await LfsAttributed(all);
        var inLfs = (await LfsFiles()).Select(f => f.Path).ToHashSet();
        return all.Where(p => attributed.Contains(p) && !inLfs.Contains(p)).ToList();
    }
}
