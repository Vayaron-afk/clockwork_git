using System.Text.RegularExpressions;

namespace ClockworkGit.Git;

/// <summary>Преобразует unified diff Git в строки <see cref="DiffRow"/>, расположенные бок о бок.</summary>
public static partial class DiffParser
{
    public const int DefaultMaxRows = 30000;
    public const int FullFileMaxRows = 300000;

    /// <summary>Формат «@@ -old[,n] +new[,n] @@ section»: группа 1 — начало старой версии, 2 — новой, 3 — текст секции.</summary>
    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@(.*)$")]
    internal static partial Regex HunkHeader();

    /// <summary>Удаляет символы возврата каретки в окончаниях строк и раскрывает табуляцию для выравнивания diff.</summary>
    static string Clean(string s) => s.TrimEnd('\r').Replace("\t", "    ");

    /// <summary>
    /// Преобразует unified diff в строки, расположенные бок о бок: блоки удалений и добавлений сопоставляются
    /// построчно, а различающиеся части выделяются. При значении <paramref name="fullFile"/> заголовки блоков
    /// опускаются (режим используется, когда контекст охватывает весь файл).
    /// Строки сохраняют индексы исходных строк diff, чтобы выбранные строки можно было преобразовать в патч.
    /// </summary>
    /// <remarks>Предел maxRows приблизительный: накопленный блок добавляется целиком и может превысить его. Пустой и двоичный diff возвращаются как информационная строка.</remarks>
    public static List<DiffRow> SideBySide(string diff, bool fullFile = false, int maxRows = DefaultMaxRows)
    {
        var rows = new List<DiffRow>();
        if (string.IsNullOrWhiteSpace(diff)) { rows.Add(DiffRow.Message("No textual changes")); return rows; }
        if (diff.Contains("\nBinary files ") || diff.StartsWith("Binary files ")) { rows.Add(DiffRow.Message("Binary file")); return rows; }

        var dels = new List<(int No, string Text, int Raw)>();
        var adds = new List<(int No, string Text, int Raw)>();
        int ln = 0, rn = 0;
        var inHunk = false;

        // Aligns the pending deletion and addition runs and adds them as changed rows.
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

    /// <summary>Находит различающиеся фрагменты двух изменённых строк после удаления общего префикса и суффикса.</summary>
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

    /// <summary>Создаёт строки diff для текстового файла, содержимое которого целиком добавлено.</summary>
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

    /// <summary>Читает файл на диске как UTF-8 и показывает строки добавленными; для каталогов, отсутствующих, крупных или двоичных файлов возвращает сообщение.</summary>
    /// <remarks>Файлы больше 8 МиБ не читаются; двоичность определяется нулевым байтом в первых 8000 байтах.</remarks>
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
