namespace ClockworkGit.Models;

/// <summary>A file locked on the LFS server; <see cref="IsMine"/> comes from git lfs locks --verify.</summary>
public sealed record LfsLock(string Id, string Path, string Owner, DateTimeOffset LockedAt, bool IsMine)
{
    public string OwnerText => IsMine ? "you" : Owner;
    public string SinceText => Format.RelativeDate(LockedAt);
}
