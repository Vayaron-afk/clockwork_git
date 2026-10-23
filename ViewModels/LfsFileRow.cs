using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ClockworkGit.ViewModels;

/// <summary>A row of the LFS window's Files tab.</summary>
public sealed class LfsFileRow(LfsFile file)
{
    static readonly IBrush Present = new ImmutableSolidColorBrush(Color.Parse("#5fbf5f"));
    static readonly IBrush Missing = new ImmutableSolidColorBrush(Color.Parse("#8a8f98"));

    public LfsFile File { get; } = file;
    public string Path => File.Path;
    public string Marker => File.Downloaded ? "●" : "○";
    public IBrush MarkerBrush => File.Downloaded ? Present : Missing;
    public string SizeText => Format.Size(File.Size);
    public string ShortOid => File.Oid.Length > 10 ? File.Oid[..10] : File.Oid;
}
