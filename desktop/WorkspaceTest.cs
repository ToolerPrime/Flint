namespace Flint;

public static class WorkspaceTest
{
    public static int Run()
    {
        int fails = 0;
        void Check(bool ok, string message) { Console.WriteLine($"{(ok ? "  ok   " : "  FAIL ")}{message}"); if (!ok) fails++; }
        Check(PixelSnap.Coordinate(1.5, true) == 2 && PixelSnap.Coordinate(-1.5, true) == -2, "pixel ties snap symmetrically");
        Check(PixelSnap.Coordinate(-1.25, false) == -1.25 && BitConverter.DoubleToInt64Bits(PixelSnap.Coordinate(-0.0, false)) < 0, "snap off preserves fractions and signed zero");
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            bool rejected = false;
            try { PixelSnap.Coordinate(value, false); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "nonfinite coordinates are rejected");
        }
        using var form = new MainForm { SuppressPrompts = true, StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) };
        form.Show(); Application.DoEvents();
        form.TestNewCanvas(16, 12);
        form.TestStroke(Tool.Pencil, Color.Lime, 1, new Point(5, 5));
        var original = form.TestDocument.Layers[0];
        form.TestSetColours(Color.Red, Color.Blue);
        Check(form.TestDocument.Layers.Count == 1, "paint colour selection does not fill the canvas");
        form.TestCanvasBackground(Color.Red);
        using (var render = form.TestDocument.RenderFrame(0)) Check(render.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "canvas background immediately renders red");
        form.TestCanvasBackground(Color.Blue);
        Check(form.TestDocument.Layers.Count == 2 && form.TestDocument.Layers.Contains(original), "recolour replaces only the managed background");
        using (var render = form.TestDocument.RenderFrame(0)) Check(render.GetPixel(0, 0).ToArgb() == Color.Blue.ToArgb(), "second colour immediately renders blue");
        form.TestUndo();
        using (var render = form.TestDocument.RenderFrame(0)) Check(render.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "undo restores red without shared-pixel mutation");
        form.TestRedo();
        string path = Path.Combine(Path.GetTempPath(), $"flint-workspace-{Guid.NewGuid():N}.flint");
        try
        {
            FlintFormat.Save(form.TestDocument, path);
            var loaded = FlintFormat.Load(path);
            var doc = loaded.Document;
            using var render = doc!.RenderFrame(0);
            Check(loaded.Ok && render.GetPixel(0, 0).ToArgb() == Color.Blue.ToArgb(), "canvas background survives save/reload");
            doc.Dispose();
        }
        finally { File.Delete(path); }
        form.TestCanvasBackground(null);
        using (var render = form.TestDocument.RenderFrame(0))
            Check(form.TestDocument.Layers.Count == 1 && render.GetPixel(0, 0).A == 0 && render.GetPixel(5, 5).ToArgb() == Color.Lime.ToArgb(), "transparent removes the background and keeps artwork");

        var originalBounds = form.TestWorkspacePanels.ToDictionary(p => p, p => p.Bounds);
        foreach (var pane in form.TestWorkspacePanels)
        {
            var content = pane.Content;
            var dock = pane.Dock;
            pane.Float(); Application.DoEvents();
            Check(pane.FloatingWindow?.Owner == form && !content.IsDisposed, $"{pane.Name} floats with live original controls");
            pane.FloatingWindow!.Close(); Application.DoEvents();
            Check(pane.FloatingWindow == null && pane.Dock == dock && ReferenceEquals(content.Parent, pane), $"closing {pane.Name} returns it to its dock");
            if (dock != DockStyle.Bottom) pane.DockTo(dock == DockStyle.Left ? DockStyle.Right : DockStyle.Left);
            pane.Float();
        }
        form.TestResetWorkspace(); Application.DoEvents();
        Check(form.TestWorkspacePanels.All(p => p.FloatingWindow == null && !p.Content.IsDisposed), "reset returns every floating panel without disposal");
        Check(form.TestWorkspacePanels.All(p => p.Bounds == originalBounds[p]), "reset restores the original panel positions and docking order");

        void Fixture()
        {
            var d = new Document(65, 64);
            var pixels = Enumerable.Repeat((byte)255, 20 * 20 * 4).ToArray();
            d.Layers[0].EnsureCel(0).Objects.Add(new RasterObject(ImageOps.FromPixels(20, 20, pixels)) { X = 10, Y = 10 });
            form.TestModifiers = Keys.None; form.TestUseDocument(d); form.TestZoom(4); form.TestGuideFlags(false, false, false);
        }
        Fixture(); form.TestPixelSnap = true;
        form.TestMouse(Tool.Transform, form.TestScreen(20, 20), form.TestScreen(21.25, 20.75));
        Check(form.TestTranslation == (1, 1), "transform drag snaps translation to whole pixels");
        form.TestCancelTransform();
        Fixture(); form.TestPixelSnap = false;
        form.TestMouse(Tool.Transform, form.TestScreen(20, 20), form.TestScreen(21.25, 20.75));
        Check(form.TestTranslation == (1.25, 0.75), "snap off retains fractional transform translation");
        form.TestCancelTransform();
        Fixture(); form.TestPixelSnap = true;
        form.TestMouse(Tool.Transform, form.TestScreen(30, 20), form.TestScreen(34.25, 20));
        Check(Math.Abs(form.TestTransformRight - 34) < 1e-5, $"axis-aligned resize edge snaps to pixel boundary (actual {form.TestTransformRight:R})");
        form.TestCancelTransform();
        Fixture(); form.TestPixelSnap = false;
        form.TestMouse(Tool.Transform, form.TestScreen(30, 20), form.TestScreen(34.25, 20));
        Check(Math.Abs(form.TestTransformRight - 34.25) < 1e-5, "snap off retains fractional resize edge");
        form.TestCancelTransform();
        Fixture(); form.TestPixelSnap = true; form.TestGuideFlags(false, false, true);
        form.TestMouse(Tool.Transform, form.TestScreen(20, 20), form.TestScreen(21.25, 20));
        Check(Math.Abs(form.TestTranslation.x - 2.5) < 1e-5, "half-pixel canvas-centre guide takes precedence");
        form.TestCancelTransform();
        form.Close();
        Console.WriteLine(fails == 0 ? "all workspace checks passed" : $"{fails} workspace checks failed");
        return fails == 0 ? 0 : 1;
    }
}
