namespace ClockworkGit.Models;

/// <summary>Снимок конкретного index.lock, который пользователь разрешил удалить.</summary>
public sealed record IndexLockInfo(string FullPath, long Length, DateTime LastWriteUtc, string ContentHash);
