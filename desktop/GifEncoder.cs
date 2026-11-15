/* Minimal GIF89a encoder: one global palette, animation, transparency.
 * No dependencies. Ported from the JavaScript version in this repo, which is
 * verified pixel-exact against an independent decoder (Pillow).
 *
 * One global colour table shared by every frame is the point: a per-frame
 * palette is what makes exported sprite animations shimmer.
 *
 * Optional size work, written for Flint: frame cropping and differencing
 * (each frame stores only the rectangle that changed; unchanged pixels inside
 * it become the transparent index) and lossy LZW. */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;

namespace Flint;

public sealed class GifFrame
{
    public byte[] Indices = Array.Empty<byte>();
    public int DelayMs = 100;
}

public static class GifEncoder
{
    public static byte[] Encode(int width, int height, IList<GifFrame> frames,
                                Palette palette, int transparentIndex = -1, bool loop = true,
                                bool optimize = false, int lossy = 0, CancellationToken ct = default)
        => EncodeEx(width, height, frames, palette, transparentIndex, loop, optimize, lossy, ct).Data;

    /// <summary>
    /// Encode, returning the file and the picture each frame shows (palette
    /// indices, full canvas). Lossy compression changes pixels, so a preview
    /// must use these. With <paramref name="optimize"/> each frame after the
    /// first stores only the rectangle that changed, unchanged pixels inside
    /// it become the transparent index, and the smaller of that and the plain
    /// encoding is kept.
    /// </summary>
    public static (byte[] Data, byte[][] Shown) EncodeEx(int width, int height, IList<GifFrame> frames,
                                Palette palette, int transparentIndex = -1, bool loop = true,
                                bool optimize = false, int lossy = 0, CancellationToken ct = default)
    {
        if (frames.Count == 0) throw new InvalidOperationException("no frames");
        if (palette.Count == 0) throw new InvalidOperationException("empty palette");
        if (palette.Count > 256) throw new InvalidOperationException("palette over 256 entries");
        foreach (var f in frames)
        {
            if (f.Indices.Length != width * height)
                throw new InvalidOperationException(
                    $"frame has {f.Indices.Length} indices, expected {width * height}");
        }
        var best = Write(width, height, frames, palette, transparentIndex, loop, false, lossy, false);
        if (!optimize || width * height == 0) return best;
        // Differencing with a spare index can cost a wider colour table, so
        // without clear pixels also try cropping alone; keep the smallest.
        foreach (bool spare in transparentIndex >= 0 ? new[] { true } : new[] { true, false })
        {
            ct.ThrowIfCancellationRequested();
            var opt = Write(width, height, frames, palette, transparentIndex, loop, true, lossy, spare);
            if (opt.Data.Length < best.Data.Length) best = opt;
        }
        return best;
    }

    internal static byte[] TestWrite(int width, int height, IList<GifFrame> frames, Palette palette,
                                     int transparentIndex, bool loop, bool optimize, int lossy, bool spare)
        => Write(width, height, frames, palette, transparentIndex, loop, optimize, lossy, spare).Data;

    private static (byte[] Data, byte[][] Shown) Write(int width, int height, IList<GifFrame> frames,
                                Palette palette, int transparentIndex, bool loop, bool optimize, int lossy, bool spare)
    {
        bool clearDoc = transparentIndex >= 0;
        // "Keep what is underneath" marker for differenced frames: the
        // transparent index, or a spare slot when the picture has no clear pixels.
        int keep = transparentIndex;
        if (optimize && spare && keep < 0 && palette.Count < 256) keep = palette.Count;
        // Lossy: a pixel already showing a colour this close counts as unchanged,
        // so one frame's lossy rewrite does not force the next frame to redraw it.
        int tol2 = lossy <= 0 ? 0 : lossy * lossy / 4;
        bool Same(byte a, byte b)
        {
            if (a == b) return true;
            if (tol2 == 0 || a == transparentIndex || b == transparentIndex || a >= palette.Count || b >= palette.Count) return false;
            int dr = palette.R[a] - palette.R[b], dg = palette.G[a] - palette.G[b], db = palette.B[a] - palette.B[b];
            return dr * dr + dg * dg + db * db <= tol2;
        }
        int count = Math.Max(palette.Count, keep + 1);

        // The colour table size must be a power of two, at least 2.
        int tableBits = 1;
        while (1 << tableBits < count) tableBits++;
        int tableSize = 1 << tableBits;
        int minCodeSize = Math.Max(2, tableBits);

        using var ms = new MemoryStream();
        void Byte(int b) => ms.WriteByte((byte)(b & 0xff));
        void Short(int v) { Byte(v); Byte(v >> 8); }        // GIF integers are little-endian
        void Ascii(string s) { foreach (char ch in s) Byte(ch); }

        Ascii("GIF89a");
        Short(width);
        Short(height);
        Byte(0x80 | ((tableBits - 1) & 7));                  // global table present, size
        Byte(transparentIndex < 0 ? 0 : transparentIndex);   // background index
        Byte(0);                                             // pixel aspect ratio

        for (int i = 0; i < tableSize; i++)
        {
            if (i < palette.Count) { Byte(palette.R[i]); Byte(palette.G[i]); Byte(palette.B[i]); }
            else { Byte(0); Byte(0); Byte(0); }
        }

        if (loop && frames.Count > 1)
        {
            Byte(0x21); Byte(0xff); Byte(11);
            Ascii("NETSCAPE2.0");
            Byte(3); Byte(1); Short(0); Byte(0);             // 0 = repeat forever
        }

        void Image(Rectangle r, byte[] data, int disposal, int trans, int delayMs)
        {
            // GIF delays are in hundredths of a second. Sub-10ms rounds to 0,
            // which many viewers silently re-read as 100ms, so clamp to 1.
            int cs = Math.Max(1, (int)Math.Round(delayMs / 10.0));
            Byte(0x21); Byte(0xf9); Byte(4);
            Byte((disposal << 2) | (trans < 0 ? 0 : 1));
            Short(cs);
            Byte(trans < 0 ? 0 : trans);
            Byte(0);

            Byte(0x2c);
            Short(r.X); Short(r.Y);
            Short(r.Width); Short(r.Height);
            Byte(0);                                          // no local table, not interlaced
            Byte(minCodeSize);
            LzwCompress(data, minCodeSize, ms, lossy, palette, trans);
        }

        int w = width, h = height, n = frames.Count;
        var shown = new byte[n][];
        var full = new Rectangle(0, 0, w, h);
        if (!optimize)
        {
            for (int i = 0; i < n; i++)
            {
                var data = (byte[])frames[i].Indices.Clone();
                Image(full, data, clearDoc ? 2 : 1, transparentIndex, frames[i].DelayMs);
                shown[i] = data;
            }
            Byte(0x3b);
            return (ms.ToArray(), shown);
        }

        // canvas = what a decoder shows; it starts clear. Each frame is written
        // only once the next one has decided the disposal it needs.
        var canvas = new byte[w * h];
        if (clearDoc) Array.Fill(canvas, (byte)transparentIndex);
        Rectangle pr = full;
        byte[] pdata = (byte[])frames[0].Indices.Clone();
        int ptrans = clearDoc ? transparentIndex : -1;
        for (int i = 1; i <= n; i++)
        {
            byte[]? target = i < n ? frames[i].Indices : null;
            int disposal = 1;
            if (clearDoc && (target != null || n > 1))
            {
                // Pixels that turn clear cannot be drawn: the frame before must
                // restore to background (disposal 2) over a rectangle covering them.
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
                if (target == null) { x0 = 0; y0 = 0; x1 = w - 1; y1 = h - 1; }   // clean slate before the loop restarts
                else
                {
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int p = y * w + x;
                            if (target[p] != transparentIndex) continue;
                            byte after = canvas[p];
                            if (pr.Contains(x, y)) { byte d = pdata[(y - pr.Y) * pr.Width + x - pr.X]; if (d != keep) after = d; }
                            if (after == transparentIndex) continue;
                            if (x < x0) x0 = x;
                            if (x > x1) x1 = x;
                            if (y < y0) y0 = y;
                            if (y > y1) y1 = y;
                        }
                }
                if (x1 >= 0)
                {
                    disposal = 2;
                    var grown = Rectangle.Union(pr, Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1));
                    if (grown != pr)
                    {
                        var g = new byte[grown.Width * grown.Height];
                        Array.Fill(g, (byte)keep);
                        for (int y = 0; y < pr.Height; y++)
                            Array.Copy(pdata, y * pr.Width, g, (y + pr.Y - grown.Y) * grown.Width + pr.X - grown.X, pr.Width);
                        pr = grown; pdata = g; ptrans = keep;
                    }
                }
            }
            Image(pr, pdata, disposal, ptrans, frames[i - 1].DelayMs);   // lossy may rewrite pdata
            for (int y = 0; y < pr.Height; y++)
                for (int x = 0; x < pr.Width; x++)
                {
                    byte d = pdata[y * pr.Width + x];
                    if (ptrans < 0 || d != ptrans) canvas[(y + pr.Y) * w + x + pr.X] = d;
                }
            shown[i - 1] = (byte[])canvas.Clone();
            if (target == null) break;
            if (disposal == 2)
                for (int y = pr.Top; y < pr.Bottom; y++) Array.Fill(canvas, (byte)transparentIndex, y * w + pr.X, pr.Width);

            // The next frame: the box around what changed, unchanged pixels kept.
            int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = -1, by1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x;
                    if (Same(canvas[p], target[p])) continue;
                    if (x < bx0) bx0 = x;
                    if (x > bx1) bx1 = x;
                    if (y < by0) by0 = y;
                    if (y > by1) by1 = y;
                }
            pr = bx1 < 0 ? new Rectangle(0, 0, 1, 1) : Rectangle.FromLTRB(bx0, by0, bx1 + 1, by1 + 1);
            pdata = new byte[pr.Width * pr.Height];
            for (int y = 0; y < pr.Height; y++)
                for (int x = 0; x < pr.Width; x++)
                {
                    int p = (y + pr.Y) * w + x + pr.X;
                    pdata[y * pr.Width + x] = keep >= 0 && Same(canvas[p], target[p]) ? (byte)keep : target[p];
                }
            ptrans = keep;
        }
        Byte(0x3b);
        return (ms.ToArray(), shown);
    }

    /* LZW as GIF specifies it: LSB-first codes, variable width, flushed in
     * sub-blocks of at most 255 bytes.
     *
     * Lossy (written for Flint, in the spirit of gifsicle --lossy): when the
     * exact next pixel would end the current string, take an existing
     * continuation whose colour is close enough instead and rewrite the pixel
     * to match. Longer strings, fewer codes. The transparent index is never
     * swapped in or out. */
    private static void LzwCompress(byte[] indices, int minCodeSize, Stream outStream,
                                    int lossy = 0, Palette? pal = null, int protect = -1)
    {
        int clearCode = 1 << minCodeSize;
        int eoiCode = clearCode + 1;

        int tol2 = lossy <= 0 || pal == null ? -1 : lossy * lossy / 4;
        var children = tol2 < 0 ? null : new List<(byte k, int code)>?[4096];

        var dict = new Dictionary<int, int>();
        int codeSize = minCodeSize + 1;
        int next = eoiCode + 1;

        int acc = 0, accBits = 0;
        var block = new List<byte>(255);

        void FlushBlock()
        {
            if (block.Count == 0) return;
            outStream.WriteByte((byte)block.Count);
            foreach (byte b in block) outStream.WriteByte(b);
            block.Clear();
        }

        void Emit(int code)
        {
            acc |= code << accBits;
            accBits += codeSize;
            while (accBits >= 8)
            {
                block.Add((byte)(acc & 0xff));
                acc >>= 8;
                accBits -= 8;
                if (block.Count == 255) FlushBlock();
            }
        }

        Emit(clearCode);
        if (indices.Length == 0)
        {
            Emit(eoiCode);
        }
        else
        {
            int cur = indices[0];
            for (int i = 1; i < indices.Length; i++)
            {
                int k = indices[i];
                int key = (cur << 8) | k;
                if (dict.TryGetValue(key, out int found)) { cur = found; continue; }
                if (children != null && k != protect && k < pal!.Count && children[cur] is { } kids)
                {
                    int best = -1, bestK = 0, bestD = tol2 + 1;
                    foreach (var (k2, code2) in kids)
                    {
                        if (k2 == protect || k2 >= pal.Count) continue;
                        int dr = pal.R[k] - pal.R[k2], dg = pal.G[k] - pal.G[k2], db = pal.B[k] - pal.B[k2];
                        int d = dr * dr + dg * dg + db * db;
                        if (d < bestD) { bestD = d; best = code2; bestK = k2; }
                    }
                    if (best >= 0) { indices[i] = (byte)bestK; cur = best; continue; }
                }
                Emit(cur);
                if (next < 4096)
                {
                    if (children != null) (children[cur] ??= new()).Add(((byte)k, next));
                    dict[key] = next++;
                    // Lockstep with the decoder, which is the subtle part. A
                    // decoder only forms its first table entry on the SECOND
                    // data code, so its table is always one entry behind ours.
                    // It widens when ITS next code reaches 1<<codeSize, so we
                    // must test next-1. Testing next widens one code early and
                    // every decoder then misreads the stream.
                    if (next - 1 >= (1 << codeSize) && codeSize < 12) codeSize++;
                }
                else
                {
                    Emit(clearCode);
                    dict.Clear();
                    if (children != null) Array.Clear(children);
                    codeSize = minCodeSize + 1;
                    next = eoiCode + 1;
                }
                cur = k;
            }
            Emit(cur);
            Emit(eoiCode);
        }

        if (accBits > 0)
        {
            block.Add((byte)(acc & 0xff));
            if (block.Count == 255) FlushBlock();
        }
        FlushBlock();
        outStream.WriteByte(0);   // block terminator
    }
}
