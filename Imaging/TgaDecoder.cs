using System.Runtime.InteropServices;
using SkiaSharp;

namespace ClockworkGit.Imaging;

/// <summary>Minimal TGA reader: true-color/grayscale, raw or RLE, 8/24/32 bit.</summary>
static class TgaDecoder
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
