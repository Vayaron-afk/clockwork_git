namespace ClockworkGit.Git;

/// <summary>Команды внутри уже захваченной блокировки клиента; область нельзя сохранять после завершения callback.</summary>
internal sealed class GitCommandScope(string repo)
{
    internal Task<GitResult> Run(params string[] args) => GitRunner.RunCoreAsync(repo, null, null, args);

    internal async Task<string> Checked(params string[] args)
    {
        var result = await Run(args);
        if (!result.Ok)
            throw new InvalidOperationException($"git {args[0]} failed:\n{result.Message}");
        return result.Output;
    }
}
