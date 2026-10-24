namespace ClockworkGit.Models;

/// <summary>Блокировка сервера LFS; IsMine определяется через locks --verify, при резервном запросе без проверки владения равен false.</summary>
public sealed record LfsLock(string Id, string Path, string Owner, DateTimeOffset LockedAt, bool IsMine)
{
    /// <summary>Имя владельца блокировки; для подтверждённой собственной блокировки отображается «you».</summary>
    public string OwnerText => IsMine ? "you" : Owner;
    /// <summary>Дата создания блокировки в формате <see cref="Format.RelativeDate"/>.</summary>
    public string SinceText => Format.RelativeDate(LockedAt);
}
