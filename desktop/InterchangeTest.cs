/* Aseprite import/export, the HTML player page, and video import.
 * Flint.exe --interchangetest <dir> */
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Flint;

public static class InterchangeTest
{
    private static int _fails;
    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private const int W = 20, H = 12;

    private static byte[] Pic(int seed, bool holes)
    {
        var px = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int o = (y * W + x) * 4;
                if (holes && (x + y + seed) % 5 == 0) continue;
                px[o] = (byte)(x * 12 + seed * 30); px[o + 1] = (byte)(y * 20); px[o + 2] = (byte)(200 - seed * 40);
                px[o + 3] = holes && x < 3 ? (byte)(60 + y * 10) : (byte)255;      // some soft alpha too
            }
        return px;
    }

    private static Document Sample()
    {
        var d = new Document(W, H);
        for (int i = 1; i < 3; i++) d.InsertFrame(i);
        d.Frames[0].Delay = 80; d.Frames[1].Delay = 150; d.Frames[2].Delay = 40;
        d.Layers[0].Name = "Back";
        var bg = d.Layers[0];
        bg.Shared = true;
        bg.EnsureCel(0).Objects.Add(new RasterObject(ImageOps.FromPixels(W, H, Pic(0, false))));
        var fx = d.AddLayer("Sprite ünï");
        fx.Opacity = 170; fx.Blend = BlendMode.Multiply;
        for (int f = 0; f < 3; f++) if (f != 1) fx.EnsureCel(f).Objects.Add(new RasterObject(ImageOps.FromPixels(W, H, Pic(f + 1, true))));
        var hidden = d.AddLayer("Hidden");
        hidden.Visible = false;
        hidden.EnsureCel(2).Objects.Add(new RasterObject(ImageOps.FromPixels(W, H, Pic(5, true))));
        d.Tags.Add(new AnimTag { Name = "Idle", Start = 0, End = 1, Colour = Color.FromArgb(10, 200, 90) });
        d.Tags.Add(new AnimTag { Name = "Hit", Start = 2, End = 2 });
        return d;
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i += 4)
        {
            if (a[i + 3] != b[i + 3]) return false;
            if (a[i + 3] != 0 && (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2])) return false;
        }
        return true;
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        string P(string n) => Path.Combine(dir, n);

        Console.WriteLine("Aseprite export and import");
        {
            var d = Sample();
            string path = P("sample.aseprite");
            string msg = AsepriteFile.Write(d, path);
            var bytes = File.ReadAllBytes(path);
            Check(BitConverter.ToUInt32(bytes, 0) == bytes.Length && BitConverter.ToUInt16(bytes, 4) == 0xA5E0 && BitConverter.ToUInt16(bytes, 6) == 3
                  && BitConverter.ToUInt16(bytes, 12) == 32, $"header: file size, magic, 3 frames, 32-bit RGBA ({msg})");
            var back = AsepriteFile.Read(path);
            var e = back.Doc;
            Check(e.Width == W && e.Height == H && e.FrameCount == 3 && e.LayerCount == 3, $"read back: {back.Summary}");
            Check(e.Frames.Select(f => f.Delay).SequenceEqual(new[] { 80, 150, 40 }), "frame durations kept exactly");
            Check(e.Layers.Select(l => l.Name).SequenceEqual(new[] { "Back", "Sprite ünï", "Hidden" }), "layer names and order kept (bottom first)");
            Check(e.Layers[1].Opacity == 170 && e.Layers[1].Blend == BlendMode.Multiply && !e.Layers[2].Visible, "layer opacity, blend mode and visibility kept");
            Check(e.Layers[0].Shared && e.Layers[0].Cels.Count == 1, "a shared layer goes out as linked cels and comes back shared");
            Check(e.Layers[1].CelAt(1) == null, "an empty cel stays empty");
            Check(e.Tags.Count == 2 && e.Tags[0].Name == "Idle" && e.Tags[0].Start == 0 && e.Tags[0].End == 1 && e.Tags[0].Colour.G == 200 && e.Tags[1].Name == "Hit",
                  "tags kept: names, ranges, colours");
            Check(Enumerable.Range(0, 3).All(f => Same(d.RenderPixels(f), e.RenderPixels(f))), "every frame composites to the same pixels, soft alpha included");
            var e2 = AsepriteFile.Read(path);
            string path2 = P("again.aseprite");
            AsepriteFile.Write(e2.Doc, path2);
            Check(File.ReadAllBytes(path2).SequenceEqual(bytes), "writing what was read gives the same file, byte for byte");
            AsepriteFile.Write(d, P("range.aseprite"), 1, 2);
            var r = AsepriteFile.Read(P("range.aseprite")).Doc;
            Check(r.FrameCount == 2 && r.Frames[0].Delay == 150 && r.Tags.Count == 2 && r.Tags[0].Start == 0 && r.Tags[0].End == 0 && r.Tags[1].Start == 1,
                  "exporting a frame range keeps the tags that overlap it, clipped");
        }
        {
            // A file built by hand from the spec: indexed colour, a raw cel, a linked cel, a group, old palette chunk.
            string path = P("handmade.ase");
            File.WriteAllBytes(path, Handmade());
            var imp = AsepriteFile.Read(path);
            var e = imp.Doc;
            Check(e.Width == 4 && e.Height == 3 && e.FrameCount == 2 && e.LayerCount == 1 && e.Layers[0].Name == "Ink", $"hand-made indexed file: {imp.Summary}");
            var f0 = e.RenderPixels(0);
            Check(f0[3] == 0 && f0[4 * 1 + 2] == 255 && f0[4 * 1 + 1] == 0 && f0[4 * 1 + 3] == 255 && f0[4 * 2 + 1] == 255 && f0[4 * 2 + 2] == 0,
                  "indexed pixels use the palette; the transparent index is clear");
            Check(Same(e.RenderPixels(1), f0) && e.Frames[1].Delay == 250, "a linked cel repeats the earlier picture");
            Check(imp.Warnings.Any(w => w.Contains("group")), "a layer group becomes a plain layer, with a note");
            File.WriteAllBytes(P("bad.ase"), new byte[50]);
            Check(Throws(() => AsepriteFile.Read(P("bad.ase"))) is InvalidDataException, "a damaged file is refused with a message");
            var trunc = File.ReadAllBytes(path)[..150];
            File.WriteAllBytes(P("trunc.ase"), trunc);
            Check(Throws(() => AsepriteFile.Read(P("trunc.ase"))) is InvalidDataException or EndOfStreamException, "a cut-off file is refused, not crashed on");
            var full = File.ReadAllBytes(path);
            // Cut inside the raw cel's pixels (the last 6 bytes of frame 1's cel), keeping the frame header's claim.
            int celEnd = Array.LastIndexOf(full, (byte)2) + 1;
            File.WriteAllBytes(P("rawcut.ase"), full[..(celEnd - 6)]);
            var ex = Throws(() => AsepriteFile.Read(P("rawcut.ase")));
            Check(ex is InvalidDataException or EndOfStreamException, $"a file cut off inside a cel's pixels is refused with a message ({ex?.GetType().Name})");
        }
        {
            // Open in the editor (File > Open) and export through the normal job.
            var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000), SuppressPrompts = true };
            form.Show();
            Application.DoEvents();
            Check(form.OpenAny(P("sample.aseprite")), "File > Open accepts an .aseprite file");
            form.Close();
            var d = Sample();
            string path = P("job.aseprite");
            Exporter.Run(new ExportJob { Doc = d, Kind = ExportKind.Aseprite, Path = path, From = 0, To = 2 }, null, default);
            Check(AsepriteFile.Read(path).Doc.FrameCount == 3, "Export > Aseprite writes the file");
        }

        Console.WriteLine("HTML player page");
        {
            var d = Sample();
            string path = P("player.html");
            string msg = Exporter.Run(new ExportJob { Doc = d, Kind = ExportKind.Html, Path = path, From = 0, To = 2, Loop = true, Matte = Color.FromArgb(17, 34, 51) }, null, default);
            string html = File.ReadAllText(path);
            var m = Regex.Match(html, "data:image/png;base64,([A-Za-z0-9+/=]+)");
            var delays = Regex.Match(html, @"const DELAYS = \[([0-9,]+)\]");
            Check(m.Success && delays.Success && delays.Groups[1].Value == "80,150,40" && html.Contains("LOOP = true") && html.Contains("#112233"),
                  $"one self-contained page with exact delays, loop and background ({msg})");
            Check(!Regex.IsMatch(html, "(src|href)=\"(http|//)"), "no outside files or links needed");
            using var sheet = new Bitmap(new MemoryStream(Convert.FromBase64String(m.Groups[1].Value)));
            var sp = ImageOps.GetPixels(sheet);
            int cols = HtmlExport.Columns(3);
            bool exact = Enumerable.Range(0, 3).All(f =>
            {
                var want = d.RenderPixels(f);
                var got = new byte[W * H * 4];
                for (int y = 0; y < H; y++) Array.Copy(sp, ((f / cols * H + y) * sheet.Width + f % cols * W) * 4, got, y * W * 4, W * 4);
                return Same(want, got);
            });
            Check(sheet.Width == W * cols && exact, $"the embedded sheet holds every frame exactly ({cols} columns)");
            Exporter.Run(new ExportJob { Doc = d, Kind = ExportKind.Html, Path = path, From = 0, To = 2, Loop = false }, null, default);
            Check(File.ReadAllText(path).Contains("LOOP = false"), "play once is kept");
        }

        Console.WriteLine("Video import");
        var ff = WebmExport.Find();
        if (ff == null) Console.WriteLine("  skip  ffmpeg not installed");
        else
        {
            // A lossless FFV1 video made from known frames, so pixels can be compared exactly.
            string frames = P("vframes");
            Directory.CreateDirectory(frames);
            var pics = Enumerable.Range(0, 10).Select(i => Pic(i % 6, false)).ToList();
            for (int i = 0; i < pics.Count; i++)
                using (var b = ImageOps.FromPixels(W, H, pics[i])) b.Save(Path.Combine(frames, $"f{i:D2}.png"), System.Drawing.Imaging.ImageFormat.Png);
            string video = P("clip.mkv");
            if (File.Exists(video)) File.Delete(video);
            RunTool(ff, $"-v error -y -framerate 10 -i \"{Path.Combine(frames, "f%02d.png")}\" -c:v ffv1 -pix_fmt bgr0 \"{video}\"");
            var info = VideoImport.Probe(video);
            Check(info.Width == W && info.Height == H && Math.Abs(info.Fps - 10) < 0.01 && Math.Abs(info.Seconds - 1) < 0.05, $"probe: {info.Width}x{info.Height}, {info.Fps} fps, {info.Seconds:0.##} s, {info.Codec}");
            using (var all = VideoImport.Load(video, 10, 0, 0))
                Check(all.Frames.Count == 10 && all.Frames.All(f => f.delay == 100) && all.Frames.Select((f, i) => Same(ImageOps.GetPixels(f.bmp), pics[i])).All(x => x),
                      $"all 10 frames, 100 ms each, pixel-exact ({all.Frames.Count})");
            using (var part = VideoImport.Load(video, 10, 0.3, 0.4))
                Check(part.Frames.Count == 4 && Same(ImageOps.GetPixels(part.Frames[0].bmp), pics[3]), $"a range: 0.3 s for 0.4 s gives frames 4-7 ({part.Frames.Count})");
            using (var slow = VideoImport.Load(video, 5, 0, 0))
                Check(slow.Frames.Count == 5 && slow.Frames.All(f => f.delay == 200), $"5 frames a second: every other frame, 200 ms each ({slow.Frames.Count})");
            // Flint's own WebM keeps transparency through import.
            var d = Sample();
            string webm = P("alpha.webm");
            Exporter.Run(new ExportJob { Doc = d, Kind = ExportKind.WebM, Path = webm, From = 0, To = 2, Quality = 10, IncludeAudio = false }, null, default);
            var wi = VideoImport.Probe(webm);
            using (var a = VideoImport.Load(webm, 1000.0 / 10, 0, 0))
            {
                var px = ImageOps.GetPixels(a.Frames[0].bmp);
                int clear = 0; for (int i = 3; i < px.Length; i += 4) if (px[i] < 20) clear++;
                Check(wi.Alpha && clear == 0, $"VP9 WebM alpha is detected ({wi.Alpha}); the shared background layer keeps frame 1 solid");
            }
            var src = d; src.Layers[0].Visible = false;
            Exporter.Run(new ExportJob { Doc = src, Kind = ExportKind.WebM, Path = webm, From = 0, To = 2, Quality = 10, IncludeAudio = false }, null, default);
            using (var a = VideoImport.Load(webm, 10, 0, 0))
            {
                var px = ImageOps.GetPixels(a.Frames[0].bmp);
                int clear = 0; for (int i = 3; i < px.Length; i += 4) if (px[i] < 20) clear++;
                Check(clear > 20, $"VP9 WebM transparency comes through import ({clear} clear pixels in frame 1)");
            }
            Check(Throws(() => VideoImport.Load(video, 120, 0, 100_000_000)) is InvalidOperationException, "an import that would need too much memory is refused before reading");
            // A phone video stored sideways with a quarter-turn flag comes in upright, not scrambled.
            string rotated = P("rotated.mp4");
            if (File.Exists(rotated)) File.Delete(rotated);
            RunTool(ff, $"-v error -y -display_rotation 90 -i \"{video}\" -c:v libx264 -crf 0 -pix_fmt yuv444p \"{rotated}\"");
            var ri = VideoImport.Probe(rotated);
            using (var rv = VideoImport.Load(rotated, 10, 0, 0, known: ri))
            {
                var px = ImageOps.GetPixels(rv.Frames[0].bmp);
                // Upright = the source turned a quarter: source pixel (x, y) lands somewhere in a H x W picture.
                var src0 = pics[0];
                bool match = false;
                foreach (var fx in new Func<int, int, (int, int)>[] { (x, y) => (y, W - 1 - x), (x, y) => (H - 1 - y, x) })
                {
                    int bad = 0;
                    for (int y = 0; y < H && bad < 5; y++)
                        for (int x = 0; x < W && bad < 5; x++)
                        {
                            var (nx, ny) = fx(x, y);
                            int o = (ny * H + nx) * 4, s = (y * W + x) * 4;
                            if (Math.Abs(px[o] - src0[s]) + Math.Abs(px[o + 1] - src0[s + 1]) + Math.Abs(px[o + 2] - src0[s + 2]) > 30) bad++;
                        }
                    if (bad < 5) match = true;
                }
                Check(ri.Width == H && ri.Height == W && rv.Frames[0].bmp.Width == H && match, $"a video marked as turned 90 degrees imports upright at {ri.Width}x{ri.Height}");
            }
            // Flint's own ProRes 4444 keeps transparency through import.
            var see = Sample(); see.Layers[0].Visible = false;
            string mov = P("alpha.mov");
            Exporter.Run(new ExportJob { Doc = see, Kind = ExportKind.MovProRes, Path = mov, From = 0, To = 2, IncludeAudio = false }, null, default);
            var mi = VideoImport.Probe(mov);
            using (var a = VideoImport.Load(mov, 10, 0, 0, known: mi))
            {
                var px = ImageOps.GetPixels(a.Frames[0].bmp);
                var want = see.RenderPixels(0);
                int wrong = 0;
                for (int i = 3; i < px.Length; i += 4) if ((px[i] < 128) != (want[i] < 128)) wrong++;
                Check(mi.Alpha && wrong == 0, $"ProRes 4444 transparency comes through import ({wrong} pixels disagree)");
            }
            File.WriteAllBytes(P("broken.mp4"), File.ReadAllBytes(rotated)[..2000]);
            Check(Throws(() => VideoImport.Load(P("broken.mp4"), 10, 0, 0)) != null, "a broken video is refused rather than half-read in silence");
            var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000), SuppressPrompts = true };
            form.Show(); Application.DoEvents();
            form.TestUseDocument(new Document(W, H));
            form.TestImport(new[] { video });
            Check(form.TestDocument.FrameCount >= 10, $"File > Import of a video adds its frames ({form.TestDocument.FrameCount} frames)");
            form.Close();
        }

        Console.WriteLine(_fails == 0 ? "all interchange checks passed" : $"{_fails} interchange check(s) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    private static void RunTool(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEndAsync(); p.StandardError.ReadToEndAsync();
        p.WaitForExit(60_000);
    }

    private static Exception? Throws(Action a) { try { a(); } catch (Exception e) { return e; } return null; }

    /// <summary>
    /// 4x3 indexed file, 2 frames: frame 1 has a group layer, the "Ink" layer,
    /// an old-style (0x0004) palette and a raw cel; frame 2 a linked cel.
    /// </summary>
    private static byte[] Handmade()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(0u); w.Write((ushort)0xA5E0); w.Write((ushort)2); w.Write((ushort)4); w.Write((ushort)3); w.Write((ushort)8);
        w.Write(1u); w.Write((ushort)100); w.Write(0u); w.Write(0u);
        w.Write((byte)0); w.Write(new byte[3]); w.Write((ushort)4); w.Write((byte)1); w.Write((byte)1);
        w.Write((short)0); w.Write((short)0); w.Write((ushort)16); w.Write((ushort)16); w.Write(new byte[84]);
        byte[] Chunk(int type, Action<BinaryWriter> body)
        {
            var c = new MemoryStream(); var b = new BinaryWriter(c);
            b.Write(0u); b.Write((ushort)type); body(b); b.Flush();
            var a = c.ToArray(); BitConverter.GetBytes((uint)a.Length).CopyTo(a, 0); return a;
        }
        void Name(BinaryWriter b, string s) { var t = Encoding.UTF8.GetBytes(s); b.Write((ushort)t.Length); b.Write(t); }
        void Layer(BinaryWriter b, int type, int level, string name)
        {
            b.Write((ushort)3); b.Write((ushort)type); b.Write((ushort)level); b.Write((ushort)0); b.Write((ushort)0);
            b.Write((ushort)0); b.Write((byte)255); b.Write(new byte[3]); Name(b, name);
        }
        void Frame(int dur, params byte[][] chunks)
        {
            w.Write((uint)(16 + chunks.Sum(c => c.Length))); w.Write((ushort)0xF1FA); w.Write((ushort)chunks.Length);
            w.Write((ushort)dur); w.Write(new byte[2]); w.Write(0u);
            foreach (var c in chunks) w.Write(c);
        }
        var pal = Chunk(0x0004, b =>
        {
            b.Write((ushort)1); b.Write((byte)0); b.Write((byte)3);
            b.Write(new byte[] { 0, 0, 0, 255, 0, 0, 0, 255, 0 });
        });
        var group = Chunk(0x2004, b => Layer(b, 1, 0, "Group"));
        var ink = Chunk(0x2004, b => Layer(b, 0, 1, "Ink"));
        var cel = Chunk(0x2005, b =>
        {
            b.Write((ushort)1); b.Write((short)0); b.Write((short)0); b.Write((byte)255); b.Write((ushort)0); b.Write((short)0); b.Write(new byte[5]);
            b.Write((ushort)4); b.Write((ushort)3);
            b.Write(new byte[] { 0, 1, 2, 1, 2, 1, 0, 0, 1, 1, 2, 2 });
        });
        var link = Chunk(0x2005, b =>
        {
            b.Write((ushort)1); b.Write((short)0); b.Write((short)0); b.Write((byte)255); b.Write((ushort)1); b.Write((short)0); b.Write(new byte[5]);
            b.Write((ushort)0);
        });
        Frame(120, pal, group, ink, cel);
        Frame(250, link);
        w.Flush();
        var all = ms.ToArray();
        BitConverter.GetBytes((uint)all.Length).CopyTo(all, 0);
        return all;
    }
}
