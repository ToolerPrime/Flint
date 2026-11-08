/* Minimal GIF89a encoder: one global palette, animation, transparency.
 * No dependencies. Ported from the JavaScript version in this repo, which is
 * verified pixel-exact against an independent decoder (Pillow).
 *
 * One global colour table shared by every frame is the point: a per-frame
 * palette is what makes exported sprite animations shimmer. */
using System;
using System.Collections.Generic;
using System.IO;

namespace Flint;

public sealed class GifFrame
{
    public byte[] Indices = Array.Empty<byte>();
    public int DelayMs = 100;
}

public static class GifEncoder
{
    public static byte[] Encode(int width, int height, IList<GifFrame> frames,
                                Palette palette, int transparentIndex = -1, bool loop = true)
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

        // The colour table size must be a power of two, at least 2.
        int tableBits = 1;
        while (1 << tableBits < palette.Count) tableBits++;
        int tableSize = 1 << tableBits;

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

        foreach (var f in frames)
        {
            // GIF delays are in hundredths of a second. Sub-10ms rounds to 0,
            // which many viewers silently re-read as 100ms, so clamp to 1.
            int cs = Math.Max(1, (int)Math.Round(f.DelayMs / 10.0));
            int disposal = transparentIndex < 0 ? 1 : 2;

            Byte(0x21); Byte(0xf9); Byte(4);
            Byte((disposal << 2) | (transparentIndex < 0 ? 0 : 1));
            Short(cs);
            Byte(transparentIndex < 0 ? 0 : transparentIndex);
            Byte(0);

            Byte(0x2c);
            Short(0); Short(0);
            Short(width); Short(height);
            Byte(0);                                          // no local table, not interlaced

            int minCodeSize = Math.Max(2, tableBits);
            Byte(minCodeSize);
            LzwCompress(f.Indices, minCodeSize, ms);
        }

        Byte(0x3b);
        return ms.ToArray();
    }

    /* LZW as GIF specifies it: LSB-first codes, variable width, flushed in
     * sub-blocks of at most 255 bytes. */
    private static void LzwCompress(byte[] indices, int minCodeSize, Stream outStream)
    {
        int clearCode = 1 << minCodeSize;
        int eoiCode = clearCode + 1;

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
                Emit(cur);
                if (next < 4096)
                {
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
