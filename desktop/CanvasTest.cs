/* Drives the real canvas mouse handlers on a real MainForm, so the wiring
 * between tools, selection, undo and the frame list is covered — not just the
 * primitives underneath. Flint.exe --canvastest */
namespace Flint;

public static class CanvasTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    public static int Run()
    {
        Console.WriteLine("Canvas wiring checks");
        var form = new MainForm
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-6000, -6000),
        };
        form.Show();
        Application.DoEvents();

        var red = Color.FromArgb(255, 220, 40, 40);

        // Pencil stroke along a row.
        form.TestNewCanvas(32, 32);
        int n = form.TestStroke(Tool.Pencil, red, 1, new Point(4, 10), new Point(20, 10));
        Check(n == 17, $"pencil drag paints 17 px along the row (got {n})");

        // Brush size is honoured.
        form.TestNewCanvas(32, 32);
        n = form.TestStroke(Tool.Pencil, red, 3, new Point(10, 10));
        Check(n == 9, $"size-3 pencil dot paints 9 px (got {n})");

        // Line tool: press at one end, release at the other.
        form.TestNewCanvas(32, 32);
        n = form.TestStroke(Tool.Line, red, 1, new Point(2, 2), new Point(2, 20));
        Check(n == 19, $"line tool paints 19 px (got {n})");

        // Filled rectangle.
        form.TestNewCanvas(32, 32);
        n = form.TestStroke(Tool.Rectangle, red, 1, new Point(4, 4), new Point(13, 11));
        Check(n == 2 * 10 + 2 * 8 - 4, $"rect outline via canvas paints 32 px (got {n})");

        // Flood fill covers the empty canvas.
        form.TestNewCanvas(20, 20);
        n = form.TestStroke(Tool.Fill, red, 1, new Point(10, 10));
        Check(n == 400, $"fill covers the blank 20x20 canvas (got {n})");

        // A selection must clip drawing.
        form.TestNewCanvas(32, 32);
        form.TestSelect(new Rectangle(10, 10, 5, 5));
        n = form.TestStroke(Tool.Pencil, red, 1, new Point(0, 12), new Point(31, 12));
        Check(n == 5, $"stroke clipped by selection paints 5 px (got {n})");

        // Fill must also respect the selection.
        form.TestNewCanvas(32, 32);
        form.TestSelect(new Rectangle(4, 4, 6, 6));
        n = form.TestStroke(Tool.Fill, red, 1, new Point(6, 6));
        Check(n == 36, $"fill clipped by selection paints 36 px (got {n})");

        // Undo restores what the stroke changed.
        form.TestNewCanvas(32, 32);
        form.TestSelect(Rectangle.Empty);
        form.TestStroke(Tool.Pencil, red, 1, new Point(5, 5), new Point(25, 5));
        form.TestUndo();
        int after = form.TestStroke(Tool.Pencil, red, 1, new Point(0, 0));
        Check(after == 1, $"after undo only the new dot remains (got {after})");

        // Adding a frame leaves the new one blank and selected.
        form.TestNewCanvas(16, 16);
        form.TestStroke(Tool.Fill, red, 1, new Point(8, 8));
        form.TestAddFrame();
        int blank = form.TestStroke(Tool.Pencil, red, 1, new Point(1, 1));
        Check(form.TestFrameCount == 2, $"frame added ({form.TestFrameCount} frames)");
        Check(blank == 1, $"the added frame started empty (got {blank} painted px)");

        Console.WriteLine("Timeline wiring");
        var blue = Color.FromArgb(255, 30, 60, 220);

        // Four frames, each painted a different way, then reordered.
        form.TestNewCanvas(16, 16);
        form.TestSetDelays(100, 100, 100, 100);
        for (int f = 0; f < 4; f++)
        {
            form.TestSelectFrames(f, f);
            form.TestStroke(Tool.Pencil, red, 1, new Point(f, 0));   // frame f marked at x=f
        }
        uint redPx = Surface.Pack(red);
        int MarkOf(int frame)
        {
            using var flat = form.TestDocument.RenderFrame(frame);
            for (int x = 0; x < 4; x++) if (flat.GetPixel(x, 0).ToArgb() == red.ToArgb()) return x;
            return -1;
        }
        Check(MarkOf(0) == 0 && MarkOf(3) == 3, "four frames each carry their own mark");

        form.TestMoveFrame(0, 3);
        Check(MarkOf(3) == 0 && MarkOf(0) == 1, $"dragging frame 1 to the end moves it there (marks {MarkOf(0)},{MarkOf(1)},{MarkOf(2)},{MarkOf(3)})");
        form.TestUndo();
        Check(MarkOf(0) == 0 && MarkOf(3) == 3, "undo puts the frame back");

        // Copy two frames and paste them after the last.
        form.TestSelectFrames(1, 2);
        form.TestCopyFrames();
        form.TestSelectFrames(3, 3);
        form.TestPasteFrames();
        Check(form.TestFrameCount == 6, $"pasting two frames gives six (got {form.TestFrameCount})");
        Check(MarkOf(4) == 1 && MarkOf(5) == 2, "pasted frames are copies of 2 and 3");

        // Pasted cels are independent copies, not shared references.
        form.TestSelectFrames(4, 4);
        form.TestStroke(Tool.Pencil, blue, 1, new Point(8, 8));
        using (var orig = form.TestDocument.RenderFrame(1))
            Check(orig.GetPixel(8, 8).ToArgb() != blue.ToArgb(), "drawing on a pasted frame leaves its original alone");

        form.TestSelectFrames(4, 5);
        form.TestDeleteFrames();
        Check(form.TestFrameCount == 4, "deleting a two-frame selection removes both");

        form.TestSelectFrames(1, 2);
        form.TestTag("Walk");
        var tag = form.TestDocument.Tags.FirstOrDefault();
        Check(tag != null && tag.Name == "Walk" && tag.Start == 1 && tag.End == 2, "tagging a selection records its range");
        form.TestSelectFrames(0, 0);
        form.TestMoveFrame(0, 3);
        var moved = form.TestDocument.Tags[0];
        Check(moved.Start == 0 && moved.End == 1,
              $"the tag follows its frames when another frame moves past them (now {moved.Start}-{moved.End})");

        Console.WriteLine("Playback in the real form");
        form.TestNewCanvas(8, 8);
        form.TestSetDelays(100, 100, 100, 100);
        form.TestPlayFor(250, LoopMode.Loop);
        int at = form.TestCurrentFrame;
        Check(at is 2 or 3, $"after 250ms of real playback the form shows frame 3 (index {at})");
        form.TestPlayFor(250, LoopMode.Loop, 2.0);
        at = form.TestCurrentFrame;
        Check(at is 0 or 1, $"at 2x speed 250ms wraps past the end (index {at})");

        Console.WriteLine("Audio on the timeline");
        string wav = Path.Combine(Path.GetTempPath(), "flint_canvas_tone.wav");
        var tone = new float[AudioFormat.MsToFrames(300) * 2];
        for (int i = 0; i < tone.Length / 2; i++)
            tone[i * 2] = tone[i * 2 + 1] = 0.4f * (float)Math.Sin(2 * Math.PI * 330 * i / AudioFormat.Rate);
        AudioIO.WritePcm16Wav(wav, tone);

        form.TestNewCanvas(8, 8);
        form.TestSetDelays(100, 100, 100, 100);
        var task = form.TestImportAudio(wav, 200);
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && wait.ElapsedMilliseconds < 10000) { Application.DoEvents(); Thread.Sleep(5); }
        var a = form.TestDocument.Audio;
        Check(task.IsCompleted && a.Tracks.Count == 1 && a.Tracks[0].Clips.Count == 1, "importing a WAV makes a track with one clip");
        if (a.Tracks.Count == 1 && a.Tracks[0].Clips.Count == 1)
        {
            var c = a.Tracks[0].Clips[0];
            Check(c.StartMs == 200 && Math.Abs(c.LengthMs - 300) < 1, $"the clip sits at the drop time, 300ms long (start {c.StartMs}, len {c.LengthMs:0})");
            var mix = AudioMixer.Mix(a, 0, 600);
            long first = -1;
            for (long i = 0; i < mix.Length / 2; i++) if (Math.Abs(mix[i * 2]) > 0.05f) { first = i; break; }
            Check(Math.Abs(AudioFormat.FramesToMs(first) - 200) < 1, $"and is heard from 200ms (first sound {AudioFormat.FramesToMs(first):0.0}ms)");
        }
        form.TestUndo();
        Check(form.TestDocument.Audio.IsEmpty, "undo removes the imported audio");
        form.TestRedo();
        Check(!form.TestDocument.Audio.IsEmpty, "redo brings it back");

        // A take lands where recording started.
        form.TestNewCanvas(8, 8);
        form.TestFinishRecording(tone, 350);
        var take = form.TestDocument.Audio.Tracks.SelectMany(t => t.Clips).FirstOrDefault();
        Check(take != null && take.StartMs == 350, $"a recorded take lands at the playhead (start {take?.StartMs})");

        // Audio survives save and reopen through the form.
        string proj = Path.Combine(Path.GetTempPath(), "flint_canvas_audio.flint");
        form.TestSave(proj);
        form.TestNewCanvas(8, 8);
        Check(form.TestOpen(proj) && form.TestDocument.Audio.Tracks.Sum(t => t.Clips.Count) == 1,
              "the form saves and reopens a project with its audio");
        File.Delete(wav);
        File.Delete(proj);

        string fwPng = @"C:\Users\Techn\Pictures\Untitled-1.png";
        if (File.Exists(fwPng))
        {
            form.TestNewCanvas(8, 8);
            Check(form.OpenFireworks(fwPng) && form.TestDocument.Width == 64 && form.TestDocument.Layers.Count == 2
                  && form.Text.StartsWith("Untitled"), "the form opens a Fireworks PNG as an untitled layered document");
        }
        else Console.WriteLine("  skip  Untitled-1.png not found");

        Console.WriteLine("History");
        form.TestNewCanvas(16, 16);
        form.TestStroke(Tool.Pencil, red, 1, new Point(1, 1));
        form.TestAddLayer();
        form.TestStroke(Tool.Pencil, red, 1, new Point(5, 5));
        form.TestAddFrame();
        Check(form.TestHistoryCount == 5 && form.TestHistoryCurrent == 4,
              $"history lists the start plus four steps ({form.TestHistoryCount} rows, at {form.TestHistoryCurrent})");
        form.TestHistoryJump(1);
        Check(form.TestLayerCount == 1 && form.TestFrameCount == 1, "jumping to step 1 undoes the later three");
        Check(form.TestHistoryCount == 5, "the undone steps stay listed for redo");
        form.TestHistoryJump(4);
        Check(form.TestLayerCount == 2 && form.TestFrameCount == 2, "jumping to the last step redoes them all");
        form.TestHistoryJump(0);
        Check(form.TestLayerCount == 1 && form.TestDocument.Layers[0].CelAt(0)?.FindRaster() is var r0
              && (r0 == null || r0.Pixels.GetPixel(1, 1).A == 0), "jumping to the start undoes everything");
        form.TestStroke(Tool.Pencil, red, 1, new Point(2, 2));
        Check(form.TestHistoryCount == 2, $"a lone stroke appears in history at once ({form.TestHistoryCount} rows)");

        Console.WriteLine("Frames view");
        form.TestNewCanvas(16, 16);
        form.TestAddFrame(); form.TestAddFrame(); form.TestAddFrame();
        form.TestSetDelays(100, 200, 300, 400);
        var tl = form.TestTimeline;
        int[] Delays() => form.TestDocument.Frames.Select(f => f.Delay).ToArray();
        Point Mid(Rectangle q) => new(q.X + q.Width / 2, q.Y + q.Height / 2);
        void Click(Point p) { tl.TestMouse("down", p); tl.TestMouse("up", p); }
        Check(form.TestFrameCount == 4 && !tl.FramesView, "starts in Timeline view with 4 frames");
        form.TestSetFramesView(true);
        Application.DoEvents();
        Check(tl.FramesView, "toggle switches to Frames view");
        var cs = Enumerable.Range(0, 4).Select(tl.TestCell).ToArray();
        Check(cs.All(c => c.Size == cs[0].Size) && cs.Zip(cs.Skip(1), (a, b) => b.Left > a.Right).All(x => x),
              "every frame cell is the same size, in order, left to right");
        Check(tl.PreferredHeight < 200, $"Frames view needs no audio lane height ({tl.PreferredHeight})");

        Click(Mid(tl.TestThumbRect(2)));
        Check(form.TestCurrentFrame == 2 && form.TestSelRange == (2, 2), "click selects a frame");
        tl.TestExtend = true;
        Click(Mid(tl.TestThumbRect(0)));
        tl.TestExtend = false;
        Check(form.TestSelRange == (0, 2), $"Shift-click selects a range ({form.TestSelRange})");

        var fr0 = tl.TestThumbRect(0);
        var last = tl.TestCell(3);
        tl.TestMouse("down", Mid(fr0));
        tl.TestMouse("move", new Point(last.Right - 8, last.Y + 50));
        tl.TestMouse("up", new Point(last.Right - 8, last.Y + 50));
        Check(Delays().SequenceEqual(new[] { 200, 300, 400, 100 }), $"dragging frame 1 to the end reorders ({string.Join(",", Delays())})");
        form.TestUndo();
        Check(Delays().SequenceEqual(new[] { 100, 200, 300, 400 }), "reorder undoes in one step");

        int hist = form.TestHistoryCurrent;
        Click(Mid(tl.TestDelayRect(1)));
        Check(tl.TestEditorOpen, "clicking the delay label opens the inline editor");
        tl.TestType("250");
        Check(!tl.TestEditorOpen && Delays()[1] == 250, $"typing 250 sets the delay ({Delays()[1]})");
        Check(form.TestHistoryCurrent == hist + 1, "delay edit is one undo step");
        form.TestUndo();
        Check(Delays()[1] == 200, "undo restores the old delay");
        Click(Mid(tl.TestDelayRect(1)));
        tl.TestType("abc");
        Check(Delays()[1] == 200, "a non-number leaves the delay alone");
        Click(Mid(tl.TestDelayRect(2)));
        tl.TestType("", commit: false);
        Check(!tl.TestEditorOpen && Delays()[2] == 300, "cancelling the editor changes nothing");

        form.TestSetFramesView(false);
        Application.DoEvents();
        Check(!tl.FramesView, "toggle returns to Timeline view");

        Console.WriteLine("Autosave");
        Recovery.Dir = Path.Combine(Path.GetTempPath(), "flint_canvas_recovery");
        if (Directory.Exists(Recovery.Dir)) Directory.Delete(Recovery.Dir, true);
        form.TestNewCanvas(8, 8);
        form.TestStroke(Tool.Pencil, red, 1, new Point(2, 2));
        form.TestAutosaveNow();
        Check(Directory.GetFiles(Recovery.Dir, "*.flint").Length == 1, "autosave writes a recovery copy");
        string saved = Path.Combine(Path.GetTempPath(), "flint_canvas_saved.flint");
        form.TestSaveViaEditor(saved);
        Check(Directory.GetFiles(Recovery.Dir, "*.flint").Length == 0, "saving the project removes the recovery copy");
        File.Delete(saved);

        form.Close();
        Console.WriteLine(_fails == 0 ? "all canvas checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
