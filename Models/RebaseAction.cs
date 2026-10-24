namespace ClockworkGit.Models;

/// <summary>Действие интерактивного rebase над коммитом.</summary>
public enum RebaseAction
{
    Pick,
    /// <summary>Сохранить изменения и использовать отредактированное сообщение.</summary>
    Reword,
    /// <summary>Объединить с предыдущим коммитом, сохранив оба сообщения.</summary>
    Squash,
    /// <summary>Объединить с предыдущим коммитом, отбросив это сообщение.</summary>
    Fixup,
    /// <summary>Удалить коммит и его изменения из истории после rebase.</summary>
    Drop,
}
