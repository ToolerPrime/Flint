/* Animated PNG.
 *
 * APNG is ordinary PNG plus three chunk types: acTL (frame count and loops),
 * fcTL (per-frame size, offset, delay, disposal) and fdAT (frame data). Each
 * frame is compressed by the normal PNG encoder and its IDAT payload re-filed
 * under the right chunk, so pixels are exactly what PNG stores — full 8-bit
 * alpha, no palette — and every frame keeps its own delay. Viewers that do
 * not know APNG simply show the first frame.
 */
using System.Buffers.Binary;
using System.Drawing.Imaging;

namespace Flint;

public static class ApngEncoder
{
    public static byte[] Encode(IList<Frame> frames, bool loop, CancellationToken ct = default,
                                IProgress<double>? progress = null)
    {
        if (frames.Count == 0) throw new InvalidOperationException("no frames");
        int w = frames[0].Bmp.Width, h = frames[0].Bmp.Height;

        using var o = new MemoryStream();
        o.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        uint seq = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var chunks = PngChunks(frames[i].Bmp);

            if (i == 0)
            {
                // IHDR, then animation control, before any image data.
                WriteChunk(o, "IHDR", chunks.First(c => c.type == "IHDR").data);
                var actl = new byte[8];
                BinaryPrimitives.WriteUInt32BigEndian(actl, (uint)frames.Count);
                BinaryPrimitives.WriteUInt32BigEndian(actl.AsSpan(4), loop ? 0u : 1u);   // 0 = forever
                WriteChunk(o, "acTL", actl);
            }

            WriteChunk(o, "fcTL", Fctl(seq++, w, h, frames[i].Delay));

            foreach (var (type, data) in chunks.Where(c => c.type == "IDAT"))
            {
                if (i == 0) WriteChunk(o, "IDAT", data);
                else
                {
                    var fd = new byte[4 + data.Length];
                    BinaryPrimitives.WriteUInt32BigEndian(fd, seq++);
                    data.CopyTo(fd, 4);
                    WriteChunk(o, "fdAT", fd);
                }
            }
            progress?.Report((i + 1) / (double)frames.Count);
        }

        WriteChunk(o, "IEND", Array.Empty<byte>());
        return o.ToArray();
    }

    private static byte[] Fctl(uint seq, int w, int h, int delayMs)
    {
        var b = new byte[26];
        BinaryPrimitives.WriteUInt32BigEndian(b, seq);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), (uint)w);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), (uint)h);
        // x and y offsets 0 (bytes 12..19): every frame is the full canvas.
        // Delay as a fraction: delayMs / 1000, exact to the millisecond.
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(20), (ushort)Math.Clamp(delayMs, 1, 65535));
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(22), 1000);
        b[24] = 1;   // dispose to transparent: each frame is drawn on a clear canvas
        b[25] = 0;   // blend source: replace, so partial alpha is not composited over the last frame
        return b;
    }

    private static List<(string type, byte[] data)> PngChunks(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        var png = ms.ToArray();
        var list = new List<(string, byte[])>();
        int p = 8;
        while (p + 8 <= png.Length)
        {
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(p));
            string type = System.Text.Encoding.ASCII.GetString(png, p + 4, 4);
            list.Add((type, png.AsSpan(p + 8, len).ToArray()));
            p += 12 + len;
            if (type == "IEND") break;
        }
        // Every frame must share the first frame's header; GDI+ always writes
        // 32-bit RGBA for our bitmaps, but check rather than assume.
        var ihdr = list.First(c => c.Item1 == "IHDR").Item2;
        if (ihdr[8] != 8 || ihdr[9] != 6)
            throw new InvalidOperationException($"unexpected PNG layout (depth {ihdr[8]}, colour type {ihdr[9]})");
        return list;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        uint crc = Crc(t, data);
        Span<byte> c = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }

    private static readonly uint[] Table = MakeTable();

    private static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var b in type) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
