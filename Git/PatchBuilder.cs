namespace ClockworkGit.Git;

/// <summary>
/// Создаёт патч только с выбранными строками unified diff для команды <c>git apply</c>.
/// Невыбранные изменения становятся контекстом или удаляются, чтобы патч применялся к нужной версии:
/// <list type="bullet">
/// <item>прямое применение (добавление в индекс): целевая версия — старая, поэтому невыбранные строки «-» остаются контекстом, а строки «+» удаляются;</item>
/// <item>обратное применение (удаление из индекса, отмена; применяется с --reverse): целевая версия — новая, поэтому невыбранные строки «+»
/// остаются контекстом, а строки «-» удаляются.</item>
/// </list>
/// Количество строк в блоках пересчитывается; начальные номера строк сохраняются из исходного diff.
/// Для патча, построенного в обратном режиме, вызывающий код должен передать git apply --reverse.
/// </summary>
public static class PatchBuilder
{
    /// <summary>Индексы исходных строк diff для выбранных строк; при необходимости выбираются целые блоки изменений или блоки diff.</summary>
    public static HashSet<int> SelectLines(IReadOnlyList<DiffRow> rows, IEnumerable<int> selectedRows, bool wholeBlocks)
    {
        var lines = new HashSet<int>();

        // Adds the raw unified-diff line indices associated with a changed row.
        void Add(DiffRow r)
        {
            if (r.Kind != DiffRowKind.Change) return;
            if (r.LeftLine >= 0) lines.Add(r.LeftLine);
            if (r.RightLine >= 0) lines.Add(r.RightLine);
        }

        foreach (var index in selectedRows)
        {
            if (index < 0 || index >= rows.Count) continue;
            var row = rows[index];
            if (row.Kind == DiffRowKind.Hunk)
            {
                for (var j = index + 1; j < rows.Count && rows[j].Kind != DiffRowKind.Hunk; j++) Add(rows[j]);
            }
            else if (row.Kind == DiffRowKind.Change && wholeBlocks)
            {
                var start = index;
                while (start > 0 && rows[start - 1].Kind == DiffRowKind.Change) start--;
                for (var j = start; j < rows.Count && rows[j].Kind == DiffRowKind.Change; j++) Add(rows[j]);
            }
            else Add(row);
        }
        return lines;
    }

    /// <summary>Патч или null, если выбранные строки не содержат изменений.</summary>
    public static string? Build(string rawDiff, IReadOnlySet<int> selected, bool reverse)
    {
        var lines = rawDiff.Split('\n');
        var output = new List<string>();
        var i = 0;
        while (i < lines.Length && !lines[i].StartsWith("@@")) output.Add(lines[i++]); // diff --git, index, ---, +++
        var any = false;

        while (i < lines.Length)
        {
            var header = DiffParser.HunkHeader().Match(lines[i].TrimEnd('\r'));
            if (!header.Success) { i++; continue; }
            var oldStart = header.Groups[1].Value;
            var newStart = header.Groups[2].Value;
            i++;

            var body = new List<string>();
            int oldCount = 0, newCount = 0;
            var hasChange = false;
            var lastKept = false; // a "\ No newline at end of file" marker belongs to the line before it

            for (; i < lines.Length && !lines[i].StartsWith("@@") && !lines[i].StartsWith("diff "); i++)
            {
                var line = lines[i];
                if (line.Length == 0) { lastKept = false; continue; }
                var isSelected = selected.Contains(i);
                switch (line[0])
                {
                    case ' ':
                        body.Add(line); oldCount++; newCount++; lastKept = true;
                        break;
                    case '-' when isSelected:
                        body.Add(line); oldCount++; hasChange = true; lastKept = true;
                        break;
                    case '-' when !reverse:
                        body.Add(" " + line[1..]); oldCount++; newCount++; lastKept = true;
                        break;
                    case '+' when isSelected:
                        body.Add(line); newCount++; hasChange = true; lastKept = true;
                        break;
                    case '+' when reverse:
                        body.Add(" " + line[1..]); oldCount++; newCount++; lastKept = true;
                        break;
                    case '\\':
                        if (lastKept) body.Add(line);
                        break;
                    default:
                        lastKept = false; // an unselected change that is dropped
                        break;
                }
            }

            if (!hasChange) continue;
            output.Add($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@");
            output.AddRange(body);
            any = true;
        }

        return any ? string.Join("\n", output) + "\n" : null;
    }
}
