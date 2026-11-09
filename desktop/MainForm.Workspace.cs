namespace Flint;

public sealed partial class MainForm
{
    private Panel _workspace = null!;
    private DockManager _docks = null!;
    private PanelPage _timelinePane = null!;
    private ToolStripMenuItem _pixelSnapItem = null!;
    private bool _pixelSnap = Theme.PixelSnap;
    private Panel _canvasBackgroundSwatch = null!;
    private const string BackgroundLayerName = "Canvas background";
    private const string BackgroundObjectName = "Canvas background fill";

    private void BuildWorkspaceMenu(ToolStripMenuItem view)
    {
        _pixelSnapItem = new ToolStripMenuItem("Snap to &pixels") { Checked = _pixelSnap, CheckOnClick = true };
        _pixelSnapItem.Click += (_, _) => { _pixelSnap = _pixelSnapItem.Checked; Theme.PixelSnap = _pixelSnap; };
        view.DropDownItems.Add(_pixelSnapItem);
    }

    /// <summary>Built-in layouts, like Fireworks' Window > Workspace Layouts presets. The first is the default.</summary>
    internal static readonly (string name, Func<WorkspaceLayout> make)[] BuiltInLayouts =
    {
        ("Default", () => MakeLayout(
            new[] { ("Main toolbar", DockSide.Top, 0), ("Tool options", DockSide.Top, 1) },
            G(DockSide.Left, 0, "Tools"),
            G(DockSide.Right, 0, "Layers") with { Weight = 1.3 },
            G(DockSide.Right, 0, "Objects") with { Collapsed = true },
            G(DockSide.Right, 0, "Effects") with { Collapsed = true },
            G(DockSide.Right, 0, "History") with { Collapsed = true },
            G(DockSide.Right, 0, "Library") with { Collapsed = true },
            G(DockSide.Right, 0, "Properties"),
            G(DockSide.Bottom, 0, "Timeline"))),
        ("Compact", () => MakeLayout(
            new[] { ("Main toolbar", DockSide.Top, 0), ("Tool options", DockSide.Top, 1) },
            G(DockSide.Left, 0, "Tools"),
            G(DockSide.Right, 0, "Layers", "Objects", "Effects", "History", "Library") with { Weight = 1.4 },
            G(DockSide.Right, 0, "Properties"),
            G(DockSide.Bottom, 0, "Timeline"))),
        ("Painting", () => MakeLayout(
            new[] { ("Tool options", DockSide.Top, 0) },
            G(DockSide.Left, 0, "Tools"),
            G(DockSide.Right, 0, "Layers", "History") with { Weight = 1.5 },
            G(DockSide.Right, 0, "Effects", "Library") with { Collapsed = true },
            G(DockSide.Right, 0, "Properties", "Objects"),
            G(DockSide.Bottom, 0, "Timeline") with { Collapsed = true },
            G(DockSide.Top, 0, "Main toolbar") with { Where = DockSide.Hidden })),
        ("Animation", () => Sized(MakeLayout(
            new[] { ("Main toolbar", DockSide.Top, 0), ("Tool options", DockSide.Top, 1) },
            G(DockSide.Left, 0, "Tools"),
            G(DockSide.Left, 1, "Layers"),
            G(DockSide.Left, 1, "Objects", "Effects") with { Collapsed = true },
            G(DockSide.Right, 0, "Properties", "History", "Library"),
            G(DockSide.Bottom, 0, "Timeline")), left: new[] { 110, 230 }, bottom: 340)),
    };

    private static WorkspaceLayout Sized(WorkspaceLayout l, int[] left, int bottom) { l.Extents["Left"] = left; l.Extents["Bottom"] = new[] { bottom }; return l; }

    private static GroupState G(DockSide side, int column, params string[] pages) => new() { Pages = pages, Where = side, Column = column, Index = 1000 };

    private static WorkspaceLayout MakeLayout((string page, DockSide side, int row)[] strips, params GroupState[] groups)
    {
        var l = new WorkspaceLayout();
        foreach (var (page, side, row) in strips) l.Groups.Add(new GroupState { Pages = new[] { page }, Where = side, Column = row });
        int i = 0;
        foreach (var g in groups) { g.Index = i++; l.Groups.Add(g); }   // Hidden entries keep a panel closed in this layout
        l.Extents["Left"] = new[] { 110 }; l.Extents["Right"] = new[] { 294 }; l.Extents["Bottom"] = new[] { 260 };
        return l;
    }

    private void BuildDocking(Control tools, List<(string name, Control content, Size min)> inspector, Control timeline, Control mainToolbar, Control optionsBar)
    {
        _docks = new DockManager(this, _workspace, _projectWindow) { Default = BuiltInLayouts[0].make(), Prompt = AskName };
        _docks.AddPage("Main toolbar", mainToolbar, new Size(300, 32), 32);
        _docks.AddPage("Tool options", optionsBar, new Size(300, 40), 40);
        _docks.AddPage("Tools", tools, new Size(60, 200));
        foreach (var (name, content, min) in inspector) _docks.AddPage(name, content, min);
        _timelinePane = _docks.AddPage("Timeline", timeline, new Size(500, 120));
        var start = Program.Headless ? null : Theme.LastWorkspace;
        try { _docks.Apply(start ?? _docks.Default); }
        catch (Exception ex) when (start != null) { Say("Your saved workspace could not be restored, so the default is shown (" + ex.Message + ")"); _docks.Apply(_docks.Default); }
        if (start != null) foreach (var c in _docks.Columns(DockSide.Bottom)) c.UserSized = true;
        FormClosing += (_, e) => { if (!e.Cancel && !Program.Headless && _projectWindow == null) Theme.LastWorkspace = _docks.Capture(); };
        BuildWindowMenu();
    }

    private string? AskName(string current)
    {
        using var dlg = new RenameDialog(current) { Text = "Rename panel group" };
        return dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.Value) ? dlg.Value.Trim() : null;
    }

    /// <summary>Window menu, as in Fireworks: toolbars, every panel, folding sides, and workspace layouts.</summary>
    private void BuildWindowMenu()
    {
        var window = new ToolStripMenuItem("&Window");
        var keys = new Dictionary<string, Keys>
        {
            ["Tools"] = Keys.Control | Keys.F2, ["Properties"] = Keys.Control | Keys.F3, ["Layers"] = Keys.F2,
            ["Timeline"] = Keys.Shift | Keys.F2, ["History"] = Keys.Shift | Keys.F10, ["Library"] = Keys.F11,
        };
        foreach (var (name, key) in keys)
        {
            var p = _docks.FindPage(name);
            if (p != null) _productivityShortcuts.Add(key, () => _docks.TogglePage(p));
        }
        _productivityShortcuts.Add(Keys.F4, ToggleAllPanels);
        window.DropDownOpening += (_, _) =>
        {
            window.DropDownItems.Clear();
            var toolbars = new ToolStripMenuItem("&Toolbars");
            foreach (var p in _docks.Pages.Where(p => p.IsStrip))
            {
                var page = p;
                toolbars.DropDownItems.Add(new ToolStripMenuItem(page.Name, null, (_, _) => _docks.TogglePage(page)) { Checked = _docks.IsShowing(page) });
            }
            window.DropDownItems.Add(toolbars);
            window.DropDownItems.Add(new ToolStripSeparator());
            foreach (var p in _docks.Pages.Where(p => !p.IsStrip))
            {
                var page = p;
                var item = new ToolStripMenuItem(page.Name, null, (_, _) => _docks.TogglePage(page)) { Checked = _docks.IsShowing(page) };
                if (keys.TryGetValue(page.Name, out var k)) item.ShortcutKeyDisplayString = new KeysConverter().ConvertToString(k);
                window.DropDownItems.Add(item);
            }
            window.DropDownItems.Add(new ToolStripSeparator());
            window.DropDownItems.Add(new ToolStripMenuItem("Hide / show all panels", null, (_, _) => ToggleAllPanels()) { ShortcutKeyDisplayString = "F4" });
            foreach (var side in new[] { DockSide.Left, DockSide.Right, DockSide.Bottom })
            {
                var s = side;
                if (_docks.Columns(s).Count == 0) continue;
                window.DropDownItems.Add(new ToolStripMenuItem($"Fold away the {s.ToString().ToLowerInvariant()} side", null, (_, _) => _docks.FoldSide(s, !_docks.IsFolded(s))) { Checked = _docks.IsFolded(s) });
            }
            window.DropDownItems.Add(new ToolStripSeparator());
            var layouts = new ToolStripMenuItem("&Workspace layouts");
            foreach (var (name, make) in BuiltInLayouts)
            {
                var m = make; string n = name;
                layouts.DropDownItems.Add(n, null, (_, _) => ApplyLayout(m(), n));
            }
            if (Theme.WorkspaceNames.Count > 0) layouts.DropDownItems.Add(new ToolStripSeparator());
            foreach (var name in Theme.WorkspaceNames)
            {
                string n = name;
                layouts.DropDownItems.Add(n, null, (_, _) => LoadWorkspace(n));
            }
            layouts.DropDownItems.Add(new ToolStripSeparator());
            layouts.DropDownItems.Add("&Save current layout…", null, (_, _) => SaveWorkspaceAs());
            var del = new ToolStripMenuItem("&Delete layout") { Enabled = Theme.WorkspaceNames.Count > 0 };
            foreach (var name in Theme.WorkspaceNames) { string n = name; del.DropDownItems.Add(n, null, (_, _) => DeleteWorkspace(n)); }
            layouts.DropDownItems.Add(del);
            window.DropDownItems.Add(layouts);
        };
        window.DropDownItems.Add("(panels)");   // filled when opened
        MainMenuStrip!.Items.Add(window);
    }

    private WorkspaceLayout? _beforeHideAll;

    /// <summary>F4: hide every panel (toolbars stay) for a clear view of the canvas; press again to bring them back.</summary>
    private void ToggleAllPanels()
    {
        if (_beforeHideAll is { } back) { _beforeHideAll = null; _docks.Apply(back); return; }
        _beforeHideAll = _docks.Capture();
        foreach (var p in _docks.Pages.Where(p => !p.IsStrip).ToList()) _docks.HidePage(p);
    }

    private void ApplyLayout(WorkspaceLayout l, string name)
    {
        _beforeHideAll = null;
        _docks.Apply(l);
        Say($"workspace layout \"{name}\"");
    }

    private void SaveWorkspaceAs()
    {
        using var dlg = new RenameDialog("My workspace") { Text = "Save workspace layout" };
        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.Value)) return;
        string name = dlg.Value.Trim();
        if (BuiltInLayouts.Any(b => b.name.Equals(name, StringComparison.OrdinalIgnoreCase))) { Say($"\"{name}\" is a built-in layout; pick another name"); return; }
        if (Theme.WorkspaceNames.Contains(name) && !SuppressPrompts
            && MessageBox.Show(this, $"Replace the saved layout \"{name}\"?", "Save workspace layout", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        Theme.SaveWorkspace(name, _docks.Capture());
        Say($"saved workspace layout \"{name}\"");
    }

    private void LoadWorkspace(string name)
    {
        if (Theme.LoadWorkspace(name) is not { } l) { Say($"no layout called \"{name}\""); return; }
        ApplyLayout(l, name);
        foreach (var c in _docks.Columns(DockSide.Bottom)) c.UserSized = true;
    }

    private void DeleteWorkspace(string name)
    {
        if (!SuppressPrompts && MessageBox.Show(this, $"Delete the saved layout \"{name}\"? This cannot be undone.", "Delete workspace layout",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Theme.DeleteWorkspace(name);
        Say($"deleted workspace layout \"{name}\"");
    }

    private void ResetWorkspace()
    {
        ApplyLayout(_docks.Default, "Default");
        RebuildStrip();
    }

    private Layer? CanvasBackgroundLayer => _doc.Layers.FirstOrDefault(l => l.Name == BackgroundLayerName && l.Shared
        && l.Cels.TryGetValue(0, out var c) && c.Objects.Count == 1 && c.Objects[0].Name == BackgroundObjectName);

    private void SyncCanvasBackground()
    {
        if (_canvasBackgroundSwatch == null) return;
        _canvasBackgroundSwatch.BackColor = CanvasBackgroundLayer?.CelAt(0)?.Objects[0] is RasterObject r
            ? r.Pixels.GetPixel(0, 0) : Theme.Panel3;
        _canvasBackgroundSwatch.Invalidate();
    }

    /// <summary>An ordinary shared bottom layer: saved, exported and undone by the existing document model.</summary>
    private void SetCanvasBackground(Color? colour)
    {
        var existing = CanvasBackgroundLayer;
        if (colour == null && existing == null) return;
        var before = Before();
        var active = ActiveLayer;
        int index = existing == null ? -1 : _doc.Layers.IndexOf(existing);
        if (index >= 0)
        {
            _doc.Layers.RemoveAt(index);
        }
        if (colour is { } c)
        {
            _doc.Layers.Insert(0, MakeCanvasBackground(c));
        }
        if (_doc.Layers.Count == 0) _doc.Layers.Add(new Layer { Name = "Layer 1" });
        _activeLayer = Math.Max(0, _doc.Layers.IndexOf(active));
        PushStructure(colour == null ? "clear canvas background" : "canvas background colour", before);
    }

    private Layer MakeCanvasBackground(Color colour)
    {
        var layer = new Layer { Name = BackgroundLayerName, Shared = true, Locked = true };
        var pixels = new byte[checked(DocW * DocH * 4)];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = colour.B; pixels[i + 1] = colour.G; pixels[i + 2] = colour.R; pixels[i + 3] = 255;
        }
        layer.EnsureCel(0).Objects.Add(new RasterObject(ImageOps.FromPixels(DocW, DocH, pixels)) { Name = BackgroundObjectName });
        return layer;
    }

    internal void TestCanvasBackground(Color? c) => SetCanvasBackground(c);
    internal DockManager TestDocks => _docks;
    internal void TestResetWorkspace() => ResetWorkspace();
    internal void TestSaveWorkspace(string name) => Theme.SaveWorkspace(name, _docks.Capture());
    internal void TestLoadWorkspace(string name) => LoadWorkspace(name);
    internal void TestDeleteWorkspace(string name) => DeleteWorkspace(name);
    internal void TestToggleAllPanels() => ToggleAllPanels();
    internal void TestApplyLayout(WorkspaceLayout l) => _docks.Apply(l);
    internal bool TestPixelSnap { get => _pixelSnap; set { _pixelSnap = value; _pixelSnapItem.Checked = value; } }
    internal (double x, double y) TestTranslation => (_xf!.Tx, _xf.Ty);
    internal double TestTransformRight => _xf!.Corners().Max(p => p.X);
}
