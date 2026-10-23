using System.Text;

namespace ClockworkGit.Git;

/// <summary>
/// One repository on disk. All operations go through the git CLI (see <see cref="GitRunner"/>);
/// the class is split by topic: history/status, diffs, LFS.
/// </summary>
public sealed partial class GitRepo
{
    /// <summary>Hash of the empty tree: the "parent" of a root commit for diffs.</summary>
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    GitRepo(string path, string gitDir)
    {
        Path = path;
        GitDir = gitDir;
    }

    public string Path { get; }
    public string GitDir { get; }

    public static async Task<GitRepo?> Open(string path)
    {
        var r = await GitRunner.RunAsync(path, "rev-parse", "--absolute-git-dir");
        return r.Ok ? new GitRepo(path, r.Output.Trim()) : null;
    }

    public Task<GitResult> Run(params string[] args) => GitRunner.RunAsync(Path, args);

    public Task<GitResult> RunWithInput(string input, params string[] args) => GitRunner.RunWithInputAsync(Path, input, args);

    public Task<GitResult> RunWithEnv(IReadOnlyDictionary<string, string> env, params string[] args) =>
        GitRunner.RunWithEnvAsync(Path, env, args);

    public RepoState GetState()
    {
        if (File.Exists(System.IO.Path.Combine(GitDir, "MERGE_HEAD"))) return RepoState.Merging;
        if (File.Exists(System.IO.Path.Combine(GitDir, "CHERRY_PICK_HEAD"))) return RepoState.CherryPicking;
        if (File.Exists(System.IO.Path.Combine(GitDir, "REVERT_HEAD"))) return RepoState.Reverting;
        if (Directory.Exists(System.IO.Path.Combine(GitDir, "rebase-merge"))
            || Directory.Exists(System.IO.Path.Combine(GitDir, "rebase-apply"))) return RepoState.Rebasing;
        return RepoState.Clean;
    }

    public string FullPath(string relPath) =>
        System.IO.Path.Combine(Path, relPath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>Commit messages go through a file: keeps newlines and non-ASCII intact on Windows.</summary>
    public static string WriteTempMessage(string message)
    {
        var tmp = System.IO.Path.GetTempFileName();
        File.WriteAllText(tmp, message, new UTF8Encoding(false));
        return tmp;
    }
}
