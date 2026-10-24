using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.Git;

/// <summary>Операции получения истории файла и сведений об авторах строк репозитория.</summary>
public sealed partial class GitRepo
{
    /// <summary>История файла от HEAD через log --follow -M: запрашивается до limit коммитов, записи без списка файлов пропускаются.</summary>
    /// <remarks>Пути разбираются по строкам и табуляции, без NUL-разделения; имена с табуляцией или переводом строки не поддерживаются этим разбором.</remarks>
    public async Task<List<FileHistoryEntry>> FileLog(string path, int limit = 2000)
    {
        var r = await Run("-c", "log.showSignature=false", "log", "--follow", "-M", $"-n{limit}",
            "--format=%x1e%H%x1f%P%x1f%an%x1f%ae%x1f%at%x1f%D%x1f%s", "--name-status", "--", path);
        var list = new List<FileHistoryEntry>();
        if (!r.Ok) return list;

        foreach (var chunk in r.Output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = chunk.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length < 2) continue; // merge commits list no files: skip them
            var p = lines[0].Split('\x1f');
            var status = lines.Skip(1).FirstOrDefault(l => l.Contains('\t'))?.Split('\t');
            if (p.Length < 7 || status == null || status.Length < 2) continue;

            var commit = new CommitInfo
            {
                Hash = p[0],
                Parents = p[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                Author = p[2],
                Email = p[3],
                Date = DateTimeOffset.FromUnixTimeSeconds(long.TryParse(p[4], out var t) ? t : 0),
                Subject = p[6],
            };
            var s = status[0][0];
            var file = s is 'R' or 'C' && status.Length >= 3
                ? new FileChange { Status = s, OldPath = status[1], RelPath = status[2] }
                : new FileChange { Status = s, RelPath = status[1] };
            list.Add(new FileHistoryEntry(commit, file));
        }
        return list;
    }

    /// <summary>
    /// Результат git blame для <paramref name="path"/> в ревизии <paramref name="rev"/> либо для файла рабочей копии,
    /// если rev равен null (у незакоммиченных строк в этом случае нулевой хеш).
    /// </summary>
    public async Task<List<BlameLine>> Blame(string path, string? rev)
    {
        var args = new List<string> { "blame", "--porcelain" };
        if (rev != null) args.Add(rev);
        args.Add("--");
        args.Add(path);
        var r = await Run(args.ToArray());
        if (!r.Ok) throw new InvalidOperationException(r.Message);
        return await Task.Run(() => ParseBlame(r.Output));
    }

    /// <summary>Метаданные, общие для всех строк blame, относящихся к одному коммиту.</summary>
    sealed class BlameCommit
    {
        public string Author = "", Summary = "", Path = "";
        public long Time;
        public string? PrevSha, PrevPath;
    }

    /// <summary>
    /// Формат porcelain: «sha origLine finalLine [count]», затем строки ключ/значение при первом появлении коммита,
    /// а после них — сама строка с префиксом табуляции.
    /// </summary>
    static List<BlameLine> ParseBlame(string output)
    {
        var commits = new Dictionary<string, BlameCommit>();
        var result = new List<BlameLine>();
        var lines = output.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var header = lines[i];
            if (header.Length < 40 || header[0] == '\t') continue;
            var parts = header.Split(' ');
            if (parts.Length < 3 || !int.TryParse(parts[2], out var finalLine)) continue;
            var sha = parts[0];
            if (!commits.TryGetValue(sha, out var c)) commits[sha] = c = new BlameCommit();

            for (i++; i < lines.Length && !lines[i].StartsWith('\t'); i++)
            {
                var line = lines[i];
                var space = line.IndexOf(' ');
                if (space < 0) continue;
                var value = line[(space + 1)..].TrimEnd('\r');
                switch (line[..space])
                {
                    case "author": c.Author = value; break;
                    case "author-time": long.TryParse(value, out c.Time); break;
                    case "summary": c.Summary = value; break;
                    case "filename": c.Path = value; break;
                    case "previous":
                        var prev = value.Split(' ', 2);
                        c.PrevSha = prev[0];
                        c.PrevPath = prev.Length > 1 ? prev[1] : null;
                        break;
                }
            }
            if (i >= lines.Length) break;

            result.Add(new BlameLine
            {
                Sha = sha,
                LineNo = finalLine,
                Text = lines[i][1..].TrimEnd('\r').Replace("\t", "    "),
                Author = c.Author,
                Date = DateTimeOffset.FromUnixTimeSeconds(c.Time),
                Summary = c.Summary,
                Path = c.Path,
                PreviousSha = c.PrevSha,
                PreviousPath = c.PrevPath,
            });
        }

        ColorByAge(result);
        return result;
    }

    static readonly IBrush Uncommitted = new ImmutableSolidColorBrush(Color.Parse("#2f5a3a"));
    static readonly IBrush[] Heat = Enumerable.Range(0, 10).Select(i =>
    {
        // From a cold grey-blue (oldest) to a warm amber (newest).
        double t = i / 9.0;
        byte R(double a, double b) => (byte)(a + (b - a) * t);
        return (IBrush)new ImmutableSolidColorBrush(Color.FromRgb(R(0x2b, 0x8a), R(0x30, 0x5c), R(0x3a, 0x1e)));
    }).ToArray();

    /// <summary>Отмечает границы последовательностей коммитов и назначает строкам blame цвета в зависимости от давности изменений.</summary>
    static void ColorByAge(List<BlameLine> lines)
    {
        var committed = lines.Where(l => !l.IsUncommitted).Select(l => l.Date.ToUnixTimeSeconds()).Distinct().Order().ToList();
        // Rank by distinct commit time, so one very old commit does not squash all others into the same colour.
        var rank = committed.Select((time, index) => (time, index)).ToDictionary(x => x.time, x => x.index);
        string? previous = null;
        foreach (var l in lines)
        {
            l.IsBlockStart = l.Sha != previous;
            previous = l.Sha;
            l.AgeBrush = l.IsUncommitted ? Uncommitted
                : committed.Count <= 1 ? Heat[^1]
                : Heat[rank[l.Date.ToUnixTimeSeconds()] * (Heat.Length - 1) / (committed.Count - 1)];
        }
    }
}
