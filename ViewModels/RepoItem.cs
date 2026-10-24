using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.ViewModels;

/// <summary>Репозиторий в списке репозиториев с состоянием рабочей копии и синхронизации.</summary>
public sealed class RepoItem(string path) : Observable
{
    static readonly IBrush Quiet = new ImmutableSolidColorBrush(Color.Parse("#8a8f98"));
    static readonly IBrush Incoming = new ImmutableSolidColorBrush(Color.Parse("#6aa6ff"));
    static readonly IBrush Failed = new ImmutableSolidColorBrush(Color.Parse("#e06060"));

    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));

    string? _branch;
    public string? Branch
    {
        get => _branch;
        set { if (Set(ref _branch, value)) Raise(nameof(Display)); }
    }

    /// <summary>Имя репозитория и, если доступно, текущая ветка.</summary>
    public string Display => Branch == null ? Name : $"{Name} ({Branch})";

    RepoSummary? _summary;
    /// <summary>Последняя сводка git status; null до загрузки, при отсутствующем каталоге или ошибке Git.</summary>
    public RepoSummary? Summary
    {
        get => _summary;
        set
        {
            if (!Set(ref _summary, value)) return;
            if (value != null) Branch = value.Branch ?? "detached";
            RaiseStatus();
        }
    }

    DateTime? _lastFetch;
    /// <summary>Время завершения последней фоновой попытки fetch, включая неуспешную; ручной Fetch основного окна его не обновляет.</summary>
    public DateTime? LastFetch { get => _lastFetch; set { if (Set(ref _lastFetch, value)) RaiseStatus(); } }

    bool _fetching;
    public bool Fetching { get => _fetching; set { if (Set(ref _fetching, value)) RaiseStatus(); } }

    string? _fetchError;
    public string? FetchError { get => _fetchError; set { if (Set(ref _fetchError, value)) RaiseStatus(); } }

    /// <summary>Формат «⟳ ✎3 ↑1 ↓2»: загрузка, изменённые файлы, коммиты для отправки и получения.</summary>
    public string Badges
    {
        get
        {
            var parts = new List<string>();
            if (Fetching) parts.Add("⟳");
            if (FetchError != null) parts.Add("⚠");
            if (Summary is { } s)
            {
                if (s.Changed > 0) parts.Add($"✎{s.Changed}");
                if (s.Ahead > 0) parts.Add($"↑{s.Ahead}");
                if (s.Behind > 0) parts.Add($"↓{s.Behind}");
            }
            return string.Join(" ", parts);
        }
    }

    /// <summary>Цвет меток состояния репозитория; приоритет имеют ошибки получения и входящие изменения.</summary>
    public IBrush BadgeBrush => FetchError != null ? Failed : Summary is { Behind: > 0 } ? Incoming : Quiet;

    /// <summary>Многострочная подсказка с путём репозитория, состоянием рабочей копии, счётчиками синхронизации и состоянием получения.</summary>
    public string Tip
    {
        get
        {
            var lines = new List<string> { Path };
            if (Summary is { } s)
            {
                lines.Add(s.Changed == 0 ? "Working tree clean" : $"{s.Changed} changed file(s)");
                lines.Add(s.Upstream == null ? "No upstream branch"
                    : s.Ahead == 0 && s.Behind == 0 ? $"In sync with {s.Upstream}"
                    : $"{s.Ahead} to push, {s.Behind} to pull ({s.Upstream})");
            }
            else lines.Add("Not available (folder missing or not a git repository)");
            lines.Add(LastFetch is { } t ? $"Fetched at {t:HH:mm}" : "Not fetched by ClockworkGit yet");
            if (FetchError != null) lines.Add("Last fetch failed: " + FetchError);
            return string.Join("\n", lines);
        }
    }

    /// <summary>Уведомляет привязки о возможном изменении меток, цвета и подсказки состояния.</summary>
    void RaiseStatus()
    {
        Raise(nameof(Badges));
        Raise(nameof(BadgeBrush));
        Raise(nameof(Tip));
    }
}
