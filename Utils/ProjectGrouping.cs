namespace ClockworkGit.Utils;

/// <summary>Группирует изменённые файлы по проектам, к которым они относятся (сайт, служба, проект Unity).</summary>
public static class ProjectGrouping
{
    const string UnityMarker = "/ProjectSettings/ProjectVersion.txt";

    /// <summary>
    /// Переданные маркерные файлы (<c>*.csproj</c>, <c>*.fsproj</c>, <c>*.vcxproj</c>, <c>package.json</c>, ProjectVersion.txt Unity) определяют каталоги проектов;
    /// сохраняются только самые внешние: ClientApp сайта относится к самому сайту. Маркеры в корне репозитория
    /// без каталога игнорируются, иначе все файлы оказались бы в одной группе.
    /// </summary>
    /// <remarks>Unity-маркер распознаётся по суффиксу /ProjectSettings/ProjectVersion.txt; путь без родительского проекта ошибочно даёт каталог ProjectSettings.</remarks>
    public static List<string> OutermostRoots(IEnumerable<string> markers)
    {
        var roots = markers
            .Select(m => m.EndsWith(UnityMarker, StringComparison.Ordinal) ? m[..^UnityMarker.Length]
                : m.LastIndexOf('/') is var i and >= 0 ? m[..i] : "")
            .Where(r => r.Length > 0)
            .Distinct()
            .OrderBy(r => r.Length)
            .ToList();
        var kept = new List<string>();
        foreach (var r in roots)
            if (!kept.Any(k => r.StartsWith(k + "/", StringComparison.Ordinal)))
                kept.Add(r);
        return kept;
    }

    /// <summary>Каталог проекта для <paramref name="path"/>; если файл не относится к проекту — каталог верхнего уровня.</summary>
    public static string GroupRoot(string path, IReadOnlyList<string> roots)
    {
        foreach (var r in roots)
            if (path.StartsWith(r + "/", StringComparison.Ordinal))
                return r;
        var slash = path.IndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    /// <summary>Заголовок и файлы каждой группы; группы упорядочены по каталогу (сначала корень репозитория), порядок файлов сохраняется.</summary>
    public static List<object> Group(IReadOnlyList<FileChange> files, IReadOnlyList<string> roots)
    {
        var rows = new List<object>();
        foreach (var g in files.GroupBy(f => GroupRoot(f.RelPath, roots))
                     .OrderBy(g => g.Key.Length > 0)
                     .ThenBy(g => g.Key, NaturalComparer.Instance))
        {
            var list = g.ToList();
            rows.Add(new FileGroup { Root = g.Key, Files = list });
            rows.AddRange(list);
        }
        return rows;
    }
}
