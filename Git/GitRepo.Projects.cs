namespace ClockworkGit.Git;

// Project roots (.NET, npm, Unity) for grouping changed files.
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

    /// <summary>Outermost tracked project folders, e.g. <c>Site1</c> rather than <c>Site1/ClientApp</c>.</summary>
    public async Task<List<string>> ProjectRoots()
    {
        var r = await Run(["ls-files", "-z", "--", .. ProjectMarkers]);
        if (!r.Ok) return new();
        return ProjectGrouping.OutermostRoots(r.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries));
    }
}
