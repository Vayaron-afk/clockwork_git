using System.Text.RegularExpressions;

namespace ClockworkGit.Git;

/// <summary>Turns git's unified diff into side-by-side <see cref="DiffRow"/>s.</summary>
public static partial class DiffParser
{
    public const int DefaultMaxRows = 30000;
    public const int FullFileMaxRows = 300000;

    /// <summary>"@@ -old[,n] +new[,n] @@ section": groups 1 = old start, 2 = new start, 3 = section text.</summary>
    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@(.*)$")]
    internal static partial Regex HunkHeader();

    static string Clean(string s) => s.TrimEnd('\r').Replace("\t", "    ");

    /// <summary>
    /// Turns a unified diff into side-by-side rows; removed/added blocks are paired line by line and the
    /// differing middle of each pair is highlighted. <paramref name="fullFile"/> drops hunk header rows
    /// (used with a context large enough to cover the whole file).
    /// Rows remember which raw diff lines they came from, so a selection can be turned into a patch.
    /// </summary>
    public static List<DiffRow> SideBySide(string diff, bool fullFile = false, int maxRows = DefaultMaxRows)
    {
        var rows = new List<DiffRow>();
        if (string.IsNullOrWhiteSpace(diff)) { rows.Add(DiffRow.Message("No textual changes")); return rows; }
        if (diff.Contains("\nBinary files ") || diff.StartsWith("Binary files ")) { rows.Add(DiffRow.Message("Binary file")); return rows; }

        var dels = new List<(int No, string Text, int Raw)>();
        var adds = new List<(int No, string Text, int Raw)>();
        int ln = 0, rn = 0;
        var inHunk = false;

        void Flush()
        {
            var n = Math.Max(dels.Count, adds.Count);
            for (var i = 0; i < n; i++)
            {
                var hasL = i < dels.Count;
                var hasR = i < adds.Count;
                var l = hasL ? dels[i].Text : "";
                var r = hasR ? adds[i].Text : "";
                var (ls, ll, rs, rl) = hasL && hasR ? InlineChange(l, r) : (0, 0, 0, 0);
                rows.Add(new DiffRow
                {
                    Kind = DiffRowKind.Change,
                    LeftNo = hasL ? dels[i].No.ToString() : "",
                    LeftText = l,
                    LeftBg = hasL ? DiffRow.RemovedBg : DiffRow.EmptyBg,
                    RightNo = hasR ? adds[i].No.ToString() : "",
                    RightText = r,
                    RightBg = hasR ? DiffRow.AddedBg : DiffRow.EmptyBg,
                    LeftHiStart = ls, LeftHiLength = ll, RightHiStart = rs, RightHiLength = rl,
                    LeftLine = hasL ? dels[i].Raw : -1,
                    RightLine = hasR ? adds[i].Raw : -1,
                });
            }
            dels.Clear();
            adds.Clear();
        }

        var rawLines = diff.Split('\n');
        for (var idx = 0; idx < rawLines.Length; idx++)
        {
            if (rows.Count > maxRows) { rows.Add(DiffRow.Message("… diff truncated")); break; }
            var line = rawLines[idx].TrimEnd('\r');
            var m = HunkHeader().Match(line);
            if (m.Success)
            {
                Flush();
                ln = int.Parse(m.Groups[1].Value);
                rn = int.Parse(m.Groups[2].Value);
                inHunk = true;
                if (!fullFile)
                    rows.Add(new DiffRow
                    {
                        Kind = DiffRowKind.Hunk, LeftText = line, LeftBg = DiffRow.HunkBg,
                        RightText = m.Groups[3].Value.Trim(), RightBg = DiffRow.HunkBg,
                    });
                continue;
            }
            if (!inHunk || line.Length == 0) continue;
            switch (line[0])
            {
                case '-': dels.Add((ln++, Clean(line[1..]), idx)); break;
                case '+': adds.Add((rn++, Clean(line[1..]), idx)); break;
                case ' ':
                    Flush();
                    var text = Clean(line[1..]);
                    rows.Add(new DiffRow { LeftNo = (ln++).ToString(), LeftText = text, RightNo = (rn++).ToString(), RightText = text });
                    break;
            }
        }
        Flush();
        return rows;
    }

    /// <summary>Common prefix/suffix of a changed pair; the middle parts are what actually changed.</summary>
    static (int LStart, int LLen, int RStart, int RLen) InlineChange(string l, string r)
    {
        var max = Math.Min(l.Length, r.Length);
        var p = 0;
        while (p < max && l[p] == r[p]) p++;
        var s = 0;
        while (s < max - p && l[l.Length - 1 - s] == r[r.Length - 1 - s]) s++;
        int ll = l.Length - p - s, rl = r.Length - p - s;
        // Nothing in common: the whole line is the change, row colour already says that.
        if (p == 0 && s == 0) return (0, 0, 0, 0);
        return (p, ll, p, rl);
    }

    public static List<DiffRow> AllAdded(string text, int maxRows = DefaultMaxRows)
    {
        var rows = new List<DiffRow>();
        var no = 1;
        foreach (var line in text.Split('\n'))
        {
            if (rows.Count > maxRows) { rows.Add(DiffRow.Message("… file truncated")); break; }
            rows.Add(new DiffRow
            {
                Kind = DiffRowKind.Change, LeftBg = DiffRow.EmptyBg,
                RightNo = (no++).ToString(), RightText = Clean(line), RightBg = DiffRow.AddedBg,
            });
        }
        return rows;
    }

    /// <summary>A file that git does not know yet: every line is an addition.</summary>
    public static List<DiffRow> FromNewFile(string path, int maxRows = DefaultMaxRows)
    {
        if (Directory.Exists(path)) return [DiffRow.Message("Directory")];
        if (!File.Exists(path)) return [DiffRow.Message("File not found")];
        var info = new FileInfo(path);
        if (info.Length > 8 * 1024 * 1024) return [DiffRow.Message($"File too large to show ({info.Length / 1024} KB)")];
        var bytes = File.ReadAllBytes(path);
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0)) return [DiffRow.Message("Binary file")];
        return AllAdded(System.Text.Encoding.UTF8.GetString(bytes), maxRows);
    }
}
