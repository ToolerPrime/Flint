/* Trace bitmap: exact round trip, options, SVG, vector objects, the file, the form.
 * Flint.exe --tracetest <dir> */
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml;

namespace Flint;

public static class TraceTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private static void Put(byte[] px, int w, int x, int y, byte r, byte g, byte b, byte a)
    {
        int o = (y * w + x) * 4;
        px[o] = b; px[o + 1] = g; px[o + 2] = r; px[o + 3] = a;
    }

    /// <summary>Ring, staircase, a checkerboard patch (corner-touching pixels), partial alpha, several colours.</summary>
    private static byte[] Sprite(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double d = Math.Sqrt((x - 12) * (x - 12) + (y - 12) * (y - 12));
                if (d <= 10 && d >= 5) Put(px, w, x, y, 200, 40, 40, 255);                 // ring: a hole
                else if (d < 5 && d > 3) Put(px, w, x, y, 40, 40, 200, 255);               // island inside the hole
                if (x >= 26 && y >= 4 && x - 26 <= y - 4 && y < 20) Put(px, w, x, y, 30, 180, 60, 255);   // diagonal staircase
                if (x >= 26 && y >= 22 && y < 28 && x < 36 && (x + y) % 2 == 0) Put(px, w, x, y, 220, 220, 20, 255);   // checkerboard
                if (y >= 28 && x < 20) Put(px, w, x, y, (byte)(x * 12), 90, 200, (byte)(x % 4 == 0 ? 77 : x % 3 == 0 ? 128 : 255));   // partial alpha
            }
        return px;
    }

    private static byte[] Noise(int w, int h, int seed)
    {
        var px = new byte[w * h * 4];
        uint s = (uint)seed;
        uint Next() { s = s * 1664525 + 1013904223; return s >> 16; }
        for (int i = 0; i < w * h; i++)
        {
            uint r = Next() % 7;
            if (r == 0) continue;   // transparent
            Put(px, w, i % w, i / w, (byte)(r * 30), (byte)(255 - r * 20), (byte)(r * 7), r == 6 ? (byte)128 : (byte)255);
        }
        return px;
    }

    private static bool Throws(Action a)
    {
        try { a(); return false; } catch (InvalidOperationException) { return true; }
    }

    private static byte[] Circle(int w, int h, int r)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if ((x - w / 2) * (x - w / 2) + (y - h / 2) * (y - h / 2) <= r * r) Put(px, w, x, y, 20, 120, 220, 255);
        return px;
    }

    private static bool Exact(byte[] src, int w, int h, TraceOptions o, out VectorArt art)
    {
        art = Tracer.Trace(src, w, h, o);
        var r = Tracer.Render(art, Affine.Identity, false);
        return r.x == 0 && r.y == 0 && r.w == w && r.h == h && r.px.SequenceEqual(Clean(src));
    }

    /// <summary>The source with alpha-0 pixels zeroed, which is what a render returns.</summary>
    private static byte[] Clean(byte[] src)
    {
        var c = (byte[])src.Clone();
        for (int i = 0; i < c.Length; i += 4) if (c[i + 3] == 0) c[i] = c[i + 1] = c[i + 2] = 0;
        return c;
    }

    private static RasterObject VectorObject(VectorArt art, Resample mode = Resample.Nearest)
    {
        var r = Tracer.Render(art, Affine.Identity, false);
        return new RasterObject(ImageOps.FromPixels(r.w, r.h, r.px)) { Vector = art, Matrix = Affine.Identity, Mode = mode, X = r.x, Y = r.y };
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        const int W = 40, H = 34;
        var sprite = Sprite(W, H);

        Console.WriteLine("Exact round trip");
        {
            Check(Exact(sprite, W, H, new TraceOptions(), out var art), "sprite with ring, island, staircase, checkerboard and partial alpha comes back byte for byte");
            Check(art.Shapes.Count > 5, $"it has several colours ({art.Shapes.Count} shapes)");
            bool allNoise = true;
            for (int seed = 1; seed <= 6; seed++) allNoise &= Exact(Noise(37, 29, seed), 37, 29, new TraceOptions(), out _);
            Check(allNoise, "six random noise images (many corner-touching pixels) come back exactly");
            Check(Exact(new byte[4 * 4 * 4], 4, 4, new TraceOptions(), out var empty) && empty.Shapes.Count == 0, "an empty image traces to nothing");
            var one = new byte[4]; Put(one, 1, 0, 0, 1, 2, 3, 1);
            Check(Exact(one, 1, 1, new TraceOptions(), out _), "a single faint pixel survives");
            Check(Tracer.Render(art, Affine.Identity, true).px.SequenceEqual(Clean(sprite)), "antialiased render at the identity is exact too");
            var moved = Tracer.Render(art, Affine.Translate(7, -3), false);
            Check(moved.x == 7 && moved.y == -3 && moved.px.SequenceEqual(Clean(sprite)), "a whole-pixel move lands at the new position, exactly");
        }

        Console.WriteLine("SVG");
        {
            var art = Tracer.Trace(sprite, W, H, new TraceOptions());
            var xml = new XmlDocument();
            string svg = Tracer.ToSvg(art);
            xml.LoadXml(svg);
            Check(xml.GetElementsByTagName("path").Count == art.Shapes.Count, "one path per shape");
            Check(xml.DocumentElement!.GetAttribute("viewBox") == $"0 0 {W} {H}" && svg.Contains("fill-rule=\"evenodd\"") && svg.Contains("fill-opacity"),
                  "viewBox, even-odd and fill-opacity for partial alpha are there");
        }

        Console.WriteLine("Options");
        {
            var grad = new byte[32 * 32 * 4];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) Put(grad, 32, x, y, (byte)(x * 8), (byte)(y * 8), (byte)((x + y) * 4), 255);
            var q = Tracer.Trace(grad, 32, 32, new TraceOptions { Colours = 4 });
            Check(q.Shapes.Count > 0 && q.Shapes.Count <= 4, $"Colours = 4 gives at most 4 shapes ({q.Shapes.Count})");

            var block = new byte[16 * 16 * 4];
            for (int y = 2; y < 14; y++) for (int x = 2; x < 14; x++) Put(block, 16, x, y, 200, 0, 0, 255);
            Put(block, 16, 7, 7, 0, 200, 0, 255);      // speck inside the block
            Put(block, 16, 0, 15, 0, 0, 200, 255);     // speck alone on transparency
            var exact = Tracer.Trace(block, 16, 16, new TraceOptions());
            var merged = Tracer.Trace(block, 16, 16, new TraceOptions { MinArea = 3 });
            var r = Tracer.Render(merged, Affine.Identity, false).px;
            Check(exact.Shapes.Count == 3 && merged.Shapes.Count == 1, $"MinArea removes 1-pixel specks ({exact.Shapes.Count} shapes become {merged.Shapes.Count})");
            Check(BitConverter.ToUInt32(r, (7 * 16 + 7) * 4) == 0xFFC80000u && r[((15 * 16) + 0) * 4 + 3] == 0, "the inner speck takes the block's colour; the lone speck vanishes");
        }

        Console.WriteLine("Smoothing");
        {
            var disc = Circle(32, 32, 12);
            var a0 = Tracer.Trace(disc, 32, 32, new TraceOptions());
            var a1 = Tracer.Trace(disc, 32, 32, new TraceOptions { Smoothing = 40 });
            Check(a1.PointCount < a0.PointCount, $"smoothing a circle uses fewer points ({a1.PointCount} against {a0.PointCount})");
            var big = Tracer.Render(a1, Affine.Scale(8, 8), true);
            var blocky = Transform.Render(disc, 32, 32, Affine.Scale(8, 8), Resample.Nearest);
            Check(big.w == blocky.w && big.h == blocky.h && !big.px.SequenceEqual(blocky.px), "8× render is not the blocky nearest-neighbour upscale");
            int filled = 0;
            for (int i = 3; i < big.px.Length; i += 4) if (big.px[i] > 0) filled++;
            double ideal = Math.PI * 12 * 12 * 64;
            Check(Math.Abs(filled - ideal) / ideal < 0.08, $"and it still covers about a circle's area ({filled} against {ideal:F0})");
            var rect = new byte[10 * 10 * 4];
            for (int y = 2; y < 8; y++) for (int x = 2; x < 8; x++) Put(rect, 10, x, y, 9, 9, 9, 255);
            Check(Tracer.Trace(rect, 10, 10, new TraceOptions { Smoothing = 10 }).PointCount == 4, "a rectangle keeps its four sharp corners at low smoothing");
        }

        Console.WriteLine("Vector objects");
        {
            var art = Tracer.Trace(sprite, W, H, new TraceOptions());
            var start = VectorObject(art);
            var startPx = ImageOps.GetPixels(start.Pixels);
            Check(start.IsTransformed, "a vector object counts as transformed, so paint tools bake it first");
            var big = start.Retransform(Affine.Scale(4, 4), Resample.Nearest);
            Check(big.Width == W * 4 && big.Vector == art, "scale 4 renders from the vector");
            var back = big.Retransform(Affine.Scale(0.25, 0.25), Resample.Nearest);
            Check(ImageOps.GetPixels(back.Pixels).SequenceEqual(startPx) && back.X == 0 && back.Y == 0, "then 0.25 returns exactly the start pixels");
            var rot = start.Retransform(Affine.Rotate(30).About(W / 2.0, H / 2.0), Resample.Nearest);
            var unrot = rot.Retransform(Affine.Rotate(-30).About(W / 2.0, H / 2.0), Resample.Nearest);
            Check(rot.Width != W && ImageOps.GetPixels(unrot.Pixels).SequenceEqual(startPx) && unrot.X == 0 && unrot.Y == 0, "rotate 30 then -30 returns the exact start");
            var sm = start.Retransform(Affine.Scale(3, 3), Resample.Smooth).Retransform(Affine.Scale(1 / 3.0, 1 / 3.0), Resample.Smooth);
            Check(sm.Width == W && sm.Vector == art, "smooth mode re-renders from the vector too");
            var moved = start.Moved(5, 6);
            Check(moved.X == 5 && moved.Y == 6 && moved.Vector == art && ReferenceEquals(moved.Pixels, start.Pixels), "a whole-pixel move reuses the pixels and keeps the vector");
            Check(((RasterObject)start.CloneObject()).Vector == art, "cloning keeps the vector");
            Check(start.With(start.Pixels).Vector == null, "With() gives plain pixels");
            var smoothArt = Tracer.Trace(sprite, W, H, new TraceOptions { Smoothing = 60 });
            var soft = VectorObject(smoothArt, Resample.Smooth).Retransform(Affine.Translate(0.5, 0.5), Resample.Smooth);
            var viaTool = soft.Retransform(Affine.Rotate(10).About(W / 2.0, H / 2.0), Resample.RotSprite);
            var tp = ImageOps.GetPixels(viaTool.Pixels);
            bool partial = false;
            for (int i = 3; i < tp.Length; i += 4) if (tp[i] is > 0 and < 255) { partial = true; break; }
            Check(viaTool.Mode == Resample.Smooth && partial,
                  "a smooth-edged vector stays smooth when the transform tool's resample setting is RotSprite");
        }

        Console.WriteLine("Project file");
        {
            var art = Tracer.Trace(sprite, W, H, new TraceOptions { Smoothing = 30 });
            var d = new Document(64, 64);
            var obj = VectorObject(art, Resample.Smooth).Retransform(Affine.Rotate(15).About(20, 17), Resample.Smooth);
            d.Layers[0].EnsureCel(0).Objects.Add(obj);
            string path = Path.Combine(dir, "trace.flint");
            FlintFormat.Save(d, path);
            var load = FlintFormat.Load(path);
            var o2 = (RasterObject)load.Document!.Layers[0].CelAt(0)!.Objects[0];
            var a2 = o2.Vector;
            Check(FlintFormat.CurrentVersion >= 6 && a2 != null && a2.Shapes.Count == art.Shapes.Count && a2.PointCount == art.PointCount
                  && a2.Shapes.Select(s => s.Colour).SequenceEqual(art.Shapes.Select(s => s.Colour)), "version 5 file keeps the vector: shapes, colours, points");
            Check(o2.Matrix == obj.Matrix && o2.Mode == Resample.Smooth && o2.Original == null, "with its matrix and mode");
            Check(ImageOps.GetPixels(o2.Pixels).SequenceEqual(ImageOps.GetPixels(obj.Pixels))
                  && ImageOps.GetPixels(o2.Retransform(Affine.Translate(0.5, 0), Resample.Smooth).Pixels)
                     .SequenceEqual(ImageOps.GetPixels(obj.Retransform(Affine.Translate(0.5, 0), Resample.Smooth).Pixels)), "a render after reloading is identical");

            string fmt(double v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            var svgDoc = new XmlDocument();
            string svgText = Tracer.ToSvg(o2.Vector!, o2.Matrix, 64, 64, 128);
            svgDoc.LoadXml(svgText);
            Check(svgText.Contains($"matrix({fmt(o2.Matrix.A)} {fmt(o2.Matrix.B)} {fmt(o2.Matrix.C)} {fmt(o2.Matrix.D)} {fmt(o2.Matrix.Tx)} {fmt(o2.Matrix.Ty)})")
                  && svgDoc.DocumentElement!.GetAttribute("viewBox") == "0 0 64 64" && svgText.Contains("opacity=\"0.50"), "SVG of a rotated, reloaded object parses, has the matrix, the canvas viewBox and the opacity");

            // An older file: take a transformed object saved by the current code and stamp it version 4.
            var d4 = new Document(32, 32);
            d4.Layers[0].EnsureCel(0).Objects.Add(new RasterObject(ImageOps.FromPixels(8, 8, Noise(8, 8, 3))).Retransform(Affine.Rotate(30).About(4, 4), Resample.Smooth));
            string p4 = Path.Combine(dir, "v4.flint");
            FlintFormat.Save(d4, p4);
            using (var zip = ZipFile.Open(p4, ZipArchiveMode.Update))
            {
                var e = zip.GetEntry("project.json")!;
                JsonNode root;
                using (var rs = new StreamReader(e.Open())) root = JsonNode.Parse(rs.ReadToEnd())!;
                root["format"] = 4;
                e.Delete();
                using var ws = new StreamWriter(zip.CreateEntry("project.json").Open());
                ws.Write(root.ToJsonString());
            }
            var l4 = FlintFormat.Load(p4);
            var o4 = l4.Document?.Layers[0].CelAt(0)?.Objects.FirstOrDefault() as RasterObject;
            Check(l4.Error == null && o4 is { Original: not null, Vector: null }, $"a version 4 file still loads ({l4.Error})");
        }

        Console.WriteLine("Through the form");
        {
            var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000), SuppressPrompts = true };
            form.Show();
            Application.DoEvents();
            var red = Color.FromArgb(255, 220, 40, 40);
            form.TestNewCanvas(40, 30);
            form.TestStroke(Tool.Pencil, red, 3, new Point(8, 10), new Point(25, 10), new Point(25, 18));
            var orig = form.TestLayerPixels();
            int steps = form.TestTrace(new TraceOptions(), false);
            var cel = form.TestActiveCel!;
            Check(steps == 1 && cel.Objects.Count == 1 && cel.Objects[0] is RasterObject { Vector: not null }, "tracing a drawn shape leaves one vector object in the cel");
            Check(form.TestLayerPixels().SequenceEqual(orig), "and it looks exactly the same");
            form.TestUndo();
            Check(form.TestLayerPixels().SequenceEqual(orig) && form.TestActiveCel!.Objects.All(o => o is RasterObject { Vector: null }), "undo restores the original pixels exactly");
            form.TestRedo();
            Check(form.TestActiveCel!.Objects[0] is RasterObject { Vector: not null }, "redo traces again");
            form.TestUndo();

            form.TestSelect(new Rectangle(0, 0, 18, 30));
            Check(form.TestTrace(new TraceOptions(), true) == 1 && form.TestSelection == null, "tracing inside a selection works and drops the selection");
            var c2 = form.TestActiveCel!;
            var bp = ImageOps.GetPixels(((RasterObject)c2.Objects[0]).Pixels);
            bool baseOk = true;
            for (int y = 0; y < 30; y++)
                for (int x = 0; x < 40; x++)
                    baseOk &= x < 18 ? bp[(y * 40 + x) * 4 + 3] == 0 : BitConverter.ToUInt32(bp, (y * 40 + x) * 4) == BitConverter.ToUInt32(orig, (y * 40 + x) * 4);
            Check(c2.Objects.Count == 2 && c2.Objects[1] is RasterObject { Vector: not null } && baseOk, "the selected part becomes a vector object above the rest, which stays plain pixels");
            form.TestUndo();

            form.TestTrace(new TraceOptions(), false);
            form.TestStroke(Tool.Pencil, Color.FromArgb(255, 0, 200, 0), 1, new Point(2, 2));
            var after = form.TestActiveCel!;
            Check(after.Objects.Count == 1 && after.Objects[0] is RasterObject { Vector: null, Width: 40, Height: 30 }, "painting on it bakes it into one plain bitmap");
            // The dialog's logic without the window: preview then Cancel, preview then Apply.
            form.TestNewCanvas(40, 30);
            form.TestStroke(Tool.Pencil, red, 3, new Point(8, 10), new Point(25, 10), new Point(25, 18));
            var start = form.TestLayerPixels();
            int done0 = form.TestUndoDone;
            Check(form.TestTraceBegin(), "the trace dialog opens on a layer with pixels");
            form.TestTracePreview(new TraceOptions(), false);
            form.TestTracePreview(new TraceOptions { Smoothing = 50 }, true);
            form.TestTracePreview(new TraceOptions(), false);
            form.TestTraceEnd(false, new TraceOptions(), false);
            Check(form.TestLayerPixels().SequenceEqual(start) && form.TestUndoDone == done0 && form.TestActiveCel!.Objects.All(o => o is RasterObject { Vector: null }),
                  "preview a few times then Cancel: pixels and undo history unchanged");
            form.TestTraceBegin();
            form.TestTracePreview(new TraceOptions { Smoothing = 30 }, true);
            form.TestTracePreview(new TraceOptions(), false);
            Check(form.TestTraceEnd(true, new TraceOptions(), false) > 0 && form.TestUndoDone == done0 + 1 && form.TestActiveCel!.Objects.Count == 1
                  && form.TestActiveCel.Objects[0] is RasterObject { Vector: not null } && form.TestLayerPixels().SequenceEqual(start), "preview then Apply: exactly one new undo step, same picture");
            form.TestUndo();

            var hidden = new RasterObject(ImageOps.NewBitmap(4, 4)) { Name = "hidden", Visible = false };
            form.TestActiveCel!.Objects.Add(hidden);
            form.TestTrace(new TraceOptions(), false);
            var objs = form.TestActiveCel!.Objects;
            Check(objs.Contains(hidden) && objs.Count == 2 && objs.OfType<RasterObject>().Count(o => o.Vector != null) == 1, "tracing keeps hidden objects in the cel");
            form.TestUndo();
            Check(form.TestActiveCel!.Objects.Contains(hidden), "and undo brings back everything");
            form.Close();
        }

        Console.WriteLine("Rendering details");
        {
            var two = new byte[20 * 16 * 4];
            for (int y = 0; y < 16; y++) for (int x = 0; x < 20; x++) Put(two, 20, x, y, (byte)(x < 10 ? 220 : 20), 40, (byte)(x < 10 ? 20 : 220), 255);
            var art2 = Tracer.Trace(two, 20, 16, new TraceOptions());
            var m = Affine.Rotate(30).About(10, 8);
            var rr = Tracer.Render(art2, m, true);
            var inv = m.Inverse();
            bool solid = true; int inside = 0;
            for (int y = 1; y < rr.h - 1; y++)
                for (int x = 1; x < rr.w - 1; x++)
                {
                    bool all = true;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            var (u, v) = inv.Apply(rr.x + x + dx + 0.5, rr.y + y + dy + 0.5);
                            all &= u > 0 && u < 20 && v > 0 && v < 16;
                        }
                    if (!all) continue;
                    inside++;
                    solid &= rr.px[(y * rr.w + x) * 4 + 3] == 255;
                }
            Check(solid && inside > 100, $"rotated opaque two-colour art has no see-through seams ({inside} interior pixels all alpha 255)");

            var disc = Circle(32, 32, 12);
            var smoothArt = Tracer.Trace(disc, 32, 32, new TraceOptions { Smoothing = 60 });
            var tr = Tracer.Render(smoothArt, Affine.Translate(3, 2), true);
            Check(tr.px.Where((_, i) => i % 4 == 3).Any(a => a > 0 && a < 255), "smooth edges give partial-alpha edge pixels even at a whole-pixel move");
            var plain = Tracer.Render(smoothArt, Affine.Translate(3, 2), false);
            Check(!plain.px.Where((_, i) => i % 4 == 3).Any(a => a > 0 && a < 255), "and none when not antialiased");

            var art0 = Tracer.Trace(disc, 32, 32, new TraceOptions());
            Check(Tracer.Trace(disc, 32, 32, new TraceOptions { Simplify = 0.7 }).PointCount < art0.PointCount, "Simplify alone (no smoothing) removes points");

            var obj = VectorObject(art0);
            Check(Throws(() => Tracer.Render(art0, Affine.Scale(0, 1), false)) && Throws(() => obj.Retransform(Affine.Scale(1, 0), Resample.Nearest)), "a squashed matrix throws, for the renderer and for a vector object");
            Check(Throws(() => obj.Retransform(Affine.Scale(1e9, 1e9), Resample.Nearest)) && Throws(() => Tracer.Render(art0, Affine.Scale(1e9, 1e9), true)), "a huge scale throws for a vector object");
            Check(Throws(() => Transform.Render(disc, 32, 32, Affine.Scale(1e9, 1e9), Resample.Nearest)), "and for Transform.Render");

            var sw = Stopwatch.StartNew();
            var big = new byte[512 * 512 * 4];
            for (int i = 0; i < 512 * 512; i++)
            {
                int k = (int)((uint)(i * 2654435761u) >> 8) % 2000;
                Put(big, 512, i % 512, i / 512, (byte)(k % 50 * 5), (byte)(k / 50 * 6), (byte)(k * 7), 255);
            }
            var many = Tracer.Trace(big, 512, 512, new TraceOptions { Colours = 16 });
            var many2 = Tracer.Render(many, Affine.Translate(0, 0), true);
            long ms = sw.ElapsedMilliseconds;
            Console.WriteLine($"  info 512x512, ~2000 scattered colours, Colours=16: trace + render {ms} ms ({many.Shapes.Count} shapes, {many.PointCount} points)");
            Check(ms < 3000 && many2.w == 512, "that trace + render takes under 3 seconds");
        }

        Console.WriteLine("Speed");
        {
            var img = new byte[256 * 256 * 4];
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    double v = Math.Sin(x / 17.0) + Math.Cos(y / 23.0) + Math.Sin((x + y) / 31.0);
                    Put(img, 256, x, y, (byte)(128 + v * 40), (byte)(128 + Math.Sin(x / 9.0) * 90), (byte)(y), 255);
                }
            var sw = Stopwatch.StartNew();
            var exact = Tracer.Trace(img, 256, 256, new TraceOptions());
            long t1 = sw.ElapsedMilliseconds; sw.Restart();
            var reduced = Tracer.Trace(img, 256, 256, new TraceOptions { Colours = 16, MinArea = 4, Smoothing = 50 });
            long t2 = sw.ElapsedMilliseconds; sw.Restart();
            Tracer.Render(reduced, Affine.Scale(2, 2).Then(Affine.Rotate(10)), true);
            long t3 = sw.ElapsedMilliseconds;
            Console.WriteLine($"  info 256x256 multi-colour: exact trace {t1} ms ({exact.Shapes.Count} shapes, {exact.PointCount} points); 16 colours + smoothing {t2} ms ({reduced.Shapes.Count} shapes, {reduced.PointCount} points); antialiased render at 2x rotated {t3} ms");
            Check(Exact(img, 256, 256, new TraceOptions(), out _), "the large image also round-trips exactly");
        }

        Console.WriteLine(_fails == 0 ? "all trace checks passed" : $"{_fails} trace check(s) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
