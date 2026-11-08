namespace Flint;

public sealed partial class MainForm
{
    private Panel _workspace = null!;
    private readonly List<WorkspacePanel> _workspacePanels = new();
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
        var workspace = new ToolStripMenuItem("&Workspace");
        workspace.DropDownOpening += (_, _) =>
        {
            workspace.DropDownItems.Clear();
            foreach (var p in _workspacePanels)
            {
                var panel = p;
                var item = new ToolStripMenuItem(panel.Name);
                item.DropDownItems.Add("Float panel", null, (_, _) => panel.Float());
                if (panel == _timelinePane) item.DropDownItems.Add("Dock bottom", null, (_, _) => panel.DockTo(DockStyle.Bottom));
                else
                {
                    item.DropDownItems.Add("Dock left", null, (_, _) => panel.DockTo(DockStyle.Left));
                    item.DropDownItems.Add("Dock right", null, (_, _) => panel.DockTo(DockStyle.Right));
                }
                workspace.DropDownItems.Add(item);
            }
            workspace.DropDownItems.Add(new ToolStripSeparator());
            workspace.DropDownItems.Add("Reset workspace", null, (_, _) => ResetWorkspace());
        };
        view.DropDownItems.Add(workspace);
    }

    private void ResetWorkspace()
    {
        _workspace.SuspendLayout();
        foreach (var panel in _workspacePanels) panel.ResetPanel();
        _workspace.ResumeLayout(true);
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
    internal IReadOnlyList<WorkspacePanel> TestWorkspacePanels => _workspacePanels;
    internal void TestResetWorkspace() => ResetWorkspace();
    internal bool TestPixelSnap { get => _pixelSnap; set { _pixelSnap = value; _pixelSnapItem.Checked = value; } }
    internal (double x, double y) TestTranslation => (_xf!.Tx, _xf.Ty);
    internal double TestTransformRight => _xf!.Corners().Max(p => p.X);
}
