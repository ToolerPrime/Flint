/* Large-document memory and undo-integrity checks. Flint.exe --memtest <dir>
 *
 * Sizes are realistic, not toy: 1280x720, two layers, 48 drawn frames is about
 * 350 MB of pixels. The point is to show what undo really costs, and that the
 * shared snapshots behind it stay correct when strokes and structural edits
 * interleave. */
using System.Diagnostics;

namespace Flint;

public static class MemTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private static long Mb(long b) => b / (1024 * 1024);

    private static long Process64()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var p = Process.GetCurrentProcess();
        p.Refresh();
        return p.PrivateMemorySize64;
    }

    private static void Fill(Bitmap b, Color c)
    {
        using var g = Graphics.FromImage(b);
        g.Clear(c);
    }

    public static int Run(string scratch)
    {
        Directory.CreateDirectory(scratch);
        Console.WriteLine($"Large document ({(Environment.Is64BitProcess ? "64" : "32")}-bit process)");
        Check(Environment.Is64BitProcess, "running as a 64-bit process");

        const int W = 1280, H = 720, Frames = 48;
        long baseline = Process64();
        var sw = Stopwatch.StartNew();

        var d = new Document(W, H);
        d.AddLayer("Character");
        for (int i = 1; i < Frames; i++) d.InsertFrame(i);
        for (int f = 0; f < Frames; f++)
        {
            Fill(d.Layers[0].EnsureCel(f).EnsureRaster(W, H).Pixels, Color.FromArgb(255, 20, 40, 60 + f));
            Fill(d.Layers[1].EnsureCel(f).EnsureRaster(W, H).Pixels, Color.FromArgb(128, 200, f * 4, 10));
        }
        long docBytes = d.ApproximateBytes();
        long afterBuild = Process64();
        Console.WriteLine($"  info built {Frames} frames x 2 layers at {W}x{H}: {Mb(docBytes)} MB of pixels in {sw.ElapsedMilliseconds} ms; process grew {Mb(afterBuild - baseline)} MB");
        Check(docBytes > 300L * 1024 * 1024, "document holds over 300 MB of pixels");

        // --- structural edits cost structure, not pixels ---
        var stack = new UndoStack();
        var before = d.Snapshot();
        d.AddLayer("Effects");
        var addLayer = new StructureEdit("add layer", before, d.Snapshot());
        stack.Push(addLayer);
        Check(addLayer.Bytes < 64 * 1024, $"adding a layer to a {Mb(docBytes)} MB project costs {addLayer.Bytes} bytes of undo");

        before = d.Snapshot();
        d.InsertFrame(10);
        var addFrame = new StructureEdit("add frame", before, d.Snapshot());
        stack.Push(addFrame);
        Check(addFrame.Bytes < 64 * 1024, $"inserting a frame costs {addFrame.Bytes} bytes of undo");

        before = d.Snapshot();
        d.MoveFrame(0, 30);
        var move = new StructureEdit("move frame", before, d.Snapshot());
        stack.Push(move);
        Check(move.Bytes < 64 * 1024, $"moving a frame costs {move.Bytes} bytes of undo");

        long pre = Process64();
        for (int i = 0; i < 40; i++)
        {
            before = d.Snapshot();
            d.InsertFrame(d.FrameCount);
            stack.Push(new StructureEdit("add frame", before, d.Snapshot()));
        }
        long grew = Process64() - pre;
        Check(grew < 64L * 1024 * 1024,
              $"40 more frame inserts grow the process by {Mb(grew)} MB, not {Mb(docBytes * 2 * 40)} MB");
        Check(stack.Used < 1024 * 1024, $"the undo stack holds {stack.Used / 1024} KB after 43 structural edits");

        // A flip really replaces every image, and undo must keep the originals.
        before = d.Snapshot();
        d.MapRasters(r => r.With(ImageOps.Flip(r.Pixels, true)));
        var flip = new StructureEdit("flip", before, d.Snapshot());
        Check(Math.Abs(flip.Bytes - docBytes) < docBytes / 50,
              $"a flip honestly costs about the document's size ({Mb(flip.Bytes)} MB vs {Mb(docBytes)} MB)");
        d.Dispose();

        // --- integrity: strokes and structure edits interleaved ---
        Console.WriteLine("Undo integrity with shared snapshots");
        {
            var doc = new Document(32, 32);
            var r0 = doc.Layers[0].EnsureCel(0).EnsureRaster(32, 32);
            Fill(r0.Pixels, Color.Red);
            r0.Pixels.SetPixel(0, 0, Color.Blue);            // a marker to watch through the flip
            var original = ImageOps.GetPixels(r0.Pixels);
            var st = new UndoStack();

            // 1. structural: flip
            var b1 = doc.Snapshot();
            doc.MapRasters(r => r.With(ImageOps.Flip(r.Pixels, true), 32 - r.X - r.Width, r.Y));
            st.Push(new StructureEdit("flip", b1, doc.Snapshot()));

            // 2. stroke on the flipped image
            var live = doc.Layers[0].CelAt(0)!.FindRaster()!;
            var rec = new PaintRecorder(doc, 0, 0, live);
            live.Pixels.SetPixel(10, 10, Color.Lime);
            st.Push(rec.Finish("pencil")!);

            // 3. structural: add a layer on top
            var b3 = doc.Snapshot();
            doc.AddLayer("Top");
            st.Push(new StructureEdit("add layer", b3, doc.Snapshot()));

            // 4. stroke on the original layer again
            live = doc.Layers[0].CelAt(0)!.FindRaster()!;
            rec = new PaintRecorder(doc, 0, 0, live);
            live.Pixels.SetPixel(20, 20, Color.Yellow);
            st.Push(rec.Finish("pencil")!);

            Color At(int x, int y) => doc.Layers[0].CelAt(0)!.FindRaster()!.Pixels.GetPixel(x, y);
            Check(At(31, 0).ToArgb() == Color.Blue.ToArgb(), "flip moved the marker to the far edge");

            st.Undo(doc);
            Check(At(20, 20).ToArgb() == Color.Red.ToArgb(), "undo 4: second stroke gone");
            st.Undo(doc);
            Check(doc.LayerCount == 1, "undo 3: added layer gone");
            Check(At(10, 10).ToArgb() == Color.Lime.ToArgb(), "  and the first stroke is still there");
            st.Undo(doc);
            Check(At(10, 10).ToArgb() == Color.Red.ToArgb(), "undo 2: first stroke gone");
            st.Undo(doc);
            var now = ImageOps.GetPixels(doc.Layers[0].CelAt(0)!.FindRaster()!.Pixels);
            Check(now.AsSpan().SequenceEqual(original), "undo 1: the image is exactly the original, unflipped");

            st.Redo(doc); st.Redo(doc); st.Redo(doc); st.Redo(doc);
            Check(At(31, 0).ToArgb() == Color.Blue.ToArgb() && At(10, 10).ToArgb() == Color.Lime.ToArgb()
                  && At(20, 20).ToArgb() == Color.Yellow.ToArgb() && doc.LayerCount == 2,
                  "redo all four: flip, both strokes and the layer are back");

            // Undo twice, then make a new edit: the redo branch is discarded,
            // and the remaining history must still undo cleanly.
            st.Undo(doc); st.Undo(doc);
            var b5 = doc.Snapshot();
            doc.InsertFrame(1);
            st.Push(new StructureEdit("add frame", b5, doc.Snapshot()));
            st.Undo(doc); st.Undo(doc); st.Undo(doc);
            now = ImageOps.GetPixels(doc.Layers[0].CelAt(0)!.FindRaster()!.Pixels);
            Check(doc.FrameCount == 1 && now.AsSpan().SequenceEqual(original),
                  "after branching, undoing to the start still gives the exact original");
            doc.Dispose();
        }

        // --- save and load the large project ---
        Console.WriteLine("Saving a large project");
        {
            var big = new Document(1280, 720);
            for (int i = 1; i < 24; i++) big.InsertFrame(i);
            for (int f = 0; f < 24; f++)
            {
                var px = big.Layers[0].EnsureCel(f).EnsureRaster(1280, 720).Pixels;
                using var g = Graphics.FromImage(px);
                g.Clear(Color.Transparent);
                g.FillEllipse(Brushes.OrangeRed, 100 + f * 30, 200, 300, 300);
            }
            string path = Path.Combine(scratch, "big.flint");
            var t = Stopwatch.StartNew();
            FlintFormat.Save(big, path);
            long saveMs = t.ElapsedMilliseconds;
            t.Restart();
            var back = FlintFormat.Load(path);
            long loadMs = t.ElapsedMilliseconds;
            Console.WriteLine($"  info 24 frames at 1280x720: saved {new FileInfo(path).Length / 1024} KB in {saveMs} ms, loaded in {loadMs} ms");
            Check(back.Ok, "large project reloads");
            if (back.Ok)
            {
                var a = ImageOps.GetPixels(big.Layers[0].CelAt(17)!.FindRaster()!.Pixels);
                var b = ImageOps.GetPixels(back.Document!.Layers[0].CelAt(17)!.FindRaster()!.Pixels);
                Check(a.AsSpan().SequenceEqual(b), "frame 18 of the large project is pixel-identical after reload");
                back.Document.Dispose();
            }
            big.Dispose();
        }

        Console.WriteLine(_fails == 0 ? "all memory checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
