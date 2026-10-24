namespace ClockworkGit.Git;

/// <summary>Получает ветки, ссылки, историю коммитов, stash и состояние рабочей копии репозитория.</summary>
public sealed partial class GitRepo
{
    /// <summary>Возвращает имя текущей ветки или null, если HEAD отсоединён либо Git не может определить имя.</summary>
    public async Task<string?> CurrentBranch()
    {
        var r = await Run("symbolic-ref", "--short", "-q", "HEAD");
        return r.Ok ? r.Output.Trim() : null;
    }

    /// <summary>Возвращает хеш HEAD или null при ошибке его определения, в том числе до первого коммита.</summary>
    public async Task<string?> HeadHash()
    {
        var r = await Run("rev-parse", "-q", "--verify", "HEAD");
        return r.Ok ? r.Output.Trim() : null;
    }

    /// <summary>Возвращает имена настроенных удалённых репозиториев.</summary>
    public async Task<List<string>> Remotes()
    {
        var r = await Run("remote");
        return r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    /// <summary>Получает не более <paramref name="limit"/> коммитов, достижимых из выбранных ссылок, в порядке git log --date-order.</summary>
    public async Task<List<CommitInfo>> Log(IEnumerable<string> refs, int limit, IReadOnlyCollection<string> remotes)
    {
        var args = new List<string>
        {
            "-c", "log.showSignature=false", "log", "--date-order", "-z", $"-n{limit}",
            "--format=%H%x1f%P%x1f%an%x1f%ae%x1f%at%x1f%D%x1f%s",
        };
        args.AddRange(refs);
        args.Add("--");
        var r = await Run(args.ToArray());
        var list = new List<CommitInfo>();
        if (!r.Ok) return list;

        foreach (var record in r.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = record.Split('\x1f');
            if (p.Length < 7) continue;
            list.Add(new CommitInfo
            {
                Hash = p[0].Trim(),
                Parents = p[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                Author = p[2],
                Email = p[3],
                Date = DateTimeOffset.FromUnixTimeSeconds(long.TryParse(p[4], out var t) ? t : 0),
                Refs = ParseDecorations(p[5], remotes),
                Subject = p[6],
            });
        }
        return list;
    }

    /// <summary>Преобразует текст декораций Git в типизированные метки веток и тегов.</summary>
    static List<RefBadge> ParseDecorations(string decorations, IReadOnlyCollection<string> remotes)
    {
        var result = new List<RefBadge>();
        foreach (var raw in decorations.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith("HEAD -> ")) result.Add(new RefBadge(raw[8..], RefKind.Head));
            else if (raw == "HEAD") result.Add(new RefBadge("HEAD", RefKind.Head));
            else if (raw.StartsWith("tag: ")) result.Add(new RefBadge(raw[5..], RefKind.Tag));
            else if (raw.EndsWith("/HEAD")) continue;
            else if (remotes.Any(rm => raw.StartsWith(rm + "/"))) result.Add(new RefBadge(raw, RefKind.Remote));
            else result.Add(new RefBadge(raw, RefKind.Local));
        }
        return result;
    }

    /// <summary>Получает локальные ветки, ветки отслеживания удалённых репозиториев и теги вместе со сведениями о вышестоящих ветках.</summary>
    public async Task<List<RefInfo>> Refs()
    {
        var r = await Run("for-each-ref",
            "--format=%(refname)%1f%(objectname:short)%1f%(upstream)%1f%(upstream:short)%1f%(upstream:track)%1f%(HEAD)%1f%(upstream:remotename)",
            "refs/heads", "refs/remotes", "refs/tags");
        var list = new List<RefInfo>();
        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.TrimEnd('\r').Split('\x1f');
            if (p.Length < 7 || p[0].EndsWith("/HEAD")) continue;
            list.Add(new RefInfo(p[0], p[1], p[2], p[3], p[4], p[5] == "*", p[6]));
        }
        return list;
    }

    /// <summary>Возвращает stash репозитория в порядке отображения Git.</summary>
    public async Task<List<StashInfo>> Stashes()
    {
        var r = await Run("stash", "list", "--format=%gd%x1f%s");
        return r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r').Split('\x1f'))
            .Where(p => p.Length == 2)
            .Select(p => new StashInfo(p[0], p[1]))
            .ToList();
    }

    /// <summary>Получает из вывода status в формате porcelain отслеживаемые, неотслеживаемые и конфликтующие изменения рабочей копии.</summary>
    public async Task<List<FileChange>> Status()
    {
        var r = await Run("status", "--porcelain=v1", "-z", "-uall");
        var list = new List<FileChange>();
        var t = r.Output.Split('\0');
        for (var i = 0; i < t.Length; i++)
        {
            var e = t[i];
            if (e.Length < 4) continue;
            char x = e[0], y = e[1];
            if (x == '!') continue;
            string? old = null;
            if ((x is 'R' or 'C' || y is 'R' or 'C') && i + 1 < t.Length) old = t[++i];
            var conflict = x == 'U' || y == 'U' || (x == 'A' && y == 'A') || (x == 'D' && y == 'D');
            list.Add(new FileChange
            {
                RelPath = e[3..],
                OldPath = old,
                IndexStatus = x,
                WorkStatus = y,
                IsConflict = conflict,
                IsWorkingTree = true,
                Status = conflict ? 'U' : x == '?' ? '?' : x != ' ' ? x : y,
            });
        }
        return list.OrderBy(f => f.RelPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Вершины цепочек коммитов, достижимых из reflog, но не из текущих ссылок (в SmartGit — «Recyclable Commits»).
    /// Из не более 5000 коммитов в порядке --date-order выбирается до limit вершин.
    /// Срок хранения зависит от очистки reflog и объектов Git; гарантии восстановления нет.
    /// </summary>
    public async Task<List<RecyclableCommit>> RecyclableCommits(int limit = 200)
    {
        var r = await Run("-c", "log.showSignature=false", "log", "--reflog", "--date-order", "-n5000",
            "--format=%H%x1f%P%x1f%an%x1f%ae%x1f%at%x1f%s", "--not", "--all");
        if (!r.Ok) return new();
        var commits = new List<CommitInfo>();
        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.TrimEnd('\r').Split('\x1f');
            if (p.Length < 6) continue;
            commits.Add(new CommitInfo
            {
                Hash = p[0],
                Parents = p[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                Author = p[2],
                Email = p[3],
                Date = DateTimeOffset.FromUnixTimeSeconds(long.TryParse(p[4], out var t) ? t : 0),
                Subject = p[5],
            });
        }
        if (commits.Count == 0) return new();

        // Tips: unreachable commits that are not the parent of another unreachable commit.
        var parents = commits.SelectMany(c => c.Parents).ToHashSet();
        var tips = commits.Where(c => !parents.Contains(c.Hash)).Take(limit).ToList();
        var reasons = await LostReasons(tips.Select(c => c.Hash).ToHashSet());
        return tips.Select(c => new RecyclableCommit(c, reasons.GetValueOrDefault(c.Hash, ""))).ToList();
    }

    /// <summary>
    /// Подбирает предполагаемую причину потери ссылки по более новой записи того же reflog; причина может отсутствовать.
    /// Приоритет отдаётся reflog веток перед HEAD, который также содержит переключения веток.
    /// </summary>
    async Task<Dictionary<string, string>> LostReasons(HashSet<string> hashes)
    {
        var r = await Run("log", "-g", "--all", "--format=%H%x1f%gD%x1f%gs");
        var result = new Dictionary<string, string>();
        var fromHead = new HashSet<string>();
        var previousOnRef = new Dictionary<string, string>(); // ref → subject of its newer entry
        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.TrimEnd('\r').Split('\x1f');
            if (p.Length < 3) continue;
            var refName = p[1][..Math.Max(0, p[1].IndexOf("@{", StringComparison.Ordinal))];
            if (hashes.Contains(p[0]) && previousOnRef.TryGetValue(refName, out var lostBy))
            {
                var isHead = refName == "HEAD";
                if (!result.ContainsKey(p[0]) || (fromHead.Contains(p[0]) && !isHead))
                {
                    result[p[0]] = lostBy;
                    if (isHead) fromHead.Add(p[0]); else fromHead.Remove(p[0]);
                }
            }
            previousOnRef[refName] = p[2];
        }
        return result;
    }

    /// <summary>Версия, с которой сравнивается коммит: его первый родитель или пустое дерево для корневого коммита.</summary>
    public static string BaseOf(CommitInfo c) => c.Parents.Length > 0 ? c.Parents[0] : EmptyTree;

    /// <summary>Возвращает отличия от первого родителя (для корневого коммита — от пустого дерева), с распознаванием переименований (-M).</summary>
    public async Task<List<FileChange>> CommitFiles(CommitInfo c)
    {
        var r = await Run("diff", "--name-status", "-z", "-M", "--no-ext-diff", BaseOf(c), c.Hash);
        var list = new List<FileChange>();
        var t = r.Output.Split('\0');
        for (var i = 0; i < t.Length;)
        {
            var st = t[i];
            if (st.Length == 0) { i++; continue; }
            var s = st[0];
            if (s is 'R' or 'C')
            {
                if (i + 2 >= t.Length) break;
                list.Add(new FileChange { Status = s, OldPath = t[i + 1], RelPath = t[i + 2] });
                i += 3;
            }
            else
            {
                if (i + 1 >= t.Length) break;
                list.Add(new FileChange { Status = s, RelPath = t[i + 1] });
                i += 2;
            }
        }
        return list;
    }

    /// <summary>Получает полное сообщение коммита указанной ревизии без завершающих пробельных символов.</summary>
    public async Task<string> CommitMessage(string rev)
    {
        var r = await Run("-c", "log.showSignature=false", "log", "-1", "--format=%B", rev);
        return r.Output.TrimEnd();
    }
}
