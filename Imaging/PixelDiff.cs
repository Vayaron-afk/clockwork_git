using System.Runtime.InteropServices;
using SkiaSharp;

namespace ClockworkGit.Imaging;

public static class PixelDiff
{
    /// <summary>
    /// Changed pixels in magenta (brighter = bigger change) over a dimmed copy of the new image.
    /// Images of different size are compared over their common top-left area.
    /// </summary>
    public static (SKBitmap Diff, double ChangedPercent) Compute(SKBitmap a, SKBitmap b)
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
}
