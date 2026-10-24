namespace ClockworkGit.Utils;

/// <summary>Splits changed files by the project they belong to (a site, a service, the Unity project).</summary>
public static class ProjectGrouping
{
    const string UnityMarker = "/ProjectSettings/ProjectVersion.txt";

    /// <summary>
    /// Marker files (<c>*.csproj</c>, <c>package.json</c>, Unity's ProjectVersion.txt) → their project folders,
    /// keeping only the outermost ones: a site's ClientApp belongs to the site. Markers at the repository root
    /// are ignored, otherwise everything would be one group.
    /// </summary>
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

    /// <summary>The project folder of <paramref name="path"/>; outside any project, its top-level folder.</summary>
    public static string GroupRoot(string path, IReadOnlyList<string> roots)
    {
        foreach (var r in roots)
            if (path.StartsWith(r + "/", StringComparison.Ordinal))
                return r;
        var slash = path.IndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    /// <summary>Header + files per group, groups by folder (repository root first), files keep their order.</summary>
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
