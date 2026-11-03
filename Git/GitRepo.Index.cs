using System.Diagnostics;
using System.Security.Cryptography;

namespace ClockworkGit.Git;

/// <summary>Проверка индекса перед изменениями и ручное снятие оставшейся блокировки.</summary>
public sealed partial class GitRepo
{
    static async Task<string?> ReadHead(GitCommandScope scope)
    {
        var head = await scope.Run("rev-parse", "-q", "--verify", "HEAD^{commit}");
        if (head.Ok)
            return head.Output.Trim();
        if (head.ExitCode != 1 || !string.IsNullOrWhiteSpace(head.Error))
            throw new InvalidOperationException($"Cannot read HEAD:\n{head.Message}");
        var branch = await scope.Run("symbolic-ref", "--quiet", "HEAD");
        if (!branch.Ok)
            throw new InvalidOperationException($"Cannot determine whether HEAD has a commit:\n{branch.Message}");
        var reference = branch.Output.Trim();
        if (!reference.StartsWith("refs/heads/", StringComparison.Ordinal))
            throw new InvalidOperationException("HEAD does not point to a local branch.");
        // A missing branch is unborn; an existing but invalid branch is an error.
        var exists = await scope.Run("show-ref", "--verify", "--quiet", reference);
        if (exists.ExitCode == 1 && string.IsNullOrWhiteSpace(exists.Error))
            return null;
        throw new InvalidOperationException($"HEAD does not resolve to a valid commit.\n{exists.Message}");
    }

    static async Task<string> IndexPath(GitCommandScope scope) =>
        System.IO.Path.GetFullPath((await scope.Checked("rev-parse", "--path-format=absolute", "--git-path", "index")).Trim());

    static async Task CheckIndex(GitCommandScope scope, string? head)
    {
        var path = await IndexPath(scope);
        if (head != null && !File.Exists(path))
            throw new InvalidOperationException($"The Git index is missing: {path}\nRestore the index from HEAD before staging or committing. Working files have not been changed.");
        if (File.Exists(path + ".lock"))
            throw new InvalidOperationException($"The index is locked: {path}.lock\nWait for Git to finish. If the lock was left behind, use Repository → Unlock index…");
        await scope.Checked("ls-files", "--stage", "-z");
    }

    /// <summary>Проверяет фактические HEAD и индекс до открытия диалога коммита.</summary>
    public Task CheckIndexAsync() => GitRunner.WithRepositoryLockAsync(Path, async scope =>
    {
        await CheckIndex(scope, await ReadHead(scope));
        return true;
    });

    /// <summary>Снимает изменения с индекса по свежему HEAD; ошибка чтения HEAD никогда не превращается в удаление записей.</summary>
    public Task UnstageAsync(IReadOnlyList<string> paths) => GitRunner.WithRepositoryLockAsync(Path, async scope =>
    {
        var head = await ReadHead(scope);
        await CheckIndex(scope, head);
        var selected = paths.Count == 0 ? new[] { ":/" } : paths.ToArray();
        if (head == null)
            await scope.Checked(["rm", "-r", "-q", "--cached", "--ignore-unmatch", "--", .. selected]);
        else
            await scope.Checked(["restore", "--staged", $"--source={head}", "--", .. selected]);
        return true;
    });

    async Task<CommitSnapshot> ValidateCommit(GitCommandScope scope, bool amend)
    {
        var head = await ReadHead(scope);
        await CheckIndex(scope, head);
        var files = ParseStatus(await scope.Checked("status", "--porcelain=v1", "-z", "-uall"));
        if (files.Any(f => f.IsConflict))
            throw new InvalidOperationException("Resolve the index conflicts before committing.");
        if (!amend && !files.Any(f => f.IsStaged))
            throw new InvalidOperationException("Nothing is staged. Stage the intended changes before committing.");
        var tracked = await scope.Checked("ls-files", "-z");
        if (head != null && tracked.Length == 0 && (await scope.Checked("ls-tree", "-r", "--name-only", "-z", head)).Length > 0)
            throw new InvalidOperationException("The index is empty while HEAD contains files. Committing would remove every tracked file.\nRestore the index from HEAD and stage only the intended changes.");
        var tree = (await scope.Checked("write-tree")).Trim();
        return new CommitSnapshot(tree, head, files);
    }

    /// <summary>Проверяет фактическое содержимое индекса и возвращает снимок для подтверждения пользователем.</summary>
    public Task<CommitSnapshot> ValidateCommitAsync(bool amend) =>
        GitRunner.WithRepositoryLockAsync(Path, scope => ValidateCommit(scope, amend));

    /// <summary>Создаёт коммит только если HEAD и дерево индекса совпадают с подтверждённым снимком.</summary>
    public Task CommitAsync(string messageFile, bool amend, CommitSnapshot expected) =>
        GitRunner.WithRepositoryLockAsync(Path, async scope =>
        {
            var current = await ValidateCommit(scope, amend);
            if (current.HeadHash != expected.HeadHash || current.TreeHash != expected.TreeHash)
                throw new InvalidOperationException("HEAD or the staged changes changed after confirmation. Review the changes and try committing again.");
            var args = new List<string> { "commit", "-F", messageFile };
            if (amend)
                args.Add("--amend");
            await scope.Checked(args.ToArray());
            return true;
        });

    static void CheckGitProcesses()
    {
        foreach (var name in new[] { "git", "git-lfs", "git-remote-https", "git-remote-http" })
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                if (processes.Length > 0)
                    throw new InvalidOperationException("Git processes are still running. Finish Git commands in all clients before removing index.lock.");
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }
    }

    static IndexLockInfo ReadLock(string path, FileStream stream) =>
        new(path, stream.Length, File.GetLastWriteTimeUtc(path), Convert.ToHexString(SHA256.HashData(stream)));

    static void CheckLockPath(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidOperationException("Refusing to remove an index.lock that is a directory or symbolic link.");
    }

    /// <summary>Показывает снимок оставшейся блокировки; ничего не изменяет и отказывает при работающем Git.</summary>
    public Task<IndexLockInfo?> InspectIndexLockAsync() => GitRunner.WithRepositoryLockAsync<IndexLockInfo?>(Path, async scope =>
    {
        var path = await IndexPath(scope) + ".lock";
        if (!File.Exists(path))
            return null;
        CheckGitProcesses();
        CheckLockPath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        return ReadLock(path, stream);
    });

    /// <summary>После подтверждения удаляет только неизменившийся index.lock, удерживая его открытым без доступа на запись.</summary>
    public Task RemoveIndexLockAsync(IndexLockInfo expected) => GitRunner.WithRepositoryLockAsync(Path, async scope =>
    {
        var path = await IndexPath(scope) + ".lock";
        if (!string.Equals(path, expected.FullPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("The index path changed. Inspect the lock again.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Manual index lock removal is supported on Windows only.");
        CheckGitProcesses();
        CheckLockPath(path);
        // Windows denies concurrent readers/writers; Delete is shared only so this
        // operation can unlink the verified file before releasing its handle.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Delete);
        if (ReadLock(path, stream) != expected)
            throw new InvalidOperationException("index.lock changed while the confirmation dialog was open. It has not been removed.");
        CheckGitProcesses();
        File.Delete(path);
        return true;
    });
}
