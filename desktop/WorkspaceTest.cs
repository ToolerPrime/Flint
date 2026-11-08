namespace Flint;

public static class WorkspaceTest
{
    /// <summary>Fireworks-style workspace: tab groups, several columns per side, folding sides, toolbars as strips, layouts.</summary>
    private static void WorkspaceChecks(MainForm form, Action<bool, string> Check)
    {
        Console.WriteLine("Workspace panels");
        form.ClientSize = new Size(1400, 900); Application.DoEvents();
        var docks = form.TestDocks;
        PanelPage P(string n) => docks.FindPage(n)!;
        WorkspacePanel G(string n) => P(n).Group!;
        string Where(string n) => P(n).Group is not { } g ? "Hidden" : g.Column?.Side.ToString() ?? "Float";
        void Shot(string tag) { if (Environment.GetEnvironmentVariable("FLINT_WS_SHOT") is { Length: > 0 } dir) { Application.DoEvents(); using var b = new Bitmap(form.Width, form.Height); form.DrawToBitmap(b, new Rectangle(Point.Empty, form.Size)); b.Save(Path.Combine(dir, "ws-" + tag + ".png")); } }

        Check(docks.Pages.Count == 10 && Where("Main toolbar") == "Top" && Where("Tool options") == "Top" && Where("Tools") == "Left"
            && Where("Layers") == "Right" && Where("Timeline") == "Bottom", "default: toolbars on top, tools left, panels right, timeline bottom (10 pages)");
        Check(docks.Columns(DockSide.Top).Count == 2 && docks.Columns(DockSide.Top).All(c => c.StripOnly) && G("Main toolbar").IsStrip,
            "each toolbar sits in its own row as a strip with a grip, no title bar");
        Check(G("History").Collapsed && G("History").Height == WorkspacePanel.HeaderHeight, "History starts folded up to its title bar");
        Shot("default");
        var defaults = docks.Capture();
        var contents = docks.Pages.ToDictionary(p => p.Name, p => p.Content);

        // every group floats and comes back
        foreach (var g in docks.Groups.ToList())
        {
            var side = g.Column!.Side; bool folded = g.Collapsed; var pages = g.Pages.ToList();
            docks.Float(g); Application.DoEvents();
            Check(g.FloatingWindow?.Owner == form && g.Column == null && pages.All(p => !p.Content.IsDisposed), $"{g.Title} floats with its live controls");
            g.FloatingWindow!.Close(); Application.DoEvents();
            Check(g.FloatingWindow == null && g.Column?.Side == side && g.Collapsed == folded, $"closing {g.Title}'s window puts it back, folded as before");
        }

        // tab groups
        docks.MovePage(P("History"), G("Layers"));
        Check(G("Layers") == G("History") && G("Layers").Pages.Count == 2 && G("Layers").Active == P("History"), "Group History with Layers: one box, two tabs, History showing");
        G("Layers").Activate(P("Layers"));
        Check(P("Layers").Content.Visible && P("Layers").Content.Parent != null && P("History").Content.Parent == null, "switching tabs shows that page's controls only");
        Check(docks.TargetAt(G("Library").HeaderScreenBounds.Location + new Size(30, 8), G("Effects")) is { Kind: DropKind.Tab } t && t.Group == G("Library"),
            "dragging a panel over another panel's title bar offers to group it in as a tab");
        docks.Drop(G("Effects"), new DropTarget(DropKind.Tab, DockSide.Right, 0, 0, G("Library")));
        Check(G("Effects") == G("Library") && G("Library").Pages.Count == 2, "dropping on a title bar groups it as a tab");
        var split = G("Layers");
        docks.SplitPage(P("History"));
        Check(G("History") != split && G("History").Column == split.Column, "New panel group takes a tab out into its own group next to it");
        docks.Prompt = _ => "My Stuff";
        docks.RenameGroup(G("Library"));
        Check(G("Library").Title == "My Stuff", "a panel group can be renamed");

        // several columns per side
        docks.Drop(G("Layers"), new DropTarget(DropKind.NewColumn, DockSide.Right, 1, 0, null));
        Check(docks.Columns(DockSide.Right).Count == 2 && G("Layers").Column == docks.Columns(DockSide.Right)[1], "dropping at a column's inner edge makes a second column beside it");
        docks.Drop(G("Timeline"), new DropTarget(DropKind.NewColumn, DockSide.Left, 1, 0, null));
        Check(Where("Timeline") == "Left" && docks.Columns(DockSide.Left).Count == 2 && docks.Columns(DockSide.Bottom).Count == 0, "the timeline can go to the left; the empty bottom row disappears");
        var right = docks.Columns(DockSide.Right)[0];
        var edge = right.PointToScreen(new Point(right.Width - 6, right.Height / 2));
        Check(docks.TargetAt(edge, G("Tools")) is { Kind: DropKind.NewColumn, Side: DockSide.Right, Column: 0 }, "the outer edge of a column offers a new column outside it");
        var mid = right.PointToScreen(new Point(right.Width / 2, 60));
        Check(docks.TargetAt(mid, G("Tools")) is { Kind: DropKind.Insert or DropKind.Tab }, "the middle of a column offers to insert there");

        // toolbars move
        docks.DockNewColumnOrEnd(G("Tool options"), DockSide.Bottom);
        Check(Where("Tool options") == "Bottom" && G("Tool options").Column!.StripOnly && G("Tool options").Column!.Height == 40, "the options bar docks at the bottom, in its own 40 px row");
        Check(docks.TargetAt(right.PointToScreen(new Point(right.Width / 2, 200)), G("Main toolbar")) is not { Kind: DropKind.Insert or DropKind.Tab },
            "a toolbar never goes into a panel column or tab group");
        docks.HidePage(P("Main toolbar"));
        Check(Where("Main toolbar") == "Hidden", "a toolbar can be hidden (Window > Toolbars)");
        docks.ShowPage(P("Main toolbar"));
        Check(Where("Main toolbar") == "Top", "and shown again where it was");

        // fold a side
        docks.FoldSide(DockSide.Left, true); Application.DoEvents();
        Check(docks.IsFolded(DockSide.Left) && docks.Columns(DockSide.Left).All(c => !c.Visible) && !docks.IsShowing(P("Tools")), "a whole side folds away");
        docks.ShowPage(P("Tools"));
        Check(!docks.IsFolded(DockSide.Left) && docks.IsShowing(P("Tools")), "showing a panel on a folded side unfolds it");

        // divider
        var col = G("Library").Column!;
        foreach (var g in col.Items) g.Collapsed = g != G("Library") && g != col.Items[0];
        Application.DoEvents();
        int at = col.Items.ToList().IndexOf(col.Items[0]);
        var a = col.Items[0]; var below = col.Items.Skip(1).First(i => !i.Collapsed);
        int h0 = a.Height, b0 = below.Height;
        col.DragBar(at, 30, a.Weight, below.Weight); Application.DoEvents();
        int grew = a.Height - h0, shrank = b0 - below.Height;
        Check(grew > 0 && Math.Abs(grew - shrank) <= 2, $"dragging a divider grows one group and shrinks the next open one (+{grew} / -{shrank})");
        Shot("custom");

        // layouts
        var custom = docks.Capture();
        form.TestSaveWorkspace("Mine");
        form.TestResetWorkspace(); Application.DoEvents();
        var reset = docks.Capture();
        Check(System.Text.Json.JsonSerializer.Serialize(reset) == System.Text.Json.JsonSerializer.Serialize(defaults)
            && docks.Pages.All(p => ReferenceEquals(p.Content, contents[p.Name]) && !p.Content.IsDisposed), "Reset to default restores the default layout exactly, keeping every control");
        form.TestLoadWorkspace("Mine"); Application.DoEvents();
        var back = docks.Capture();
        static string Shape(WorkspaceLayout l) => string.Join(";", l.Groups.Select(g => $"{g.Where}{g.Column}.{g.Index}:{string.Join(",", g.Pages)}:{g.Collapsed}:{g.Title}"));
        Check(Shape(back) == Shape(custom) && back.Extents["Right"].SequenceEqual(custom.Extents["Right"]), "a saved layout comes back exactly: tabs, columns, folds, names, widths");
        form.TestDeleteWorkspace("Mine"); Check(!Theme.WorkspaceNames.Contains("Mine"), "a saved layout can be deleted");
        foreach (var (name, make) in MainForm.BuiltInLayouts)
        {
            form.TestApplyLayout(make()); Application.DoEvents();
            Check(docks.Pages.All(p => p.Group != null || (name == "Painting" && p.Name == "Main toolbar")) && docks.Groups.All(g => g.Pages.Count > 0),
                $"built-in layout \"{name}\" places every panel");
        }
        var json = System.Text.Json.JsonSerializer.Serialize(custom);
        Check(Shape(System.Text.Json.JsonSerializer.Deserialize<WorkspaceLayout>(json)!) == Shape(custom), "layouts survive being written to and read from the settings file");
        var odd = new WorkspaceLayout();
        odd.Groups.Add(new GroupState { Pages = new[] { "Nope", "Layers", "Main toolbar" }, Where = DockSide.Left, Column = 7 });
        odd.Extents["Right"] = new[] { -5, 99999 };
        form.TestApplyLayout(odd); Application.DoEvents();
        Check(docks.Pages.All(p => p.Group != null) && Where("Layers") == "Left", "a damaged layout keeps what it can and puts the rest where the default does");

        form.TestResetWorkspace();
        form.TestToggleAllPanels();
        Check(docks.Pages.Where(p => !p.IsStrip).All(p => p.Group == null) && docks.Pages.Where(p => p.IsStrip).All(p => p.Group != null), "F4 hides every panel but keeps the toolbars");
        form.TestToggleAllPanels(); Check(docks.Pages.All(p => p.Group != null), "F4 again brings them all back");
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
