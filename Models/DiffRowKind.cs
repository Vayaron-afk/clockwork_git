namespace ClockworkGit.Models;

/// <summary>Определяет тип отображаемой строки diff: контекст, изменение, заголовок блока или сообщение.</summary>
public enum DiffRowKind { Context, Change, Hunk, Message }
