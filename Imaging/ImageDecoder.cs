using System.Runtime.InteropServices;
using ImageMagick;
using SkiaSharp;

namespace ClockworkGit.Imaging;

/// <summary>
/// Bytes of any supported format → RGBA8888 <b>unpremultiplied</b> <see cref="SKBitmap"/>, so colour hidden
/// under zero alpha survives (packed texture maps keep data there).
/// </summary>
public static class ImageDecoder
{
    /// <summary>Null when there is no data or the format cannot be decoded.</summary>
    public static SKBitmap? Decode(byte[]? data, string path)
    {
        if (data == null || data.Length == 0) return null;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var src = ext == ".tga" ? TgaDecoder.Decode(data) ?? DecodeMagick(data, ext)
            : ImageFormats.SkiaExtensions.Contains(ext) ? DecodeSkia(data) ?? DecodeMagick(data, ext)
            : DecodeMagick(data, ext);
        if (src == null) return null;
        var dst = new SKBitmap(new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var pixmap = src.PeekPixels();
        if (pixmap == null || !pixmap.ReadPixels(dst.Info, dst.GetPixels(), dst.RowBytes))
        {
            dst.Dispose();
            dst = src.Copy(SKColorType.Rgba8888);
        }
        if (!ReferenceEquals(dst, src)) src.Dispose();
        return dst;
    }

    /// <summary>Skia decode asking for unpremultiplied output, so it does not drop colour under transparent pixels.</summary>
    static SKBitmap? DecodeSkia(byte[] data)
    {
        using var codec = SKCodec.Create(new MemoryStream(data));
        if (codec == null) return SKBitmap.Decode(data);
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bmp = new SKBitmap(info);
        var result = codec.GetPixels(info, bmp.GetPixels());
        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput) return bmp;
        bmp.Dispose();
        return SKBitmap.Decode(data);
    }

    /// <summary>True when some pixel is not fully opaque.</summary>
    public static bool UsesAlpha(SKBitmap b)
    {
        var px = b.GetPixelSpan();
        for (var y = 0; y < b.Height; y++)
        {
            var row = y * b.RowBytes;
            for (var x = 0; x < b.Width; x++)
                if (px[row + x * 4 + 3] != 255) return true;
        }
        return false;
    }

    /// <summary>
    /// ImageMagick fallback: PSD (merged composite), TIFF, EXR/HDR, DDS, AVIF, HEIC, SVG…
    /// Only the first frame/layer is read; vector SVG is rasterized so that the long side is about 1024 px.
    /// </summary>
    static SKBitmap? DecodeMagick(byte[] data, string ext)
    {
        try
        {
            var settings = new MagickReadSettings
            {
                BackgroundColor = MagickColors.Transparent,
                FrameIndex = 0,
                FrameCount = 1,
            };
            if (ext is ".svg" or ".svgz")
            {
                settings.Format = MagickFormat.Svg;
                var info = new MagickImageInfo(data, new MagickReadSettings { Format = MagickFormat.Svg });
                var baseDensity = info.Density?.X is > 0 and var d ? d : 96;
                // No upper cap: icons with different viewBox sizes must still render to the same size to be comparable.
                var scale = Math.Max(1, 1024.0 / Math.Max(1, Math.Max(info.Width, info.Height)));
                settings.Density = new Density(baseDensity * scale);
            }

            using var img = new MagickImage(data, settings);
            img.AutoOrient();
            if (img.ColorSpace is not (ColorSpace.sRGB or ColorSpace.Gray or ColorSpace.LinearGray))
                img.ColorSpace = ColorSpace.sRGB; // linear EXR/HDR, CMYK PSD/JPEG
            img.Depth = 8;
            var w = (int)img.Width;
            var h = (int)img.Height;
            var pixels = img.ToByteArray(MagickFormat.Rgba);

            var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            var dst = bmp.GetPixels();
            for (var y = 0; y < h; y++)
                Marshal.Copy(pixels, y * w * 4, dst + y * bmp.RowBytes, w * 4);
            return bmp;
        }
        catch (MagickException)
        {
            return null;
        }
    }
}
