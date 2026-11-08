/* Writes the same cases as the JavaScript harness so test/verify.py can check
 * the C# encoder with Pillow, an independent decoder. Run:
 *     dotnet run -- --selftest <outdir>
 * then: python verify.py  (from the test folder, pointing at that outdir)
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Flint;

public static class SelfTest
{
    private sealed class Case
    {
        public int Width, Height;
        public Palette Palette = new();
        public List<byte[]> Frames = new();
        public List<int> Delays = new();
        public int Transparent = -1;
        public List<byte[]>? SourceRgba;
    }

    public static int Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var cases = new Dictionary<string, Case>();

        // 1. Tiny, exact: four colours, two frames, distinct delays.
        {
            var c = new Case
            {
                Width = 4, Height = 4,
                Palette = Pal(new byte[,] { { 255, 0, 0 }, { 0, 255, 0 }, { 0, 0, 255 }, { 255, 255, 255 } }),
            };
            c.Frames.Add(new byte[] { 0, 1, 2, 3, 1, 2, 3, 0, 2, 3, 0, 1, 3, 0, 1, 2 });
            c.Frames.Add(new byte[] { 3, 3, 3, 3, 2, 2, 2, 2, 1, 1, 1, 1, 0, 0, 0, 0 });
            c.Delays.AddRange(new[] { 120, 500 });
            cases["tiny"] = c;
        }

        // 2. Walks the code width up through 4..9 bits.
        {
            const int w = 64, h = 64, n = 256;
            var pal = new byte[n, 3];
            for (int i = 0; i < n; i++) { pal[i, 0] = (byte)i; pal[i, 1] = (byte)(i * 7); pal[i, 2] = (byte)(i * 13); }
            var idx = new byte[w * h];
            for (int i = 0; i < idx.Length; i++) idx[i] = (byte)(i * 31 % n);
            var c = new Case { Width = w, Height = h, Palette = Pal(pal) };
            c.Frames.Add(idx);
            c.Delays.Add(100);
            cases["widths"] = c;
        }

        // 3. Drives the dictionary to 4096 and forces a reset.
        {
            const int w = 220, h = 220, n = 256;
            var pal = new byte[n, 3];
            for (int i = 0; i < n; i++) { pal[i, 0] = (byte)(i * 5); pal[i, 1] = (byte)i; pal[i, 2] = (byte)(255 - i); }
            var idx = new byte[w * h];
            long s = 12345;
            for (int i = 0; i < idx.Length; i++)
            {
                s = (s * 1103515245 + 12345) & 0x7fffffff;
                idx[i] = (byte)((s >> 7) & 255);
            }
            var c = new Case { Width = w, Height = h, Palette = Pal(pal) };
            c.Frames.Add(idx);
            c.Delays.Add(40);
            cases["reset"] = c;
        }

        // 4. Transparency: index 0 transparent across two frames.
        {
            var c = new Case
            {
                Width = 4, Height = 4,
                Palette = Pal(new byte[,] { { 0, 0, 0 }, { 200, 30, 40 }, { 20, 180, 90 } }),
                Transparent = 0,
            };
            c.Frames.Add(new byte[] { 0, 1, 1, 0, 0, 2, 2, 0, 0, 1, 1, 0, 0, 0, 0, 0 });
            c.Frames.Add(new byte[] { 1, 1, 0, 0, 2, 2, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0 });
            c.Delays.AddRange(new[] { 80, 80 });
            cases["alpha"] = c;
        }

        // 5. Quantiser: two synthetic frames sharing one palette.
        {
            const int w = 48, h = 48;
            byte[] Make(int phase)
            {
                var px = new byte[w * h * 4];              // BGRA, as the app sees it
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int o = (y * w + x) * 4;
                        px[o + 2] = (byte)(x * 5 + phase);        // R
                        px[o + 1] = (byte)(y * 5);                // G
                        px[o + 0] = (byte)((x + y) * 3 + phase * 2); // B
                        px[o + 3] = 255;
                    }
                return px;
            }
            var a = Make(0); var b = Make(60);
            var hist = Quantizer.BuildHistogram(new[] { a, b }, out double total);
            var pal = Quantizer.Quantize(hist, total, 64);
            var ix = new Quantizer.Indexer(pal);

            byte[] ToIdx(byte[] px)
            {
                var o = new byte[w * h];
                for (int i = 0; i < o.Length; i++)
                    o[i] = (byte)ix.Nearest(px[i * 4 + 2], px[i * 4 + 1], px[i * 4 + 0]);
                return o;
            }

            var c = new Case { Width = w, Height = h, Palette = pal };
            c.Frames.Add(ToIdx(a));
            c.Frames.Add(ToIdx(b));
            c.Delays.AddRange(new[] { 100, 100 });
            c.SourceRgba = new List<byte[]> { ToRgba(a), ToRgba(b) };
            cases["quant"] = c;
            Console.WriteLine($"quant: palette={pal.Count} colours from 2 frames");
        }

        var manifest = new StringBuilder("{");
        bool firstCase = true;
        foreach (var (name, c) in cases)
        {
            var frames = new List<GifFrame>();
            for (int i = 0; i < c.Frames.Count; i++)
                frames.Add(new GifFrame { Indices = c.Frames[i], DelayMs = c.Delays[i] });

            byte[] gif = GifEncoder.Encode(c.Width, c.Height, frames, c.Palette, c.Transparent, loop: true);
            File.WriteAllBytes(Path.Combine(outDir, name + ".gif"), gif);
            Console.WriteLine($"{name}.gif  {gif.Length} bytes");

            if (!firstCase) manifest.Append(',');
            firstCase = false;
            manifest.Append(Q(name)).Append(":{");
            manifest.Append("\"width\":").Append(c.Width);
            manifest.Append(",\"height\":").Append(c.Height);
            manifest.Append(",\"palette\":[");
            for (int i = 0; i < c.Palette.Count; i++)
            {
                if (i > 0) manifest.Append(',');
                manifest.Append('[').Append(c.Palette.R[i]).Append(',')
                        .Append(c.Palette.G[i]).Append(',').Append(c.Palette.B[i]).Append(']');
            }
            manifest.Append("],\"frames\":[");
            for (int i = 0; i < c.Frames.Count; i++)
            {
                if (i > 0) manifest.Append(',');
                manifest.Append(Arr(c.Frames[i]));
            }
            manifest.Append("],\"delays\":[");
            for (int i = 0; i < c.Delays.Count; i++)
            {
                if (i > 0) manifest.Append(',');
                manifest.Append(c.Delays[i].ToString(CultureInfo.InvariantCulture));
            }
            manifest.Append(']');
            if (c.Transparent >= 0) manifest.Append(",\"transparent\":").Append(c.Transparent);
            if (c.SourceRgba != null)
            {
                manifest.Append(",\"source\":[");
                for (int i = 0; i < c.SourceRgba.Count; i++)
                {
                    if (i > 0) manifest.Append(',');
                    manifest.Append(Arr(c.SourceRgba[i]));
                }
                manifest.Append(']');
            }
            manifest.Append('}');
        }
        manifest.Append('}');
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), manifest.ToString());
        Console.WriteLine("manifest written to " + outDir);
        return 0;
    }

    private static byte[] ToRgba(byte[] bgra)
    {
        var o = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            o[i] = bgra[i + 2]; o[i + 1] = bgra[i + 1]; o[i + 2] = bgra[i]; o[i + 3] = bgra[i + 3];
        }
        return o;
    }

    private static Palette Pal(byte[,] rows)
    {
        int n = rows.GetLength(0);
        var p = new Palette { R = new byte[n], G = new byte[n], B = new byte[n] };
        for (int i = 0; i < n; i++) { p.R[i] = rows[i, 0]; p.G[i] = rows[i, 1]; p.B[i] = rows[i, 2]; }
        return p;
    }

    private static string Q(string s) => "\"" + s + "\"";

    private static string Arr(byte[] a)
    {
        var sb = new StringBuilder(a.Length * 3 + 2);
        sb.Append('[');
        for (int i = 0; i < a.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(a[i]);
        }
        sb.Append(']');
        return sb.ToString();
    }
}
