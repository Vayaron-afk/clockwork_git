namespace ClockworkGit.Imaging;

/// <summary>Which files are shown as pictures, and by which decoder.</summary>
public static class ImageFormats
{
    /// <summary>Decoded by Skia directly (with ImageMagick as a fallback).</summary>
    internal static readonly HashSet<string> SkiaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".webp", ".ico",
    };

    /// <summary>Decoded by ImageMagick only.</summary>
    internal static readonly HashSet<string> MagickExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".svg", ".svgz", ".psd", ".psb", ".tif", ".tiff", ".exr", ".hdr", ".dds", ".avif", ".heic", ".heif",
        ".jxl", ".cur", ".pnm", ".ppm", ".pgm", ".pbm", ".xcf",
    };

    public static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path);
        return SkiaExtensions.Contains(ext) || MagickExtensions.Contains(ext) || ext.Equals(".tga", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Text-based image formats, which can also be shown as a text diff.</summary>
    public static bool IsTextImage(string path) => Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase);
}
