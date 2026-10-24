using System.Runtime.InteropServices;
using SkiaSharp;

namespace ClockworkGit.Imaging;

public static class PixelDiff
{
    /// <summary>
    /// Changed pixels in magenta (brighter = bigger change) over a dimmed copy of the new image, comparing only
    /// the channels in <paramref name="channel"/>. Inputs are unpremultiplied RGBA8888; images of different size
    /// are compared over their common top-left area.
    /// </summary>
    public static (SKBitmap Diff, double ChangedPercent) Compute(SKBitmap a, SKBitmap b, ImageChannel channel = ImageChannel.RGBA)
    {
        int[] channels = channel switch
        {
            ImageChannel.RGB => [0, 1, 2],
            ImageChannel.R => [0],
            ImageChannel.G => [1],
            ImageChannel.B => [2],
            ImageChannel.A => [3],
            _ => [0, 1, 2, 3],
        };
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
                foreach (var c in channels) m = Math.Max(m, Math.Abs(pa[ia + c] - pb[ib + c]));
                if (m > 0)
                {
                    changed++;
                    var v = (byte)Math.Min(255, 96 + m * 4);
                    buf[id] = v; buf[id + 1] = 0; buf[id + 2] = v; buf[id + 3] = 255;
                }
                else
                {
                    var sum = 0;
                    foreach (var c in channels) sum += pb[ib + c];
                    var g = (byte)(sum * 0.3 / channels.Length);
                    buf[id] = g; buf[id + 1] = g; buf[id + 2] = g; buf[id + 3] = 255;
                }
            }
        }
        Marshal.Copy(buf, 0, result.GetPixels(), buf.Length);
        return (result, w * h == 0 ? 0 : changed * 100.0 / (w * h));
    }
}
