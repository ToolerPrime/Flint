namespace Flint;

public static class WorkspaceTest
{
    /// <summary>Movable panels: docking anywhere, folding, dividers, floating, hiding, saved layouts.</summary>
    private static void WorkspaceChecks(MainForm form, Action<bool, string> Check)
    {
        Console.WriteLine("Workspace panels");
        form.ClientSize = new Size(1400, 900); Application.DoEvents();
        var docks = form.TestDocks;
        WorkspacePanel P(string n) => docks.Find(n)!;
        string Where(string n) => P(n).Column?.Side.ToString() ?? (P(n).FloatingWindow != null ? "Float" : "Hidden");
        Check(docks.Panels.Count == 8 && Where("Tools") == "Left" && Where("Layers") == "Right" && Where("Properties") == "Right" && Where("Timeline") == "Bottom",
            "default: tools left, layers to properties on the right, timeline at the bottom (8 panels)");
        Check(P("History").Collapsed && !P("Layers").Collapsed && P("History").Height == WorkspacePanel.HeaderHeight, "History starts folded up to its title bar");
        void Shot(string tag) { if (Environment.GetEnvironmentVariable("FLINT_WS_SHOT") is { Length: > 0 } dir) { using var b = new Bitmap(form.Width, form.Height); form.DrawToBitmap(b, new Rectangle(Point.Empty, form.Size)); b.Save(Path.Combine(dir, "ws-" + tag + ".png")); } }
        Shot("default");
        var original = docks.Capture();
        var originalBounds = docks.Panels.ToDictionary(p => p.Name, p => p.Bounds);
        var contents = docks.Panels.ToDictionary(p => p.Name, p => p.Content);

        foreach (var pane in docks.Panels.ToList())
        {
            var side = pane.Column!.Side; bool folded = pane.Collapsed;
            docks.Float(pane); Application.DoEvents();
            Check(pane.FloatingWindow?.Owner == form && !pane.Content.IsDisposed && pane.Column == null, $"{pane.Name} floats with its live controls");
            pane.FloatingWindow!.Close(); Application.DoEvents();
            Check(pane.FloatingWindow == null && pane.Column?.Side == side && pane.Collapsed == folded && ReferenceEquals(pane.Content.Parent, pane), $"closing {pane.Name}'s window puts it back where it was, folded as before");
        }

        docks.Dock(P("Timeline"), DockSide.Right, 0); docks.Dock(P("Tools"), DockSide.Bottom, 0); Application.DoEvents();
        Check(Where("Timeline") == "Right" && docks.Column(DockSide.Right).Items[0] == P("Timeline") && Where("Tools") == "Bottom"
            && !docks.Column(DockSide.Left).Visible, "any panel docks on any side; an emptied side disappears");
        var col = docks.Column(DockSide.Right);
        int before = P("Layers").Height;
        P("History").Collapsed = false; Application.DoEvents();
        Check(P("History").Height > WorkspacePanel.HeaderHeight && P("Layers").Height < before, "unfolding a panel shares the column's height");
        P("Timeline").Collapsed = true; Application.DoEvents();   // room to drag in this crowded column
        int at = col.Items.ToList().IndexOf(P("Layers"));
        var below = col.Items.Skip(at + 1).First(i => !i.Collapsed);
        double wa = P("Layers").Weight, wb = below.Weight; int h0 = P("Layers").Height, b0 = below.Height;
        col.DragBar(at, 40, wa, wb); Application.DoEvents();
        int grew = P("Layers").Height - h0, shrank = b0 - below.Height;
        Check(grew > 0 && Math.Abs(grew - shrank) <= 2 && below.Height >= below.MinContent.Height + WorkspacePanel.HeaderHeight - 1,
            $"dragging a divider grows one panel and shrinks the next open one, never below its minimum (+{grew} / -{shrank})");
        col.Extent = 360; Check(col.Width == 360, "a dock column's width can be dragged");

        var tl = docks.Column(DockSide.Right).PointToScreen(new Point(10, 5));
        Check(docks.TargetAt(tl) is { side: DockSide.Right, index: 0 }, "dropping on the top of the right column docks there, first");
        var host = docks.Column(DockSide.Bottom).Parent!;
        Check(docks.TargetAt(host.PointToScreen(new Point(10, 300))) is { side: DockSide.Left }, "dropping near the empty left edge docks on the left");
        Check(docks.TargetAt(host.PointToScreen(new Point(host.Width / 2, 300))) == null, "dropping over the middle of the canvas leaves the panel floating");

        docks.Hide(P("Library")); Check(P("Library").IsHidden && !P("Library").Visible, "a panel can be closed");
        docks.Show(P("Library")); Check(P("Library").Column != null, "and shown again (Window menu)");

        Shot("custom");
        var custom = docks.Capture();
        form.TestSaveWorkspace("Painting");
        form.TestResetWorkspace(); Application.DoEvents();
        Check(docks.Panels.All(p => p.Bounds == originalBounds[p.Name] && ReferenceEquals(p.Content, contents[p.Name]) && !p.Content.IsDisposed),
            "reset to default restores every panel's original place and size, keeping its controls");
        form.TestLoadWorkspace("Painting"); Application.DoEvents();
        var back = docks.Capture();
        Check(back.Panels.All(kv => custom.Panels[kv.Key].Where == kv.Value.Where && custom.Panels[kv.Key].Index == kv.Value.Index
            && custom.Panels[kv.Key].Collapsed == kv.Value.Collapsed) && back.RightWidth == custom.RightWidth,
            "a saved layout comes back exactly: sides, order, folded panels, column widths");
        Check(Theme.WorkspaceNames.Contains("Painting"), "saved layouts are listed by name");
        form.TestDeleteWorkspace("Painting"); Check(!Theme.WorkspaceNames.Contains("Painting"), "a saved layout can be deleted");

        var json = System.Text.Json.JsonSerializer.Serialize(custom);
        var copy = System.Text.Json.JsonSerializer.Deserialize<WorkspaceLayout>(json)!;
        Check(copy.Panels.Count == 8 && copy.Panels["Timeline"].Where == DockSide.Right, "layouts survive being written to and read from the settings file");
        var odd = new WorkspaceLayout { RightWidth = -5 }; odd.Panels["Nope"] = new PanelState { Where = DockSide.Left };
        docks.Apply(odd); Application.DoEvents();
        Check(docks.Panels.All(p => p.Column != null) && docks.Column(DockSide.Right).Width >= 120, "a layout missing panels or with bad sizes falls back to defaults");

        form.TestResetWorkspace();
        form.TestToggleAllPanels(); Check(docks.Panels.All(p => p.IsHidden), "F4 hides every panel");
        form.TestToggleAllPanels(); Check(docks.Panels.All(p => !p.IsHidden) && docks.Capture().Panels["Layers"].Where == DockSide.Right, "F4 again brings them all back");
        form.TestResetWorkspace(); Application.DoEvents();
    }

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

        WorkspaceChecks(form, Check);

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
