namespace ClockworkGit.Models;

/// <summary>Ссылка из git for-each-ref; <see cref="Track"/> содержит, например, «[ahead 2, behind 1]» или «[gone]».</summary>
public sealed record RefInfo(
    string FullName, string Hash, string Upstream, string UpstreamShort, string Track, bool IsHead, string UpstreamRemote)
{
    /// <summary>Определяет по полному имени, является ли ссылка локальной веткой, удалённой веткой или тегом.</summary>
    public RefKind Kind =>
        FullName.StartsWith("refs/heads/") ? RefKind.Local
        : FullName.StartsWith("refs/remotes/") ? RefKind.Remote
        : RefKind.Tag;

    /// <summary>Имя ссылки без префикса <c>refs/heads/</c>, <c>refs/remotes/</c> или <c>refs/tags/</c>.</summary>
    public string ShortName =>
        FullName.StartsWith("refs/heads/") ? FullName["refs/heads/".Length..]
        : FullName.StartsWith("refs/remotes/") ? FullName["refs/remotes/".Length..]
        : FullName.StartsWith("refs/tags/") ? FullName["refs/tags/".Length..]
        : FullName;

    /// <summary>Показывает, что настроенная upstream-ветка больше не существует (Git вернул [gone]).</summary>
    public bool IsGone => Track == "[gone]";

    /// <summary>Отображает опережение/отставание, например «↑2 ↓1»; пусто, если ветки синхронизированы.</summary>
    public string TrackShort =>
        IsGone ? "" : Track.Trim('[', ']').Replace("ahead ", "↑").Replace("behind ", "↓").Replace(",", "");
}
