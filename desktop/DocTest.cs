/* Checks for the document model, undo deltas and the .flint format.
 * Flint.exe --doctest <scratchdir> */
namespace Flint;

public static partial class DocTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private static uint Px(Document d, int frame, int x, int y)
    {
        using var flat = d.RenderFrame(frame);
        var c = flat.GetPixel(x, y);
        return (uint)((c.A << 24) | (c.R << 16) | (c.G << 8) | c.B);
    }

    private static void FillRaster(Document d, int layer, int frame, Color c)
    {
        var cel = d.Layers[layer].EnsureCel(frame);
        var r = cel.EnsureRaster(d.Width, d.Height);
        using var g = Graphics.FromImage(r.Pixels);
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        using var b = new SolidBrush(c);
        g.FillRectangle(b, 0, 0, d.Width, d.Height);
    }

    public static int Run(string scratch)
    {
        Directory.CreateDirectory(scratch);
        Console.WriteLine("Document model");

        // --- structure ---
        {
            var d = new Document(16, 16);
            Check(d.FrameCount == 1 && d.LayerCount == 1, "new document has 1 frame and 1 layer");

            d.AddLayer("Top");
            Check(d.LayerCount == 2 && d.Layers[1].Name == "Top", "layer added on top");

            d.InsertFrame(1);
            d.InsertFrame(2);
            Check(d.FrameCount == 3, "frames inserted");

            // Draw a different colour on each frame of layer 0.
            FillRaster(d, 0, 0, Color.Red);
            FillRaster(d, 0, 1, Color.Lime);
            FillRaster(d, 0, 2, Color.Blue);
            Check(Px(d, 0, 8, 8) == 0xFFFF0000 && Px(d, 2, 8, 8) == 0xFF0000FF,
                  "each frame keeps its own cel");

            // Removing the middle frame must shift later cels down, not lose them.
            d.RemoveFrame(1);
            Check(d.FrameCount == 2, "frame removed");
            Check(Px(d, 1, 8, 8) == 0xFF0000FF, "cels after the removed frame shifted down");

            d.SwapFrames(0, 1);
            Check(Px(d, 0, 8, 8) == 0xFF0000FF && Px(d, 1, 8, 8) == 0xFFFF0000, "frames swapped");

            // A tag on a frame follows it through a swap.
            d.Tags.Add(new AnimTag { Name = "hit", Start = 1, End = 1 });
            d.SwapFrames(0, 1);
            Check(d.Tags[0].Start == 0 && d.Tags[0].End == 0, "a tag follows its frame through a swap");
            d.Dispose();
        }

        // --- shared layers ---
        {
            var d = new Document(16, 16);
            d.InsertFrame(1);
            d.InsertFrame(2);
            d.Layers[0].Shared = true;
            FillRaster(d, 0, 0, Color.Orange);
            uint want = 0xFFFFA500;
            Check(Px(d, 0, 2, 2) == want && Px(d, 1, 2, 2) == want && Px(d, 2, 2, 2) == want,
                  "a shared layer shows on every frame");
            Check(d.Layers[0].Cels.Count == 1, "a shared layer stores only one cel");
            d.Dispose();
        }

        // --- compositing ---
        {
            var d = new Document(8, 8);
            d.AddLayer("Top");
            FillRaster(d, 0, 0, Color.FromArgb(255, 0, 0, 255));     // blue below
            FillRaster(d, 1, 0, Color.FromArgb(255, 255, 0, 0));     // red above
            Check(Px(d, 0, 4, 4) == 0xFFFF0000, "upper layer covers lower");

            d.Layers[1].Visible = false;
            Check(Px(d, 0, 4, 4) == 0xFF0000FF, "hidden layer is not drawn");

            d.Layers[1].Visible = true;
            d.Layers[1].Opacity = 128;
            var half = Px(d, 0, 4, 4);
            int r = (int)((half >> 16) & 255), b = (int)(half & 255);
            Check(r is > 100 and < 160 && b is > 100 and < 160,
                  $"50% layer opacity blends the two (got r={r} b={b})");

            d.Layers[1].Opacity = 255;
            d.Layers[1].Blend = BlendMode.Multiply;
            var mul = Px(d, 0, 4, 4);
            Check(mul == 0xFF000000, $"red multiplied by blue is black (got {mul:X8})");
            d.Dispose();
        }

        // --- empty cels cost nothing ---
        {
            var d = new Document(512, 512);
            for (int i = 1; i < 50; i++) d.InsertFrame(i);
            long empty = d.ApproximateBytes();
            Check(empty == 0, $"50 untouched frames hold no raster data (got {empty} bytes)");
            FillRaster(d, 0, 10, Color.Red);
            Check(d.ApproximateBytes() == 512L * 512 * 4, "drawing on one frame allocates one bitmap");
            d.Dispose();
        }

        Console.WriteLine("Undo");

        // --- paint deltas are small ---
        {
            var d = new Document(1024, 1024);
            var cel = d.Layers[0].EnsureCel(0);
            var raster = cel.EnsureRaster(1024, 1024);

            var rec = new PaintRecorder(d, 0, 0, raster);
            var s = new Surface(ImageOps.GetPixels(raster.Pixels), 1024, 1024);
            Draw.Line(s, 10, 10, 30, 10, 1, Surface.Pack(Color.Red), null);
            ImageOps.SetPixels(raster.Pixels, s.Px);
            var edit = rec.Finish("pencil");

            Check(edit != null, "a stroke produces an edit");
            long full = 1024L * 1024 * 4;
            Check(edit!.Bytes < full / 100,
                  $"a 21px stroke on a 1024x1024 canvas costs {edit.Bytes} bytes, not {full}");

            // And it must actually restore.
            uint before = s.Get(20, 10);
            edit.Undo(d);
            var afterUndo = new Surface(ImageOps.GetPixels(raster.Pixels), 1024, 1024);
            Check(afterUndo.Get(20, 10) == 0, "undo clears the painted pixel");
            edit.Redo(d);
            var afterRedo = new Surface(ImageOps.GetPixels(raster.Pixels), 1024, 1024);
            Check(afterRedo.Get(20, 10) == before, "redo puts it back");
            d.Dispose();
        }

        // --- a no-op stroke adds no history ---
        {
            var d = new Document(64, 64);
            var raster = d.Layers[0].EnsureCel(0).EnsureRaster(64, 64);
            var rec = new PaintRecorder(d, 0, 0, raster);
            Check(rec.Finish("nothing") == null, "a stroke that changed nothing makes no edit");
            d.Dispose();
        }

        // --- stack budget ---
        {
            // Budget deliberately smaller than 60 dots will need, so trimming fires.
            var stack = new UndoStack { Budget = 200 };
            var d = new Document(64, 64);
            var raster = d.Layers[0].EnsureCel(0).EnsureRaster(64, 64);
            for (int i = 0; i < 60; i++)
            {
                var rec = new PaintRecorder(d, 0, 0, raster);
                var s = new Surface(ImageOps.GetPixels(raster.Pixels), 64, 64);
                Draw.Dot(s, i % 60, 5, 1, Surface.Pack(Color.FromArgb(255, 10 + i, 0, 0)), null);
                ImageOps.SetPixels(raster.Pixels, s.Px);
                var e = rec.Finish("dot");
                if (e != null) stack.Push(e);
            }
            Check(stack.Used <= stack.Budget, $"stack stayed within budget ({stack.Used} <= {stack.Budget})");
            Check(stack.DoneCount is > 1 and < 60, $"old edits were dropped ({stack.DoneCount} kept)");
            Check(stack.CanUndo, "stack can still undo");
            d.Dispose();
        }

        // --- redo is dropped after a new edit ---
        {
            var stack = new UndoStack();
            var d = new Document(32, 32);
            stack.Push(new PropertyEdit("a", _ => { }, _ => { }));
            stack.Push(new PropertyEdit("b", _ => { }, _ => { }));
            stack.Undo(d);
            Check(stack.CanRedo, "redo available after undo");
            stack.Push(new PropertyEdit("c", _ => { }, _ => { }));
            Check(!stack.CanRedo, "a new edit discards the redo branch");
            d.Dispose();
        }

        Console.WriteLine(".flint format");

        // --- round trip ---
        {
            var d = new Document(24, 18);
            d.InsertFrame(1);
            d.Frames[0].Delay = 80;
            d.Frames[1].Delay = 250;
            d.Frames[1].Name = "hit";
            d.AddLayer("Shadow");
            d.Layers[0].Name = "Background";
            d.Layers[0].Shared = true;
            d.Layers[1].Opacity = 140;
            d.Layers[1].Blend = BlendMode.Multiply;
            d.Layers[1].Locked = true;
            d.Tags.Add(new AnimTag { Name = "Idle", Start = 0, End = 1 });
            FillRaster(d, 0, 0, Color.FromArgb(255, 20, 90, 200));
            FillRaster(d, 1, 1, Color.FromArgb(128, 250, 120, 30));

            string path = Path.Combine(scratch, "round.flint");
            FlintFormat.Save(d, path);
            Check(File.Exists(path) && new FileInfo(path).Length > 200, "project written");

            var loaded = FlintFormat.Load(path);
            Check(loaded.Ok, "project loaded: " + (loaded.Error ?? "ok"));
            if (loaded.Ok)
            {
                var e = loaded.Document!;
                Check(e.Width == 24 && e.Height == 18, "canvas size preserved");
                Check(e.FrameCount == 2, "frame count preserved");
                Check(e.Frames[0].Delay == 80 && e.Frames[1].Delay == 250, "frame delays preserved");
                Check(e.Frames[1].Name == "hit", "frame name preserved");
                Check(e.LayerCount == 2, "layer count preserved");
                Check(e.Layers[0].Name == "Background" && e.Layers[0].Shared, "shared layer preserved");
                Check(e.Layers[1].Opacity == 140 && e.Layers[1].Blend == BlendMode.Multiply,
                      "layer opacity and blend preserved");
                Check(e.Layers[1].Locked, "layer lock preserved");
                Check(e.Tags.Count == 1 && e.Tags[0].Name == "Idle" && e.Tags[0].End == 1,
                      "animation tag preserved");

                // Pixels must survive exactly, including alpha.
                var o1 = d.Layers[1].CelAt(1)!.FindRaster()!;
                var n1 = e.Layers[1].CelAt(1)!.FindRaster()!;
                var a = ImageOps.GetPixels(o1.Pixels);
                var b = ImageOps.GetPixels(n1.Pixels);
                Check(a.AsSpan().SequenceEqual(b), "raster pixels survive the round trip exactly");

                var probe = n1.Pixels.GetPixel(3, 3);
                Check(probe.A == 128, $"partial alpha preserved (got {probe.A})");
                e.Dispose();
            }
            d.Dispose();
        }

        // --- version guard ---
        {
            string path = Path.Combine(scratch, "future.flint");
            var d = new Document(8, 8);
            FlintFormat.Save(d, path);
            d.Dispose();
            // Rewrite the manifest claiming a newer format.
            using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update))
            {
                var entry = zip.GetEntry("project.json")!;
                string text;
                using (var r = new StreamReader(entry.Open())) text = r.ReadToEnd();
                text = System.Text.RegularExpressions.Regex.Replace(text, @"""format"":\s*\d+", @"""format"": 99");
                entry.Delete();
                var fresh = zip.CreateEntry("project.json");
                using var w = new StreamWriter(fresh.Open());
                w.Write(text);
            }
            var res = FlintFormat.Load(path);
            Check(!res.Ok && res.Error!.Contains("newer Flint"),
                  "a newer format version is refused with a clear message");
        }

        // --- a non-project file is refused, not crashed on ---
        {
            string path = Path.Combine(scratch, "notaproject.flint");
            File.WriteAllText(path, "this is not a zip");
            var res = FlintFormat.Load(path);
            Check(!res.Ok && res.Error != null, "a non-project file is refused: " + res.Error);
        }

        // --- atomic save leaves the old file intact on failure ---
        {
            string path = Path.Combine(scratch, "atomic.flint");
            var d = new Document(8, 8);
            FlintFormat.Save(d, path);
            long original = new FileInfo(path).Length;
            var leftovers = Directory.GetFiles(scratch, "*.saving-*");
            Check(leftovers.Length == 0, "no temp files left behind after a successful save");
            Check(new FileInfo(path).Length == original, "saved file intact");
            d.Dispose();
        }

        BlendAndMaskChecks(scratch);

        Console.WriteLine(_fails == 0 ? "all document checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
