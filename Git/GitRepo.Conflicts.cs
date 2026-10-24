using System.Text;

namespace ClockworkGit.Git;

// Conflicted files: index stages and a fresh three-way merge of them.
public sealed partial class GitRepo
{
    public async Task<ConflictSides> LoadConflict(string path)
    {
        var b = Blob(":1", path);
        var o = Blob(":2", path);
        var t = Blob(":3", path);
        await Task.WhenAll(b, o, t);
        return new ConflictSides(b.Result, o.Result, t.Result);
    }

    /// <summary>
    /// Re-runs the merge of the three stages with diff3 markers, so clean changes are merged and each conflict
    /// also shows the ancestor. Null when git cannot merge (e.g. binary input).
    /// </summary>
    public async Task<string?> MergeFile(ConflictSides sides)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClockworkGit", "merge-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            string Write(string name, byte[]? bytes)
            {
                var file = System.IO.Path.Combine(dir, name);
                File.WriteAllBytes(file, bytes ?? []);
                return file;
            }

            var ours = Write("ours", sides.Ours);
            var bas = Write("base", sides.Base);
            var theirs = Write("theirs", sides.Theirs);
            var r = await Run("merge-file", "-p", "--diff3",
                "-L", MergeFileParser.OursLabel, "-L", MergeFileParser.BaseLabel, "-L", MergeFileParser.TheirsLabel,
                ours, bas, theirs);
            // The exit code is the number of conflicts; negative means failure.
            return r.ExitCode >= 0 ? r.Output : null;
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    /// <summary>Writes the resolved text with the working file's line endings and BOM, optionally staging it.</summary>
    public async Task SaveResolution(string path, string text, bool useCrlf, bool bom, bool stage)
    {
        if (useCrlf) text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        await File.WriteAllTextAsync(FullPath(path), text, new UTF8Encoding(bom));
        if (stage)
        {
            var r = await Run("add", "--", path);
            if (!r.Ok) throw new InvalidOperationException(r.Message);
        }
    }
}
