namespace Flint;

public sealed partial class MainForm
{
    private Panel _workspace = null!;
    private DockManager _docks = null!;
    private WorkspacePanel _timelinePane = null!;
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

    /// <summary>Default arrangement: tools on the left, layers and properties on the right (the rest folded up), timeline at the bottom.</summary>
    private static WorkspaceLayout DefaultLayout()
    {
        var l = new WorkspaceLayout { LeftWidth = 100, RightWidth = 294, BottomHeight = 260 };
        void P(string name, DockSide side, int index, double weight = 1, bool folded = false) =>
            l.Panels[name] = new PanelState { Where = side, Index = index, Weight = weight, Collapsed = folded };
        P("Tools", DockSide.Left, 0);
        P("Layers", DockSide.Right, 1, 1.3);
        P("Objects", DockSide.Right, 2, 1, true);
        P("Effects", DockSide.Right, 3, 1, true);
        P("History", DockSide.Right, 4, 1, true);
        P("Library", DockSide.Right, 5, 1, true);
        P("Properties", DockSide.Right, 6, 1);
        P("Timeline", DockSide.Bottom, 0);
        return l;
    }

    private void BuildDocking(Control tools, List<(string name, Control content, Size min)> inspector, Control timeline)
    {
        _docks = new DockManager(this, _workspace) { Default = DefaultLayout() };
        _docks.Add("Tools", tools, new Size(60, 200));
        foreach (var (name, content, min) in inspector) _docks.Add(name, content, min);
        _timelinePane = _docks.Add("Timeline", timeline, new Size(500, 120));
        var start = Program.Headless ? null : Theme.LastWorkspace;
        _docks.Apply(start ?? _docks.Default);
        if (start != null) _docks.Column(DockSide.Bottom).UserSized = true;
        FormClosing += (_, e) => { if (!e.Cancel && !Program.Headless) Theme.LastWorkspace = _docks.Capture(); };
        BuildWindowMenu();
    }

    /// <summary>Window menu, as in Fireworks: show or hide each panel, and save or switch workspace layouts.</summary>
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
            var p = _docks.Find(name);
            if (p != null) _productivityShortcuts.Add(key, () => _docks.Toggle(p));
        }
        _productivityShortcuts.Add(Keys.F4, ToggleAllPanels);
        window.DropDownOpening += (_, _) =>
        {
            window.DropDownItems.Clear();
            foreach (var p in _docks.Panels)
            {
                var panel = p;
                var item = new ToolStripMenuItem(panel.Name, null, (_, _) => _docks.Toggle(panel)) { Checked = !panel.IsHidden };
                if (keys.TryGetValue(panel.Name, out var k)) item.ShortcutKeyDisplayString = new KeysConverter().ConvertToString(k);
                window.DropDownItems.Add(item);
            }
            window.DropDownItems.Add(new ToolStripSeparator());
            window.DropDownItems.Add(new ToolStripMenuItem("Hide / show all panels", null, (_, _) => ToggleAllPanels()) { ShortcutKeyDisplayString = "F4" });
            window.DropDownItems.Add(new ToolStripSeparator());
            var layouts = new ToolStripMenuItem("&Workspace layouts");
            foreach (var name in Theme.WorkspaceNames)
            {
                string n = name;
                layouts.DropDownItems.Add(n, null, (_, _) => LoadWorkspace(n));
            }
            if (layouts.DropDownItems.Count > 0) layouts.DropDownItems.Add(new ToolStripSeparator());
            layouts.DropDownItems.Add("&Save current layout…", null, (_, _) => SaveWorkspaceAs());
            var del = new ToolStripMenuItem("&Delete layout") { Enabled = Theme.WorkspaceNames.Count > 0 };
            foreach (var name in Theme.WorkspaceNames) { string n = name; del.DropDownItems.Add(n, null, (_, _) => DeleteWorkspace(n)); }
            layouts.DropDownItems.Add(del);
            layouts.DropDownItems.Add("&Reset to default", null, (_, _) => ResetWorkspace());
            window.DropDownItems.Add(layouts);
        };
        window.DropDownItems.Add("(panels)");   // filled when opened
        MainMenuStrip!.Items.Add(window);
    }

    private WorkspaceLayout? _beforeHideAll;

    /// <summary>F4: hide every panel for a clear view of the canvas; press again to bring them back.</summary>
    private void ToggleAllPanels()
    {
        if (_beforeHideAll is { } back) { _beforeHideAll = null; _docks.Apply(back); return; }
        _beforeHideAll = _docks.Capture();
        foreach (var p in _docks.Panels.ToList()) _docks.Hide(p);
    }

    private void SaveWorkspaceAs()
    {
        using var dlg = new RenameDialog("My workspace") { Text = "Save workspace layout" };
        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.Value)) return;
        string name = dlg.Value.Trim();
        if (Theme.WorkspaceNames.Contains(name) && !SuppressPrompts
            && MessageBox.Show(this, $"Replace the saved layout \"{name}\"?", "Save workspace layout", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        Theme.SaveWorkspace(name, _docks.Capture());
        Say($"saved workspace layout \"{name}\"");
    }

    private void LoadWorkspace(string name)
    {
        if (Theme.LoadWorkspace(name) is not { } l) { Say($"no layout called \"{name}\""); return; }
        _beforeHideAll = null;
        _docks.Apply(l);
        _docks.Column(DockSide.Bottom).UserSized = true;
        Say($"workspace layout \"{name}\"");
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
        _beforeHideAll = null;
        _docks.Apply(_docks.Default);
        _docks.Column(DockSide.Bottom).UserSized = false;
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
            var layer = new Layer { Name = BackgroundLayerName, Shared = true, Locked = true };
            var pixels = new byte[checked(DocW * DocH * 4)];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R; pixels[i + 3] = 255; }
            layer.EnsureCel(0).Objects.Add(new RasterObject(ImageOps.FromPixels(DocW, DocH, pixels)) { Name = BackgroundObjectName });
            _doc.Layers.Insert(0, layer);
        }
        if (_doc.Layers.Count == 0) _doc.Layers.Add(new Layer { Name = "Layer 1" });
        _activeLayer = Math.Max(0, _doc.Layers.IndexOf(active));
        PushStructure(colour == null ? "clear canvas background" : "canvas background colour", before);
    }

    internal void TestCanvasBackground(Color? c) => SetCanvasBackground(c);
    internal DockManager TestDocks => _docks;
    internal void TestResetWorkspace() => ResetWorkspace();
    internal void TestSaveWorkspace(string name) => Theme.SaveWorkspace(name, _docks.Capture());
    internal void TestLoadWorkspace(string name) => LoadWorkspace(name);
    internal void TestDeleteWorkspace(string name) => DeleteWorkspace(name);
    internal void TestToggleAllPanels() => ToggleAllPanels();
    internal bool TestPixelSnap { get => _pixelSnap; set { _pixelSnap = value; _pixelSnapItem.Checked = value; } }
    internal (double x, double y) TestTranslation => (_xf!.Tx, _xf.Ty);
    internal double TestTransformRight => _xf!.Corners().Max(p => p.X);
}
