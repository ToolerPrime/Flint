/* Import modes, pixel preservation, palette conversion, notes, and the atlas
 * export through the real exporter. Flint.exe --importtest <dir> */
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Flint;

public static class ImportTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private static string Png(string dir, string name, int w, int h, Func<int, int, Color> f)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = f(x, y);
                int o = (y * w + x) * 4;
                px[o] = c.B; px[o + 1] = c.G; px[o + 2] = c.R; px[o + 3] = c.A;
            }
        string path = Path.Combine(dir, name);
        using var b = ImageOps.FromPixels(w, h, px);
        b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    private static byte[] Raw(string path)
    {
        using var img = Image.FromFile(path);
        return ImageOps.GetPixels(ImageOps.ReadExact(img));
    }

    public static int Run(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);

        // Three soft-edged sprite frames, and a bigger image.
        var paths = Enumerable.Range(0, 3).Select(i => Png(dir, $"walk{i}.png", 24, 20,
            (x, y) => Color.FromArgb((x * 10 + i * 30) % 256, 40 + i * 60, 200 - x * 4, 90 + y))).ToArray();
        string big = Png(dir, "big.png", 60, 50, (x, y) => Color.FromArgb(255, x * 4, y * 5, 100));

        List<ImportedFile> Load(params string[] p) => p.Select(Importer.Load).ToList();

        Console.WriteLine("Defaults");
        {
            var o = new ImportOptions();
            Check(o.PreservePixels && !o.ConvertToPalette && o.Mode == ImportMode.Frames,
                  "by default: preserve pixels on, palette conversion off, import as frames");
        }

        Console.WriteLine("As frames");
        {
            var d = new Document(64, 64);
            var files = Load(paths);
            Importer.Apply(d, 0, files, new ImportOptions(), out int first, out int layer);
            Check(d.FrameCount == 3 && first == 0, $"three images into a blank document make three frames ({d.FrameCount})");
            Check(d.Width == 24 && d.Height == 20, $"a blank document takes the images' size ({d.Width}x{d.Height})");
            for (int i = 0; i < 3; i++)
                Check(d.Layers[layer].CelAt(i)!.FindRaster()!.Pixels is var b && ImageOps.GetPixels(b).AsSpan().SequenceEqual(Raw(paths[i])),
                      $"frame {i + 1} holds image {i + 1}'s exact pixels, soft alpha included");
            foreach (var f in files) f.Dispose();

            // Into an existing animation: insert after the current frame.
            var mark = d.AddLayer("existing");
            mark.EnsureCel(2).EnsureRaster(24, 20).Pixels.SetPixel(0, 0, Color.Red);
            files = Load(paths[0]);
            Importer.Apply(d, 1, files, new ImportOptions(), out first, out _);
            Check(d.FrameCount == 4 && first == 2, $"importing into frame 2 inserts a new frame 3 ({d.FrameCount} frames, first {first})");
            Check(mark.CelAt(3) != null && mark.CelAt(2) == null, "other layers' later frames shift along with it");
            foreach (var f in files) f.Dispose();
            d.Dispose();
        }

        Console.WriteLine("Size handling");
        {
            var d = new Document(32, 32);
            d.Layers[0].EnsureCel(0).EnsureRaster(32, 32).Pixels.SetPixel(1, 1, Color.Blue);
            var files = Load(big);
            Importer.Apply(d, 0, files, new ImportOptions { Mode = ImportMode.OneImage }, out _, out int layer);
            Check(d.Width == 60 && d.Height == 50, $"preserving pixels grows the canvas to fit ({d.Width}x{d.Height})");
            var r = d.Layers[layer].CelAt(0)!.FindRaster()!;
            Check(r.Width == 60 && ImageOps.GetPixels(r.Pixels).AsSpan().SequenceEqual(Raw(big)), "and the image is untouched");
            foreach (var f in files) f.Dispose();
            d.Dispose();

            d = new Document(30, 25);
            d.Layers[0].EnsureCel(0).EnsureRaster(30, 25).Pixels.SetPixel(1, 1, Color.Blue);
            files = Load(big);
            Importer.Apply(d, 0, files, new ImportOptions { Mode = ImportMode.OneImage, PreservePixels = false, FitToCanvas = true }, out _, out layer);
            r = d.Layers[layer].CelAt(0)!.FindRaster()!;
            Check(d.Width == 30 && r.Width == 30 && r.Height == 25, $"fit-to-canvas shrinks instead ({r.Width}x{r.Height}), canvas stays 30x25");
            var src = new HashSet<int>(); var srcPx = Raw(big);
            for (int i = 0; i < srcPx.Length; i += 4) src.Add(BitConverter.ToInt32(srcPx, i));
            var outPx = ImageOps.GetPixels(r.Pixels);
            bool noNew = true;
            for (int i = 0; i < outPx.Length; i += 4) if (!src.Contains(BitConverter.ToInt32(outPx, i))) { noNew = false; break; }
            Check(noNew, "nearest-neighbour shrinking adds no new colours");
            foreach (var f in files) f.Dispose();
            d.Dispose();
        }

        Console.WriteLine("Palette conversion");
        {
            // A document whose palette is just four colours.
            var d = new Document(24, 20);
            var bg = d.Layers[0].EnsureCel(0).EnsureRaster(24, 20).Pixels;
            var four = new[] { Color.FromArgb(255, 20, 20, 20), Color.FromArgb(255, 220, 60, 50), Color.FromArgb(255, 60, 200, 90), Color.FromArgb(255, 240, 240, 230) };
            for (int y = 0; y < 20; y++) for (int x = 0; x < 24; x++) bg.SetPixel(x, y, four[(x / 6) % 4]);
            var files = Load(paths[1]);
            var palBefore = Importer.ProjectPalette(d, files, 4);
            var palSet = Enumerable.Range(0, palBefore.Count).Select(i => (palBefore.R[i], palBefore.G[i], palBefore.B[i])).ToHashSet();
            Importer.Apply(d, 0, files, new ImportOptions { Mode = ImportMode.Layers, ConvertToPalette = true, PaletteColours = 4 }, out _, out int layer);
            var px = ImageOps.GetPixels(d.Layers[layer].CelAt(0)!.FindRaster()!.Pixels);
            var srcPx = Raw(paths[1]);
            var colours = new HashSet<(byte, byte, byte)>();
            bool alphaKept = true;
            for (int i = 0; i < px.Length; i += 4)
            {
                if (px[i + 3] != srcPx[i + 3]) alphaKept = false;
                if (px[i + 3] > 0) colours.Add((px[i + 2], px[i + 1], px[i]));
            }
            Check(colours.Count > 0 && colours.All(palSet.Contains),
                  $"every converted pixel is one of the {palSet.Count} project colours ({colours.Count} used)");
            Check(alphaKept, "palette conversion keeps every pixel's alpha exactly");
            var notes = Importer.Notes(files, new ImportOptions { ConvertToPalette = true, PaletteColours = 4 });
            Check(notes.Any(n => n.Contains("nearest of 4")), "the dialog warns that palette conversion changes colours");
            foreach (var f in files) f.Dispose();
            d.Dispose();
        }

        Console.WriteLine("Sprite sheets");
        {
            // Four sprites of different heights on a transparent sheet.
            string sheet = Png(dir, "sheet.png", 100, 30, (x, y) =>
            {
                int col = x / 25, h = 10 + col * 4;
                return x % 25 >= 4 && x % 25 < 20 && y >= 30 - h ? Color.FromArgb(255, 50 + col * 40, 90, 160) : Color.Transparent;
            });
            var d = new Document(16, 16);
            var files = Load(sheet);
            string s = Importer.Apply(d, 0, files, new ImportOptions { Mode = ImportMode.SheetAuto }, out _, out int layer);
            Check(d.FrameCount == 4, $"automatic slicing finds 4 sprites ({s})");
            Check(d.Width == 16 && d.Height == 22, $"frames are sized to the largest sprite, 16x22 ({d.Width}x{d.Height})");
            // Bottom-centre: every frame's lowest opaque row is the canvas's last row.
            bool feet = Enumerable.Range(0, 4).All(f => d.RenderPixels(f)[((d.Height - 1) * d.Width + 8) * 4 + 3] == 255);
            Check(feet, "every frame's feet sit on the same bottom line");
            foreach (var f in files) f.Dispose();
            d.Dispose();

            d = new Document(16, 16);
            files = Load(sheet);
            Importer.Apply(d, 0, files, new ImportOptions { Mode = ImportMode.SheetGrid, CellW = 25, CellH = 30 }, out _, out _);
            Check(d.FrameCount == 4 && d.Width == 25 && d.Height == 30, $"a 25x30 grid gives 4 frames of exactly that size ({d.FrameCount}, {d.Width}x{d.Height})");
            foreach (var f in files) f.Dispose();
            d.Dispose();
        }

        Console.WriteLine("Notes");
        {
            string jpg = Path.Combine(dir, "photo.jpg");
            using (var b = new Bitmap(16, 16)) b.Save(jpg, System.Drawing.Imaging.ImageFormat.Jpeg);
            var files = Load(jpg);
            var notes = Importer.Notes(files, new ImportOptions());
            Check(notes.Any(n => n.Contains("JPEG is lossy") && n.Contains("cannot restore")),
                  "a JPEG import says it is lossy and that detail cannot be restored");
            foreach (var f in files) f.Dispose();

            string icc = Path.Combine(dir, "icc.png");
            string ok = Py("from PIL import Image, ImageCms\n" +
                           "p = ImageCms.ImageCmsProfile(ImageCms.createProfile('sRGB'))\n" +
                           "Image.new('RGBA', (8, 8), (10, 200, 30, 255)).save(r'" + icc + "', icc_profile=p.tobytes())\n" +
                           "print('ok')\n");
            if (ok.Trim() == "ok")
            {
                files = Load(icc);
                Check(files[0].HasProfile, "an embedded colour profile is detected");
                Check(Importer.Notes(files, new ImportOptions()).Any(n => n.Contains("colour profile")),
                      "and the dialog says the colours are kept, not converted");
                var px = ImageOps.GetPixels(files[0].Frames[0].bmp);
                Check(px[2] == 10 && px[1] == 200 && px[0] == 30, $"the stored colour values are kept as they are ({px[2]},{px[1]},{px[0]})");
                foreach (var f in files) f.Dispose();
            }
            else Console.WriteLine("  skip  colour profile: " + ok.Trim());
        }

        Console.WriteLine("Fireworks documents");
        {
            string fwSample = @"C:\Program Files (x86)\Macromedia\Fireworks 8\Samples\Pocket_PC.png";
            if (File.Exists(fwSample))
            {
                var files = Load(fwSample);
                Check(files[0].IsFireworksDocument, "a real Fireworks 8 sample is recognised as a Fireworks document");
                var notes = Importer.Notes(files, new ImportOptions());
                Check(notes.Any(n => n.Contains("Fireworks document") && n.Contains("flattened") && n.Contains("not changed")),
                      "and the dialog warns its layers will be flattened and the original is untouched");
                foreach (var f in files) f.Dispose();
            }
            else Console.WriteLine("  skip  no Fireworks 8 install to test against");
            var plain = Load(paths[0]);
            Check(!plain[0].IsFireworksDocument, "an ordinary PNG is not mistaken for one");
            foreach (var f in plain) f.Dispose();
        }

        Console.WriteLine("Atlas export");
        {
            var d = new Document(64, 64);
            var files = Load(paths.Concat(new[] { paths[0] }).ToArray());   // frame 4 repeats frame 1
            Importer.Apply(d, 0, files, new ImportOptions(), out _, out _);
            foreach (var f in files) f.Dispose();
            d.Frames[1].Pivot = new Point(12, 19);
            d.Tags.Add(new AnimTag { Name = "Walk", Start = 0, End = 3 });
            var t = new AudioTrack { Name = "SFX" };
            d.Audio.Tracks.Add(t);
            d.Audio.AddClip(t, d.Audio.AddSource(new float[AudioFormat.MsToFrames(40) * 2], "step"), 100);

            string png = Path.Combine(dir, "hero.png");
            string msg = Exporter.Run(new ExportJob { Doc = d, Kind = ExportKind.Atlas, Path = png, To = 3 }, null, default);
            Check(File.Exists(png) && File.Exists(Path.ChangeExtension(png, ".json")), "atlas writes a PNG and a JSON: " + msg);
            Check(File.Exists(Path.Combine(dir, "hero.wav")) && File.Exists(Path.Combine(dir, "hero.audio.json")),
                  "with audio it also writes a WAV and an audio cue file");
            var json = JsonNode.Parse(File.ReadAllText(Path.ChangeExtension(png, ".json")))!;
            Check(json["frames"]!.AsObject().Count == 4 && msg.Contains("3 unique"), "4 frames, the repeated one stored once");
            Check(json["meta"]!["frameTags"]![0]!["name"]!.GetValue<string>() == "Walk", "the Walk tag is in the JSON");
            d.Dispose();
        }

        Console.WriteLine(_fails == 0 ? "all import checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    private static string Py(string code)
    {
        string script = Path.Combine(Path.GetTempPath(), "flint_imp_" + Guid.NewGuid().ToString("N")[..8] + ".py");
        File.WriteAllText(script, code);
        try
        {
            var psi = new ProcessStartInfo("python", $"-W ignore \"{script}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync();
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(60_000);
            return o.Length > 0 ? o : err.Result;
        }
        finally { File.Delete(script); }
    }
}
