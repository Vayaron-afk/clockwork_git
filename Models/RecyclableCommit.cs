namespace ClockworkGit.Models;

/// <summary>
/// Вершина цепочки коммитов, недостижимой из текущих ссылок, но достижимой из reflog
/// (например, после reset, amend или rebase). <see cref="Reason"/> содержит предполагаемое действие reflog,
/// оставившее коммит без ссылки, или пустую строку, если причину установить не удалось.
/// </summary>
public sealed record RecyclableCommit(CommitInfo Commit, string Reason);
