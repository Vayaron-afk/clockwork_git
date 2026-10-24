using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace ClockworkGit.Imaging;

/// <summary>Пиксели RGBA8888 с цветовыми компонентами, предварительно умноженными на альфа-канал, подготовленные для растрового изображения Avalonia.</summary>
public sealed record RenderedImage(byte[] Pixels, int Width, int Height);

/// <summary>Передаёт декодированные пиксели в Avalonia: <see cref="Render"/> можно выполнять в любом потоке, <see cref="ToAvalonia"/> — в UI-потоке.</summary>
public static class AvaloniaBitmaps
{
    /// <summary>Преобразует RGBA8888 без умножения на альфу в premultiplied RGBA, непрозрачный RGB или один канал в оттенках серого.</summary>
    public static RenderedImage Render(SKBitmap b, ImageChannel channel = ImageChannel.RGBA)
    {
        int w = b.Width, h = b.Height;
        var src = b.GetPixelSpan();
        var dst = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            int rs = y * b.RowBytes, rd = y * w * 4;
            for (var x = 0; x < w; x++)
            {
                int s = rs + x * 4, d = rd + x * 4;
                byte r = src[s], g = src[s + 1], bl = src[s + 2], a = src[s + 3];
                switch (channel)
                {
                    case ImageChannel.RGBA:
                        dst[d] = (byte)(r * a / 255); dst[d + 1] = (byte)(g * a / 255); dst[d + 2] = (byte)(bl * a / 255); dst[d + 3] = a;
                        break;
                    case ImageChannel.RGB:
                        dst[d] = r; dst[d + 1] = g; dst[d + 2] = bl; dst[d + 3] = 255;
                        break;
                    default:
                        var v = channel switch { ImageChannel.R => r, ImageChannel.G => g, ImageChannel.B => bl, _ => a };
                        dst[d] = v; dst[d + 1] = v; dst[d + 2] = v; dst[d + 3] = 255;
                        break;
                }
            }
        }
        return new RenderedImage(dst, w, h);
    }

    /// <summary>Копирует отрисованный буфер RGBA в растровое изображение Avalonia, доступное для записи.</summary>
    public static Bitmap ToAvalonia(RenderedImage image)
    {
        var wb = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        using var fb = wb.Lock();
        var rowLen = image.Width * 4;
        for (var y = 0; y < image.Height; y++)
            Marshal.Copy(image.Pixels, y * rowLen, fb.Address + y * fb.RowBytes, rowLen);
        return wb;
    }

    /// <summary>Серая шахматная подложка под изображениями, позволяющая видеть прозрачные области.</summary>
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
