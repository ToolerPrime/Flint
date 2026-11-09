namespace Flint;

internal static class MotionTweenTest
{
    public static int Run()
    {
        int failures = 0;
        void Check(bool ok, string message)
        {
            Console.WriteLine($"{(ok ? "  ok   " : "  FAIL ")}{message}");
            if (!ok) failures++;
        }

        using var form = new MainForm
        {
            SuppressPrompts = true,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-6000, -6000),
        };
        form.Show();
        Application.DoEvents();
        form.TestNewCanvas(32, 32);
        var spec = new PrimitiveSpec(false, 4, 4, 0x99FF0000, true, 1);
        var start = EditableSources.Shape(spec, Affine.Translate(2, 10));
        form.TestAddEditable(start);
        form.TestSelectObjects(0);
        var doc = form.TestDocument;
        var layer = doc.Layers[0];
        var first = layer.CelAt(0)!;
        first.Mask = new RasterObject(ImageOps.NewBitmap(32, 32));
        var still = doc.AddLayer("Other artwork");
        still.EnsureCel(0).Objects.Add(EditableSources.Shape(spec, Affine.Translate(20, 20)));
        var shared = doc.AddLayer("Shared background");
        shared.Shared = true;
        var sharedCel = shared.EnsureCel(0);
        doc.Frames[0].Delay = 80;
        doc.Frames[0].Pivot = new Point(5, 6);
        doc.InsertFrame(1, new FrameSpec { Name = "Later", Delay = 150 });
        doc.Tags.Add(new AnimTag { Start = 1, End = 1 });
        var later = layer.EnsureCel(1);
        int undoCount = form.TestUndoCount;

        Check(!form.TestCreateMotionTween(Point.Empty, 3) && doc.FrameCount == 2 && form.TestUndoCount == undoCount,
            "zero displacement leaves the document and undo alone");
        layer.Shared = true;
        Check(!form.TestCreateMotionTween(new Point(9, -5), 3) && doc.FrameCount == 2,
            "shared layers cannot be tweened");
        layer.Shared = false;
        Check(form.TestCreateMotionTween(new Point(9, -5), 3) && form.TestCurrentFrame == 3,
            "the frame command inserts three frames and selects the final one");
        Check(doc.Frames.Select(f => f.Delay).SequenceEqual(new[] { 80, 80, 80, 80, 150 }) &&
            doc.Frames[2].Pivot == new Point(5, 6) && doc.Frames[4].Name == "Later" &&
            doc.Tags[0].Start == 4 && ReferenceEquals(layer.CelAt(4), later),
            "timing, frame metadata, tags and later cels follow frame insertion");

        var moving = Enumerable.Range(0, 4).Select(i => (RasterObject)layer.CelAt(i)!.Objects[0]).ToArray();
        Check(moving.Select(o => new Point(o.X, o.Y)).SequenceEqual(
            new[] { new Point(2, 10), new Point(5, 8), new Point(8, 7), new Point(11, 5) }),
            "motion is linear, rounded from the start and lands on the exact endpoint");
        Check(moving.All(o => o.Primitive == spec && ImageOps.GetPixels(o.Pixels).SequenceEqual(ImageOps.GetPixels(start.Pixels))),
            "translation preserves editable shape source and exact pixels");
        Check(!ReferenceEquals(first.Mask!.Pixels, layer.CelAt(1)!.Mask!.Pixels) &&
            !ReferenceEquals(still.CelAt(0)!.Objects[0], still.CelAt(2)!.Objects[0]) &&
            shared.Cels.Count == 1 && ReferenceEquals(shared.CelAt(3), sharedCel),
            "held artwork and masks get independent copies while shared layers stay shared");
        Check(form.TestUndoCount == undoCount + 1, "the complete tween costs one undo step");
        form.TestUndo();
        Check(form.TestDocument.FrameCount == 2 && form.TestDocument.Layers[0].CelAt(0)!.Objects[0].X == 2,
            "undo restores the starting timeline");
        form.TestRedo();
        Check(form.TestDocument.FrameCount == 5 && form.TestDocument.Layers[0].CelAt(3)!.Objects[0].X == 11,
            "redo restores the tween");

        string path = Path.Combine(Path.GetTempPath(), $"flint-tween-{Guid.NewGuid():N}.flint");
        try
        {
            FlintFormat.Save(form.TestDocument, path);
            var loaded = FlintFormat.Load(path).Document!;
            var last = (RasterObject)loaded.Layers[0].CelAt(3)!.Objects[0];
            Check(loaded.FrameCount == 5 && last.X == 11 && last.Y == 5 && last.Primitive == spec,
                "generated frames save and reopen through the existing project format");
            loaded.Dispose();
        }
        finally { File.Delete(path); }

        form.TestNewCanvas(32, 32);
        var painted = new RasterObject(ImageOps.NewBitmap(32, 32));
        form.TestAddEditable(painted);
        form.TestSelectObjects(0);
        Check(form.TestCreateMotionTween(new Point(1, 0), 3), "a plain bitmap can be tweened");
        var firstStep = (RasterObject)form.TestDocument.Layers[0].CelAt(1)!.Objects[0];
        Check(firstStep.X == 0 && !ReferenceEquals(firstStep.Pixels, painted.Pixels) &&
            !ReferenceEquals(firstStep.Original, painted.Pixels),
            "even a step rounded to zero owns pixels independent of the paintable start frame");
        var generatedPixels = ImageOps.GetPixels(firstStep.Pixels);
        form.TestSelectFrames(0, 0);
        form.TestStroke(Tool.Pencil, Color.Blue, 1, new Point(2, 2), new Point(4, 2));
        Check(!ImageOps.GetPixels(painted.Pixels).SequenceEqual(generatedPixels) &&
            ImageOps.GetPixels(firstStep.Pixels).SequenceEqual(generatedPixels),
            "painting the start frame leaves the generated frame untouched");

        form.TestNewCanvas(32, 32);
        form.TestStroke(Tool.Pencil, Color.Red, 3, new Point(5, 5), new Point(10, 10));
        int traced = form.TestTrace(new TraceOptions(), false);
        form.TestSelectObjects(0);
        var vector = form.TestSelectedObject?.Vector;
        Check(traced > 0 && vector != null && form.TestCreateMotionTween(new Point(4, 0), 2) &&
            form.TestSelectedObject?.Vector == vector, "a traced bitmap stays vector art through the tween");

        form.Close();
        return failures == 0 ? 0 : 1;
    }
}
