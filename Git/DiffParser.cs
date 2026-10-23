using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.Git;

public sealed class DiffRow
{
    public static readonly IBrush ContextBg = Brushes.Transparent;
    public static readonly IBrush RemovedBg = new ImmutableSolidColorBrush(Color.Parse("#4b2226"));
    public static readonly IBrush AddedBg = new ImmutableSolidColorBrush(Color.Parse("#1f4526"));
    public static readonly IBrush EmptyBg = new ImmutableSolidColorBrush(Color.Parse("#26272a"));
    public static readonly IBrush HunkBg = new ImmutableSolidColorBrush(Color.Parse("#2b3442"));

    public string LeftNo { get; init; } = "";
    public string LeftText { get; init; } = "";
    public IBrush LeftBg { get; init; } = ContextBg;
    public string RightNo { get; init; } = "";
    public string RightText { get; init; } = "";
    public IBrush RightBg { get; init; } = ContextBg;

    public static DiffRow Message(string text) => new() { LeftText = text, LeftBg = HunkBg, RightBg = HunkBg };
}

public static partial class DiffParser
{
    const int MaxRows = 30000;

    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@(.*)$")]
    private static partial Regex HunkHeader();

    static string Clean(string s) => s.TrimEnd('\r').Replace("\t", "    ");

    /// <summary>Turns a unified diff into side-by-side rows; removed/added blocks are paired line by line.</summary>
    public static List<DiffRow> SideBySide(string diff)
    {
        var rows = new List<DiffRow>();
        if (string.IsNullOrWhiteSpace(diff)) { rows.Add(DiffRow.Message("No textual changes")); return rows; }
        if (diff.Contains("\nBinary files ") || diff.StartsWith("Binary files ")) { rows.Add(DiffRow.Message("Binary file")); return rows; }

        var dels = new List<(int No, string Text)>();
        var adds = new List<(int No, string Text)>();
        int ln = 0, rn = 0;
        var inHunk = false;

        void Flush()
        {
            var n = Math.Max(dels.Count, adds.Count);
            for (var i = 0; i < n; i++)
            {
                var hasL = i < dels.Count;
                var hasR = i < adds.Count;
                rows.Add(new DiffRow
                {
                    LeftNo = hasL ? dels[i].No.ToString() : "",
                    LeftText = hasL ? dels[i].Text : "",
                    LeftBg = hasL ? DiffRow.RemovedBg : DiffRow.EmptyBg,
                    RightNo = hasR ? adds[i].No.ToString() : "",
                    RightText = hasR ? adds[i].Text : "",
                    RightBg = hasR ? DiffRow.AddedBg : DiffRow.EmptyBg,
                });
            }
            dels.Clear();
            adds.Clear();
        }

        foreach (var raw in diff.Split('\n'))
        {
            if (rows.Count > MaxRows) { rows.Add(DiffRow.Message("… diff truncated")); break; }
            var line = raw.TrimEnd('\r');
            var m = HunkHeader().Match(line);
            if (m.Success)
            {
                Flush();
                ln = int.Parse(m.Groups[1].Value);
                rn = int.Parse(m.Groups[2].Value);
                inHunk = true;
                rows.Add(new DiffRow { LeftText = line, LeftBg = DiffRow.HunkBg, RightText = m.Groups[3].Value.Trim(), RightBg = DiffRow.HunkBg });
                continue;
            }
            if (!inHunk || line.Length == 0) continue;
            switch (line[0])
            {
                case '-': dels.Add((ln++, Clean(line[1..]))); break;
                case '+': adds.Add((rn++, Clean(line[1..]))); break;
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

    public static List<DiffRow> AllAdded(string text)
    {
        var rows = new List<DiffRow>();
        var no = 1;
        foreach (var line in text.Split('\n'))
        {
            if (rows.Count > MaxRows) { rows.Add(DiffRow.Message("… file truncated")); break; }
            rows.Add(new DiffRow { LeftBg = DiffRow.EmptyBg, RightNo = (no++).ToString(), RightText = Clean(line), RightBg = DiffRow.AddedBg });
        }
        return rows;
    }
}
