namespace ClockworkGit.Imaging;

/// <summary>Какие файлы отображаются как изображения и каким декодером они обрабатываются.</summary>
public static class ImageFormats
{
    /// <summary>Декодируется средствами Skia; при неудаче используется ImageMagick.</summary>
    internal static readonly HashSet<string> SkiaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".webp", ".ico",
    };

    /// <summary>Декодируется только средствами ImageMagick.</summary>
    internal static readonly HashSet<string> MagickExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".svg", ".svgz", ".psd", ".psb", ".tif", ".tiff", ".exr", ".hdr", ".dds", ".avif", ".heic", ".heif",
        ".jxl", ".cur", ".pnm", ".ppm", ".pgm", ".pbm", ".xcf",
    };

    /// <summary>Определяет, поддерживается ли расширение файла одним из декодеров изображений.</summary>
    public static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path);
        return SkiaExtensions.Contains(ext) || MagickExtensions.Contains(ext) || ext.Equals(".tga", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Текстовые форматы изображений, которые также можно отображать как текстовый diff.</summary>
    public static bool IsTextImage(string path) => Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase);
}
