namespace ClockworkGit.Models;

/// <summary>Различия файла: исходный unified diff Git (пустой для неотслеживаемого файла) и строки сравнения бок о бок.</summary>
public sealed record DiffResult(string Raw, List<DiffRow> Rows);
