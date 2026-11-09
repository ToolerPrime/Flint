/* Still export formats (JPEG, WebP, BMP, TIFF, PNG-8) checked by Pillow, and
 * the Export preview: real encoded sizes, decoded previews, stale jobs.
 * Flint.exe --exporttest <dir> */
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Flint;

public static class ExportTest
{
    private static int _fails;
    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private const int W = 48, H = 32;
    private static readonly int[] Delays = { 80, 120, 60 };

    /// <summary>Three frames: colour gradients, a soft alpha ramp, and a fully clear corner.</summary>
    private static Document Sample()
    {
        var d = new Document(W, H);
        for (int i = 1; i < Delays.Length; i++) d.InsertFrame(i);
        for (int f = 0; f < Delays.Length; f++)
        {
            d.Frames[f].Delay = Delays[f];
            var px = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int o = (y * W + x) * 4;
                    px[o] = (byte)(x * 5); px[o + 1] = (byte)(y * 7 + f * 20); px[o + 2] = (byte)(200 - x * 3 + f * 30);
                    px[o + 3] = x < 8 && y < 8 ? (byte)0 : (byte)(x < 16 ? 90 + y * 5 : 255);
                }
            d.Layers[0].EnsureCel(f).Objects.Add(new RasterObject(ImageOps.FromPixels(W, H, px)));
        }
        return d;
    }

    private const string Reader = @"
import sys, json
from PIL import Image
path, raw, w, h, matte = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), sys.argv[5]
mr, mg, mb = [int(matte[i:i+2], 16) for i in (0, 2, 4)]
exp = open(raw, 'rb').read()
fs = w * h * 4
im = Image.open(path)
n = getattr(im, 'n_frames', 1)
def clear(b):
    b = bytearray(b)
    for p in range(0, len(b), 4):
        if b[p+3] == 0: b[p] = b[p+1] = b[p+2] = 0
    return bytes(b)
def flat(b):
    o = []
    for p in range(0, len(b), 4):
        r, g, bb, a = b[p:p+4]
        o += [(r*a + mr*(255-a) + 127)//255, (g*a + mg*(255-a) + 127)//255, (bb*a + mb*(255-a) + 127)//255]
    return o
out = {'n': n, 'mode': im.mode, 'size': [im.width, im.height], 'frames': []}
for i in range(n):
    im.seek(i)
    got = im.convert('RGBA').tobytes()
    want = exp[i*fs:(i+1)*fs] if (i+1)*fs <= len(exp) else exp[:fs]
    a, b = flat(got), flat(want)
    mse = sum((x-y)**2 for x, y in zip(a, b)) / len(a)
    out['frames'].append({'exact': clear(got) == clear(want), 'psnr': 99.0 if mse == 0 else 10 * __import__('math').log10(255*255/mse)})
print(json.dumps(out))
";

    private static string Py(string script, params string[] args)
    {
        var psi = new ProcessStartInfo("python", "-W ignore \"" + script + "\" " + string.Join(" ", args.Select(a => "\"" + a + "\"")))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        string o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(60_000);
        if (o.Trim().Length == 0) Console.WriteLine("  note  reader: " + err.Result.Trim());
        return o;
    }

    private static byte[] Rgba(byte[] bgra)
    {
        var o = new byte[bgra.Length];
        for (int i = 0; i < o.Length; i += 4) { o[i] = bgra[i + 2]; o[i + 1] = bgra[i + 1]; o[i + 2] = bgra[i]; o[i + 3] = bgra[i + 3]; }
        return o;
    }

    private sealed class Verdict { public bool Exact; public double Psnr; public int N; public string Mode = ""; }

    /// <summary>Decode a file with Pillow and compare each frame to the expected BGRA frames.</summary>
    private static List<Verdict> Verify(string dir, string file, byte[][] expected, Color matte)
    {
        string raw = Path.Combine(dir, "expected.raw");
        File.WriteAllBytes(raw, expected.SelectMany(Rgba).ToArray());
        var json = JsonNode.Parse(Py(Path.Combine(dir, "reader.py"), file, raw, W.ToString(), H.ToString(), $"{matte.R:X2}{matte.G:X2}{matte.B:X2}"));
        var list = new List<Verdict>();
        if (json == null) return list;
        foreach (var f in json["frames"]!.AsArray())
            list.Add(new Verdict { Exact = f!["exact"]!.GetValue<bool>(), Psnr = f["psnr"]!.GetValue<double>(), N = json["n"]!.GetValue<int>(), Mode = json["mode"]!.GetValue<string>() });
        return list;
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "reader.py"), Reader);
        var doc = Sample();
        var frames = Enumerable.Range(0, doc.FrameCount).Select(f => doc.RenderPixels(f)).ToArray();
        var matte = Color.FromArgb(255, 200, 60, 120);
        bool ffmpeg = WebmExport.Find() != null;

        var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000), SuppressPrompts = true };
        form.Show();
        Application.DoEvents();
        form.TestUseDocument(doc);

        Console.WriteLine("Still formats, decoded by Pillow");
        string P(string n) => Path.Combine(dir, n);
        ExportJob Job(ExportKind k, string path, int q = 90) => new() { Doc = doc, Kind = k, Path = path, Current = 1, From = 1, To = 1, Quality = q, Matte = matte, GifColours = 32 };

        foreach (var (kind, name, exact) in new[] { (ExportKind.Bmp, "still.bmp", true), (ExportKind.Tiff, "still.tif", true) })
        {
            string path = P(name);
            string msg = Exporter.Run(Job(kind, path), null, default);
            var v = Verify(dir, path, new[] { frames[1] }, matte);
            Check(v.Count == 1 && v[0].Exact && v[0].Mode == "RGBA", $"{kind}: all {W * H} pixels identical including alpha, Pillow mode {(v.Count > 0 ? v[0].Mode : "?")} ({msg})");
        }

        {
            string path = P("still.jpg");
            Exporter.Run(Job(ExportKind.Jpeg, path, 90), null, default);
            var v = Verify(dir, path, new[] { frames[1] }, matte);
            Check(v.Count == 1 && v[0].Psnr >= 30, $"JPEG q90: PSNR {(v.Count > 0 ? v[0].Psnr : 0):0.0} dB against the frame flattened on the matte colour (need 30)");
            Exporter.Run(Job(ExportKind.Jpeg, P("lo.jpg"), 10), null, default);
            long lo = new FileInfo(P("lo.jpg")).Length;
            Check(lo < new FileInfo(path).Length, $"JPEG q10 ({lo} B) is smaller than q90 ({new FileInfo(path).Length} B)");
            // Transparent pixels take the matte colour, not black.
            using var img = new Bitmap(path);
            var c = img.GetPixel(2, 2);
            Check(Math.Abs(c.R - matte.R) < 12 && Math.Abs(c.G - matte.G) < 12 && Math.Abs(c.B - matte.B) < 12, $"JPEG: a clear pixel comes out as the matte colour ({c.R},{c.G},{c.B})");
        }

        {
            string path = P("still8.png");
            Exporter.Run(Job(ExportKind.Png8, path), null, default);
            StillExport.Png8(frames[1], W, H, 32, false, out var pic);
            var v = Verify(dir, path, new[] { pic }, matte);
            Check(v.Count == 1 && v[0].Exact, "PNG-8: Pillow's decode equals the palette picture the preview shows");
            var js = JsonNode.Parse(Py(P("reader.py"), path, P("expected.raw"), W.ToString(), H.ToString(), "000000"));
            Check(js!["mode"]!.GetValue<string>() is "P" or "RGBA" or "PA", "PNG-8: Pillow opens it as an indexed image");
            var head = File.ReadAllBytes(path);
            Check(head[24] == 8 && head[25] == 3, $"PNG-8: the header says 8 bits, colour type 3 (palette): {head[24]}, {head[25]}");
        }

        if (ffmpeg)
        {
            string path = P("still_ll.webp");
            Exporter.Run(Job(ExportKind.WebPStill, path, 0), null, default);
            var v = Verify(dir, path, new[] { frames[1] }, matte);
            Check(v.Count == 1 && v[0].Exact, "WebP still, lossless: every pixel identical, alpha included");
            string lossy = P("still_lossy.webp");
            Exporter.Run(Job(ExportKind.WebPStill, lossy, 80), null, default);
            v = Verify(dir, lossy, new[] { frames[1] }, matte);
            Check(v.Count == 1 && v[0].Psnr >= 28, $"WebP still, lossy q80: PSNR {(v.Count > 0 ? v[0].Psnr : 0):0.0} dB (need 28), {new FileInfo(lossy).Length} B vs {new FileInfo(path).Length} B lossless");
        }
        else Console.WriteLine("  skip  WebP: ffmpeg not installed");

        Console.WriteLine("Export preview: real encoded sizes");
        var src = form.TestOptSource();
        Check(src.Frames.Length == 3 && src.Current == 0, "the preview source holds every composited frame");
        src.Current = 1;
        src.Matte = matte;
        foreach (var fmt in Enum.GetValues<OptFormat>())
        {
            if (fmt is OptFormat.WebpLossless or OptFormat.WebpLossy && !ffmpeg) continue;
            var s = new OptSettings { Format = fmt, Colours = 24, Dither = fmt == OptFormat.Gif, Quality = 70 };
            var r = OptimizeEncoder.Encode(s, src, default);
            if (fmt == OptFormat.Original) { Check(r.Bytes < 0 && r.Preview.Length == 3 && r.Preview[1].SequenceEqual(frames[1]), "Original: the exact composite, no encoding"); continue; }
            var (kind, _) = form.TestKindFor(s);
            string path = P("opt_" + fmt + ".bin");
            form.TestSelectFrames(1, 1);
            form.TestRunOptimized(s, path, matte);
            var file = File.ReadAllBytes(path);
            Check(r.Bytes == file.Length && r.Data!.SequenceEqual(file), $"{fmt}: shown size {r.Bytes} B is exactly the exported file ({file.Length} B, {kind})");
        }

        Console.WriteLine("Export preview: the decoded picture is what the file shows");
        {
            var gif = OptimizeEncoder.Encode(new OptSettings { Format = OptFormat.Gif, Colours = 16, Dither = false }, src, default);
            string p = P("prev.gif"); File.WriteAllBytes(p, gif.Data!);
            var v = Verify(dir, p, gif.Preview, matte);
            Check(v.Count == 3 && v.All(x => x.Exact), $"GIF 16 colours: Pillow's {v.Count} decoded frames equal the preview frames exactly");
            var plain = OptimizeEncoder.Encode(new OptSettings { Format = OptFormat.Gif, Colours = 256 }, src, default);
            Check(gif.Bytes < plain.Bytes, $"fewer colours is smaller ({gif.Bytes} B at 16 vs {plain.Bytes} B at 256)");
            var gifD = OptimizeEncoder.Encode(new OptSettings { Format = OptFormat.Gif, Colours = 16, Dither = true }, src, default);
            Check(!gifD.Preview[1].SequenceEqual(gif.Preview[1]), "dither changes the picture");

            var jpg = OptimizeEncoder.Encode(new OptSettings { Format = OptFormat.Jpeg, Quality = 60 }, src, default);
            p = P("prev.jpg"); File.WriteAllBytes(p, jpg.Data!);
            v = Verify(dir, p, jpg.Preview, matte);
            Check(v.Count == 1 && v[0].Psnr >= 40, $"JPEG q60: preview vs Pillow's decode PSNR {(v.Count > 0 ? v[0].Psnr : 0):0.0} dB");
            var j90 = OptimizeEncoder.Encode(new OptSettings { Format = OptFormat.Jpeg, Quality = 90 }, src, default);
            Check(j90.Bytes > jpg.Bytes, $"JPEG quality 90 ({j90.Bytes} B) is bigger than 60 ({jpg.Bytes} B)");

            var apng = OptimizeEncoder.Encode(new OptSettings { Format = OptFormat.Apng }, src, default);
            p = P("prev.png"); File.WriteAllBytes(p, apng.Data!);
            v = Verify(dir, p, apng.Preview, matte);
            Check(v.Count == 3 && v.All(x => x.Exact), "APNG: lossless, three frames identical to the preview");

            if (ffmpeg)
            {
                var wl = OptimizeEncoder.Encode(new OptSettings { Format = OptFormat.WebpLossy, Quality = 40 }, src, default);
                p = P("prev.webp"); File.WriteAllBytes(p, wl.Data!);
                v = Verify(dir, p, new[] { wl.Preview[0] }.Concat(new byte[2][].Select(_ => wl.Preview[0])).ToArray(), matte);
                // The animated file's frame 1 against the decoded current frame.
                Check(v.Count == 3 && v[1].Psnr >= 35, $"WebP lossy: preview of the current frame vs Pillow's decode of the animated file {(v.Count > 1 ? v[1].Psnr : 0):0.0} dB (need 35)");
            }
        }

        Console.WriteLine("Export preview dialog");
        form.TestSelectFrames(0, 0);
        {
            using var dlg = form.TestOptimizeDialog();
            dlg.StartPosition = FormStartPosition.Manual; dlg.Location = new Point(-6000, -6000);
            dlg.Show();
            Application.DoEvents();
            dlg.TestMode(3);
            Wait(dlg);
            var s0 = new OptSettings { Format = OptFormat.Gif, Colours = 20 };
            dlg.Pane(0).SetSettings(s0);
            // Change it again straight away: the first (stale) job must not win.
            var s1 = new OptSettings { Format = OptFormat.Gif, Colours = 6 };
            dlg.Pane(0).SetSettings(s1);
            dlg.Pane(1).SetSettings(new OptSettings { Format = OptFormat.Bmp });
            dlg.Pane(2).SetSettings(new OptSettings { Format = OptFormat.Jpeg, Quality = 50 });
            dlg.Pane(3).SetSettings(new OptSettings { Format = OptFormat.Tiff });
            Wait(dlg);
            var want = OptimizeEncoder.Encode(s1, form.TestOptSource(), default);
            Check(dlg.Pane(0).Result?.Bytes == want.Bytes, $"4-Up: pane 1 shows the newest settings' real size ({dlg.Pane(0).Result?.Bytes} B), not the cancelled job's");
            Check(dlg.Pane(0).SizeText.Contains(want.Bytes.ToString("N0")), $"the size label reads \"{dlg.Pane(0).SizeText}\"");
            Check(dlg.Pane(1).Result?.Bytes == StillExport.Bmp(frames[0], W, H).Length, "pane 2 (BMP) size is the BMP's byte count");
            Check(dlg.Pane(2).Result?.Bytes > 0 && dlg.Pane(3).Result?.Bytes == StillExport.Tiff(frames[0], W, H).Length, "panes 3 and 4 have their own sizes");
            dlg.TestMode(0);
            Wait(dlg);
            Check(dlg.Pane(0).Result?.Bytes < 0 && dlg.Pane(0).Result?.Preview[0].SequenceEqual(frames[0]) == true, "Original tab: pane 1 shows the exact composite");
            dlg.TestMode(2);
            Wait(dlg);
            Check(dlg.Pane(0).Result?.Bytes == want.Bytes, "back to 2-Up: the pane's own settings are restored");
            dlg.TestAdvance();
            dlg.View.Zoom = 3; dlg.View.Raise();
            Check(true, "play step and shared zoom run without error");
            if (ffmpeg)
            {
                dlg.Pane(1).SetSettings(new OptSettings { Format = OptFormat.WebpLossy, Quality = 50 });
                Wait(dlg);
                Check(dlg.Pane(1).Result?.Bytes > 0 && dlg.Pane(1).Error == "", $"WebP lossy pane: {dlg.Pane(1).SizeText}");
            }
            var more = dlg.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>()
                .Single(b => b.Text == "More formats…");
            more.PerformClick();
            Check(dlg.OtherFormatsChosen && dlg.DialogResult == DialogResult.OK && dlg.Chosen == null,
                "More formats routes to the full export list without choosing a preview preset");
            dlg.Close();
        }

        form.Close();
        doc.Dispose();
        Console.WriteLine(_fails == 0 ? "all export checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    private static void Wait(OptimizeDialog d)
    {
        var sw = Stopwatch.StartNew();
        Application.DoEvents();
        while (sw.ElapsedMilliseconds < 30_000)
        {
            Application.DoEvents();
            Thread.Sleep(20);
            if (sw.ElapsedMilliseconds > 400 && d.Idle) { Application.DoEvents(); return; }
        }
    }
}
