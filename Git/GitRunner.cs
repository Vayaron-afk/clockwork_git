using System.Diagnostics;
using System.Text;

namespace ClockworkGit.Git;

public sealed record GitResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Runs the git CLI. Never prompts: no terminal prompts, no editors.</summary>
public static class GitRunner
{
    static ProcessStartInfo CreateStartInfo(string repo, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var opt in new[] { "core.quotepath=off", "i18n.logOutputEncoding=UTF-8", "color.ui=never" })
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(opt);
        }
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_EDITOR"] = "true";
        psi.Environment["GIT_SEQUENCE_EDITOR"] = "true";
        psi.Environment["GIT_MERGE_AUTOEDIT"] = "no";
        return psi;
    }

    public static async Task<GitResult> RunAsync(string repo, params string[] args)
    {
        try
        {
            using var p = Process.Start(CreateStartInfo(repo, args))!;
            p.StandardInput.Close();
            var output = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return new GitResult(p.ExitCode, await output, await error);
        }
        catch (Exception ex)
        {
            return new GitResult(-1, "", ex.Message);
        }
    }

    public static async Task<byte[]?> ReadBytesAsync(string repo, params string[] args)
    {
        try
        {
            using var p = Process.Start(CreateStartInfo(repo, args))!;
            p.StandardInput.Close();
            using var ms = new MemoryStream();
            var copy = p.StandardOutput.BaseStream.CopyToAsync(ms);
            var error = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            await copy;
            await error;
            return p.ExitCode == 0 ? ms.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }
}
