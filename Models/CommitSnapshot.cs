namespace ClockworkGit.Models;

/// <summary>Проверенное дерево индекса, HEAD и статусы для подтверждения конкретного содержимого коммита.</summary>
public sealed record CommitSnapshot(string TreeHash, string? HeadHash, IReadOnlyList<FileChange> Files);
