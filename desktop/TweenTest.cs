/* Tweens with easing: ease curves, custom cubic-bezier, curved paths at even
 * speed, opacity / scale / rotation, editable spans (edit, frame count,
 * remove, undo, frame insertion), .flint round trip.
 * Flint.exe --tweentest <dir> */
namespace Flint;

public static class TweenTest
{
    private static int _fails;
    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private static bool Near(double a, double b, double tol = 1e-6) => Math.Abs(a - b) <= tol;

    private static RasterObject Box(int x, int y, int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i + 2] = 220; px[i + 3] = 255; }
        return new RasterObject(ImageOps.FromPixels(w, h, px)) { X = x, Y = y, Name = "box" };
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);

        Console.WriteLine("Ease curves");
        foreach (EaseKind k in Enum.GetValues<EaseKind>())
        {
            var e = k == EaseKind.Custom ? new Ease(k, 0.3, 0.1, 0.7, 0.9) : new Ease(k);
            Check(Near(e.Apply(0), 0) && Near(e.Apply(1), 1), $"{Ease.Describe(k)}: starts at 0, ends at 1");
        }
        Check(Near(new Ease(EaseKind.InQuad).Apply(0.5), 0.25) && Near(new Ease(EaseKind.OutQuad).Apply(0.5), 0.75) && Near(new Ease(EaseKind.InCubic).Apply(0.5), 0.125),
              "quad and cubic have the textbook values at the middle");
        Check(new Ease(EaseKind.InBack).Apply(0.2) < 0 && new Ease(EaseKind.OutBack).Apply(0.8) > 1, "back eases overshoot");
        Check(new Ease(EaseKind.OutElastic).Apply(0.15) > 1 && new Ease(EaseKind.OutBounce).Apply(0.5) <= 1, "elastic overshoots, bounce stays within");
        Check(Near(new Ease(EaseKind.Hold).Apply(0.99), 0) && Near(new Ease(EaseKind.Hold).Apply(1), 1), "hold stays put until the last frame");
        Check(Enumerable.Range(0, 11).All(i => Near(new Ease(EaseKind.Custom, 0, 0, 1, 1).Apply(i / 10.0), i / 10.0, 1e-9)), "custom curve with straight handles is linear");
        Check(Near(new Ease(EaseKind.Custom, 0.42, 0, 0.58, 1).Apply(0.5), 0.5, 1e-9) && new Ease(EaseKind.Custom, 0.42, 0, 0.58, 1).Apply(0.25) < 0.25,
              "custom symmetric ease-in-out passes the middle and starts slow");
        Check(Near(Ease.CubicBezier(0.25, 0.1, 0.25, 1, 0.5), 0.8024033877399112, 1e-6), "cubic-bezier(.25,.1,.25,1) matches the CSS 'ease' curve at t=0.5");

        Console.WriteLine("Curved motion path");
        {
            var path = new MotionPath(100, 0, 50, -60);
            var mid = path.Point(0.5);
            Check(Near(mid.x, 50, 0.5) && mid.y < -25, $"bends towards the control point (middle at {mid.x:0.0}, {mid.y:0.0})");
            var end = path.Point(1);
            Check(Near(end.x, 100, 1e-9) && Near(end.y, 0, 1e-9), "ends exactly at the end point");
            var pts = Enumerable.Range(0, 21).Select(i => path.Point(i / 20.0)).ToArray();
            var steps = pts.Zip(pts.Skip(1), (a, b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y))).ToArray();
            Check(steps.Max() / steps.Min() < 1.05, $"moves at an even speed along the curve (steps {steps.Min():0.00}-{steps.Max():0.00} px)");
            var over = path.Point(1.1);
            Check(over.x > 100, "an overshooting ease carries on past the end");
        }

        Console.WriteLine("Tween with ease, scale, rotation and opacity");
        var doc = new Document(60, 40);
        doc.Layers[0].EnsureCel(0).Objects.Add(Box(2, 10, 6, 4));
        doc.AddLayer("Back").EnsureCel(0).Objects.Add(Box(40, 30, 3, 3));
        using var form = new MainForm { SuppressPrompts = true, StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) };
        form.Show();
        Application.DoEvents();
        form.TestUseDocument(doc);
        form.TestSetActiveLayer(0);
        form.TestSelectObjects(0);
        var spec = new TweenSpec { NewFrames = 4, Dx = 40, Ease = new Ease(EaseKind.InQuad) };
        Check(form.TestCreateTween(spec), "tween created");
        RasterObject O(int f) => (RasterObject)doc.Layers[0].CelAt(f)!.Objects[0];
        var xs = Enumerable.Range(0, 5).Select(f => O(f).X).ToArray();
        Check(xs.SequenceEqual(new[] { 2, 2 + 3, 2 + 10, 2 + 23, 42 }), $"ease in: slow then fast ({string.Join(",", xs)})");
        var span = doc.Layers[0].Tweens.Single();
        Check(span.Start == 0 && span.Frames == 4 && span.ObjectIndex == 0 && span.Spec.Ease.Kind == EaseKind.InQuad, "the tween is kept as an editable span");
        Check(doc.Layers[1].CelAt(4) != null, "other layers carry on through the new frames");

        Console.WriteLine("Edit tween");
        form.TestSetCurrent(2);
        Check(form.TestTweenHere == span, "a frame inside the tween finds it");
        var edited = new TweenSpec { Dx = 40, Dy = 0, CurveX = 20, CurveY = -20, ScaleTo = 200, Rotate = 90, OpacityFrom = 100, OpacityTo = 0, Ease = new Ease(EaseKind.OutCubic) };
        Check(form.EditTween(span, edited, 6) && doc.FrameCount == 7, $"editing to 6 frames adds two at the end ({doc.FrameCount} frames)");
        var last = O(6);
        Check(last.Width == 8 && last.Height == 12 && last.Opacity == 0, $"last frame: scaled 200% and turned a quarter (6x4 -> {last.Width}x{last.Height}), faded out");
        Check(O(3).Y < 10 - 4, $"the curve lifts the middle frames (frame 4 top at {O(3).Y})");
        Check(Math.Abs(O(3).Opacity - 255 * (1 - (1 - Math.Pow(1 - 0.5, 3)))) < 3, $"opacity follows the ease (frame 4: {O(3).Opacity})");
        Check(doc.Layers[1].CelAt(6) != null, "added frames copy the other layers' last tween frame");
        var span2 = doc.Layers[0].Tweens.Single();
        Check(span2.Frames == 6 && span2.Spec.CurveX == 20, "the span now holds the new settings");
        Check(form.EditTween(span2, edited, 3) && doc.FrameCount == 4 && doc.Layers[0].Tweens.Single().Frames == 3, "editing to 3 frames removes frames from the end");
        // Move the start object; editing again makes the frames follow.
        var s3 = doc.Layers[0].Tweens.Single();
        doc.Layers[0].CelAt(0)!.Objects[0] = O(0).Moved(0, 5);
        form.EditTween(s3, new TweenSpec { Dx = 10 }, 3);
        Check(O(3).X == O(0).X + 10 && O(3).Y == O(0).Y, "after moving the start object, Edit tween makes the frames follow it");
        Check(!ReferenceEquals(O(1).Pixels, O(0).Pixels) && !ReferenceEquals(O(1).Original, O(0).Pixels), "edited frames own their pixels");

        Console.WriteLine("Undo, remove, frame changes");
        int frames = doc.FrameCount;
        form.TestUndo();
        Check(doc.Layers[0].Tweens.Single().Frames == 3, "undo brings back the previous tween settings");
        form.TestRedo();
        form.TestSetCurrent(1);
        Check(form.RemoveTween() && doc.Layers[0].Tweens.Count == 0 && doc.FrameCount == frames, "remove tween keeps the frames");
        form.TestUndo();
        Check(doc.Layers[0].Tweens.Count == 1, "undo brings the tween back");
        doc.InsertFrame(0);
        Check(doc.Layers[0].Tweens.Single().Start == 1, "adding a frame before the tween moves it along");
        doc.InsertFrame(2);
        Check(doc.Layers[0].Tweens.Count == 0, "adding a frame inside the tween turns it into plain frames");

        Console.WriteLine("Review cases: fade off, budget, object changes, shared, frame moves");
        {
            var d = new Document(60, 40);
            var obj = Box(2, 2, 6, 4); obj.Opacity = 153;                         // its own 60%
            d.Layers[0].EnsureCel(0).Objects.Add(obj);
            d.Layers[0].EnsureCel(0).Objects.Add(Box(30, 2, 4, 4));
            using var f = new MainForm { SuppressPrompts = true, StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) };
            f.Show(); Application.DoEvents();
            f.TestUseDocument(d); f.TestSetActiveLayer(0); f.TestSelectObjects(0);
            RasterObject Q(int fr) => (RasterObject)d.Layers[0].CelAt(fr)!.Objects[0];
            f.TestCreateTween(new TweenSpec { NewFrames = 3, Dx = 6, OpacityFrom = 0, OpacityTo = 100 });
            Check(Q(0).Opacity == 0 && Q(3).Opacity == 255, "fade in: starts clear, ends solid");
            f.TestSelectObjects(0);
            int beforeChain = d.FrameCount;
            Check(f.TestCreateTween(new TweenSpec { NewFrames = 2, Dx = 4 }) && d.FrameCount == beforeChain + 2 && d.Layers[0].Tweens.Count == 2,
                  "a new tween can start on the last frame of another (chained)");
            f.TestUndo();
            f.TestSetCurrent(2);
            var sp = f.TestTweenHere!;
            f.EditTween(sp, new TweenSpec { Dx = 6 }, 3);
            Check(Q(0).Opacity == 153 && Q(3).Opacity == 153, $"turning the fade off gives the object its own 60% back ({Q(0).Opacity}, {Q(3).Opacity})");
            sp = d.Layers[0].Tweens.Single();
            int fc = d.FrameCount;
            var big = new Document(4000, 4000);
            big.Layers[0].EnsureCel(0).Objects.Add(new RasterObject(ImageOps.NewBitmap(4000, 4000)) { Name = "Bitmap" });
            using (var g = new MainForm { SuppressPrompts = true, StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) })
            {
                g.Show(); Application.DoEvents();
                g.TestUseDocument(big); g.TestSetActiveLayer(0); g.TestSelectObjects(0);
                g.TestCreateTween(new TweenSpec { NewFrames = 1, Dx = 1 });
                g.TestSetCurrent(1);
                var bs = g.TestTweenHere!;
                int bf = big.FrameCount;
                Check(!g.EditTween(bs, new TweenSpec { Dx = 1, ScaleTo = 1000 }, 120) && big.FrameCount == bf, "an edit over the pixel budget is refused and changes nothing");
            }
            f.TestSetCurrent(2);
            sp = d.Layers[0].Tweens.Single();
            var pos = Enumerable.Range(0, 4).Select(fr => (Q(fr).X, Q(fr).Opacity)).ToArray();
            f.EditTween(sp, new TweenSpec { Dx = 6 }, 5);
            f.TestUndo();
            Check(d.FrameCount == fc && Enumerable.Range(0, 4).Select(fr => (Q(fr).X, Q(fr).Opacity)).SequenceEqual(pos) && d.Layers[0].Tweens.Single().Frames == 3,
                  "undo of an edit that added frames restores the frame count and the tween");
            // Deleting an object on the first frame: Edit refuses instead of tweening the wrong one.
            d.Layers[0].CelAt(0)!.Objects.RemoveAt(1);
            sp = d.Layers[0].Tweens.Single();
            Check(!f.EditTween(sp, new TweenSpec { Dx = 9 }, 3), "after objects on the first frame change, Edit refuses rather than tween the wrong object");
            d.Layers[0].CelAt(0)!.Objects.Add(Box(30, 2, 4, 4));
            // Moving a frame across the tween drops it; swapping an inside frame with an outside one too.
            d.InsertFrame(d.FrameCount);
            d.SwapFrames(2, d.FrameCount - 1);
            Check(d.Layers[0].Tweens.Count == 0, "swapping a tween frame with one outside turns it into plain frames");
            d.Layers[0].Tweens.Add(new TweenSpan(0, 3, 0, new TweenSpec { Dx = 6 }, 153, 2));
            d.RemoveFrame(2);
            Check(d.Layers[0].Tweens.Count == 0, "deleting a frame inside the tween turns it into plain frames");
            d.Layers[0].Tweens.Add(new TweenSpan(0, 2, 0, new TweenSpec { Dx = 6 }, 153, 2));
            f.TestSetCurrent(1);
            f.TestSetLayerShared(true);
            Check(d.Layers[0].Tweens.Count == 0 && f.TestTweenHere == null, "making the layer shared drops its tweens");
        }
        {
            // Bad tween data in a file loads with no tween and no crash.
            var d = new Document(20, 20);
            d.Layers[0].EnsureCel(0).Objects.Add(Box(1, 1, 4, 4));
            for (int i = 1; i <= 2; i++) d.InsertFrame(i);
            string good = Path.Combine(dir, "good.flint");
            d.Layers[0].Tweens.Add(new TweenSpan(0, 2, 0, new TweenSpec { Dx = 3 }));
            FlintFormat.Save(d, good);
            var bad = new[]
            {
                ("NaN move", "\"dx\":3", "\"dx\":\"NaN\""), ("huge move", "\"dx\":3", "\"dx\":1e300"), ("ease 99", "\"ease\":\"Linear\"", "\"ease\":\"99\""),
                ("past the end", "\"frames\":2", "\"frames\":9"), ("no object", "\"object\":0", "\"object\":5"), ("bad bezier", "\"ease\":\"Linear\"", "\"ease\":\"Custom\",\"bezier\":[5,0,9,1]"),
            };
            foreach (var (name, from, to) in bad)
            {
                string path = Path.Combine(dir, "bad.flint");
                File.Copy(good, path, true);
                using (var z = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update))
                {
                    var e = z.GetEntry("project.json")!;
                    string json;
                    using (var r = new StreamReader(e.Open())) json = r.ReadToEnd();
                    json = json.Replace(from, to).Replace(from.Replace(":", ": "), to);
                    e.Delete();
                    using var w = new StreamWriter(z.CreateEntry("project.json").Open());
                    w.Write(json);
                }
                FlintFormat.LoadResult? res = null; Exception? ex = null;
                try { res = FlintFormat.Load(path); } catch (Exception e2) { ex = e2; }
                var t = res?.Document?.Layers[0].Tweens.SingleOrDefault();
                bool safe = ex == null && res!.Ok && (t == null || (name is "bad bezier" or "ease 99" && t.Spec.Ease.Kind == EaseKind.Linear));
                Check(safe, $"bad tween in a file ({name}) loads safely, tween dropped or made harmless");
            }
        }

        Console.WriteLine("Saved in .flint");
        {
            var d = new Document(20, 20);
            d.Layers[0].EnsureCel(0).Objects.Add(Box(1, 1, 4, 4));
            for (int i = 1; i <= 3; i++) { d.InsertFrame(i); d.Layers[0].EnsureCel(i).Objects.Add(Box(1 + i, 1, 4, 4)); }
            var sp = new TweenSpec { NewFrames = 3, Dx = 3, CurveX = 1, CurveY = -2, ScaleTo = 150, Rotate = -30, OpacityFrom = 80, OpacityTo = 20, Ease = new Ease(EaseKind.Custom, 0.1, 0.2, 0.3, 1.2) };
            d.Layers[0].Tweens.Add(new TweenSpan(0, 3, 0, sp));
            string path = Path.Combine(dir, "tween.flint");
            FlintFormat.Save(d, path);
            var back = FlintFormat.Load(path);
            var t = back.Document!.Layers[0].Tweens.SingleOrDefault();
            Check(back.Ok && t != null && t.Start == 0 && t.Frames == 3 && t.Spec.Dx == 3 && t.Spec.CurveX == 1 && t.Spec.CurveY == -2 && t.Spec.ScaleTo == 150
                  && t.Spec.Rotate == -30 && t.Spec.OpacityFrom == 80 && t.Spec.OpacityTo == 20 && t.Spec.Ease == sp.Ease,
                  "every tween setting round-trips, custom curve included");
            Check(FlintFormat.CurrentVersion >= 12, "format 12 or later");
        }

        Console.WriteLine(_fails == 0 ? "all tween checks passed" : $"{_fails} tween check(s) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
