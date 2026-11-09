/* Checks for playback timing and onion-skin planning. Flint.exe --animtest */
namespace Flint;

public static class AnimTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private static string Seq(Playback p, int[] d, int from, int to, int stepMs, int steps)
    {
        var s = new List<int>();
        for (int i = 0; i < steps; i++) s.Add(p.FrameAfter(d, from, to, i * stepMs));
        return string.Join(",", s);
    }

    public static int Run()
    {
        Console.WriteLine("Playback timing");
        int[] even = { 100, 100, 100, 100 };

        var loop = new Playback { Mode = LoopMode.Loop };
        Check(Seq(loop, even, 0, 3, 100, 9) == "0,1,2,3,0,1,2,3,0", "loop wraps around: " + Seq(loop, even, 0, 3, 100, 9));

        var pp = new Playback { Mode = LoopMode.PingPong };
        string ppSeq = Seq(pp, even, 0, 3, 100, 10);
        Check(ppSeq == "0,1,2,3,2,1,0,1,2,3", "ping-pong does not repeat end frames: " + ppSeq);

        var once = new Playback { Mode = LoopMode.Once };
        Check(Seq(once, even, 0, 3, 100, 5) == "0,1,2,3,-1", "once stops after the last frame");

        // Uneven delays: a long frame stays up for its whole duration.
        int[] uneven = { 50, 300, 50 };
        Check(Seq(loop, uneven, 0, 2, 50, 9) == "0,1,1,1,1,1,1,2,0",
              "a 300ms frame holds for six 50ms ticks: " + Seq(loop, uneven, 0, 2, 50, 9));

        // Speed scales time, not frame count.
        var fast = new Playback { Mode = LoopMode.Loop, Speed = 2 };
        Check(Seq(fast, even, 0, 3, 50, 5) == "0,1,2,3,0", "2x speed shows each frame for half as long");
        var slow = new Playback { Mode = LoopMode.Loop, Speed = 0.5 };
        Check(Seq(slow, even, 0, 3, 100, 4) == "0,0,1,1", "0.5x speed doubles each frame");

        // Range playback (an animation tag) stays inside its frames.
        int[] eight = { 100, 100, 100, 100, 100, 100, 100, 100 };
        Check(Seq(loop, eight, 4, 6, 100, 6) == "4,5,6,4,5,6", "a tag range loops inside itself");

        // A late timer tick must land on the right frame, not the next one.
        Check(loop.FrameAfter(even, 0, 3, 250) == 2, "late tick at 250ms lands on frame 2, not 1");

        Check(Playback.TotalMs(uneven) == 400, "total duration sums delays");
        Check(Playback.FrameStart(uneven, 2) == 350, "frame start is the sum of earlier delays");
        Check(Playback.FrameAt(uneven, 349) == 1 && Playback.FrameAt(uneven, 350) == 2,
              "frame boundaries are exact to the millisecond");

        Console.WriteLine("Onion skin");
        var s = new OnionSettings { Enabled = true, Before = 2, After = 1, Opacity = 0.4f };
        var plan = Onion.Plan(s, 3, 8);
        Check(plan.Count == 3, $"2 before + 1 after = 3 ghosts (got {plan.Count})");
        Check(plan[0].frame == 2 && plan[1].frame == 1 && plan[2].frame == 4,
              "ghost frames are 2,1 before and 4 after");
        Check(plan[0].opacity > plan[1].opacity, "nearer ghosts are more opaque");
        Check(Math.Abs(plan[0].opacity - 0.4f) < 0.001, "nearest ghost uses the set opacity");

        // Wrapping on a 2-frame animation must not ghost the current frame or
        // the same neighbour twice.
        var wrap = Onion.Plan(new OnionSettings { Enabled = true, Before = 3, After = 3 }, 0, 2);
        Check(wrap.Count == 1 && wrap[0].frame == 1, "short animation: one ghost, never itself");

        var noWrap = Onion.Plan(new OnionSettings { Enabled = true, Before = 2, After = 0, Wrap = false }, 0, 8);
        Check(noWrap.Count == 0, "without wrap, frame 0 has nothing before it");

        Check(Onion.Plan(new OnionSettings { Enabled = false, Before = 2 }, 3, 8).Count == 0,
              "disabled onion skin plans nothing");

        {
            var doc = new Document(5, 1);
            doc.InsertFrame(1, new FrameSpec());
            doc.InsertFrame(2, new FrameSpec());
            var animated = doc.Layers[0];
            var background = doc.AddLayer("Background", 0);
            background.Shared = true;
            background.EnsureCel(0).Objects.Add(new RasterObject(
                ImageOps.FromPixels(5, 1, Enumerable.Repeat((byte)255, 20).ToArray())));
            var black = new byte[] { 0, 0, 0, 255 };
            for (int i = 0; i < 3; i++)
                animated.EnsureCel(i).Objects.Add(new RasterObject(ImageOps.FromPixels(1, 1, black)) { X = i });
            animated.EnsureCel(0).Objects.Add(new RasterObject(ImageOps.FromPixels(1, 1, black)) { X = 1 });

            var before = doc.RenderPixels(1);
            using var composite = doc.RenderFrame(1);
            using (var graphics = Graphics.FromImage(composite))
                Onion.Draw(graphics, doc, new OnionSettings { Enabled = true, Opacity = 0.65f }, 1, new Rectangle(0, 0, 5, 1));
            var past = composite.GetPixel(0, 0);
            var future = composite.GetPixel(2, 0);
            Check(past.R > past.B && future.B > future.R, "red and blue ghosts remain visible over a shared opaque background");
            Check(composite.GetPixel(1, 0).ToArgb() == Color.Black.ToArgb(), "ghosts leave opaque current-frame art intact");
            Check(composite.GetPixel(4, 0).ToArgb() == Color.White.ToArgb(), "the shared background itself is not ghosted");
            Check(before.SequenceEqual(doc.RenderPixels(1)), "drawing onion skin leaves export pixels unchanged");
            doc.Dispose();
        }

        Console.WriteLine(_fails == 0 ? "all animation checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
