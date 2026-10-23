using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace ClockworkGit.Imaging;

/// <summary>Bridges decoded pixels into Avalonia. Call on the UI thread.</summary>
public static class AvaloniaBitmaps
{
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

    /// <summary>Grey checkerboard behind images, so transparency is visible.</summary>
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
