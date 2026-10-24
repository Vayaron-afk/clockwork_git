namespace ClockworkGit.Models;

/// <summary>
/// Фрагмент трёхстороннего слияния: строки, слитые без конфликта (<see cref="Common"/>), либо конфликт
/// со строками каждой стороны. Завершающий '\r' каждой строки сохраняется при разборе CRLF.
/// </summary>
public sealed record MergeChunk(List<string> Common, List<string>? Ours = null, List<string>? Base = null, List<string>? Theirs = null)
{
    /// <summary>Показывает, является ли фрагмент конфликтом, а не успешно слитыми строками.</summary>
    public bool IsConflict => Ours != null;
}
