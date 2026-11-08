/* Still-image encoders for the extra export formats and the Export preview
 * dialog: JPEG, BMP, TIFF (32-bit with alpha), indexed PNG-8, WebP still.
 * Everything works on straight-alpha BGRA bytes and returns the file's bytes,
 * so the preview can measure the real size and decode it back. BMP, TIFF and
 * PNG-8 are written by hand (exact pixels; GDI+ would premultiply alpha). */
using System.Drawing.Imaging;
using System.IO.Compression;

namespace Flint;

/// <summary>Palette-reduced frames: what a GIF or PNG-8 file really contains.</summary>
public sealed class IndexedFrames
{
    public Palette Palette = null!;       // includes the transparent slot 0 when used
    public int TransparentIndex = -1;
    public List<byte[]> Indices = new();

    /// <summary>The picture the file will show for frame i, as BGRA.</summary>
    public byte[] ToBgra(int i)
    {
        var ix = Indices[i];
        var o = new byte[ix.Length * 4];
        for (int q = 0; q < ix.Length; q++)
        {
            int c = ix[q];
            if (c == TransparentIndex) continue;
            o[q * 4] = Palette.B[c]; o[q * 4 + 1] = Palette.G[c]; o[q * 4 + 2] = Palette.R[c]; o[q * 4 + 3] = 255;
        }
        return o;
    }
}

public static class StillExport
{
    public static bool IsStill(ExportKind k) => k is ExportKind.Jpeg or ExportKind.Bmp or ExportKind.Tiff or ExportKind.Png8 or ExportKind.WebPStill;

    /// <summary>One shared palette over all frames, as the GIF export builds it (so sizes agree).</summary>
    public static IndexedFrames Index(IList<byte[]> frames, int w, int h, int colours, bool dither, CancellationToken ct = default)
    {
        bool anyTransparent = frames.Any(px => { for (int i = 3; i < px.Length; i += 4) if (px[i] < 128) return true; return false; });
        int offset = anyTransparent ? 1 : 0;
        var hist = Quantizer.BuildHistogram(frames, out double total);
        var colors = Quantizer.Quantize(hist, total, Math.Clamp(colours, 2, 256) - offset);
        if (colors.Count == 0) throw new InvalidOperationException("no opaque pixels to build a palette from");
        var ix = new Quantizer.Indexer(colors);
        var pal = anyTransparent
            ? new Palette
            {
                R = new byte[] { 0 }.Concat(colors.R).ToArray(),
                G = new byte[] { 0 }.Concat(colors.G).ToArray(),
                B = new byte[] { 0 }.Concat(colors.B).ToArray(),
            }
            : colors;
        var res = new IndexedFrames { Palette = pal, TransparentIndex = anyTransparent ? 0 : -1 };
        foreach (var px in frames)
        {
            ct.ThrowIfCancellationRequested();
            if (dither) { res.Indices.Add(ImageOps.Dither(px, w, h, colors, ix, offset)); continue; }
            var idx = new byte[w * h];
            for (int q = 0; q < idx.Length; q++)
            {
                int o = q * 4;
                idx[q] = px[o + 3] < 128 ? (byte)0 : (byte)(ix.Nearest(px[o + 2], px[o + 1], px[o]) + offset);
            }
            res.Indices.Add(idx);
        }
        return res;
    }

    // ---------- PNG-8 ----------

    public static byte[] Png8(IndexedFrames f, int w, int h)
    {
        using var o = new MemoryStream();
        o.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BE(ihdr, 0, w); BE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 3;
        Chunk(o, "IHDR", ihdr);
        var plte = new byte[f.Palette.Count * 3];
        for (int i = 0; i < f.Palette.Count; i++) { plte[i * 3] = f.Palette.R[i]; plte[i * 3 + 1] = f.Palette.G[i]; plte[i * 3 + 2] = f.Palette.B[i]; }
        Chunk(o, "PLTE", plte);
        if (f.TransparentIndex >= 0) Chunk(o, "tRNS", new byte[] { 0 });   // entry 0 only; the rest default to opaque
        var raw = new byte[(w + 1) * h];
        for (int y = 0; y < h; y++) Buffer.BlockCopy(f.Indices[0], y * w, raw, y * (w + 1) + 1, w);
        Chunk(o, "IDAT", Zlib(raw));
        Chunk(o, "IEND", Array.Empty<byte>());
        return o.ToArray();
    }

    /// <summary>PNG-8 straight from pixels; also returns what it will look like.</summary>
    public static byte[] Png8(byte[] bgra, int w, int h, int colours, bool dither, out byte[] preview)
    {
        var f = Index(new[] { bgra }, w, h, colours, dither);
        preview = f.ToBgra(0);
        return Png8(f, w, h);
    }

    // ---------- JPEG ----------

    /// <summary>Straight alpha flattened onto a matte, rounded.</summary>
    public static byte[] Flatten(byte[] bgra, Color matte)
    {
        var o = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int a = bgra[i + 3];
            o[i] = (byte)((bgra[i] * a + matte.B * (255 - a) + 127) / 255);
            o[i + 1] = (byte)((bgra[i + 1] * a + matte.G * (255 - a) + 127) / 255);
            o[i + 2] = (byte)((bgra[i + 2] * a + matte.R * (255 - a) + 127) / 255);
            o[i + 3] = 255;
        }
        return o;
    }

    public static byte[] Jpeg(byte[] bgra, int w, int h, int quality, Color matte)
    {
        using var bmp = ImageOps.FromPixels(w, h, Flatten(bgra, matte));
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(quality, 1, 100));
        using var ms = new MemoryStream();
        bmp.Save(ms, codec, ep);
        return ms.ToArray();
    }

    /// <summary>Decode any GDI+-readable still (JPEG, PNG, BMP) to straight BGRA.</summary>
    public static byte[] DecodeGdi(byte[] file)
    {
        using var ms = new MemoryStream(file);
        using var img = Image.FromStream(ms);
        using var bmp = ImageOps.ReadExact(img);
        return ImageOps.GetPixels(bmp);
    }

    // ---------- BMP ----------

    /// <summary>32-bit BMP with a V4 header carrying an alpha mask, rows bottom-up.</summary>
    public static byte[] Bmp(byte[] bgra, int w, int h)
    {
        int size = 14 + 108 + bgra.Length;
        var o = new byte[size];
        o[0] = (byte)'B'; o[1] = (byte)'M';
        LE32(o, 2, size); LE32(o, 10, 14 + 108);
        LE32(o, 14, 108); LE32(o, 18, w); LE32(o, 22, h);
        o[26] = 1; o[28] = 32; LE32(o, 30, 3);           // planes, bpp, BI_BITFIELDS
        LE32(o, 34, bgra.Length);
        LE32(o, 54, 0x00FF0000); LE32(o, 58, 0x0000FF00); LE32(o, 62, 0x000000FF); LE32(o, 66, unchecked((int)0xFF000000));
        LE32(o, 70, 0x73524742);                          // "sRGB"
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(bgra, y * w * 4, o, 14 + 108 + (h - 1 - y) * w * 4, w * 4);
        return o;
    }

    // ---------- TIFF ----------

    /// <summary>Baseline little-endian TIFF, 8-bit RGBA, uncompressed, unassociated alpha.</summary>
    public static byte[] Tiff(byte[] bgra, int w, int h)
    {
        const int entries = 12;
        int ifd = 8, bps = ifd + 2 + entries * 12 + 4, data = bps + 8;
        var o = new byte[data + bgra.Length];
        o[0] = (byte)'I'; o[1] = (byte)'I'; o[2] = 42; LE32(o, 4, ifd);
        LE16(o, ifd, entries);
        int e = ifd + 2;
        void Ent(int tag, int type, int count, int value)
        {
            LE16(o, e, tag); LE16(o, e + 2, type); LE32(o, e + 4, count);
            if (type == 3 && count == 1) LE16(o, e + 8, value); else LE32(o, e + 8, value);
            e += 12;
        }
        Ent(254, 4, 1, 0);                  // NewSubfileType
        Ent(256, 4, 1, w); Ent(257, 4, 1, h);
        Ent(258, 3, 4, bps);                // BitsPerSample 8,8,8,8
        Ent(259, 3, 1, 1);                  // no compression
        Ent(262, 3, 1, 2);                  // RGB
        Ent(273, 4, 1, data);               // one strip
        Ent(277, 3, 1, 4);
        Ent(278, 4, 1, h);
        Ent(279, 4, 1, bgra.Length);
        Ent(284, 3, 1, 1);                  // chunky
        Ent(338, 3, 1, 2);                  // ExtraSamples: unassociated alpha
        for (int i = 0; i < 4; i++) LE16(o, bps + i * 2, 8);
        for (int i = 0; i < bgra.Length; i += 4)
        {
            o[data + i] = bgra[i + 2]; o[data + i + 1] = bgra[i + 1]; o[data + i + 2] = bgra[i]; o[data + i + 3] = bgra[i + 3];
        }
        return o;
    }

    // ---------- WebP still (ffmpeg) ----------

    /// <summary>quality 0 = lossless. Throws with a plain message when ffmpeg is missing.</summary>
    public static byte[] WebpStill(byte[] bgra, int w, int h, int quality, CancellationToken ct)
    {
        var ffmpeg = WebmExport.Find()
            ?? throw new InvalidOperationException("ffmpeg not found (winget install Gyan.FFmpeg); WebP needs it");
        string work = Path.Combine(Path.GetTempPath(), "flint-still-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            using (var bmp = ImageOps.FromPixels(w, h, bgra)) bmp.Save(Path.Combine(work, "in.png"), ImageFormat.Png);
            string codec = quality <= 0
                ? "-c:v libwebp -lossless 1 -pix_fmt bgra"
                : $"-c:v libwebp -lossless 0 -quality {Math.Clamp(quality, 1, 100)} -pix_fmt yuva420p";
            string outp = Path.Combine(work, "out.webp");
            var (ok, msg) = WebmExport.RunFfmpeg(ffmpeg, $"-y -nostats -loglevel error -i \"{Path.Combine(work, "in.png")}\" {codec} -frames:v 1 \"{outp}\"", 60_000, ct);
            if (!ok) throw new InvalidOperationException(msg);
            return File.ReadAllBytes(outp);
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    /// <summary>Decode a WebP back to BGRA with ffmpeg (preview only).</summary>
    public static byte[] DecodeWebp(byte[] webp, CancellationToken ct)
    {
        var ffmpeg = WebmExport.Find() ?? throw new InvalidOperationException("ffmpeg not found");
        string work = Path.Combine(Path.GetTempPath(), "flint-still-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            File.WriteAllBytes(Path.Combine(work, "in.webp"), webp);
            string outp = Path.Combine(work, "out.png");
            var (ok, msg) = WebmExport.RunFfmpeg(ffmpeg, $"-y -nostats -loglevel error -i \"{Path.Combine(work, "in.webp")}\" -pix_fmt rgba \"{outp}\"", 60_000, ct);
            if (!ok) throw new InvalidOperationException(msg);
            return DecodeGdi(File.ReadAllBytes(outp));
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    /// <summary>The still export for a job: the current frame, encoded.</summary>
    public static string Save(ExportJob j, byte[] bgra, int w, int h, CancellationToken ct)
    {
        byte[] bytes = j.Kind switch
        {
            ExportKind.Jpeg => Jpeg(bgra, w, h, j.Quality, j.Matte),
            ExportKind.Bmp => Bmp(bgra, w, h),
            ExportKind.Tiff => Tiff(bgra, w, h),
            ExportKind.Png8 => Png8(bgra, w, h, j.GifColours, j.Dither, out _),
            ExportKind.WebPStill => WebpStill(bgra, w, h, j.Quality, ct),
            _ => throw new NotSupportedException(j.Kind.ToString()),
        };
        File.WriteAllBytes(j.Path, bytes);
        return $"wrote {Path.GetFileName(j.Path)}, {bytes.Length / 1024.0:0.0} KB";
    }

    // ---------- byte helpers ----------

    private static void LE16(byte[] b, int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); }
    private static void LE32(byte[] b, int at, int v) { LE16(b, at, v); LE16(b, at + 2, v >> 16); }
    private static void BE(byte[] b, int at, int v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

    private static byte[] Zlib(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw);
        return ms.ToArray();
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        uint c = 0xFFFFFFFF;
        foreach (var b in t) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        c ^= 0xFFFFFFFF;
        var crc = new byte[4]; BE(crc, 0, (int)c);
        s.Write(crc);
    }
}
