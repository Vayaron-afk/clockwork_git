namespace ClockworkGit.Git;

/// <summary>Находит корневые каталоги проектов в репозитории для группировки изменённых файлов в интерфейсе.</summary>
public sealed partial class GitRepo
{
    static readonly string[] ProjectMarkers =
    [
        ":(glob)**/*.csproj",
        ":(glob)**/*.fsproj",
        ":(glob)**/*.vcxproj",
        ":(glob)**/package.json",
        ":(glob)**/ProjectSettings/ProjectVersion.txt",
    ];

    /// <summary>Самые внешние отслеживаемые каталоги проектов, например <c>Site1</c>, а не <c>Site1/ClientApp</c>.</summary>
    public async Task<List<string>> ProjectRoots()
    {
        var r = await Run(["ls-files", "-z", "--", .. ProjectMarkers]);
        if (!r.Ok) return new();
        return ProjectGrouping.OutermostRoots(r.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries));
    }
}
