/* GIF quality and size: frame cropping/differencing decodes identically
 * (Pillow), palette types, dithers, lossy, palette files, colour-table edits,
 * optimize to size, export presets.
 * Flint.exe --giftest <dir> */
using System.Diagnostics;

namespace Flint;

public static class GifTest
{
    private static int _fails;
    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private const string Reader = @"
import sys
from PIL import Image
im = Image.open(sys.argv[1])
out = open(sys.argv[2], 'wb')
n = getattr(im, 'n_frames', 1)
for i in range(n):
    im.seek(i)
    out.write(im.convert('RGBA').tobytes())
print(n)
";

    private static string _dir = "";

    /// <summary>Pillow's decode of a GIF: RGBA per frame.</summary>
    private static List<byte[]> Decode(byte[] gif, int w, int h)
    {
        string path = Path.Combine(_dir, "t.gif"), raw = Path.Combine(_dir, "t.raw");
        File.WriteAllBytes(path, gif);
        var psi = new ProcessStartInfo("python", $"-W ignore \"{Path.Combine(_dir, "reader.py")}\" \"{path}\" \"{raw}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        string o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(60_000);
        if (!int.TryParse(o.Trim(), out int n)) { Console.WriteLine("  note  reader: " + err.Result.Trim()); return new(); }
        var all = File.ReadAllBytes(raw);
        int fs = w * h * 4;
        return Enumerable.Range(0, n).Select(i => all.AsSpan(i * fs, fs).ToArray()).ToList();
    }

    /// <summary>Same picture: equal RGB where opaque, both clear elsewhere.</summary>
    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i += 4)
        {
            bool ca = a[i + 3] == 0, cb = b[i + 3] == 0;
            if (ca != cb) return false;
            if (!ca && (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3])) return false;
        }
        return true;
    }

    /// <summary>The preview's BGRA as RGBA.</summary>
    private static byte[] Rgba(byte[] bgra)
    {
        var o = new byte[bgra.Length];
        for (int i = 0; i < o.Length; i += 4) { o[i] = bgra[i + 2]; o[i + 1] = bgra[i + 1]; o[i + 2] = bgra[i]; o[i + 3] = bgra[i + 3]; }
        return o;
    }

    /// <summary>A sprite moving over a background: clear background or solid.</summary>
    private static byte[][] Sprite(int w, int h, int n, bool clearBg, bool manyColours = false)
    {
        var frames = new byte[n][];
        for (int f = 0; f < n; f++)
        {
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    bool inSprite = x >= 4 + f * 3 && x < 14 + f * 3 && y >= 6 && y < 16 + (f % 2) * 3;
                    if (inSprite) { px[o] = (byte)(40 + x * 9); px[o + 1] = (byte)(200 - y * 5); px[o + 2] = (byte)(f * 40); px[o + 3] = 255; }
                    else if (!clearBg) { px[o] = manyColours ? (byte)(x * 7) : (byte)30; px[o + 1] = manyColours ? (byte)(y * 9) : (byte)90; px[o + 2] = manyColours ? (byte)((x ^ y) * 5) : (byte)140; px[o + 3] = 255; }
                    else if (y > h - 4) { px[o] = 10; px[o + 1] = 160; px[o + 2] = 10; px[o + 3] = 255; }   // a static floor
                }
            frames[f] = px;
        }
        return frames;
    }

    private static OptSource Src(byte[][] frames, int w, int h) => new()
    {
        Frames = frames, Delays = frames.Select((_, i) => 70 + i * 10).ToArray(), W = w, H = h, Loop = true,
    };

    private static void Optimized(string name, byte[][] frames, int w, int h, int colours = 64)
    {
        var src = Src(frames, w, h);
        var plain = new OptSettings { Format = OptFormat.Gif, Colours = colours };
        plain.Gif.Optimize = false;
        var opt = plain.Clone(); opt.Gif.Optimize = true;
        var a = OptimizeEncoder.Encode(plain, src, default);
        var b = OptimizeEncoder.Encode(opt, src, default);
        var da = Decode(a.Data!, w, h);
        var db = Decode(b.Data!, w, h);
        bool same = da.Count == frames.Length && db.Count == frames.Length && da.Zip(db).All(t => Same(t.First, t.Second));
        Check(same, $"{name}: optimized GIF decodes pixel-identical to the plain one in Pillow ({db.Count} frames)");
        Check(b.Bytes < a.Bytes, $"{name}: optimized is smaller ({b.Bytes} B vs {a.Bytes} B plain)");
        Check(db.Count == frames.Length && db.Select((f, i) => Same(f, Rgba(b.PreviewFor(i)))).All(x => x), $"{name}: the preview shows exactly what Pillow decodes");
        // Flint's own importer reads GIFs through Windows (GDI+): it must agree too.
        string path = Path.Combine(_dir, "rt.gif");
        File.WriteAllBytes(path, b.Data!);
        using var imp = Importer.Load(path);
        bool rt = imp.Frames.Count == frames.Length && imp.Frames.Select((f, i) => Same(Rgba(ImageOps.GetPixels(f.bmp)), Rgba(b.PreviewFor(i)))).All(x => x);
        Check(rt, $"{name}: re-importing into Flint gives the same frames ({imp.Frames.Count})");
    }

    public static int Run(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "reader.py"), Reader);
        ExportPresets.FilePath = Path.Combine(dir, "presets.json");
        if (File.Exists(ExportPresets.FilePath)) File.Delete(ExportPresets.FilePath);
        const int W = 40, H = 28;

        Console.WriteLine("Frame cropping and differencing");
        Optimized("sprite on clear background", Sprite(W, H, 6, clearBg: true), W, H);
        Optimized("sprite on solid background", Sprite(W, H, 6, clearBg: false), W, H);
        {
            var f = Sprite(W, H, 4, clearBg: true);
            f[2] = (byte[])f[1].Clone();                       // a held frame
            Optimized("held (identical) frame", f, W, H);
        }
        {
            // Full 256-colour picture with no clear pixels: no spare index, crop only.
            var f = Sprite(W, H, 5, clearBg: false, manyColours: true);
            var src = Src(f, W, H);
            var s = new OptSettings { Format = OptFormat.Gif, Colours = 256 };
            var r = OptimizeEncoder.Encode(s, src, default);
            var d = Decode(r.Data!, W, H);
            Check(d.Count == 5 && d.Select((x, i) => Same(x, Rgba(r.PreviewFor(i)))).All(x => x), $"256 colours, nothing clear: decodes as previewed ({r.Indexed!.Palette.Count} colours)");
        }
        {
            // Everything visible then everything clear: needs restore-to-background.
            var f = Sprite(W, H, 3, clearBg: true);
            f[1] = new byte[W * H * 4];
            Optimized("frame that turns fully clear", f, W, H);
        }

        {
            // Exactly 16 opaque colours: the spare "keep" slot doubles the colour table.
            var f = Sprite(W, H, 4, clearBg: false);
            for (int i = 0; i < f.Length; i++)
                for (int q = 0; q < W * H; q++) { int o = q * 4; int c = (f[i][o] + f[i][o + 1] * 3 + q / 97) % 16; f[i][o] = (byte)(c * 16); f[i][o + 1] = (byte)(255 - c * 15); f[i][o + 2] = 77; }
            var o16 = GifPalette.Index(f, W, H, new GifOptions { Palette = PaletteKind.Exact });
            var gf = o16.Indices.Select((ix, i) => new GifFrame { Indices = ix, DelayMs = 100 }).ToList();
            var plain = GifEncoder.Encode(W, H, gf, o16.Palette, o16.TransparentIndex, true, false);
            var opt = GifEncoder.EncodeEx(W, H, gf, o16.Palette, o16.TransparentIndex, true, true);
            var dp = Decode(plain, W, H); var dq = Decode(opt.Data, W, H);
            Check(o16.Palette.Count == 16 && dp.Count == 4 && dq.Count == 4 && dp.Zip(dq).All(t => Same(t.First, t.Second)),
                  $"16 colours exactly: optimized ({opt.Data.Length} B, table {2 << (opt.Data[10] & 7)}) decodes as plain ({plain.Length} B, table {2 << (plain[10] & 7)})");
            string p16 = Path.Combine(_dir, "rt16.gif"); File.WriteAllBytes(p16, opt.Data);
            using (var imp = Importer.Load(p16))
                Check(imp.Frames.Select((fr, i) => Same(Rgba(ImageOps.GetPixels(fr.bmp)), dp[i])).All(x => x), "16 colours exactly: Flint's own import agrees");
            // Force the spare-slot variant directly and check it too (it may lose the size contest above).
            var spare = GifTestHooks.WriteSpare(W, H, gf, o16.Palette);
            var ds = Decode(spare, W, H);
            Check((spare[10] & 7) == 4 && ds.Count == 4 && ds.Zip(dp).All(t => Same(t.First, t.Second)), "the spare-slot encoding widens the table to 32 and still decodes as plain");
            File.WriteAllBytes(p16, spare);
            using (var imp = Importer.Load(p16))
                Check(imp.Frames.Select((fr, i) => Same(Rgba(ImageOps.GetPixels(fr.bmp)), dp[i])).All(x => x), "the spare-slot encoding imports into Flint correctly");
        }
        {
            // 256 colours, nothing clear, optimized vs plain.
            var f = Sprite(W, H, 4, clearBg: false, manyColours: true);
            var ix = GifPalette.Index(f, W, H, new GifOptions { Colours = 256, RemoveUnused = false });
            var gf = ix.Indices.Select(x => new GifFrame { Indices = x }).ToList();
            var a = Decode(GifEncoder.Encode(W, H, gf, ix.Palette, ix.TransparentIndex, true, false), W, H);
            var b = Decode(GifEncoder.Encode(W, H, gf, ix.Palette, ix.TransparentIndex, true, true), W, H);
            Check(ix.Palette.Count == 256 && a.Count == 4 && b.Count == 4 && a.Zip(b).All(t => Same(t.First, t.Second)), "exactly 256 colours, nothing clear: optimized decodes as plain");
        }
        {
            // Loop restart: with clear pixels the last frame must clear the whole canvas.
            var f = Sprite(W, H, 4, clearBg: true);
            var ix = GifPalette.Index(f, W, H, new GifOptions());
            var gf = ix.Indices.Select(x => new GifFrame { Indices = x }).ToList();
            var opt = GifEncoder.EncodeEx(W, H, gf, ix.Palette, ix.TransparentIndex, true, true).Data;
            var last = GifTestHooks.LastFrame(opt);
            Check(last.disposal == 2 && last.rect == new Rectangle(0, 0, W, H), $"the last frame restores the whole canvas before the loop restarts (disposal {last.disposal}, {last.rect})");
        }
        {
            // Lossy with clear pixels, optimized: Flint's own import agrees with the preview.
            var src = Src(Sprite(W, H, 5, clearBg: true), W, H);
            var s = new OptSettings { Format = OptFormat.Gif, Colours = 64 }; s.Gif.Lossy = 120;
            var r = OptimizeEncoder.Encode(s, src, default);
            string pl = Path.Combine(_dir, "lossyclear.gif"); File.WriteAllBytes(pl, r.Data!);
            using var imp = Importer.Load(pl);
            Check(imp.Frames.Count == 5 && imp.Frames.Select((fr, i) => Same(Rgba(ImageOps.GetPixels(fr.bmp)), Rgba(r.PreviewFor(i)))).All(x => x), "lossy with clear pixels: Flint's own import matches the preview");
        }
        {
            // The main Export menu (no preview settings) now optimizes: same picture as unoptimized.
            var doc = new Document(W, H);
            var f = Sprite(W, H, 4, clearBg: true);
            for (int i = 1; i < 4; i++) doc.InsertFrame(i);
            for (int i = 0; i < 4; i++) doc.Layers[0].EnsureCel(i).Objects.Add(new RasterObject(ImageOps.FromPixels(W, H, f[i])));
            string pa = Path.Combine(_dir, "menu.gif"), pb = Path.Combine(_dir, "menu_plain.gif");
            Exporter.Run(new ExportJob { Doc = doc, Kind = ExportKind.Gif, Path = pa, From = 0, To = 3, GifColours = 256, Dither = true, Loop = true }, null, default);
            var plainOpt = Exporter.GifFor(new ExportJob { GifColours = 256, Dither = true }); plainOpt.Optimize = false;
            Exporter.Run(new ExportJob { Doc = doc, Kind = ExportKind.Gif, Path = pb, From = 0, To = 3, Gif = plainOpt, Loop = true }, null, default);
            var da = Decode(File.ReadAllBytes(pa), W, H); var db = Decode(File.ReadAllBytes(pb), W, H);
            Check(da.Count == 4 && db.Count == 4 && da.Zip(db).All(t => Same(t.First, t.Second)) && new FileInfo(pa).Length <= new FileInfo(pb).Length,
                  $"Export menu GIF: optimized ({new FileInfo(pa).Length} B) decodes as unoptimized ({new FileInfo(pb).Length} B) in Pillow");
            using var imp = Importer.Load(pa);
            Check(imp.Frames.Select((fr, i) => Same(Rgba(ImageOps.GetPixels(fr.bmp)), db[i])).All(x => x), "Export menu GIF: Flint's own import agrees");
        }

        Console.WriteLine("Palette types");
        var src0 = Src(Sprite(W, H, 4, clearBg: false, manyColours: true), W, H);
        IndexedFrames Ix(PaletteKind k, int colours = 64, Action<GifOptions>? tweak = null)
        {
            var o = new GifOptions { Palette = k, Colours = colours };
            tweak?.Invoke(o);
            return GifPalette.Index(src0.Frames, W, H, o);
        }
        foreach (var k in Enum.GetValues<PaletteKind>())
        {
            var s = new OptSettings { Format = OptFormat.Gif };
            s.Gif.Palette = k; s.Gif.Colours = 64;
            if (k == PaletteKind.Custom) s.Gif.Custom = new[] { 0xff0000, 0x00ff00, 0x0000ff, 0x000000, 0xffffff };
            if (k == PaletteKind.Exact)
            {
                var ex = Assert.Throws(() => OptimizeEncoder.Encode(s, src0, default));
                Check(ex?.Message.Contains("Exact") == true, $"Exact on a many-colour picture says why it cannot: \"{ex?.Message}\"");
                continue;
            }
            var r = OptimizeEncoder.Encode(s, src0, default);
            var d = Decode(r.Data!, W, H);
            Check(d.Count == 4 && d.Select((x, i) => Same(x, Rgba(r.PreviewFor(i)))).All(x => x), $"{GifOptions.Describe(k)}: {r.Indexed!.Palette.Count} colours, Pillow decode equals the preview");
        }
        {
            var p = Ix(PaletteKind.Web216);
            Check(Enumerable.Range(0, p.Palette.Count).All(i => p.Palette.R[i] % 51 == 0 && p.Palette.G[i] % 51 == 0 && p.Palette.B[i] % 51 == 0), $"Web 216: every colour is web-safe ({p.Palette.Count} used of 216)");
            var all = Ix(PaletteKind.Web216, tweak: o => o.RemoveUnused = false);
            Check(all.Palette.Count == 216 && p.Palette.Count < 216, "Remove unused colours drops the web colours no pixel uses");
            var g = Ix(PaletteKind.Greyscale, 16);
            Check(g.Palette.Count <= 16 && Enumerable.Range(0, g.Palette.Count).All(i => g.Palette.R[i] == g.Palette.G[i] && g.Palette.G[i] == g.Palette.B[i]), $"Greyscale 16: {g.Palette.Count} greys");
            var bw = Ix(PaletteKind.BlackWhite);
            Check(bw.Palette.Count <= 2 && Enumerable.Range(0, bw.Palette.Count).All(i => bw.Palette.R[i] is 0 or 255), "Black and white: only black and white");
            var u = Ix(PaletteKind.Uniform, 64, o => o.RemoveUnused = false);
            Check(u.Palette.Count == 64, $"Uniform 64: a 4x4x4 grid ({u.Palette.Count})");
            var wa = Ix(PaletteKind.WebAdaptive, 64);
            var ad = Ix(PaletteKind.Adaptive, 64);
            int webSafe(IndexedFrames f) => Enumerable.Range(0, f.Palette.Count).Count(i => f.Palette.R[i] % 51 == 0 && f.Palette.G[i] % 51 == 0 && f.Palette.B[i] % 51 == 0);
            Check(webSafe(wa) > webSafe(ad), $"Web Adaptive snaps near colours to web-safe ({webSafe(wa)} vs {webSafe(ad)} in Adaptive)");
            // Exact keeps a few-colour picture's pixels exactly.
            var few = Sprite(W, H, 2, clearBg: true);
            for (int i = 0; i < few[0].Length; i += 4) if (few[0][i + 3] > 0) { few[0][i] = (byte)(i % 3); few[0][i + 1] = 7; few[0][i + 2] = 9; }
            var ex = GifPalette.Index(new[] { few[0] }, W, H, new GifOptions { Palette = PaletteKind.Exact });
            Check(Same(Rgba(ex.ToBgra(0)), Rgba(few[0])), $"Exact: colours one level apart stay exact ({ex.Palette.Count} colours)");
        }

        Console.WriteLine("Dithering");
        {
            var none = Ix(PaletteKind.Adaptive, 8);
            foreach (var dk in new[] { DitherKind.FloydSteinberg, DitherKind.Ordered, DitherKind.Noise })
            {
                var d = Ix(PaletteKind.Adaptive, 8, o => o.Dither = dk);
                Check(!d.Indices[1].SequenceEqual(none.Indices[1]), $"{GifOptions.Describe(dk)} changes the picture");
                var zero = Ix(PaletteKind.Adaptive, 8, o => { o.Dither = dk; o.DitherAmount = 0; });
                Check(zero.Indices[1].SequenceEqual(none.Indices[1]), $"{GifOptions.Describe(dk)} at amount 0 is no dither");
            }
            // The old pipeline, rebuilt inline: Kolb palette, Indexer, ImageOps.Dither at offset 0.
            var hist = Quantizer.BuildHistogram(src0.Frames, out double total);
            var kolb = Quantizer.Quantize(hist, total, 8);
            var kix = new Quantizer.Indexer(kolb);
            var fs = Ix(PaletteKind.Adaptive, 8, o => { o.Dither = DitherKind.FloydSteinberg; o.RemoveUnused = false; });
            Check(src0.Frames.Select((f, i) => ImageOps.Dither(f, W, H, kolb, kix, 0).SequenceEqual(fs.Indices[i])).All(x => x),
                  "Floyd-Steinberg at 100% equals the old dither pipeline exactly");
            // 256 colours, nothing clear: the last palette entry must survive the dither.
            var fs256 = Ix(PaletteKind.Adaptive, 256, o => { o.Dither = DitherKind.FloydSteinberg; o.RemoveUnused = false; });
            Check(fs256.Palette.Count == 256 && fs256.TransparentIndex < 0 && fs256.Indices.Any(f => f.Contains((byte)255)),
                  $"Floyd-Steinberg with a full 256-colour palette still uses entry 255 ({fs256.Palette.Count} colours)");
            var plain256 = GifPalette.Index(src0.Frames, W, H, new GifOptions { Colours = 256, Dither = DitherKind.FloydSteinberg, RemoveUnused = false });
            int wrong = 0;
            for (int f = 0; f < 4; f++)
                for (int q = 0; q < W * H; q++)
                {
                    int k = plain256.Indices[f][q], o = q * 4;
                    int d = Math.Abs(plain256.Palette.R[k] - src0.Frames[f][o + 2]) + Math.Abs(plain256.Palette.G[k] - src0.Frames[f][o + 1]) + Math.Abs(plain256.Palette.B[k] - src0.Frames[f][o]);
                    if (d > 150) wrong++;
                }
            Check(wrong == 0, $"no dithered pixel lands on a far-off colour ({wrong} did)");
        }

        Console.WriteLine("Lossy");
        {
            var src = Src(Sprite(W * 3, H * 3, 4, clearBg: false, manyColours: true), W * 3, H * 3);
            var s = new OptSettings { Format = OptFormat.Gif, Colours = 128 };
            var exact = OptimizeEncoder.Encode(s, src, default);
            var ls = s.Clone(); ls.Gif.Lossy = 80;
            var lossy = OptimizeEncoder.Encode(ls, src, default);
            var d = Decode(lossy.Data!, W * 3, H * 3);
            Check(lossy.Bytes < exact.Bytes, $"lossy 80 is smaller ({lossy.Bytes} B vs {exact.Bytes} B)");
            Check(d.Count == 4 && d.Select((x, i) => Same(x, Rgba(lossy.PreviewFor(i)))).All(x => x), "lossy: the preview shows exactly what Pillow decodes");
            double err = 0; int nCh = 0;
            for (int f = 0; f < 4; f++)
                for (int i = 0; i < d[f].Length; i += 4)
                    for (int c = 0; c < 3; c++) { err += Math.Abs(d[f][i + c] - exact.PreviewFor(f)[i + 2 - c]); nCh++; }
            Check(err / nCh < 12, $"lossy 80 stays close to the exact picture (mean error {err / nCh:0.00}/255)");
        }

        Console.WriteLine("Palette files");
        {
            var cols = new[] { 0x102030, 0xff0000, 0x00ff7f, 0xfefefe, 0x000001 };
            foreach (var ext in new[] { ".act", ".gpl", ".pal" })
            {
                string p = Path.Combine(dir, "pal" + ext);
                PaletteFiles.Save(p, cols);
                var back = PaletteFiles.Load(p);
                Check(back.SequenceEqual(cols), $"{ext}: saves and loads {back.Length} colours exactly");
            }
            var raw = new byte[768]; raw[3] = 255;                          // plain 768-byte ACT
            File.WriteAllBytes(Path.Combine(dir, "plain.act"), raw);
            Check(PaletteFiles.Load(Path.Combine(dir, "plain.act")).Length == 256, "a plain 768-byte .act loads 256 colours");
            Check(Assert.Throws(() => PaletteFiles.Load(Path.Combine(dir, "reader.py"))) is InvalidDataException, "a non-palette file is refused with a message");
        }

        Console.WriteLine("Colour table: lock, edit, transparent");
        {
            var baseIx = Ix(PaletteKind.Adaptive, 8);
            int odd = 0x123456;
            var locked = Ix(PaletteKind.Adaptive, 8, o => { o.Locked.Add(odd); o.RemoveUnused = false; });
            Check(locked.Source.Contains(odd) && locked.Palette.Count <= 8, "a locked colour stays in the palette and counts toward the limit");
            int victim = baseIx.Source[1];
            var edited = Ix(PaletteKind.Adaptive, 8, o => { o.Locked.Add(victim); o.Edits[victim] = 0xff00ff; });
            int slot = edited.Source.IndexOf(victim);
            Check(slot >= 0 && edited.Palette.R[slot] == 255 && edited.Palette.G[slot] == 0 && edited.Palette.B[slot] == 255, "an edited colour is written with its new value");
            int usedBefore = baseIx.Indices.Sum(f => f.Count(b => b == 1));
            int usedAfter = edited.Indices.Sum(f => f.Count(b => b == slot));
            Check(usedAfter > 0, $"pixels that used the colour keep their entry ({usedAfter} px; {usedBefore} before)");
            var tr = Ix(PaletteKind.Adaptive, 8, o => o.Transparent.Add(victim));
            int clear = tr.Indices.Sum(f => f.Count(b => b == tr.TransparentIndex));
            Check(tr.TransparentIndex == 0 && clear > 0, $"marking a colour transparent clears its pixels ({clear} px)");
            var s = new OptSettings { Format = OptFormat.Gif, Colours = 8 };
            s.Gif.Transparent.Add(victim);
            var r = OptimizeEncoder.Encode(s, src0, default);
            var d = Decode(r.Data!, W, H);
            Check(d.Count == 4 && d.Select((x, i) => Same(x, Rgba(r.PreviewFor(i)))).All(x => x) && d[0].Where((_, i) => i % 4 == 3).Any(a => a == 0), "the transparent colour decodes clear in Pillow");
        }

        Console.WriteLine("Optimize to size");
        {
            var src = Src(Sprite(W * 2, H * 2, 4, clearBg: false, manyColours: true), W * 2, H * 2);
            var s = new OptSettings { Format = OptFormat.Gif, Colours = 256 };
            long full = OptimizeEncoder.Encode(s, src, default).Bytes;
            long target = full * 6 / 10;
            var hit = OptimizeToSize.Find(s, src, target, default);
            long got = hit == null ? -1 : OptimizeEncoder.Encode(hit, src, default).Bytes;
            Check(hit != null && got <= target, $"target {target} B: {hit?.Colours} colours, lossy {hit?.Gif.Lossy}, {got} B");
            if (hit != null && hit.Colours < 256)
            {
                var more = hit.Clone(); more.Colours++;
                Check(OptimizeEncoder.Encode(more, src, default).Bytes > target || hit.Gif.Lossy > 0, "one more colour would not fit (the search found the most)");
            }
            Check(OptimizeToSize.Find(s, src, 50, default) == null, "an impossible target reports that it cannot fit");
        }

        Console.WriteLine("Export presets");
        {
            var s = new OptSettings { Format = OptFormat.Gif, Colours = 48 };
            s.Gif.Palette = PaletteKind.Web216; s.Gif.Dither = DitherKind.Ordered; s.Gif.DitherAmount = 60; s.Gif.Lossy = 40;
            s.Gif.Locked.Add(0xabcdef); s.Gif.Edits[0x010203] = 0x040506; s.Gif.Transparent.Add(0x111111);
            ExportPresets.Save("Small web GIF", s);
            ExportPresets.Save("JPEG", new OptSettings { Format = OptFormat.Jpeg, Quality = 65 });
            var all = ExportPresets.Load();
            var b = all.GetValueOrDefault("Small web GIF");
            Check(all.Count == 2 && b != null && b.Format == OptFormat.Gif && b.Colours == 48 && b.Gif.Palette == PaletteKind.Web216
                  && b.Gif.Dither == DitherKind.Ordered && b.Gif.DitherAmount == 60 && b.Gif.Lossy == 40
                  && b.Gif.Locked.SequenceEqual(new[] { 0xabcdef }) && b.Gif.Edits[0x010203] == 0x040506 && b.Gif.Transparent.Contains(0x111111),
                  "a saved preset loads back with every GIF setting");
            Check(all["JPEG"].Quality == 65, "a JPEG preset keeps its quality");
            ExportPresets.Delete("JPEG");
            Check(!ExportPresets.Load().ContainsKey("JPEG"), "a preset can be deleted");
            File.WriteAllText(ExportPresets.FilePath, "{ broken");
            Check(ExportPresets.Load().Count == 0, "a broken presets file loads as empty rather than crashing");
        }

        Console.WriteLine("Export job uses the preview's settings");
        {
            var doc = new Document(W, H);
            for (int i = 1; i < 4; i++) doc.InsertFrame(i);
            for (int f = 0; f < 4; f++) doc.Layers[0].EnsureCel(f).Objects.Add(new RasterObject(ImageOps.FromPixels(W, H, src0.Frames[f])));
            var s = new OptSettings { Format = OptFormat.Gif, Colours = 32 };
            s.Gif.Palette = PaletteKind.Web216; s.Gif.Lossy = 30;
            var src = new OptSource { Frames = Enumerable.Range(0, 4).Select(i => doc.RenderPixels(i)).ToArray(), Delays = doc.Frames.Select(f => f.Delay).ToArray(), W = W, H = H, Loop = true };
            var want = OptimizeEncoder.Encode(s, src, default);
            string path = Path.Combine(dir, "job.gif");
            Exporter.Run(new ExportJob { Doc = doc, Kind = ExportKind.Gif, Path = path, From = 0, To = 3, Gif = s.Gif.Clone(), Loop = true }, null, default);
            Check(File.ReadAllBytes(path).SequenceEqual(want.Data!), $"exported file is byte-identical to the preview's ({want.Bytes} B)");
        }

        Console.WriteLine("Export preview window: GIF controls, colour table, presets");
        {
            var src = Src(Sprite(W, H, 3, clearBg: true), W, H);
            using var dlg = new OptimizeDialog(src) { StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) };
            dlg.Show();
            Application.DoEvents();
            var s = new OptSettings { Format = OptFormat.Gif, Colours = 40 };
            s.Gif.Palette = PaletteKind.WebAdaptive; s.Gif.Dither = DitherKind.Noise; s.Gif.DitherAmount = 35; s.Gif.Lossy = 25;
            s.Gif.Locked.Add(0x00ff00); s.Gif.Transparent.Add(0x0a0a0a);
            dlg.Pane(1).SetSettings(s);
            var back = dlg.Pane(1).Settings;
            Check(back.Gif.Palette == PaletteKind.WebAdaptive && back.Gif.Dither == DitherKind.Noise && back.Gif.DitherAmount == 35 && back.Gif.Lossy == 25
                  && back.Colours == 40 && back.Gif.Locked.Contains(0x00ff00) && back.Gif.Transparent.Contains(0x0a0a0a),
                  "a pane keeps every GIF setting, including colour-table edits");
            var deadline = DateTime.Now.AddSeconds(20);
            while (dlg.Pane(1).Busy && DateTime.Now < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            Check(dlg.Pane(1).Result?.Bytes == OptimizeEncoder.Encode(back, src, default).Bytes, $"the pane shows the real size for those settings ({dlg.Pane(1).Result?.Bytes} B)");

            ExportPresets.Save("Tiny", new OptSettings { Format = OptFormat.Gif, Colours = 8, Gif = new GifOptions { Colours = 8, Palette = PaletteKind.Greyscale } });
            dlg.TestSetActive(1);
            dlg.TestApplyPreset("Tiny");
            Check(dlg.Pane(1).Settings.Gif.Palette == PaletteKind.Greyscale && dlg.Pane(1).Settings.Colours == 8, "choosing a preset sets the selected pane");

            using var table = new ColourTableDialog(new GifOptions { Colours = 16 }, g => GifPalette.Index(src.Frames, W, H, g));
            int n0 = table.Table!.Palette.Count;
            table.TestSelect(2);
            int c2 = table.Table.Source[2];
            table.TestLock();
            Check(table.Options.Locked.Contains(c2), $"colour table: lock adds #{c2:X6} to the locked colours");
            table.TestSelect(table.Table.Source.IndexOf(c2));
            table.TestEdit(0x00ffff);
            int at = table.Table.Source.IndexOf(c2);
            Check(at >= 0 && table.Table.Palette.G[at] == 255 && table.Table.Palette.B[at] == 255 && table.Table.Palette.R[at] == 0, "colour table: edit rewrites that swatch");
            table.TestSelect(at);
            table.TestTransparent();
            Check(table.Options.Transparent.Contains(c2) && table.Table.Indices.Any(f => f.Contains((byte)table.Table.TransparentIndex)), $"colour table: transparent on ({n0} -> {table.Table.Palette.Count} entries)");
            int again = table.Table.Source.IndexOf(c2);
            Check(again >= 0, "colour table: a transparent colour keeps its swatch (Remove unused is on)");
            table.TestSelect(again);
            table.TestTransparent();
            Check(!table.Options.Transparent.Contains(c2), "colour table: transparent can be turned off again");
            dlg.Close();
        }

        Console.WriteLine(_fails == 0 ? "all GIF checks passed" : $"{_fails} GIF check(s) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    private static class Assert
    {
        public static Exception? Throws(Action a)
        {
            try { a(); } catch (Exception e) { return e; }
            return null;
        }
    }
}

/// <summary>Test-only access to encoder internals.</summary>
internal static class GifTestHooks
{
    /// <summary>The differenced encoding with a spare keep slot (no clear pixels in the source).</summary>
    public static byte[] WriteSpare(int w, int h, IList<GifFrame> frames, Palette pal) =>
        GifEncoder.TestWrite(w, h, frames, pal, -1, true, true, 0, true);

    /// <summary>Disposal and rectangle of the last image in a GIF.</summary>
    public static (int disposal, Rectangle rect) LastFrame(byte[] gif)
    {
        int pos = 13 + (gif[10] >> 7 != 0 ? 3 * (2 << (gif[10] & 7)) : 0);
        int disposal = -1; var rect = Rectangle.Empty;
        while (pos < gif.Length)
        {
            byte b = gif[pos];
            if (b == 0x3b) break;
            if (b == 0x21)
            {
                if (gif[pos + 1] == 0xf9) disposal = gif[pos + 3] >> 2 & 7;
                pos += 2;
                while (gif[pos] != 0) pos += gif[pos] + 1;
                pos++;
            }
            else if (b == 0x2c)
            {
                rect = new Rectangle(gif[pos + 1] | gif[pos + 2] << 8, gif[pos + 3] | gif[pos + 4] << 8, gif[pos + 5] | gif[pos + 6] << 8, gif[pos + 7] | gif[pos + 8] << 8);
                pos += 10 + 1;                       // descriptor, then LZW minimum code size
                while (gif[pos] != 0) pos += gif[pos] + 1;
                pos++;
            }
            else break;
        }
        return (disposal, rect);
    }
}
