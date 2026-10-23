using Avalonia.Controls;
using ClockworkGit.Git;

namespace ClockworkGit.UI;

/// <summary>Track/untrack/convert flows shared by the main window and the LFS window.</summary>
public static class LfsActions
{
    public static string ExtensionPattern(string relPath)
    {
        var ext = Path.GetExtension(relPath);
        return ext.Length > 0 ? "*" + ext : "";
    }

    static string Pathspec(string pattern, bool literal) => literal ? ":(literal)" + pattern : pattern.TrimStart('/');

    public static async Task<bool> Track(Window owner, GitRepo repo, string pattern, bool literal)
    {
        var choice = await Dialogs.Choose(owner, "Track in LFS",
            $"Add '{pattern}' to the LFS rules in .gitattributes and stage .gitattributes.\n\n" +
            "Also convert matching files that are already in the repository? Their current versions get staged " +
            "as LFS pointers and move to LFS with your next commit. Older versions stay in history as they are.",
            "Track and convert", "Track rule only", "Cancel");
        if (choice < 0) return false;

        var args = literal ? new[] { "lfs", "track", "--filename", pattern } : new[] { "lfs", "track", pattern };
        if (!await Run(owner, repo, args)) return false;
        if (!await Run(owner, repo, "add", ".gitattributes")) return false;
        return choice != 0 || await Run(owner, repo, "add", "--renormalize", "--", Pathspec(pattern, literal));
    }

    public static async Task<bool> Untrack(Window owner, GitRepo repo, string pattern)
    {
        var choice = await Dialogs.Choose(owner, "Untrack from LFS",
            $"Remove the rule '{pattern}' from .gitattributes and stage .gitattributes.\n\n" +
            "Also convert matching files back to regular git files? Their current versions get staged in full. " +
            "Versions already uploaded stay in the LFS storage on GitHub and keep using its quota.",
            "Untrack and convert back", "Remove rule only", "Cancel");
        if (choice < 0) return false;

        if (!await Run(owner, repo, "lfs", "untrack", pattern)) return false;
        if (!await Run(owner, repo, "add", ".gitattributes")) return false;
        return choice != 0 || await Run(owner, repo, "add", "--renormalize", "--", Pathspec(pattern, false));
    }

    /// <summary>Re-stages the given tracked files so the current LFS rules apply to them.</summary>
    public static async Task<bool> ConvertExisting(Window owner, GitRepo repo, IReadOnlyCollection<string> paths)
    {
        var r = await repo.RunWithInput(string.Join('\0', paths) + '\0',
            "add", "--renormalize", "--pathspec-from-file=-", "--pathspec-file-nul");
        if (!r.Ok) await Dialogs.ShowMessage(owner, "git add --renormalize failed", (r.Error + "\n" + r.Output).Trim());
        return r.Ok;
    }

    static async Task<bool> Run(Window owner, GitRepo repo, params string[] args)
    {
        var r = await repo.Run(args);
        if (!r.Ok) await Dialogs.ShowMessage(owner, $"git {string.Join(' ', args.Take(2))} failed", (r.Error + "\n" + r.Output).Trim());
        return r.Ok;
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };
}
