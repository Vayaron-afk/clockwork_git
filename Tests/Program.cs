using System.Diagnostics;
using System.Security.Cryptography;
using ClockworkGit.Git;

namespace ClockworkGit.Tests;

internal static class Program
{
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        { return; }
        throw new Exception("Unsafe operation was accepted.");
    }

    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    static async Task Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Unstage preserves existing tracked files and working changes", async () =>
            {
                using var f = await TestRepository.Create();
                var original = (await f.Git("write-tree")).Trim();
                File.WriteAllText(f.File("one.txt"), "changed\n");
                await f.Git("add", "one.txt");
                await f.Repo.UnstageAsync([]);
                Assert((await f.Git("write-tree")).Trim() == original, "Unstage emptied or changed baseline tree.");
                Assert(File.ReadAllText(f.File("one.txt")) == "changed\n", "Working changes were lost.");
                Assert((await f.Repo.Status()).All(x => !x.IsStaged), "Changes remain staged.");
            }),
            ("Unstage supports a real unborn branch", async () =>
            {
                using var f = await TestRepository.Create(commit: false);
                Assert(await f.Repo.HeadHash() == null, "Unborn HEAD not recognized.");
                await f.Repo.UnstageAsync([]);
                Assert((await f.Git("ls-files", "-z")).Length == 0, "Initial files remain staged.");
                Assert(File.Exists(f.File("one.txt")), "Initial working file was removed.");
            }),
            ("Broken HEAD aborts Unstage without touching the index", async () =>
            {
                using var f = await TestRepository.Create();
                var index = await f.IndexPath();
                var before = Hash(index);
                File.WriteAllText(f.File(".git/HEAD"), "not a valid HEAD\n");
                await Reject(async () => { await f.Repo.HeadHash(); });
                await Reject(() => f.Repo.UnstageAsync([]));
                Assert(Hash(index) == before, "Failed HEAD lookup modified the index.");
            }),
            ("Invalid existing branch is not mistaken for an unborn branch", async () =>
            {
                using var f = await TestRepository.Create();
                var reference = (await f.Git("symbolic-ref", "HEAD")).Trim();
                File.WriteAllText(f.File(".git/" + reference), new string('f', 40) + "\n");
                await Reject(async () => { await f.Repo.HeadHash(); });
                await Reject(() => f.Repo.UnstageAsync([]));
            }),
            ("Missing index aborts staging checks, Unstage and commit validation", async () =>
            {
                using var f = await TestRepository.Create();
                var index = await f.IndexPath();
                File.Delete(index);
                await Reject(() => f.Repo.CheckIndexAsync());
                await Reject(() => f.Repo.UnstageAsync([]));
                await Reject(async () => { await f.Repo.ValidateCommitAsync(false); });
                Assert(!File.Exists(index) && File.Exists(f.File("one.txt")), "Missing index was silently replaced.");
            }),
            ("Empty index cannot commit deletion of the complete repository", async () =>
            {
                using var f = await TestRepository.Create();
                var before = await f.Repo.HeadHash();
                await f.Git("rm", "-r", "--cached", "--", ".");
                await Reject(async () => { await f.Repo.ValidateCommitAsync(false); });
                Assert(await f.Repo.HeadHash() == before && File.Exists(f.File("one.txt")), "HEAD or files changed.");
            }),
            ("Normal commit includes staged content and preserves unstaged changes", async () =>
            {
                using var f = await TestRepository.Create();
                File.WriteAllText(f.File("one.txt"), "staged\n");
                File.WriteAllText(f.File("two.txt"), "unstaged\n");
                await f.Git("add", "one.txt");
                var snapshot = await f.Repo.ValidateCommitAsync(false);
                await f.Repo.CommitAsync(await f.MessageFile(), false, snapshot);
                Assert(await f.Git("show", "HEAD:one.txt") == "staged\n", "Staged content missing.");
                Assert(await f.Git("show", "HEAD:two.txt") == "two\n", "Unstaged content committed.");
                Assert(File.ReadAllText(f.File("two.txt")) == "unstaged\n", "Working changes lost.");
            }),
            ("Changed staged tree invalidates an earlier commit confirmation", async () =>
            {
                using var f = await TestRepository.Create();
                File.WriteAllText(f.File("one.txt"), "staged\n");
                await f.Git("add", "one.txt");
                var before = await f.Repo.HeadHash();
                var snapshot = await f.Repo.ValidateCommitAsync(false);
                File.WriteAllText(f.File("two.txt"), "additional\n");
                await f.Git("add", "two.txt");
                var message = await f.MessageFile();
                await Reject(() => f.Repo.CommitAsync(message, false, snapshot));
                Assert(await f.Repo.HeadHash() == before, "Unexpected tree was committed.");
            }),
            ("Failed status lookup is not returned as an empty clean status", async () =>
            {
                using var f = await TestRepository.Create();
                File.WriteAllText(await f.IndexPath(), "corrupted index");
                await Reject(async () => { await f.Repo.Status(); });
            }),
            ("Background status does not refresh the index on disk", async () =>
            {
                using var f = await TestRepository.Create();
                var index = await f.IndexPath();
                var before = Hash(index);
                File.SetLastWriteTimeUtc(f.File("one.txt"), DateTime.UtcNow.AddMinutes(-5));
                await f.Repo.Status();
                await RepoSummary.Read(f.Path);
                Assert(Hash(index) == before, "Status rewrote the index.");
            }),
            ("Client Git commands are serialized for the same repository", async () =>
            {
                using var f = await TestRepository.Create();
                var first = GitRunner.RunAsync(f.Path, "-c", "alias.hold=!echo ready > gate-marker; sleep 1", "hold");
                for (var i = 0; i < 200 && !File.Exists(f.File("gate-marker")); i++) await Task.Delay(10);
                Assert(File.Exists(f.File("gate-marker")), "Gate test did not start.");
                var second = GitRunner.RunAsync(f.Path + System.IO.Path.DirectorySeparatorChar, "rev-parse", "HEAD");
                await Task.Delay(50);
                Assert(!second.IsCompleted, "Concurrent command bypassed repository gate.");
                Assert((await first).Ok && (await second).Ok, "Serialized commands failed.");
            }),
        };

        var windows = new (string Name, Func<Task> Run)[]
        {
            ("Removing a stale lock preserves the exact index and working files", async () =>
            {
                using var f = await TestRepository.Create();
                var index = await f.IndexPath();
                var before = Hash(index);
                var lockPath = index + ".lock";
                File.WriteAllText(lockPath, "leftover");
                var info = await f.Repo.InspectIndexLockAsync() ?? throw new Exception("Lock not found.");
                await f.Repo.RemoveIndexLockAsync(info);
                Assert(!File.Exists(lockPath), "Lock still exists.");
                Assert(Hash(index) == before && File.Exists(f.File("one.txt")), "Recovery changed index or working files.");
                Assert((await f.Repo.Status()).Count == 0, "Recovery changed repository status.");
            }),
            ("Lock changed during confirmation is preserved", async () =>
            {
                using var f = await TestRepository.Create();
                var path = await f.IndexPath() + ".lock";
                File.WriteAllText(path, "first");
                var info = await f.Repo.InspectIndexLockAsync() ?? throw new Exception("Lock not found.");
                File.WriteAllText(path, "second");
                await Reject(() => f.Repo.RemoveIndexLockAsync(info));
                Assert(File.ReadAllText(path) == "second", "New lock was removed.");
            }),
            ("An open lock is never removed", async () =>
            {
                using var f = await TestRepository.Create();
                var path = await f.IndexPath() + ".lock";
                File.WriteAllText(path, "leftover");
                var info = await f.Repo.InspectIndexLockAsync() ?? throw new Exception("Lock not found.");
                using var active = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                await Reject(() => f.Repo.RemoveIndexLockAsync(info));
                Assert(File.Exists(path), "An open lock was removed.");
            }),
            ("An active external Git process prevents lock removal", async () =>
            {
                using var f = await TestRepository.Create();
                var path = await f.IndexPath() + ".lock";
                File.WriteAllText(path, "leftover");
                var info = await f.Repo.InspectIndexLockAsync() ?? throw new Exception("Lock not found.");
                var start = new ProcessStartInfo("git") { WorkingDirectory = f.Path, UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "-c", "alias.hold=!sleep 5", "hold" }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start) ?? throw new Exception("Cannot start test Git process.");
                try
                {
                    await Task.Delay(100);
                    await Reject(() => f.Repo.RemoveIndexLockAsync(info));
                    Assert(File.Exists(path), "Lock removed while Git was running.");
                }
                finally
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }),
            ("Linked worktree recovery targets its own index.lock", async () =>
            {
                using var f = await TestRepository.Create();
                var linked = f.File("linked");
                await f.Git("worktree", "add", "--detach", linked);
                var repo = await GitRepo.Open(linked) ?? throw new Exception("Cannot open worktree.");
                var index = (await repo.Run("rev-parse", "--path-format=absolute", "--git-path", "index")).Output.Trim();
                var mainLock = await f.IndexPath() + ".lock";
                var before = Hash(index);
                File.WriteAllText(mainLock, "main lock");
                File.WriteAllText(index + ".lock", "worktree lock");
                var info = await repo.InspectIndexLockAsync() ?? throw new Exception("Worktree lock not found.");
                await repo.RemoveIndexLockAsync(info);
                Assert(File.Exists(mainLock) && !File.Exists(index + ".lock"), "Wrong worktree lock was removed.");
                Assert(Hash(index) == before, "Worktree index changed.");
            }),
        };

        var failures = 0;
        foreach (var (name, run) in OperatingSystem.IsWindows() ? tests.Concat(windows) : tests)
        {
            try { await run(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + "\n" + ex); }
        }
        if (failures > 0) Environment.ExitCode = 1;
        Console.WriteLine($"Failures: {failures}");
    }
}
