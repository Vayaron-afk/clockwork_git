namespace ClockworkGit.Git;

public sealed record GitResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;

    /// <summary>stderr and stdout together, for error dialogs (git prints progress and hints to stderr).</summary>
    public string Message => (Error + "\n" + Output).Trim();

    /// <summary>Last non-empty line of the output, for the status bar.</summary>
    public string? LastLine =>
        (Error + "\n" + Output).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
}
