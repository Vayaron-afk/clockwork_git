namespace ClockworkGit.Git;

/// <summary>A cheap overview of a repository for the Repositories list (one git status call).</summary>
public sealed record RepoSummary(string? Branch, string? Upstream, int Changed, int Ahead, int Behind)
{
    public static async Task<RepoSummary?> Read(string path)
    {
        if (!Directory.Exists(path)) return null;
        var r = await GitRunner.RunAsync(path, "status", "--porcelain=v2", "--branch");
        if (!r.Ok) return null;

        string? branch = null, upstream = null;
        int changed = 0, ahead = 0, behind = 0;
        foreach (var raw in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("# branch.head ")) branch = line[14..] == "(detached)" ? null : line[14..];
            else if (line.StartsWith("# branch.upstream ")) upstream = line[18..];
            else if (line.StartsWith("# branch.ab "))
            {
                // "# branch.ab +1 -2"
                var parts = line[12..].Split(' ');
                if (parts.Length == 2)
                {
                    int.TryParse(parts[0].TrimStart('+'), out ahead);
                    int.TryParse(parts[1].TrimStart('-'), out behind);
                }
            }
            else if (!line.StartsWith('#')) changed++;
        }
        return new RepoSummary(branch, upstream, changed, ahead, behind);
    }
}
