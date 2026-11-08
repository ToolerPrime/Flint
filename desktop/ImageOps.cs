/* Pixel operations on frames. Kept apart from the form so they stay testable
 * and so the form is only wiring. */
using System.Drawing.Imaging;

namespace Flint;

public static class ImageOps
{
    public static Bitmap NewBitmap(int w, int h)
    {
        var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        return bmp;
    }

    /// <summary>Copy of the pixels as BGRA, which is the in-memory order.</summary>
    public static byte[] GetPixels(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var buf = new byte[bmp.Width * bmp.Height * 4];
            // Rows may be padded, so copy row by row rather than in one block.
            for (int y = 0; y < bmp.Height; y++)
            {
                IntPtr src = data.Scan0 + y * data.Stride;
                System.Runtime.InteropServices.Marshal.Copy(src, buf, y * bmp.Width * 4, bmp.Width * 4);
            }
            return buf;
        }
        finally { bmp.UnlockBits(data); }
    }

    public static void SetPixels(Bitmap bmp, byte[] buf)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bmp.Height; y++)
            {
                IntPtr dst = data.Scan0 + y * data.Stride;
                System.Runtime.InteropServices.Marshal.Copy(buf, y * bmp.Width * 4, dst, bmp.Width * 4);
            }
        }
        finally { bmp.UnlockBits(data); }
    }

    /// <summary>Copy one rectangle out of a full-image BGRA buffer.</summary>
    public static byte[] CropRegion(byte[] buf, int stridePixels, Rectangle r)
    {
        var o = new byte[r.Width * r.Height * 4];
        for (int y = 0; y < r.Height; y++)
        {
            Buffer.BlockCopy(buf, ((r.Y + y) * stridePixels + r.X) * 4,
                             o, y * r.Width * 4, r.Width * 4);
        }
        return o;
    }

    /// <summary>Write a rectangle of BGRA back into a bitmap.</summary>
    public static void SetRegion(Bitmap bmp, Rectangle r, byte[] region)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        if (!rect.Contains(r)) return;
        var data = bmp.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < r.Height; y++)
            {
                IntPtr dst = data.Scan0 + y * data.Stride;
                System.Runtime.InteropServices.Marshal.Copy(region, y * r.Width * 4, dst, r.Width * 4);
            }
        }
        finally { bmp.UnlockBits(data); }
    }

    /* Every transform below works on raw bytes. GDI+ drawing converts
     * through premultiplied alpha even with SourceCopy, and measured it changed
     * about 92% of semi-transparent pixels on a plain copy. Pixel art needs
     * the exact values back, so GDI+ drawing is used only where resampling is
     * the point (smooth scaling) or for on-screen display. */

    public static Bitmap FromPixels(int w, int h, byte[] px)
    {
        var b = NewBitmap(w, h);
        SetPixels(b, px);
        return b;
    }

    public static Bitmap Clone(Bitmap src) => FromPixels(src.Width, src.Height, GetPixels(src));

    /// <summary>
    /// Copy an image's stored pixels into a new 32-bit bitmap without drawing
    /// it. Used for import and project loading, so nothing is re-rendered.
    /// </summary>
    public static Bitmap ReadExact(Image img)
    {
        if (img is Bitmap raster) return FromPixels(img.Width, img.Height, GetPixels(raster));
        var b = NewBitmap(img.Width, img.Height);          // vector formats only
        using var g = Graphics.FromImage(b);
        g.DrawImage(img, 0, 0, img.Width, img.Height);
        return b;
    }

    /// <summary>Place the image at dx,dy on a transparent canvas of a new size.</summary>
    public static Bitmap Reframe(Bitmap src, int w, int h, int dx, int dy) =>
        FromPixels(w, h, Blit(GetPixels(src), src.Width, src.Height, w, h, dx, dy));

    /// <summary>A rectangle of the image; any part outside it comes out transparent.</summary>
    public static Bitmap Crop(Bitmap src, Rectangle r) =>
        FromPixels(r.Width, r.Height, Blit(GetPixels(src), src.Width, src.Height, r.Width, r.Height, -r.X, -r.Y));

    private static byte[] Blit(byte[] s, int sw, int sh, int w, int h, int dx, int dy)
    {
        var d = new byte[w * h * 4];
        int x0 = Math.Max(0, dx), x1 = Math.Min(w, dx + sw);
        if (x1 <= x0) return d;
        int rowBytes = (x1 - x0) * 4;
        for (int y = Math.Max(0, dy); y < Math.Min(h, dy + sh); y++)
            Buffer.BlockCopy(s, ((y - dy) * sw + (x0 - dx)) * 4, d, (y * w + x0) * 4, rowBytes);
        return d;
    }

    public static Bitmap Flip(Bitmap src, bool horizontal)
    {
        int w = src.Width, h = src.Height;
        var s = GetPixels(src);
        var d = new byte[s.Length];
        for (int y = 0; y < h; y++)
        {
            int ty = horizontal ? y : h - 1 - y;
            if (horizontal)
                for (int x = 0; x < w; x++)
                    Buffer.BlockCopy(s, (y * w + x) * 4, d, (ty * w + (w - 1 - x)) * 4, 4);
            else
                Buffer.BlockCopy(s, y * w * 4, d, ty * w * 4, w * 4);
        }
        return FromPixels(w, h, d);
    }

    public static Bitmap Scale(Bitmap src, int w, int h, bool smooth)
    {
        if (!smooth)
        {
            // Nearest neighbour by pixel centres: every output pixel is an exact
            // copy of one input pixel, so no new colours and no alpha drift.
            int sw = src.Width, sh = src.Height;
            var s = GetPixels(src);
            var d = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int sy = Math.Min(sh - 1, (int)((y + 0.5) * sh / h));
                for (int x = 0; x < w; x++)
                {
                    int sx = Math.Min(sw - 1, (int)((x + 0.5) * sw / w));
                    Buffer.BlockCopy(s, (sy * sw + sx) * 4, d, (y * w + x) * 4, 4);
                }
            }
            return FromPixels(w, h, d);
        }
        var dst = NewBitmap(w, h);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.DrawImage(src, new Rectangle(0, 0, w, h));
        return dst;
    }

    /// <summary>
    /// Straight-alpha "source over" of <paramref name="s"/> onto <paramref name="d"/>
    /// at ox,oy, with an extra opacity 0..255. Onto a transparent pixel at full
    /// opacity the result is an exact copy, which is what keeps a single-layer
    /// export identical to what was drawn.
    /// </summary>
    public static void Over(byte[] d, int dw, int dh, byte[] s, int sw, int sh, int ox, int oy, int opacity)
    {
        if (opacity <= 0) return;
        for (int y = Math.Max(0, oy); y < Math.Min(dh, oy + sh); y++)
        {
            for (int x = Math.Max(0, ox); x < Math.Min(dw, ox + sw); x++)
            {
                int si = ((y - oy) * sw + (x - ox)) * 4, di = (y * dw + x) * 4;
                int sa = s[si + 3];
                if (opacity < 255) sa = (sa * opacity + 127) / 255;
                if (sa == 0) continue;
                int da = d[di + 3];
                if (sa == 255 || da == 0)
                {
                    d[di] = s[si]; d[di + 1] = s[si + 1]; d[di + 2] = s[si + 2]; d[di + 3] = (byte)sa;
                    continue;
                }
                // Porter-Duff over in straight alpha, in units of 255*255.
                int inv = 255 - sa;
                int oa = sa * 255 + da * inv;                       // output alpha * 255
                for (int c = 0; c < 3; c++)
                {
                    int v = (s[si + c] * sa * 255 + d[di + c] * da * inv + oa / 2) / oa;
                    d[di + c] = (byte)v;
                }
                d[di + 3] = (byte)((oa + 127) / 255);
            }
        }
    }

    /// <summary>Make one colour transparent, within a tolerance.</summary>
    public static void ColorKey(Bitmap bmp, Color key, int tolerance)
    {
        var buf = GetPixels(bmp);
        int limit = tolerance * tolerance * 3;
        for (int i = 0; i < buf.Length; i += 4)
        {
            if (buf[i + 3] == 0) continue;
            int db = buf[i] - key.B, dg = buf[i + 1] - key.G, dr = buf[i + 2] - key.R;
            if (dr * dr + dg * dg + db * db <= limit)
            {
                buf[i] = 0; buf[i + 1] = 0; buf[i + 2] = 0; buf[i + 3] = 0;
            }
        }
        SetPixels(bmp, buf);
    }

    /// <summary>
    /// Tightest box holding an opaque pixel in ANY of the frames, so trimming
    /// an animation keeps it registered against itself. Empty if all clear.
    /// </summary>
    public static Rectangle ContentBounds(IEnumerable<Bitmap> frames)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        foreach (var bmp in frames)
        {
            var buf = GetPixels(bmp);
            for (int y = 0; y < bmp.Height; y++)
            {
                int row = y * bmp.Width * 4;
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (buf[row + x * 4 + 3] == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }
        if (maxX < 0) return Rectangle.Empty;
        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /* Floyd-Steinberg, serpentine. Off by default: on pixel art the noise
     * reads as dirt rather than as extra colour. */
    public static byte[] Dither(byte[] bgra, int w, int h, Palette pal,
                                Quantizer.Indexer ix, int offset)
    {
        var err = new float[w * h * 3];
        var outIdx = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            bool leftToRight = (y & 1) == 0;
            for (int i = 0; i < w; i++)
            {
                int x = leftToRight ? i : w - 1 - i;
                int p = y * w + x, o = p * 4, e = p * 3;
                if (bgra[o + 3] < 128) { outIdx[p] = 0; continue; }
                float r = Math.Clamp(bgra[o + 2] + err[e], 0, 255);
                float g = Math.Clamp(bgra[o + 1] + err[e + 1], 0, 255);
                float b = Math.Clamp(bgra[o + 0] + err[e + 2], 0, 255);
                int idx = ix.Nearest((byte)r, (byte)g, (byte)b);
                outIdx[p] = (byte)(idx + offset);
                float dr = r - pal.R[idx], dg = g - pal.G[idx], db = b - pal.B[idx];
                int fwd = leftToRight ? 1 : -1;
                void Spread(int nx, int ny, float f)
                {
                    if (nx < 0 || nx >= w || ny >= h) return;
                    int ne = (ny * w + nx) * 3;
                    err[ne] += dr * f; err[ne + 1] += dg * f; err[ne + 2] += db * f;
                }
                Spread(x + fwd, y, 7f / 16);
                Spread(x - fwd, y + 1, 3f / 16);
                Spread(x, y + 1, 5f / 16);
                Spread(x + fwd, y + 1, 1f / 16);
            }
        }
        return outIdx;
    }
}
