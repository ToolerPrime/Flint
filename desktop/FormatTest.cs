/* Export formats, verified by an independent reader, and cancellation.
 * Flint.exe --formattest <dir> */
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Flint;

public static class FormatTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private static readonly int[] Delays = { 80, 120, 80, 200, 60 };

    /// <summary>Five frames of a semi-transparent sprite with uneven delays.</summary>
    private static Document Sample()
    {
        var d = new Document(40, 28);
        for (int i = 1; i < Delays.Length; i++) d.InsertFrame(i);
        for (int f = 0; f < Delays.Length; f++)
        {
            d.Frames[f].Delay = Delays[f];
            var px = new byte[40 * 28 * 4];
            for (int y = f * 3; y < f * 3 + 10; y++)
                for (int x = 0; x < 40; x++)
                {
                    int o = (y * 40 + x) * 4;
                    px[o] = (byte)(30 + x * 5); px[o + 1] = (byte)(200 - y * 4); px[o + 2] = (byte)(90 + f * 30);
                    px[o + 3] = (byte)(40 + x * 5);            // soft alpha ramp
                }
            d.Layers[0].EnsureCel(f).Objects.Add(new RasterObject(ImageOps.FromPixels(40, 28, px)));
        }
        return d;
    }

    /// <summary>md5 of RGBA with colour cleared under zero alpha, as the Python side does.</summary>
    private static string Hash(byte[] bgra)
    {
        var rgba = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            if (bgra[i + 3] == 0) continue;
            rgba[i] = bgra[i + 2]; rgba[i + 1] = bgra[i + 1]; rgba[i + 2] = bgra[i]; rgba[i + 3] = bgra[i + 3];
        }
        return Convert.ToHexString(MD5.HashData(rgba)).ToLowerInvariant();
    }

    private const string Reader = @"
import sys, json, hashlib, struct
from PIL import Image
path, kind = sys.argv[1], sys.argv[2]
im = Image.open(path)
out = {'n': getattr(im, 'n_frames', 1), 'md5': [], 'dur': []}
for i in range(out['n']):
    im.seek(i)
    f = im.convert('RGBA')
    b = bytearray(f.tobytes())
    for p in range(0, len(b), 4):
        if b[p + 3] == 0: b[p] = b[p + 1] = b[p + 2] = 0
    out['md5'].append(hashlib.md5(bytes(b)).hexdigest())
    out['dur'].append(im.info.get('duration'))
out['loop'] = im.info.get('loop')
if kind == 'webp':
    d = open(path, 'rb').read(); p = 12; out['dur'] = []
    while p + 8 <= len(d):
        tag, size = d[p:p+4], struct.unpack('<I', d[p+4:p+8])[0]
        body = d[p+8:p+8+size]
        if tag == b'ANIM': out['loop'] = struct.unpack('<H', body[4:6])[0]
        if tag == b'ANMF': out['dur'].append(int.from_bytes(body[12:15], 'little'))
        p += 8 + size + (size & 1)
print(json.dumps(out))
";

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var doc = Sample();
        var expected = Enumerable.Range(0, doc.FrameCount).Select(f => Hash(doc.RenderPixels(f))).ToList();
        string script = Path.Combine(dir, "reader.py");
        File.WriteAllText(script, Reader);

        foreach (var (kind, ext, exact) in new[]
        {
            (ExportKind.Apng, "png", true),
            (ExportKind.WebP, "webp", true),
        })
        {
            Console.WriteLine($"{kind}");
            if (kind == ExportKind.WebP && WebmExport.Find() == null) { Console.WriteLine("  skip  ffmpeg not installed"); continue; }
            string path = Path.Combine(dir, $"anim.{ext}");
            File.Delete(path);
            string msg;
            try { msg = Exporter.Run(new ExportJob { Doc = doc, Kind = kind, Path = path, To = doc.FrameCount - 1, Quality = 0 }, null, default); }
            catch (Exception ex) { Check(false, $"{kind} export: {ex.Message}"); continue; }
            Check(File.Exists(path), $"{kind}: {msg}");

            var json = JsonNode.Parse(Py(script, path, ext));
            if (json == null) { Check(false, "reader produced no output"); continue; }
            int n = json["n"]!.GetValue<int>();
            Check(n == Delays.Length, $"{kind}: PIL sees {n} frames, expected {Delays.Length}");
            var durs = json["dur"]!.AsArray().Select(x => x == null ? -1 : (int)Math.Round(x.GetValue<double>())).ToArray();
            Check(durs.SequenceEqual(Delays), $"{kind}: every frame keeps its delay, last included ({string.Join(",", durs)})");
            Check(json["loop"]?.GetValue<int>() == 0, $"{kind}: loops forever");
            var got = json["md5"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
            int same = got.Zip(expected).Count(p => p.First == p.Second);
            Check(exact ? same == expected.Count : same >= 0,
                  $"{kind}: {same} of {expected.Count} frames pixel-identical to Flint's render, soft alpha included");
        }

        Console.WriteLine("Sprite sheet and sequence");
        {
            string sheet = Path.Combine(dir, "sheet.png");
            Exporter.Run(new ExportJob { Doc = doc, Kind = ExportKind.Sheet, Path = sheet, To = 4, SheetCols = 2 }, null, default);
            using var s = new Bitmap(sheet);
            Check(s.Width == 80 && s.Height == 28 * 3, $"5 frames at 2 columns make an 80x84 sheet (got {s.Width}x{s.Height})");
            var cell = ImageOps.GetPixels(ImageOps.Crop(ImageOps.ReadExact(s), new Rectangle(40, 28, 40, 28)));
            Check(Hash(cell) == expected[3], "sheet cell 4 is pixel-identical to frame 4");

            string seq = Path.Combine(dir, "seq");
            if (Directory.Exists(seq)) Directory.Delete(seq, true);
            Directory.CreateDirectory(seq);
            Exporter.Run(new ExportJob { Doc = doc, Kind = ExportKind.Sequence, Path = seq, From = 1, To = 3 }, null, default);
            var files = Directory.GetFiles(seq, "*.png").OrderBy(f => f).ToArray();
            Check(files.Length == 3, $"exporting frames 2-4 as a sequence writes 3 files (got {files.Length})");
            using var f0 = new Bitmap(files[0]);
            Check(Hash(ImageOps.GetPixels(ImageOps.ReadExact(f0))) == expected[1], "the first file is frame 2, exactly");
        }

        Console.WriteLine("Cancellation");
        {
            var big = new Document(512, 512);
            for (int i = 1; i < 64; i++) big.InsertFrame(i);
            for (int f = 0; f < 64; f++)
            {
                var px = big.Layers[0].EnsureCel(f).EnsureRaster(512, 512).Pixels;
                using var g = Graphics.FromImage(px);
                g.FillEllipse(Brushes.Teal, f * 6, 50, 200, 200);
            }
            string path = Path.Combine(dir, "cancelled.png");
            File.Delete(path);
            using var cts = new CancellationTokenSource();
            var job = new ExportJob { Doc = big, Kind = ExportKind.Apng, Path = path, To = 63 };
            var sw = Stopwatch.StartNew();
            var task = Task.Run(() => Exporter.Run(job, null, cts.Token));
            Thread.Sleep(60);
            cts.Cancel();
            bool cancelled = false;
            try { task.Wait(20_000); } catch (AggregateException ae) when (ae.InnerException is OperationCanceledException) { cancelled = true; }
            Check(cancelled, $"cancelling a 64-frame export stops it ({sw.ElapsedMilliseconds} ms)");
            Check(!File.Exists(path), "a cancelled export leaves no partial file");
            big.Dispose();
        }

        var ffmpeg = WebmExport.Find();
        if (ffmpeg != null)
        {
            // A deliberately slow ffmpeg job, cancelled mid-encode.
            using var cts = new CancellationTokenSource();
            string slow = Path.Combine(dir, "slow.webm");
            var sw = Stopwatch.StartNew();
            var task = Task.Run(() => WebmExport.RunFfmpeg(ffmpeg,
                $"-y -v error -f lavfi -i testsrc2=size=1280x720:rate=60:duration=120 -c:v libvpx-vp9 -deadline best \"{slow}\"",
                600_000, cts.Token));
            Thread.Sleep(1500);
            int before = Process.GetProcessesByName("ffmpeg").Length;
            cts.Cancel();
            bool cancelled = false;
            try { task.Wait(10_000); } catch (AggregateException ae) when (ae.InnerException is OperationCanceledException) { cancelled = true; }
            Thread.Sleep(300);
            int after = Process.GetProcessesByName("ffmpeg").Length;
            Check(cancelled && sw.ElapsedMilliseconds < 6000, $"cancelling stops a running ffmpeg encode ({sw.ElapsedMilliseconds} ms)");
            Check(before > after, $"and the ffmpeg process is gone ({before} running before, {after} after)");
            try { File.Delete(slow); } catch { }
        }

        doc.Dispose();
        Console.WriteLine(_fails == 0 ? "all format checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    private static string Py(string script, string a, string b)
    {
        var psi = new ProcessStartInfo("python", $"-W ignore \"{script}\" \"{a}\" {b}")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        string o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(60_000);
        if (o.Trim().Length == 0) Console.WriteLine("  note  reader: " + err.Result.Trim());
        return o;
    }
}
