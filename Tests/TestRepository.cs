using ClockworkGit.Git;

namespace ClockworkGit.Tests;

internal sealed class TestRepository : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clockwork-index-tests-" + Guid.NewGuid().ToString("N"));
    internal GitRepo Repo { get; private set; } = null!;

    internal static async Task<TestRepository> Create(bool commit = true)
    {
        var fixture = new TestRepository();
        try
        {
            Directory.CreateDirectory(fixture.Path);
            await fixture.Git("init", "-q");
            await fixture.Git("config", "user.name", "Clockwork tests");
            await fixture.Git("config", "user.email", "clockwork-tests@example.invalid");
            await fixture.Git("config", "core.hooksPath", System.IO.Path.Combine(fixture.Path, "no-hooks"));
            await fixture.Git("config", "commit.gpgsign", "false");
            fixture.Repo = await GitRepo.Open(fixture.Path) ?? throw new Exception("Cannot open test repository.");
            System.IO.File.WriteAllText(fixture.File("one.txt"), "one\n");
            System.IO.File.WriteAllText(fixture.File("two.txt"), "two\n");
            await fixture.Git("add", "--", "one.txt", "two.txt");
            if (commit)
                await fixture.Git("commit", "-qm", "Initial commit");
            return fixture;
        }
        catch { fixture.Dispose(); throw; }
    }

    internal string File(string path) => System.IO.Path.Combine(Path, path);

    internal async Task<string> Git(params string[] args)
    {
        var result = await GitRunner.RunAsync(Path, args);
        if (!result.Ok)
            throw new Exception($"git {args[0]} failed: {result.Message}");
        return result.Output;
    }

    internal async Task<string> IndexPath() => (await Git("rev-parse", "--path-format=absolute", "--git-path", "index")).Trim();

    internal async Task<string> MessageFile()
    {
        var path = System.IO.Path.Combine(Repo.GitDir, "test-message");
        await System.IO.File.WriteAllTextAsync(path, "Test change");
        return path;
    }

    public void Dispose()
    {
        var root = System.IO.Path.GetFullPath(Path);
        var temp = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetTempPath()) + System.IO.Path.DirectorySeparatorChar;
        if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !System.IO.Path.GetFileName(root).StartsWith("clockwork-index-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test cleanup path.");
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            System.IO.File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, recursive: true);
    }
}
