using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace ClockworkGit.UI;

public static class ImageTools
{
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tga", ".ico",
    };

    public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>Decodes into RGBA8888 premultiplied; null when absent or unsupported.</summary>
    public static SKBitmap? Decode(byte[]? data, string path)
    {
        if (data == null || data.Length == 0) return null;
        var src = Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase)
            ? Tga.Decode(data)
            : SKBitmap.Decode(data);
        if (src == null) return null;
        var dst = new SKBitmap(new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(dst))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(src, 0, 0);
        }
        src.Dispose();
        return dst;
    }

    /// <summary>Changed pixels in magenta over a dimmed copy of the new image.</summary>
    public static (SKBitmap Diff, double ChangedPercent) Diff(SKBitmap a, SKBitmap b)
    {
        int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
        var result = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        var pa = a.GetPixelSpan();
        var pb = b.GetPixelSpan();
        var stride = result.RowBytes;
        var buf = new byte[stride * h];
        long changed = 0;
        for (var y = 0; y < h; y++)
        {
            int ra = y * a.RowBytes, rb = y * b.RowBytes, rd = y * stride;
            for (var x = 0; x < w; x++)
            {
                int ia = ra + x * 4, ib = rb + x * 4, id = rd + x * 4;
                var m = 0;
                for (var c = 0; c < 4; c++) m = Math.Max(m, Math.Abs(pa[ia + c] - pb[ib + c]));
                if (m > 0)
                {
                    changed++;
                    var v = (byte)Math.Min(255, 96 + m * 4);
                    buf[id] = v; buf[id + 1] = 0; buf[id + 2] = v; buf[id + 3] = 255;
                }
                else
                {
                    var g = (byte)((pb[ib] + pb[ib + 1] + pb[ib + 2]) / 3 * 0.3);
                    buf[id] = g; buf[id + 1] = g; buf[id + 2] = g; buf[id + 3] = 255;
                }
            }
        }
        Marshal.Copy(buf, 0, result.GetPixels(), buf.Length);
        return (result, w * h == 0 ? 0 : changed * 100.0 / (w * h));
    }

    public static Bitmap ToAvalonia(SKBitmap b)
    {
        var wb = new WriteableBitmap(new PixelSize(b.Width, b.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        using var fb = wb.Lock();
        var rowLen = b.Width * 4;
        var row = new byte[rowLen];
        var src = b.GetPixels();
        for (var y = 0; y < b.Height; y++)
        {
            Marshal.Copy(src + y * b.RowBytes, row, 0, rowLen);
            Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, rowLen);
        }
        return wb;
    }

    public static IBrush CheckerBrush()
    {
        var wb = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            var px = new byte[16 * 4];
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var v = (byte)(((x < 8) ^ (y < 8)) ? 0x3a : 0x2c);
                    px[x * 4] = v; px[x * 4 + 1] = v; px[x * 4 + 2] = v; px[x * 4 + 3] = 255;
                }
                Marshal.Copy(px, 0, fb.Address + y * fb.RowBytes, px.Length);
            }
        }
        return new ImageBrush(wb)
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
        };
    }
}

/// <summary>Minimal TGA reader: true-color/grayscale, raw or RLE, 8/24/32 bit.</summary>
static class Tga
{
    public static SKBitmap? Decode(byte[] d)
    {
        if (d.Length < 18) return null;
        int idLen = d[0], cmapType = d[1], type = d[2];
        int w = d[12] | d[13] << 8, h = d[14] | d[15] << 8, bpp = d[16], desc = d[17];
        if (cmapType != 0 || type is not (2 or 3 or 10 or 11) || w == 0 || h == 0) return null;
        var bytesPP = bpp / 8;
        if (bytesPP is not (1 or 3 or 4)) return null;

        var px = new byte[w * h * 4];
        var pos = 18 + idLen;
        var rle = type >= 9;
        var i = 0;

        void Put(int p)
        {
            var o = i++ * 4;
            if (bytesPP == 1) { px[o] = px[o + 1] = px[o + 2] = d[p]; px[o + 3] = 255; return; }
            px[o] = d[p + 2]; px[o + 1] = d[p + 1]; px[o + 2] = d[p];
            px[o + 3] = bytesPP == 4 ? d[p + 3] : (byte)255;
        }

        while (i < w * h && pos < d.Length)
        {
            if (!rle)
            {
                if (pos + bytesPP > d.Length) break;
                Put(pos); pos += bytesPP;
                continue;
            }
            var header = d[pos++];
            var count = (header & 0x7f) + 1;
            if ((header & 0x80) != 0)
            {
                if (pos + bytesPP > d.Length) break;
                for (var k = 0; k < count && i < w * h; k++) Put(pos);
                pos += bytesPP;
            }
            else
            {
                for (var k = 0; k < count && i < w * h && pos + bytesPP <= d.Length; k++) { Put(pos); pos += bytesPP; }
            }
        }

        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var topDown = (desc & 0x20) != 0;
        var dst = bmp.GetPixels();
        for (var y = 0; y < h; y++)
        {
            var srcRow = topDown ? y : h - 1 - y;
            Marshal.Copy(px, srcRow * w * 4, dst + y * bmp.RowBytes, w * 4);
        }
        return bmp;
    }
}
