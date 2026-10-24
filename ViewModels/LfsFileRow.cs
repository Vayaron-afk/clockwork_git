using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.ViewModels;

/// <summary>Строка вкладки «Файлы» окна LFS.</summary>
public sealed class LfsFileRow(LfsFile file)
{
    static readonly IBrush Present = new ImmutableSolidColorBrush(Color.Parse("#5fbf5f"));
    static readonly IBrush Missing = new ImmutableSolidColorBrush(Color.Parse("#8a8f98"));

    public LfsFile File { get; } = file;
    /// <summary>Путь к LFS-файлу относительно корня репозитория.</summary>
    public string Path => File.Path;
    /// <summary>Заполненный маркер, если объект LFS есть локально; иначе пустой маркер.</summary>
    public string Marker => File.Downloaded ? "●" : "○";
    /// <summary>Кисть для маркера наличия локальной копии.</summary>
    public IBrush MarkerBrush => File.Downloaded ? Present : Missing;
    /// <summary>Размер объекта LFS в удобном для чтения формате.</summary>
    public string SizeText => Format.Size(File.Size);
    /// <summary>Первые десять символов идентификатора объекта LFS.</summary>
    public string ShortOid => File.Oid.Length > 10 ? File.Oid[..10] : File.Oid;
}
