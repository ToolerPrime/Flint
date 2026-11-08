using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Flint;

/// <summary>A composited frame handed to the exporters.</summary>
public sealed class Frame
{
    public Bitmap Bmp;
    public int Delay;
    public Frame(Bitmap bmp, int delay) { Bmp = bmp; Delay = delay; }
}

/// <summary>Panel that paints without flicker.</summary>
public sealed class CanvasPanel : Panel
{
    public CanvasPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoScroll = true;
    }
}

public sealed partial class MainForm : ThemedForm
{
    // ---------- document ----------
    private Document _doc = new(64, 64);
    private int _cur;                     // current frame
    private int _activeLayer;
    private readonly UndoStack _undo = new();
    private string? _path;
    private bool _dirty;

    // Composite caches. The frame is re-composited only when something
    // changes; during a stroke the layers below and above the active one are
    // held still so each mouse move only redraws one layer.
    private Bitmap? _composite;
    private bool _compositeValid;
    private Bitmap? _below, _above;

    private int _zoom = 4;
    private readonly AudioEngine _engine = new();
    private readonly Playback _playback = new();
    private readonly OnionSettings _onionSet = new();
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private int _rangeFrom, _rangeTo;
    private double _rangeStartMs;
    private bool _playAudio;
    private int _selStart, _selEnd;        // frame selection in the timeline
    private int _countdown;                // > 0 while counting in to a recording
    private double _recStartMs;
    private AudioProject? _audioBefore;
    private List<(FrameSpec spec, Dictionary<int, Cel> cels)>? _frameClipboard;
    private readonly System.Windows.Forms.Timer _play = new();
    private readonly System.Windows.Forms.Timer _autosave = new() { Interval = 60_000 };
    private DateTime? _lastAutosave;

    // ---------- tools ----------
    private Tool _tool = Tool.Pencil;
    private Color _fg = Color.Black, _bg = Color.White;
    private Selection? _selection;
    private Rectangle? _hitDraft;
    private bool _dragging;
    private Point _dragStart, _dragLast;
    private byte[]? _strokeBase;
    private PaintRecorder? _recorder;
    private readonly Dictionary<Tool, Button> _toolButtons = new();

    // ---------- controls ----------
    private CanvasPanel _canvas = null!;
    private TimelinePanel _timeline = null!;
    private Panel _bottom = null!;
    private LayersPanel _layers = null!;
    private ListBox _history = null!;
    private bool _historyBusy;
    private ToolStripStatusLabel _status = null!, _info = null!, _mem = null!;
    private Button _playBtn = null!;
    private Panel _fgSwatch = null!, _bgSwatch = null!, _keySwatch = null!;
    private NumericUpDown _delay = null!, _tol = null!, _scaleBy = null!,
                          _rw = null!, _rh = null!,
                          _colors = null!, _sheetCols = null!, _brush = null!,
                          _fillTol = null!, _webmQuality = null!, _layerOpacity = null!;
    private ComboBox _blend = null!, _modeBox = null!, _speedBox = null!, _micBox = null!;
    private NumericUpDown _onionBefore = null!, _onionAfter = null!, _onionOpacity = null!;
    private Button _recBtn = null!;
    private ProgressBar _level = null!;
    private CheckBox _applyAll = null!, _smooth = null!, _center = null!,
                     _dither = null!, _loop = null!, _onionBox = null!,
                     _onionTint = null!, _includeAudio = null!, _rangeOnly = null!,
                     _filled = null!, _sharedBox = null!;
    private Color _key = Color.Magenta;
    private bool _syncing;
    private bool _splitPlaced;   // once the user drags the divider, leave it there

    private int DocW => _doc.Width;
    private int DocH => _doc.Height;

    public MainForm()
    {
        Text = "Flint";
        MinimumSize = new Size(1120, 700);
        Size = new Size(1380, 860);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        KeyPreview = true;

        BuildUi();

        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                                          || e.Data?.GetDataPresent(LibraryPanel.DragFormat) == true
            ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => HandleDrop(e);
        KeyDown += OnKey;
        FormClosing += (_, e) =>
        {
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
            StopPlay();
            _engine.Dispose();
            Recovery.Clear();   // a clean exit leaves nothing to recover
        };
        _play.Tick += (_, _) => Advance();
        _autosave.Tick += (_, _) => Autosave();
        if (!Program.Headless)
        {
            _autosave.Start();
            Shown += (_, _) => OfferRecovery();
        }
        RefreshAll();
    }

    #region UI construction

    private static Label Lbl(string text, int x, int y, int w = 58) => new()
    {
        Text = text, Left = x, Top = y + 4, Width = w, AutoSize = false,
    };

    private static Button Btn(string text, int x, int y, int w, EventHandler onClick)
    {
        var b = new Button { Text = text, Left = x, Top = y, Width = w, Height = 26 };
        b.Click += onClick;
        return b;
    }

    private static NumericUpDown Num(int x, int y, int w, decimal val, decimal min, decimal max,
                                     int decimals = 0, decimal step = 1) => new()
    {
        Left = x, Top = y, Width = w, Minimum = min, Maximum = max, Value = val,
        DecimalPlaces = decimals, Increment = step, TextAlign = HorizontalAlignment.Right,
    };

    private static void Add(ToolStripMenuItem menu, string text, Keys keys, Action action) =>
        ((ToolStripMenuItem)menu.DropDownItems.Add(text, null, (_, _) => action())).ShortcutKeys = keys;

    private void BuildUi()
    {
        // ---------- menu ----------
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&File");
        Add(file, "&New canvas…", Keys.Control | Keys.N, NewCanvas);
        Add(file, "&Open…", Keys.Control | Keys.O, OpenProject);
        file.DropDownItems.Add(new ToolStripSeparator());
        Add(file, "&Save", Keys.Control | Keys.S, () => SaveProject(false));
        Add(file, "Save &as…", Keys.Control | Keys.Shift | Keys.S, () => SaveProject(true));
        file.DropDownItems.Add(new ToolStripSeparator());
        Add(file, "&Import images…", Keys.Control | Keys.I, ChooseFiles);
        Add(file, "&Export…", Keys.Control | Keys.E, ExportAny);
        Add(file, "Export previe&w…", Keys.Control | Keys.Shift | Keys.E, ExportPreview);
        file.DropDownItems.Add("Export PNG se&quence…", null, (_, _) => ExportSequence());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("E&xit", null, (_, _) => Close());

        var edit = new ToolStripMenuItem("&Edit");
        Add(edit, "&Undo", Keys.Control | Keys.Z, Undo);
        Add(edit, "&Redo", Keys.Control | Keys.Y, Redo);
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add("C&lear selected pixels", null, (_, _) => ClearSelection());

        var layerMenu = new ToolStripMenuItem("&Layer");
        layerMenu.DropDownItems.Add("&Add layer", null, (_, _) => AddLayer());
        layerMenu.DropDownItems.Add("D&uplicate layer", null, (_, _) => DuplicateLayer());
        layerMenu.DropDownItems.Add("De&lete layer", null, (_, _) => DeleteLayer());
        layerMenu.DropDownItems.Add(new ToolStripSeparator());
        layerMenu.DropDownItems.Add("Move &up", null, (_, _) => MoveLayer(1));
        layerMenu.DropDownItems.Add("Move &down", null, (_, _) => MoveLayer(-1));
        layerMenu.DropDownItems.Add(new ToolStripSeparator());
        layerMenu.DropDownItems.Add("&Merge down", null, (_, _) => MergeDown());
        layerMenu.DropDownItems.Add("&Flatten to one layer", null, (_, _) => Flatten());

        var frameMenu = new ToolStripMenuItem("F&rame");
        frameMenu.DropDownItems.Add("&Add blank frame", null, (_, _) => AddBlankFrame());
        frameMenu.DropDownItems.Add("D&uplicate frame", null, (_, _) => DuplicateFrame());
        frameMenu.DropDownItems.Add("De&lete frame", null, (_, _) => DeleteFrame());
        frameMenu.DropDownItems.Add(new ToolStripSeparator());
        frameMenu.DropDownItems.Add("&Reverse order", null, (_, _) => ReverseOrder());
        frameMenu.DropDownItems.Add(new ToolStripSeparator());
        Add(frameMenu, "&Copy frames", Keys.Control | Keys.Shift | Keys.C, CopyFrames);
        Add(frameMenu, "&Paste frames", Keys.Control | Keys.Shift | Keys.V, PasteFrames);
        frameMenu.DropDownItems.Add("&Tag selection…", null, (_, _) => TagSelection());
        frameMenu.DropDownItems.Add("&Name frame…", null, (_, _) => RenameFrame(_cur));

        var audioMenu = new ToolStripMenuItem("&Audio");
        audioMenu.DropDownItems.Add("&Import audio…", null, (_, _) => ChooseAudio());
        audioMenu.DropDownItems.Add("Add &track", null, (_, _) => AddTrack());
        audioMenu.DropDownItems.Add("&Record from microphone", null, (_, _) => ToggleRecord());

        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(layerMenu);
        menu.Items.Add(frameMenu);
        menu.Items.Add(audioMenu);
        MainMenuStrip = menu;
        BuildEditMenus(menu, edit, layerMenu);

        var tools = BuildToolStrip();
        var statusBar = new StatusStrip();
        _status = new ToolStripStatusLabel("New canvas, or drop images here to begin")
        { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _info = new ToolStripStatusLabel("no document");
        _mem = new ToolStripStatusLabel("");
        statusBar.Items.Add(_status);
        statusBar.Items.Add(_info);
        statusBar.Items.Add(_mem);

        // ---------- canvas ----------
        _canvas = new CanvasPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(60, 62, 68) };
        _canvas.Paint += PaintCanvas;
        _canvas.MouseDown += CanvasDown;
        _canvas.MouseMove += CanvasMove;
        _canvas.MouseUp += CanvasUp;
        _canvas.MouseWheel += CanvasWheel;
        _canvas.MouseDoubleClick += (_, e) =>
        {
            if (_tool == Tool.ObjectSelect && PickObject(ToImage(e.X, e.Y))) EditObjectProperties();
        };

        // ---------- toolbox ----------
        var box = BuildToolbox();
        var optionsBar = BuildOptionsBar();
        var side = BuildSidePanel();
        var bottom = _bottom = BuildTransport();

        _workspace = new Panel { Dock = DockStyle.Fill };
        _workspace.Controls.Add(_canvas);
        _workspacePanels.Add(new WorkspacePanel(this, _workspace, "Tools", box, DockStyle.Left, new Size(100, 500)));
        _workspacePanels.Add(new WorkspacePanel(this, _workspace, "Inspector", side, DockStyle.Right, new Size(294, 500)));
        _timelinePane = new WorkspacePanel(this, _workspace, "Timeline", bottom, DockStyle.Bottom, new Size(1100, 260));
        _workspacePanels.Add(_timelinePane);
        Controls.Add(_workspace);
        Controls.Add(optionsBar);
        Controls.Add(tools);
        Controls.Add(menu);
        Controls.Add(statusBar);

        SetTool(Tool.Pencil);
    }

    private Panel Swatch(int x, int y, Color initial, Func<Color> get, Action<Color> set)
    {
        var p = new Panel
        {
            Left = x, Top = y, Width = 34, Height = 30, BackColor = initial,
            BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand, Tag = "keep",
        };
        p.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = get(), FullOpen = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            set(dlg.Color);
            p.BackColor = dlg.Color;
        };
        return p;
    }

    private void SetTool(Tool t)
    {
        if (t != _tool && _xf != null) CommitTransform();
        if (t != Tool.Crop) _cropRect = null;
        _tool = t;
        StyleToolButtons();
        UpdateOptionsBar();
        _canvas.Cursor = t switch
        {
            Tool.Pan => Cursors.Hand,
            Tool.Eyedropper or Tool.Marquee or Tool.Pivot or Tool.Hitbox or Tool.EllipseSelect
                or Tool.Lasso or Tool.Wand or Tool.Crop or Tool.Clone => Cursors.Cross,
            Tool.Move or Tool.ObjectSelect => Cursors.SizeAll,
            Tool.Text => Cursors.IBeam,
            _ => Cursors.Default,
        };
        if (t == Tool.Transform && _xf == null) BeginTransform("transform");
        _canvas.Invalidate();
        Say(t switch
        {
            Tool.Pencil => "Pencil: left draws foreground, right draws background",
            Tool.Eraser => "Eraser: clears to transparent",
            Tool.Fill => "Fill: click a region",
            Tool.Line => "Line: drag",
            Tool.Rectangle => "Rectangle: drag; tick filled for solid",
            Tool.Ellipse => "Oval: drag; tick filled for solid",
            Tool.Marquee => "Select: drag a box. Edits stay inside it.",
            Tool.Eyedropper => "Eyedropper: click to take a colour",
            Tool.Pan => "Pan: drag to scroll",
            Tool.Pivot => "Pivot: click to set the origin point of the selected frames",
            Tool.Hitbox => "Hitbox: drag a box on this frame. Delete clears this frame's hitboxes.",
            Tool.ObjectSelect => "Object: pick or drag one object. Double-click or Objects > Edit changes its properties.",
            Tool.Move => "Move: drag the layer, or the selected pixels. Arrow keys nudge (Shift = 10).",
            Tool.Transform => "Transform: drag inside to move, handles to scale (Shift keeps shape), outside to rotate. Enter applies, Esc cancels.",
            Tool.Crop => "Crop: drag the area to keep, then press Enter",
            Tool.EllipseSelect => "Ellipse select: drag. Shift adds, Alt subtracts, both intersect.",
            Tool.Lasso => "Lasso: draw round the area. Shift adds, Alt subtracts.",
            Tool.Wand => "Magic wand: click a colour. Tolerance is 'Fill tol'; untick contiguous to pick every match.",
            Tool.Gradient => "Gradient: drag from start to end. Type is under Options.",
            Tool.Clone => "Clone: Alt+click the spot to copy from, then paint",
            Tool.Text => "Text: click where the text should start",
            _ => "",
        });
    }

    #endregion

    #region compositing

    private Layer ActiveLayer => _doc.Layers[Math.Clamp(_activeLayer, 0, _doc.Layers.Count - 1)];

    private void InvalidateComposite()
    {
        _compositeValid = false;
        _canvas.Invalidate();
    }

    private Bitmap Composite()
    {
        if (_compositeValid && _composite != null) return _composite;
        _composite?.Dispose();
        _composite = _doc.RenderFrame(_cur);
        _compositeValid = true;
        return _composite;
    }

    /// <summary>
    /// Build the unchanging halves once so a stroke only re-draws the active
    /// layer instead of the whole stack on every mouse move.
    /// </summary>
    private void BeginStrokeCaches()
    {
        _below?.Dispose();
        _above?.Dispose();
        _below = ImageOps.NewBitmap(DocW, DocH);
        _above = ImageOps.NewBitmap(DocW, DocH);
        using (var g = Graphics.FromImage(_below))
            for (int i = 0; i < _activeLayer; i++) DrawLayer(g, i);
        using (var g = Graphics.FromImage(_above))
            for (int i = _activeLayer + 1; i < _doc.Layers.Count; i++) DrawLayer(g, i);
    }

    private void DrawLayer(Graphics g, int index)
    {
        var l = _doc.Layers[index];
        if (!l.Visible) return;
        var cel = l.CelAt(_cur);
        if (cel == null) return;
        foreach (var o in cel.Objects) o.Draw(g, l.Opacity);
    }

    private void EndStrokeCaches()
    {
        _below?.Dispose(); _below = null;
        _above?.Dispose(); _above = null;
        InvalidateComposite();
    }

    /// <summary>During a stroke: cached below + live active layer + cached above.</summary>
    private void RecomposeDuringStroke()
    {
        if (_below == null || _above == null || NeedsExactComposite()) { InvalidateComposite(); return; }
        _composite?.Dispose();
        _composite = ImageOps.Clone(_below);
        using (var g = Graphics.FromImage(_composite))
        {
            DrawLayer(g, _activeLayer);
            g.DrawImageUnscaled(_above, 0, 0);
        }
        _compositeValid = true;
        _canvas.Invalidate();
    }

    #endregion

    #region canvas painting

    private Point CanvasOrigin()
    {
        int w = DocW * _zoom, h = DocH * _zoom;
        return new Point(
            Math.Max(RulerPad, (_canvas.ClientSize.Width - w) / 2) + _canvas.AutoScrollPosition.X,
            Math.Max(RulerPad, (_canvas.ClientSize.Height - h) / 2) + _canvas.AutoScrollPosition.Y);
    }

    private Point ToImage(int sx, int sy)
    {
        var o = CanvasOrigin();
        return new Point(
            (int)Math.Floor((sx - o.X) / (double)_zoom),
            (int)Math.Floor((sy - o.Y) / (double)_zoom));
    }

    private void PaintCanvas(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        int w = DocW * _zoom, h = DocH * _zoom;
        var o = CanvasOrigin();
        DrawChecker(g, new Rectangle(o.X, o.Y, w, h));

        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        Onion.Draw(g, _doc, _onionSet, _cur, new Rectangle(o.X, o.Y, w, h));

        g.DrawImage(Composite(), new Rectangle(o.X, o.Y, w, h));

        using (var pen = new Pen(Color.FromArgb(96, 100, 110)))
            g.DrawRectangle(pen, o.X - 1, o.Y - 1, w + 1, h + 1);

        if (_zoom >= 8)
        {
            using var grid = new Pen(Color.FromArgb(40, 255, 255, 255));
            for (int x = 1; x < DocW; x++)
                g.DrawLine(grid, o.X + x * _zoom, o.Y, o.X + x * _zoom, o.Y + h);
            for (int y = 1; y < DocH; y++)
                g.DrawLine(grid, o.X, o.Y + y * _zoom, o.X + w, o.Y + y * _zoom);
        }

        PaintGuides(g, o);

        PaintEditOverlay(g, o);
        PaintObjectOutline(g, o);
        PaintRulersAndGuides(g, o);
    }

    private static void DrawChecker(Graphics g, Rectangle r)
    {
        using var light = new SolidBrush(Color.FromArgb(80, 82, 90));
        using var dark = new SolidBrush(Color.FromArgb(66, 68, 76));
        g.FillRectangle(light, r);
        const int s = 8;
        for (int y = 0; y * s < r.Height; y++)
            for (int x = 0; x * s < r.Width; x++)
            {
                if (((x + y) & 1) == 0) continue;
                g.FillRectangle(dark, new Rectangle(
                    r.X + x * s, r.Y + y * s,
                    Math.Min(s, r.Width - x * s), Math.Min(s, r.Height - y * s)));
            }
    }

    #endregion

    #region tools

    private RasterObject ActiveRaster() => EditingMask ? ActiveCelOrNull!.Mask! : ActiveLayer.EnsureCel(_cur).EnsureRaster(DocW, DocH);

    private int ActiveFrameKey => ActiveLayer.Shared ? 0 : _cur;

    private void CanvasDown(object? sender, MouseEventArgs e)
    {
        if (GuideDown(e)) return;
        var p = ToImageSnapped(e);
        _dragStart = _dragLast = p;
        if (EditDown(e, p)) return;

        if (_tool == Tool.Pan) { _dragging = true; return; }

        if (_tool == Tool.Eyedropper)
        {
            if (!InDoc(p)) { Say("picked outside the canvas"); return; }
            var c = Composite().GetPixel(p.X, p.Y);
            if (e.Button == MouseButtons.Right) { _bg = c; _bgSwatch.BackColor = c; }
            else
            {
                _fg = c; _fgSwatch.BackColor = c;
                _key = Color.FromArgb(255, c); _keySwatch.BackColor = _key;
            }
            Say($"picked #{c.R:X2}{c.G:X2}{c.B:X2} (alpha {c.A})");
            return;
        }

        if (_tool == Tool.Marquee) { _dragging = true; _selection = null; _canvas.Invalidate(); return; }

        if (_tool == Tool.Pivot)
        {
            if (!InDoc(p)) return;
            SetPivot(p);
            return;
        }
        if (_tool == Tool.Hitbox) { _dragging = true; _hitDraft = null; return; }

        if (ActiveLayer.Locked) { Say($"layer '{ActiveLayer.Name}' is locked"); return; }

        EnsurePaintable();
        var raster = ActiveRaster();
        _recorder = new PaintRecorder(_doc, _activeLayer, ActiveFrameKey, raster);
        _strokeBase = ImageOps.GetPixels(raster.Pixels);
        _dragging = true;
        BeginStrokeCaches();

        uint colour = InkFor(e.Button);
        var surface = new Surface((byte[])_strokeBase.Clone(), DocW, DocH);

        switch (_tool)
        {
            case Tool.Pencil:
            case Tool.Eraser:
                Draw.Dot(surface, p.X, p.Y, (int)_brush.Value, colour, _selection);
                Commit(surface, raster);
                break;
            case Tool.Fill:
                int painted = Draw.Fill(surface, p.X, p.Y, colour, (int)_fillTol.Value, _selection);
                Commit(surface, raster);
                Say($"filled {painted} pixels");
                _dragging = false;
                FinishStroke("fill");
                break;
        }
    }

    private void CanvasMove(object? sender, MouseEventArgs e)
    {
        if (GuideMove(e)) return;
        var p = ToImageSnapped(e);
        if (InDoc(p)) UpdateInfo(p);
        if (EditMove(e, p)) return;

        if (!_dragging) return;

        if (_tool == Tool.Pan)
        {
            var pos = _canvas.AutoScrollPosition;
            _canvas.AutoScrollPosition = new Point(
                -pos.X - (e.X - _dragLast.X * _zoom) / 4, -pos.Y);
            return;
        }

        if (_tool == Tool.Hitbox)
        {
            _hitDraft = Draw.FromPoints(Clamp(_dragStart), Clamp(p));
            _canvas.Invalidate();
            return;
        }

        if (_strokeBase == null) return;
        var raster = ActiveRaster();
        uint colour = InkFor(e.Button);

        if (_tool is Tool.Pencil or Tool.Eraser)
        {
            var surface = new Surface(ImageOps.GetPixels(raster.Pixels), DocW, DocH);
            Draw.Line(surface, _dragLast.X, _dragLast.Y, p.X, p.Y, (int)_brush.Value, colour, _selection);
            Commit(surface, raster);
            _dragLast = p;
            return;
        }

        var preview = new Surface((byte[])_strokeBase.Clone(), DocW, DocH);
        DrawShape(preview, _dragStart, p, colour);
        Commit(preview, raster);
    }

    private void CanvasUp(object? sender, MouseEventArgs e)
    {
        if (GuideUp(e)) return;
        var up = ToImageSnapped(e);
        if (EditUp(e, up)) return;
        if (!_dragging) return;
        _dragging = false;
        var p = up;

        if (_tool == Tool.Pan) return;

        if (_tool == Tool.Hitbox)
        {
            if (_hitDraft is { Width: > 1, Height: > 1 } r) AddHitbox(r);
            _hitDraft = null;
            _canvas.Invalidate();
            return;
        }

        if (_strokeBase != null && _tool is Tool.Line or Tool.Rectangle or Tool.Ellipse)
        {
            var raster = ActiveRaster();
            var final = new Surface((byte[])_strokeBase.Clone(), DocW, DocH);
            DrawShape(final, _dragStart, p, InkFor(e.Button));
            Commit(final, raster);
        }

        FinishStroke(_tool.ToString().ToLowerInvariant());
    }

    /// <summary>
    /// Ctrl+wheel zooms by whole steps, keeping the pixel under the cursor in
    /// place; a plain wheel scrolls as usual.
    /// </summary>
    private void CanvasWheel(object? sender, MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) == 0) return;
        var before = ToImage(e.X, e.Y);
        int z = Math.Clamp(_zoom + (e.Delta > 0 ? 1 : -1), 1, 32);
        if (z == _zoom) return;
        _zoom = z;
        RefreshAll();
        // Scroll so the same image pixel stays under the mouse.
        var after = ToImage(e.X, e.Y);
        var pos = _canvas.AutoScrollPosition;
        _canvas.AutoScrollPosition = new Point(
            -pos.X + (before.X - after.X) * _zoom,
            -pos.Y + (before.Y - after.Y) * _zoom);
        if (e is HandledMouseEventArgs h) h.Handled = true;
        UpdateInfo(before);
    }

    private void FinishStroke(string label)
    {
        var edit = _recorder?.Finish(label);
        if (edit != null) { _undo.Push(edit); Touch(); }
        _recorder = null;
        _strokeBase = null;
        EndStrokeCaches();
        RebuildStrip();
        UpdateInfo(null);
    }

    private void DrawShape(Surface s, Point a, Point b, uint colour)
    {
        int size = (int)_brush.Value;
        switch (_tool)
        {
            case Tool.Line: Draw.Line(s, a.X, a.Y, b.X, b.Y, size, colour, _selection); break;
            case Tool.Rectangle: Draw.Rect(s, Draw.FromPoints(a, b), size, colour, _filled.Checked, _selection); break;
            case Tool.Ellipse: Draw.Ellipse(s, Draw.FromPoints(a, b), size, colour, _filled.Checked, _selection); break;
        }
    }

    private uint InkFor(MouseButtons button) => EditingMask
        ? (_tool == Tool.Eraser ? 0xFF000000 : GreyInk(button == MouseButtons.Right ? _bg : _fg))
        : _tool == Tool.Eraser ? 0u
        : Surface.Pack(button == MouseButtons.Right ? _bg : _fg);

    private void Commit(Surface s, RasterObject raster)
    {
        ImageOps.SetPixels(raster.Pixels, s.Px);
        RecomposeDuringStroke();
    }

    private bool InDoc(Point p) => p.X >= 0 && p.Y >= 0 && p.X < DocW && p.Y < DocH;

    private Point Clamp(Point p) => new(
        Math.Clamp(p.X, 0, Math.Max(0, DocW - 1)),
        Math.Clamp(p.Y, 0, Math.Max(0, DocH - 1)));

    private void SelectAll()
    {
        if (_tool == Tool.ObjectSelect || _objects.Focused) { SelectAllObjects(); return; }
        SetSelection(Selection.FromRect(DocW, DocH, new Rectangle(0, 0, DocW, DocH)), $"selected all {DocW}×{DocH}");
    }

    private void ClearSelection()
    {
        if (ActiveLayer.Locked) { Say($"layer '{ActiveLayer.Name}' is locked"); return; }
        EnsurePaintable();
        var area = _selection?.Bounds ?? new Rectangle(0, 0, DocW, DocH);
        var raster = ActiveRaster();
        var rec = new PaintRecorder(_doc, _activeLayer, ActiveFrameKey, raster);
        var s = new Surface(ImageOps.GetPixels(raster.Pixels), DocW, DocH);
        uint clearInk = EditingMask ? 0xFF000000 : 0;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
                s.Set(x, y, clearInk, _selection);
        ImageOps.SetPixels(raster.Pixels, s.Px);
        var edit = rec.Finish("clear");
        if (edit != null) { _undo.Push(edit); Touch(); }
        InvalidateComposite();
        RefreshAll();
        Say($"cleared {area.Width}×{area.Height}");
    }

    #endregion

    #region history

    /// <summary>
    /// Row 0 is the starting state; then every step that can be undone, then
    /// greyed steps that can be redone. The selected row is where you are.
    /// </summary>
    private void RefreshHistory()
    {
        if (_history == null) return;
        _historyBusy = true;
        try
        {
            _history.BeginUpdate();
            _history.Items.Clear();
            _history.Items.Add("(start)");
            foreach (var l in _undo.Labels) _history.Items.Add(Capital(l));
            foreach (var l in _undo.RedoLabels) _history.Items.Add(Capital(l));
            _history.SelectedIndex = _undo.DoneCount;
            _history.EndUpdate();
            _history.TopIndex = Math.Max(0, _undo.DoneCount - 8);
        }
        finally { _historyBusy = false; }
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private void DrawHistoryItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool redo = e.Index > _undo.DoneCount;
        bool here = e.Index == _undo.DoneCount;
        using var bg = new SolidBrush(here ? Theme.AccentSoft(Theme.Panel2, 0.28f) : Theme.Panel2);
        e.Graphics.FillRectangle(bg, e.Bounds);
        if (here) { using var bar = new SolidBrush(Theme.Accent); e.Graphics.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height); }
        var ink = here ? Theme.Accent : redo ? Theme.Muted : Theme.Text;
        TextRenderer.DrawText(e.Graphics, _history.Items[e.Index].ToString(), e.Font, new Point(e.Bounds.X + 8, e.Bounds.Y + 2), ink);
    }

    /// <summary>Undo or redo until the given row is the current state.</summary>
    private void JumpToHistory(int row)
    {
        if (row < 0) return;
        StopPlay();
        int steps = row - _undo.DoneCount;
        string? last = null;
        for (; steps < 0; steps++) last = _undo.Undo(_doc) ?? last;
        for (; steps > 0; steps--) last = _undo.Redo(_doc) ?? last;
        if (last == null) return;
        ClampCursors();
        InvalidateComposite();
        Touch();
        RefreshAll();
        Say(row == 0 ? "back to the start" : $"history: at '{_history.Items[row]}'");
    }

    #endregion

    #region pivots and hitboxes

    private (int lo, int hi) FrameSel() => (Math.Min(_selStart, _selEnd), Math.Max(_selStart, _selEnd));

    private void SetPivot(Point p)
    {
        var (lo, hi) = FrameSel();
        var old = Enumerable.Range(lo, hi - lo + 1).Select(f => _doc.Frames[f].Pivot).ToArray();
        for (int f = lo; f <= hi; f++) _doc.Frames[f].Pivot = p;
        _undo.Push(new PropertyEdit("set pivot",
            d => { for (int f = lo; f <= hi; f++) d.Frames[f].Pivot = old[f - lo]; },
            d => { for (int f = lo; f <= hi; f++) d.Frames[f].Pivot = p; }));
        Touch();
        _canvas.Invalidate();
        Say($"pivot set to {p.X},{p.Y} on frame{(hi > lo ? $"s {lo + 1}–{hi + 1}" : $" {lo + 1}")}");
    }

    private void AddHitbox(Rectangle r)
    {
        var before = Before();
        int n = _doc.Frames[_cur].Hitboxes.Count + 1;
        _doc.Frames[_cur].Hitboxes.Add(new Hitbox { Name = n == 1 ? "hit" : $"hit{n}", Rect = r });
        PushStructure("add hitbox", before);
        Say($"hitbox {r.Width}×{r.Height} at {r.X},{r.Y} on frame {_cur + 1}");
    }

    private void ClearHitboxes()
    {
        if (_doc.Frames[_cur].Hitboxes.Count == 0) return;
        var before = Before();
        _doc.Frames[_cur].Hitboxes.Clear();
        PushStructure("clear hitboxes", before);
        Say($"hitboxes cleared on frame {_cur + 1}");
    }

    /// <summary>Pivot crosshair and hitbox outlines, drawn over the art.</summary>
    private void PaintGuides(Graphics g, Point o)
    {
        var f = _doc.Frames[_cur];
        using var hit = new Pen(Color.FromArgb(220, 255, 60, 60), 1);
        using var hitFill = new SolidBrush(Color.FromArgb(40, 255, 60, 60));
        foreach (var h in f.Hitboxes.Select(x => x.Rect).Concat(_hitDraft is { } d ? new[] { d } : Array.Empty<Rectangle>()))
        {
            var r = new Rectangle(o.X + h.X * _zoom, o.Y + h.Y * _zoom, h.Width * _zoom, h.Height * _zoom);
            g.FillRectangle(hitFill, r);
            g.DrawRectangle(hit, r);
        }
        if (f.Pivot is { } p)
        {
            int cx = o.X + p.X * _zoom + _zoom / 2, cy = o.Y + p.Y * _zoom + _zoom / 2;
            using var pen = new Pen(Color.FromArgb(255, 255, 0, 220), 2);
            g.DrawLine(pen, cx - 7, cy, cx + 7, cy);
            g.DrawLine(pen, cx, cy - 7, cx, cy + 7);
            g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
        }
    }

    #endregion

    #region layers

    /// <summary>
    /// Snapshot for a structural edit. An open transform is applied first, so
    /// another command can never change the document underneath it.
    /// </summary>
    private Document Before()
    {
        if (_xf != null) CommitTransform();
        return _doc.Snapshot();
    }

    private void PushStructure(string label, Document before)
    {
        _undo.Push(new StructureEdit(label, before, _doc.Snapshot()));
        Touch();
        InvalidateComposite();
        RefreshAll();
    }

    private void AddLayer()
    {
        var before = Before();
        _doc.AddLayer(null, _activeLayer + 1);
        _activeLayer++;
        PushStructure("add layer", before);
        Say("layer added");
    }

    private void DuplicateLayer()
    {
        var before = Before();
        var copy = ActiveLayer.Clone();
        copy.Name = ActiveLayer.Name + " copy";
        _doc.Layers.Insert(_activeLayer + 1, copy);
        _activeLayer++;
        PushStructure("duplicate layer", before);
        Say("layer duplicated");
    }

    private void DeleteLayer()
    {
        if (_doc.Layers.Count <= 1) { Say("a document needs at least one layer"); return; }
        var before = Before();
        _doc.Layers.RemoveAt(_activeLayer);
        _activeLayer = Math.Clamp(_activeLayer - 1, 0, _doc.Layers.Count - 1);
        PushStructure("delete layer", before);
        Say("layer deleted");
    }

    private void MoveLayer(int dir)
    {
        int to = _activeLayer + dir;
        if (to < 0 || to >= _doc.Layers.Count) return;
        var before = Before();
        (_doc.Layers[_activeLayer], _doc.Layers[to]) = (_doc.Layers[to], _doc.Layers[_activeLayer]);
        _activeLayer = to;
        PushStructure("reorder layers", before);
    }

    /// <summary>
    /// Merge the active layer into the one below, keeping the picture the same: the lower layer's enabled mask is baked into its pixels
    /// first, the upper layer's object blends, layer blend and enabled mask are applied while compositing, and neither mask remains.
    /// A shared lower layer (one picture for every frame) is merged into once, and only from a shared upper layer; an upper layer
    /// that differs per frame is refused with a message (unshare the lower layer or share the upper one first).
    /// </summary>
    private void MergeDown()
    {
        if (_activeLayer == 0) { Say("nothing below to merge into"); return; }
        var upper = _doc.Layers[_activeLayer];
        var lower = _doc.Layers[_activeLayer - 1];
        if (lower.Shared && !upper.Shared && upper.Cels.Count > 0)
        {
            Say($"'{lower.Name}' is shared (one picture for every frame) but '{upper.Name}' changes per frame: untick shared on the lower layer, or make this one shared, then merge");
            return;
        }
        var before = Before();

        var frames = lower.Shared ? new[] { 0 } : Enumerable.Range(0, _doc.FrameCount).ToArray();
        foreach (int f in frames)
        {
            var uc = upper.CelAt(f);
            if (uc == null) continue;
            var lc = lower.EnsureCel(f);
            // The lower layer's own mask goes into its pixels first, or the merged-in pixels would be hidden by it.
            if (lower.MaskEnabled && lc.Mask is { } lm && lm.Width == DocW && lm.Height == DocH)
            {
                BakeCel(lc);
                int bi = lc.Objects.FindLastIndex(o => o is RasterObject r && r.X == 0 && r.Y == 0 && r.Width == DocW && r.Height == DocH);
                if (bi >= 0)
                {
                    var lpx = ImageOps.GetPixels(((RasterObject)lc.Objects[bi]).Pixels);
                    Document.ApplyMask(lpx, ImageOps.GetPixels(lm.Pixels));
                    lc.Objects[bi] = ((RasterObject)lc.Objects[bi]).With(ImageOps.FromPixels(DocW, DocH, lpx));
                }
                lc.Mask = null;
            }
            // Draw into a copy and swap it in; the original is shared with undo.
            int idx = lc.Objects.FindLastIndex(o => o is RasterObject r && r.X == 0 && r.Y == 0
                                                     && r.Width == DocW && r.Height == DocH);
            var bytes = idx >= 0 ? ImageOps.GetPixels(((RasterObject)lc.Objects[idx]).Pixels)
                                 : new byte[DocW * DocH * 4];
            var maskPx = upper.MaskEnabled && uc.Mask is { } um && um.Width == DocW && um.Height == DocH ? ImageOps.GetPixels(um.Pixels) : null;
            bool special = maskPx != null || upper.Blend != BlendMode.Normal;
            var target = special ? new byte[DocW * DocH * 4] : bytes;
            foreach (var o in uc.Objects)
                if (o is RasterObject r && r.Visible)
                    Blending.Over(target, DocW, DocH, ImageOps.GetPixels(r.Pixels), r.Width, r.Height,
                                  r.X, r.Y, r.Opacity * upper.Opacity / 255, r.Blend);
            if (special)
            {
                if (maskPx != null) Document.ApplyMask(target, maskPx);
                Blending.Layer(bytes, target, upper.Blend, DocW, DocH);
            }
            var px = ImageOps.FromPixels(DocW, DocH, bytes);
            if (idx >= 0) lc.Objects[idx] = ((RasterObject)lc.Objects[idx]).With(px);
            else lc.Objects.Add(new RasterObject(px) { Name = "Bitmap" });
        }
        _doc.Layers.RemoveAt(_activeLayer);
        _activeLayer--;
        PushStructure("merge down", before);
        Say("merged down");
    }

    private void Flatten()
    {
        var before = Before();
        var flat = new Layer { Name = "Flattened" };
        for (int f = 0; f < _doc.FrameCount; f++)
        {
            var cel = new Cel();
            cel.Objects.Add(new RasterObject(_doc.RenderFrame(f)) { Name = "Bitmap" });
            flat.Cels[f] = cel;
        }
        _doc.Layers.Clear();   // the old layers live on in the undo snapshot
        _doc.Layers.Add(flat);
        _activeLayer = 0;
        SelectObject(-1);
        PushStructure("flatten", before);
        Say("flattened to one layer");
    }

    private void SetLayerOpacity(int percent)
    {
        byte v = (byte)Math.Clamp(percent * 255 / 100, 0, 255);
        if (ActiveLayer.Opacity == v) return;
        int layer = _activeLayer;
        byte old = ActiveLayer.Opacity;
        ActiveLayer.Opacity = v;
        _undo.Push(new PropertyEdit("layer opacity",
            d => d.Layers[layer].Opacity = old,
            d => d.Layers[layer].Opacity = v));
        Touch();
        InvalidateComposite();
        _layers.Invalidate();
    }

    private void SetLayerBlend()
    {
        if (_blend.SelectedItem is not string name) return;
        if (!Enum.TryParse<BlendMode>(name, out var mode)) return;
        if (ActiveLayer.Blend == mode) return;
        int layer = _activeLayer;
        var old = ActiveLayer.Blend;
        ActiveLayer.Blend = mode;
        _undo.Push(new PropertyEdit("layer blend",
            d => d.Layers[layer].Blend = old,
            d => d.Layers[layer].Blend = mode));
        Touch();
        InvalidateComposite();
        _layers.Invalidate();
    }

    private void SetLayerShared(bool shared)
    {
        if (ActiveLayer.Shared == shared) return;
        var before = Before();
        var l = ActiveLayer;
        if (shared)
        {
            var cel = l.Cels.TryGetValue(_cur, out var c) ? c : new Cel();
            l.Cels.Clear();
            l.Cels[0] = cel;
        }
        l.Shared = shared;
        PushStructure("shared layer", before);
        Say(shared ? "layer now shared across all frames" : "layer is per-frame again");
    }

    #endregion

    #region document, frames

    private void Touch()
    {
        _dirty = true;
        UpdateTitle();
    }

    private void UpdateTitle() =>
        Text = (_path == null ? "Untitled" : Path.GetFileName(_path)) + (_dirty ? " *" : "") + " — Flint";

    /// <summary>Set by headless tests: a modal prompt would block forever.</summary>
    internal bool SuppressPrompts;

    private bool ConfirmDiscard()
    {
        if (!_dirty || SuppressPrompts || Program.Headless) return true;
        var r = MessageBox.Show(this, "Save changes to this project first?", "Flint",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel) return false;
        return r != DialogResult.Yes || SaveProject(false);
    }

    private void NewCanvas()
    {
        if (!ConfirmDiscard()) return;
        using var dlg = new NewCanvasDialog(DocW, DocH);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        StopPlay();
        _undo.Clear();
        _doc.Dispose();
        _doc = new Document(dlg.CanvasWidth, dlg.CanvasHeight);
        if (!dlg.Transparent)
        {
            var r = _doc.Layers[0].EnsureCel(0).EnsureRaster(DocW, DocH);
            using var g = Graphics.FromImage(r.Pixels);
            g.Clear(_bg);
        }
        _cur = 0;
        _activeLayer = 0;
        _selection = null;
        _path = null;
        _dirty = false;
        _undo.Clear();
        Recovery.Clear();
        FitZoom();
        InvalidateComposite();
        RefreshAll();
        Say($"new {DocW}×{DocH} canvas");
    }

    private void OpenProject()
    {
        if (!ConfirmDiscard()) return;
        using var dlg = new OpenFileDialog
        {
            Filter = "Flint project or Fireworks PNG|*.flint;*.png|All files|*.*", Title = "Open project",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenAny(dlg.FileName);
    }

    private void LoadProject(string path)
    {
        var result = FlintFormat.Load(path);
        if (!result.Ok)
        {
            MessageBox.Show(this, result.Error, "Could not open", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Say("open failed: " + result.Error);
            return;
        }
        StopPlay();
        _undo.Clear();
        Recovery.Clear();
        _doc.Dispose();
        _doc = result.Document!;
        _cur = 0;
        _activeLayer = _doc.Layers.Count - 1;
        _selection = null;
        _path = path;
        _dirty = false;
        _undo.Clear();
        FitZoom();
        InvalidateComposite();
        RefreshAll();
        Say(result.Warnings.Count > 0
            ? $"opened with warnings: {string.Join("; ", result.Warnings)}"
            : $"opened {Path.GetFileName(path)}");
    }

    private bool SaveProject(bool forceAsk)
    {
        FinishFloat();
        string? target = _path;
        if (forceAsk || target == null)
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "Flint project|*.flint",
                FileName = Path.GetFileNameWithoutExtension(_path ?? "") is { Length: > 0 } n ? n : "untitled",
                DefaultExt = "flint",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            target = dlg.FileName;
        }
        try
        {
            // Big projects save behind a progress dialog so the window stays alive.
            if (_doc.ApproximateBytes() > 64L * 1024 * 1024)
            {
                var doc = _doc;
                var (ok, msg) = ProgressDialog.Run(this, "Saving", (p, ct) => { FlintFormat.Save(doc, target); return "saved"; });
                if (!ok) throw new IOException(msg);
            }
            else FlintFormat.Save(_doc, target);
            _path = target;
            _dirty = false;
            Recovery.Clear();
            UpdateTitle();
            Say($"saved {Path.GetFileName(target)}");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Say("save failed: " + ex.Message);
            return false;
        }
    }

    private void FitZoom()
    {
        int availW = Math.Max(64, _canvas.ClientSize.Width - 40);
        int availH = Math.Max(64, _canvas.ClientSize.Height - 40);
        int z = Math.Min(availW / Math.Max(1, DocW), availH / Math.Max(1, DocH));
        _zoom = Math.Clamp(z, 1, 16);
    }

    private void AddBlankFrame()
    {
        var before = Before();
        _doc.InsertFrame(_cur + 1, new FrameSpec { Delay = _doc.Frames[_cur].Delay });
        _cur++;
        PushStructure("add frame", before);
        Say("blank frame added");
    }

    private void DuplicateFrame()
    {
        var before = Before();
        _doc.InsertFrame(_cur + 1, _doc.Frames[_cur].Clone());
        foreach (var l in _doc.Layers)
        {
            if (l.Shared) continue;
            if (l.Cels.TryGetValue(_cur, out var src)) l.Cels[_cur + 1] = src.Clone();
        }
        _cur++;
        PushStructure("duplicate frame", before);
    }

    private void DeleteFrame()
    {
        if (_doc.FrameCount <= 1) { Say("a document needs at least one frame"); return; }
        var before = Before();
        _doc.RemoveFrame(_cur);
        _cur = Math.Clamp(_cur, 0, _doc.FrameCount - 1);
        PushStructure("delete frame", before);
    }

    private void MoveFrame(int dir)
    {
        int to = _cur + dir;
        if (to < 0 || to >= _doc.FrameCount) return;
        var before = Before();
        _doc.SwapFrames(_cur, to);
        _cur = to;
        PushStructure("reorder frames", before);
    }

    private void ReverseOrder()
    {
        var before = Before();
        for (int i = 0, j = _doc.FrameCount - 1; i < j; i++, j--) _doc.SwapFrames(i, j);
        _cur = _doc.FrameCount - 1 - _cur;
        PushStructure("reverse frames", before);
        Say("order reversed");
    }

    private void SetFrameDelay(int ms)
    {
        if (_doc.Frames[_cur].Delay == ms) return;
        int frame = _cur, old = _doc.Frames[frame].Delay;
        _doc.Frames[frame].Delay = ms;
        _undo.Push(new PropertyEdit("frame delay",
            d => d.Frames[frame].Delay = old,
            d => d.Frames[frame].Delay = ms));
        Touch();
        RebuildStrip();
        UpdateInfo(null);
    }

    private void DelayAll()
    {
        var before = Before();
        int v = (int)_delay.Value;
        foreach (var f in _doc.Frames) f.Delay = v;
        PushStructure("set all delays", before);
        Say($"all frames set to {v}ms");
    }

    #endregion

    #region transforms

    private void Flip(bool horizontal)
    {
        var before = Before();
        int w = DocW, h = DocH;
        _doc.MapMasks(k => ImageOps.Flip(k.Pixels, horizontal));
        _doc.MapRasters(r => r.With(ImageOps.Flip(r.Pixels, horizontal),
            horizontal ? w - r.X - r.Width : r.X,
            horizontal ? r.Y : h - r.Y - r.Height));
        PushStructure("flip", before);
        Say(horizontal ? "flipped horizontally" : "flipped vertically");
    }

    private void Trim() => TrimCanvas();

    private void ScaleAll()
    {
        try { var size=ResizePlan.Scale(DocW,DocH,(double)_scaleBy.Value);ResizeImage(size.Width,size.Height,ImageQuality); }
        catch(ArgumentException ex) { Say(ex.Message); }
    }

    private void ResizeCanvas()
    {
        int w = (int)_rw.Value, h = (int)_rh.Value;
        if (w <= 0 || h <= 0) { Say("set a width and height first"); return; }
        var before = Before();
        int dx = _center.Checked ? (w - DocW) / 2 : 0;
        int dy = _center.Checked ? (h - DocH) / 2 : 0;
        _doc.Resize(w, h, dx, dy);
        _selection = null;
        FitZoom();
        PushStructure("resize canvas", before);
        Say($"canvas now {w}×{h}");
    }

    private void RemoveKey()
    {
        var before = Before();
        int active = _activeLayer, frameKey = ActiveFrameKey, tol = (int)_tol.Value;
        bool all = _applyAll.Checked;
        var key = _key;
        int n = _doc.MapRasters(r =>
        {
            var px = ImageOps.Clone(r.Pixels);
            ImageOps.ColorKey(px, key, tol);
            return r.With(px);
        }, layer: l => l == active, celKey: k => all || k == frameKey);
        if (n == 0) { Say("nothing on this layer to key"); return; }
        PushStructure("remove colour", before);
        Say($"colour removed from {n} cel(s) of '{ActiveLayer.Name}'");
    }

    private void Slice(int cols, int rows)
    {
        int cw = DocW / cols, ch = DocH / rows;
        if (cw < 1 || ch < 1) { Say("grid is finer than the image"); return; }

        var before = Before();
        int delay = _doc.Frames[_cur].Delay;
        string layerName = ActiveLayer.Name;
        using var flat = _doc.RenderFrame(_cur);

        var sliced = new Layer { Name = layerName };
        int index = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var cel = new Cel();
                cel.Objects.Add(new RasterObject(
                    ImageOps.Crop(flat, new Rectangle(c * cw, r * ch, cw, ch))) { Name = "Bitmap" });
                sliced.Cels[index++] = cel;
            }

        _doc.Layers.Clear();   // the old content lives on in the undo snapshot
        _doc.Frames.Clear();
        for (int i = 0; i < index; i++) _doc.Frames.Add(new FrameSpec { Delay = delay });
        _doc.Layers.Add(sliced);
        _doc.Width = cw;
        _doc.Height = ch;
        _cur = 0;
        _activeLayer = 0;
        _selection = null;
        FitZoom();
        PushStructure("slice sheet", before);
        Say($"sliced into {index} frames of {cw}×{ch}");
    }

    #endregion

    #region undo

    private void Undo()
    {
        if (_xf != null) { CancelTransform(); return; }
        StopPlay();
        var label = _undo.Undo(_doc);
        if (label == null) { Say("nothing to undo"); return; }
        ClampCursors();
        InvalidateComposite();
        RefreshAll();
        Touch();
        Say($"undid {label}");
    }

    private void Redo()
    {
        FinishFloat();
        StopPlay();
        var label = _undo.Redo(_doc);
        if (label == null) { Say("nothing to redo"); return; }
        ClampCursors();
        InvalidateComposite();
        RefreshAll();
        Touch();
        Say($"redid {label}");
    }

    private void ClampCursors()
    {
        _cur = Math.Clamp(_cur, 0, Math.Max(0, _doc.FrameCount - 1));
        _selStart = Math.Clamp(_selStart, 0, Math.Max(0, _doc.FrameCount - 1));
        _selEnd = Math.Clamp(_selEnd, 0, Math.Max(0, _doc.FrameCount - 1));
        _activeLayer = Math.Clamp(_activeLayer, 0, Math.Max(0, _doc.Layers.Count - 1));
    }

    #endregion

    #region import

    private void ChooseFiles()
    {
        using var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Images|*.png;*.gif;*.jpg;*.jpeg;*.bmp|All files|*.*",
            Title = "Import images",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) Import(dlg.FileNames);
    }

    private void DropFiles(string[] paths)
    {
        if (paths.Length == 1 && paths[0].EndsWith(FlintFormat.Extension, StringComparison.OrdinalIgnoreCase))
        {
            if (ConfirmDiscard()) LoadProject(paths[0]);
            return;
        }
        var audio = paths.Where(AudioIO.IsAudio).ToArray();
        if (audio.Length > 0) _ = ImportAudio(audio, SelectedTrackOrNew(), _timeline.PlayheadMs);
        var images = paths.Where(p => !AudioIO.IsAudio(p)).ToArray();
        if (images.Length > 0) Import(images);
    }

    /// <summary>Options from the last import dialog, so the next one starts the same way.</summary>
    private ImportOptions _lastImport = new();

    private void Import(string[] paths)
    {
        StopPlay();
        var files = new List<ImportedFile>();
        var failures = new List<string>();
        foreach (var path in paths)
        {
            try { files.Add(Importer.Load(path)); }
            catch (Exception ex) { failures.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
        if (files.Count == 0)
        {
            Say(failures.Count > 0 ? "import failed: " + string.Join("; ", failures) : "nothing imported");
            return;
        }

        try
        {
            var options = _lastImport;
            if (!Program.Headless)
            {
                using var dlg = new ImportDialog(files, _doc);
                if (dlg.ShowDialog(this) != DialogResult.OK) { Say("import cancelled"); return; }
                options = _lastImport = dlg.Options;
            }

            var before = Before();
            // Keep each imported picture in the library for reuse (sheets as a whole).
            foreach (var f in files) if (f.Frames.Count == 1) AddToLibrary(f.Frames[0].bmp, Path.GetFileNameWithoutExtension(f.Path), f.Path);
            string summary = Importer.Apply(_doc, _cur, files, options, out int first, out int layer);
            _activeLayer = layer;
            _cur = _selStart = _selEnd = Math.Clamp(first, 0, _doc.FrameCount - 1);
            FitZoom();
            PushStructure("import", before);
            Say(failures.Count > 0 ? $"{summary}; failed: {string.Join("; ", failures)}" : summary);
        }
        finally { foreach (var f in files) f.Dispose(); }
    }

    #endregion

    #region timeline, playback, status

    /// <summary>Push current state into the timeline. Thumbnails rebuild lazily.</summary>
    private void RebuildStrip()
    {
        _timeline.Current = _cur;
        _timeline.SelStart = _selStart;
        _timeline.SelEnd = _selEnd;
        if (!_play.Enabled) _timeline.PlayheadMs = Playback.FrameStart(Delays(), _cur);
        _timeline.InvalidateThumbnails();
        _timeline.InvalidateWaveforms();
        _timeline.Bind(_doc);
        int want = Math.Clamp(_timeline.PreferredHeight + 70, 200, 380);
        if (_timelinePane != null && !_timelinePane.UserSized && _timelinePane.Dock == DockStyle.Bottom)
            _timelinePane.Height = want + 25;
    }

    private int[] Delays() => _doc.Frames.Select(f => Math.Max(1, f.Delay)).ToArray();

    private List<LibraryItem>? _libraryShown;

    private void RefreshAll()
    {
        if (_xf != null && (_xf.Layer != _activeLayer || _xf.Frame != _cur)) CommitTransform();
        if (_library != null && !ReferenceEquals(_libraryShown, _doc.Library))
        {
            _library.Bind(_doc.Library);
            _libraryShown = _doc.Library;
        }
        ClampCursors();
        CheckMaskEditTarget();
        SyncCanvasBackground();
        if (!_rw.Focused) _rw.Value = Math.Clamp(DocW,0,20000);
        if (!_rh.Focused) _rh.Value = Math.Clamp(DocH,0,20000);
        RebuildStrip();
        _layers.Bind(_doc, _activeLayer, _cur, EditingMask);
        SyncLayerControls();
        RefreshObjects();
        RefreshEffects();
        RefreshHistory();
        UpdateInfo(null);
        UpdateTitle();
        _canvas.AutoScrollMinSize = new Size(DocW * _zoom + 40, DocH * _zoom + 40);
        _canvas.Invalidate();
    }

    private void SyncLayerControls()
    {
        _syncing = true;
        try
        {
            var l = ActiveLayer;
            _layerOpacity.Value = Math.Clamp(l.Opacity * 100 / 255, 0, 100);
            _blend.SelectedItem = l.Blend.ToString();
            _sharedBox.Checked = l.Shared;
        }
        finally { _syncing = false; }
    }

    private void UpdateInfo(Point? cursor)
    {
        int total = _doc.Frames.Sum(f => f.Delay);
        string at = cursor is { } p ? $"   [{p.X},{p.Y}]" : "";
        _info.Text = (EditingMask ? "EDITING MASK   " : "") + $"{DocW}×{DocH}   {_doc.LayerCount} layer{(_doc.LayerCount == 1 ? "" : "s")}   " +
                     $"{_doc.FrameCount} frame{(_doc.FrameCount == 1 ? "" : "s")}   {total}ms   {_zoom}×{at}";
        _mem.Text = $"{_doc.ApproximateBytes() / 1048576.0:0.0} MB · undo {_undo.Used / 1048576.0:0.0} MB" +
                    (_lastAutosave is { } t ? $" · autosaved {t:HH:mm}" : "");
        decimal v = Math.Clamp(_doc.Frames[_cur].Delay, (int)_delay.Minimum, (int)_delay.Maximum);
        if (_delay.Value != v) { _syncing = true; _delay.Value = v; _syncing = false; }
    }

    private void Say(string msg) => _status.Text = msg;

    private void Zoom(int dir)
    {
        _zoom = Math.Clamp(_zoom + dir, 1, 32);
        RefreshAll();
    }

    private void TogglePlay()
    {
        if (_play.Enabled) StopPlay();
        else StartPlay();
    }

    /// <summary>Play the selected frame range, or everything if one frame is selected.</summary>
    private void StartPlay(bool forRecording = false)
    {
        FinishFloat();
        if (_doc.FrameCount < 1) return;
        int lo = Math.Min(_selStart, _selEnd), hi = Math.Max(_selStart, _selEnd);
        bool range = hi > lo && !forRecording;
        _rangeFrom = range ? lo : 0;
        _rangeTo = range ? hi : _doc.FrameCount - 1;

        var d = Delays();
        // Start from the current frame when it is inside the range.
        int startFrame = _cur >= _rangeFrom && _cur <= _rangeTo ? _cur : _rangeFrom;
        _rangeStartMs = Playback.FrameStart(d, _rangeFrom);
        double offsetMs = Playback.FrameStart(d, startFrame) - _rangeStartMs;

        // Audio can only follow forward playback at normal speed.
        _playAudio = !_doc.Audio.IsEmpty && _playback.Speed == 1.0
                     && (_playback.Mode != LoopMode.PingPong || forRecording);
        if (!_doc.Audio.IsEmpty && !_playAudio)
            Say("audio is silent in ping-pong or at other than 1× speed");

        _clockOffsetMs = _playAudio ? 0 : offsetMs / Math.Max(0.01, _playback.Speed);
        if (_playAudio)
        {
            try { _engine.Play(_doc.Audio, _rangeStartMs + offsetMs); }
            catch (Exception ex) { _playAudio = false; Say("audio output unavailable: " + ex.Message); }
        }
        _clock.Restart();
        _play.Interval = 15;
        _play.Start();
        _playBtn.Text = "Stop";
    }

    private double _clockOffsetMs;

    /// <summary>Elapsed playback time inside the range. Audio is the clock when it is playing.</summary>
    private double Elapsed() => _playAudio
        ? _engine.PositionMs - _rangeStartMs
        : _clockOffsetMs + _clock.Elapsed.TotalMilliseconds;
    // Playback.FrameAfter applies Speed itself, so the silent clock stays raw;
    // the offset is pre-divided so a start mid-range lands on the right frame.


    private void StopPlay()
    {
        _play.Stop();
        _clock.Stop();
        _engine.Stop();
        _playAudio = false;
        _playBtn.Text = "Play";
        if (_engine.Recording || _countdown > 0) FinishRecording();
    }

    private void Advance()
    {
        if (_countdown > 0) return;
        var d = Delays();
        double elapsed = Elapsed();
        double span = Playback.FrameStart(d, _rangeTo + 1) - _rangeStartMs;

        // Audio plays straight through; loop it back when the range ends.
        if (_playAudio && _playback.Mode == LoopMode.Loop && elapsed >= span && !_engine.Recording)
        {
            _engine.Play(_doc.Audio, _rangeStartMs);
            _clockOffsetMs = 0;
            elapsed = 0;
        }

        // While recording, play through once and hold, so the take is never
        // timed against a picture that jumped back to the start.
        var clock = _engine.Recording
            ? new Playback { Mode = LoopMode.Once, Speed = 1 }
            : _playback;
        int f = clock.FrameAfter(d, _rangeFrom, _rangeTo, elapsed);
        if (f < 0)
        {
            // Recording carries on past the last frame until stopped.
            if (_engine.Recording) { _timeline.PlayheadMs = _rangeStartMs + elapsed; _timeline.Invalidate(); UpdateLevel(); return; }
            StopPlay();
            return;
        }

        double shown = clock.Mode == LoopMode.Loop ? elapsed % Math.Max(1, span) : elapsed;
        _timeline.PlayheadMs = _rangeStartMs + Math.Min(shown, span);
        _timeline.Reveal(_timeline.PlayheadMs);
        UpdateLevel();
        if (f != _cur)
        {
            _cur = f;
            _timeline.Current = f;
            InvalidateComposite();
            UpdateInfo(null);
        }
        _timeline.Invalidate();
    }

    private void UpdateLevel()
    {
        int v = (int)Math.Clamp(_engine.InputLevel * 100, 0, 100);
        if (_level.Value != v) _level.Value = v;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_objects != null && _objects.Focused && e.KeyCode is not (Keys.Enter or Keys.Delete)) return;
        if (ActiveControl is NumericUpDown or TextBox or ComboBox) return;
        if (EditKey(e)) { e.Handled = true; return; }
        switch (e.KeyCode)
        {
            case Keys.Space: TogglePlay(); e.Handled = true; break;
            case Keys.Left: StopPlay(); _cur = Math.Max(0, _cur - 1); _selStart = _selEnd = _cur; InvalidateComposite(); RefreshAll(); e.Handled = true; break;
            case Keys.Right: StopPlay(); _cur = Math.Min(_doc.FrameCount - 1, _cur + 1); _selStart = _selEnd = _cur; InvalidateComposite(); RefreshAll(); e.Handled = true; break;
            case Keys.Escape: _selection = null; _canvas.Invalidate(); e.Handled = true; break;
            case Keys.Delete:
                if (_tool == Tool.ObjectSelect) { if (SelectedObject != null) DeleteSelectedObject(); else Say("Pick an object first."); }
                else if (_tool == Tool.Hitbox) ClearHitboxes(); else ClearSelection();
                e.Handled = true; break;
            case Keys.B: SetTool(Tool.Pencil); break;
            case Keys.E: if (!e.Control) SetTool(Tool.Eraser); break;
            case Keys.G: SetTool(Tool.Fill); break;
            case Keys.L: SetTool(Tool.Line); break;
            case Keys.R: SetTool(Tool.Rectangle); break;
            case Keys.O: if (!e.Control) SetTool(Tool.Ellipse); break;
            case Keys.M: SetTool(Tool.Marquee); break;
            case Keys.I: if (!e.Control) SetTool(Tool.Eyedropper); break;
            case Keys.H: SetTool(Tool.Pan); break;
        }
    }

    #endregion

    #region export

    /// <summary>Frames to export: the selection when asked for and present, else all.</summary>
    private (int from, int to) ExportRange()
    {
        int lo = Math.Min(_selStart, _selEnd), hi = Math.Max(_selStart, _selEnd);
        return _rangeOnly.Checked && hi > lo ? (lo, hi) : (0, _doc.FrameCount - 1);
    }

    private static readonly (string filter, ExportKind kind)[] ExportFormats =
    {
        ("Animated GIF (256 colours, 1-bit transparency)|*.gif", ExportKind.Gif),
        ("Animated PNG (full colour and alpha)|*.png", ExportKind.Apng),
        ("Animated WebP, lossless|*.webp", ExportKind.WebP),
        ("WebM video, keeps transparency|*.webm", ExportKind.WebM),
        ("MP4 video, no transparency|*.mp4", ExportKind.Mp4),
        ("PNG, current frame|*.png", ExportKind.Png),
        ("Sprite sheet PNG|*.png", ExportKind.Sheet),
        ("Texture atlas, PNG + JSON for game engines|*.png", ExportKind.Atlas),
        ("WAV audio|*.wav", ExportKind.Wav),
        ("JPEG, current frame|*.jpg", ExportKind.Jpeg),
        ("WebP still, lossless, current frame|*.webp", ExportKind.WebPStill),
        ("WebP still, lossy, current frame|*.webp", ExportKind.WebPStill),
        ("BMP, current frame (32-bit with alpha)|*.bmp", ExportKind.Bmp),
        ("TIFF, current frame (32-bit with alpha)|*.tif", ExportKind.Tiff),
        ("PNG-8 indexed, current frame|*.png", ExportKind.Png8),
    };

    private void ExportAny()
    {
        FinishFloat();
        using var dlg = new SaveFileDialog
        {
            Title = "Export",
            Filter = string.Join("|", ExportFormats.Select(f => f.filter)),
            FilterIndex = 1,
            FileName = Path.GetFileNameWithoutExtension(_path ?? "") is { Length: > 0 } n ? n : "animation",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (StillExport.IsStill(ExportFormats[dlg.FilterIndex - 1].kind)) { RunStillExport(dlg.FilterIndex - 1, dlg.FileName); return; }
        RunExport(ExportFormats[dlg.FilterIndex - 1].kind, dlg.FileName);
    }

    private void ExportSequence()
    {
        using var dlg = new FolderBrowserDialog { Description = "Write one PNG per frame into…" };
        if (dlg.ShowDialog(this) == DialogResult.OK) RunExport(ExportKind.Sequence, dlg.SelectedPath);
    }

    private void RunExport(ExportKind kind, string path)
    {
        StopPlay();
        if (Exporter.NeedsFfmpeg(kind) && WebmExport.Find() == null)
        {
            MessageBox.Show(this,
                "This format uses ffmpeg, which was not found.\n\n" +
                "Install it with:\n    winget install Gyan.FFmpeg\n\n" +
                "or put ffmpeg.exe next to Flint.exe, then try again.",
                "ffmpeg required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Say("ffmpeg not found");
            return;
        }

        var (from, to) = ExportRange();
        var job = new ExportJob
        {
            Doc = _doc, Kind = kind, Path = path, From = from, To = to, Current = _cur,
            GifColours = (int)_colors.Value, Dither = _dither.Checked, Loop = _loop.Checked,
            IncludeAudio = _includeAudio.Checked,
            Quality = kind == ExportKind.WebP ? 0 : (int)_webmQuality.Value,
            Matte = _bg, SheetCols = (int)_sheetCols.Value,
        };

        var (ok, message) = ProgressDialog.Run(this, "Exporting", (p, ct) => Exporter.Run(job, p, ct));

        // Formats without sound say so, rather than silently dropping it.
        string note = ok && !_doc.Audio.IsEmpty && !Exporter.HasAudio(kind)
            ? " (this format has no audio; export WAV separately)" : "";
        Say((ok ? message : "export " + (message == "cancelled" ? "cancelled" : "failed: " + message)) + note);
        if (!ok && message != "cancelled")
            MessageBox.Show(this, message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    #endregion

    #region autosave and recovery

    /// <summary>
    /// Write the recovery copy when there is something to lose and the user is
    /// not mid-stroke or playing. Runs on the UI thread: GDI+ bitmaps cannot be
    /// read safely from another thread while the editor may be drawing on them.
    /// </summary>
    private void Autosave()
    {
        if (!_dirty || _dragging || _play.Enabled || MouseButtons != MouseButtons.None) return;
        try
        {
            Recovery.Write(_doc, _path);
            _lastAutosave = DateTime.Now;
            UpdateInfo(null);
        }
        catch (Exception ex) { Say("autosave failed: " + ex.Message); }
    }

    private void OfferRecovery()
    {
        List<Recovery.Entry> orphans;
        try { orphans = Recovery.Orphans(); } catch { return; }
        foreach (var e in orphans)
        {
            var r = MessageBox.Show(this,
                $"Flint did not close normally last time.{Environment.NewLine}{Environment.NewLine}Recover {e.Display}?{Environment.NewLine}{Environment.NewLine}" +
                "Yes opens it as an unsaved copy. No discards it. Cancel keeps it for later.",
                "Recover work", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.No) { Recovery.Discard(e); continue; }

            var loaded = FlintFormat.Load(e.ProjectPath);
            if (!loaded.Ok)
            {
                MessageBox.Show(this, "That recovery copy could not be read: " + loaded.Error,
                    "Recover work", MessageBoxButtons.OK, MessageBoxIcon.Error);
                continue;
            }
            _undo.Clear();
            _doc.Dispose();
            _doc = loaded.Document!;
            _cur = 0;
            _activeLayer = _doc.Layers.Count - 1;
            _path = e.OriginalPath;          // Save goes back to where it came from
            _dirty = true;                   // it is not saved there yet
            FitZoom();
            InvalidateComposite();
            RefreshAll();
            // Re-protect it under this session before removing the old copy.
            try { Recovery.Write(_doc, _path); Recovery.Discard(e); } catch { }
            Say($"recovered {e.Display} — save it to keep it");
            return;
        }
    }

    #endregion

    #region timeline wiring

    private void WireTimeline()
    {
        _timeline.FrameClicked += (f, extend) =>
        {
            StopPlay();
            if (extend) _selEnd = f; else { _selStart = _selEnd = f; }
            _cur = f;
            InvalidateComposite();
            RefreshAll();
        };
        _timeline.Seek += ms =>
        {
            if (_play.Enabled) StopPlay();
            _timeline.PlayheadMs = ms;
            int f = Playback.FrameAt(Delays(), (int)ms);
            if (f != _cur) { _cur = f; _selStart = _selEnd = f; InvalidateComposite(); UpdateInfo(null); }
            _timeline.Current = _cur;
            _timeline.Invalidate();
        };
        _timeline.FrameMoved += (from, to) => MoveFrameTo(from, to);
        _timeline.DelayDragging += (f, ms) => { if (f == _cur) UpdateInfo(null); };
        _timeline.DelayDragged += (f, old, now) =>
        {
            _undo.Push(new PropertyEdit("frame delay",
                d => d.Frames[f].Delay = old, d => d.Frames[f].Delay = now));
            Touch();
            RefreshAll();
        };
        _timeline.FrameDoubleClicked += RenameFrame;
        _timeline.FrameMenu += ShowFrameMenu;
        _timeline.TagClicked += t =>
        {
            _selStart = t.Start; _selEnd = t.End; _cur = t.Start;
            InvalidateComposite(); RefreshAll();
            Say($"selected '{t.Name}', frames {t.Start + 1}–{t.End + 1}. Play loops it.");
        };
        _timeline.AudioEditStarted += () => _audioBefore = _doc.Audio.Clone();
        _timeline.AudioEditEnded += label =>
        {
            if (_audioBefore != null) _undo.Push(new AudioEdit(label, _audioBefore, _doc.Audio.Clone()));
            _audioBefore = null;
            Touch();
            _timeline.InvalidateWaveforms();
        };
        _timeline.LaneMenu += ShowLaneMenu;
        _timeline.AudioDropped += (files, lane, ms) =>
            _ = ImportAudio(files, lane >= 0 ? lane : SelectedTrackOrNew(), ms);
    }

    private void MoveFrameTo(int from, int to)
    {
        if (from == to) return;
        var before = Before();
        _doc.MoveFrame(from, to);
        _cur = to; _selStart = _selEnd = to;
        PushStructure("move frame", before);
        Say($"moved frame {from + 1} to {to + 1}");
    }

    private void RenameFrame(int f)
    {
        using var dlg = new RenameDialog(_doc.Frames[f].Name);
        dlg.Text = $"Name frame {f + 1}";
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string old = _doc.Frames[f].Name, now = dlg.Value;
        _doc.Frames[f].Name = now;
        _undo.Push(new PropertyEdit("frame name", d => d.Frames[f].Name = old, d => d.Frames[f].Name = now));
        Touch();
        RefreshAll();
    }

    private void TagSelection()
    {
        int lo = Math.Min(_selStart, _selEnd), hi = Math.Max(_selStart, _selEnd);
        using var dlg = new RenameDialog(lo == hi ? "Idle" : "Walk");
        dlg.Text = $"Tag frames {lo + 1}–{hi + 1}";
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Value.Length == 0) return;
        var before = Before();
        var colours = new[]
        {
            Color.FromArgb(40, 170, 120), Color.FromArgb(61, 200, 160), Color.FromArgb(30, 135, 105),
            Color.FromArgb(90, 190, 140), Color.FromArgb(20, 115, 95),
        };
        _doc.Tags.RemoveAll(t => t.Start == lo && t.End == hi);
        _doc.Tags.Add(new AnimTag { Name = dlg.Value, Start = lo, End = hi, Colour = colours[_doc.Tags.Count % colours.Length] });
        PushStructure("tag frames", before);
        Say($"tagged frames {lo + 1}–{hi + 1} as '{dlg.Value}'. Click the tag to select it.");
    }

    private void ShowFrameMenu(int f, Point screen)
    {
        if (f < Math.Min(_selStart, _selEnd) || f > Math.Max(_selStart, _selEnd)) { _selStart = _selEnd = f; }
        _cur = f;
        InvalidateComposite();
        RefreshAll();
        var m = new ContextMenuStrip();
        m.Items.Add("Insert blank frame before", null, (_, _) => InsertBlankAt(f));
        m.Items.Add("Insert blank frame after", null, (_, _) => InsertBlankAt(f + 1));
        m.Items.Add("Duplicate", null, (_, _) => DuplicateFrame());
        m.Items.Add("Delete", null, (_, _) => DeleteFrames());
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Copy frames", null, (_, _) => CopyFrames());
        var paste = m.Items.Add("Paste frames after", null, (_, _) => PasteFrames());
        paste.Enabled = _frameClipboard != null;
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Name frame…", null, (_, _) => RenameFrame(f));
        m.Items.Add("Tag selection…", null, (_, _) => TagSelection());
        var tag = _doc.Tags.FirstOrDefault(t => f >= t.Start && f <= t.End);
        if (tag != null)
            m.Items.Add($"Remove tag '{tag.Name}'", null, (_, _) =>
            {
                var before = Before();
                _doc.Tags.Remove(_doc.Tags.First(t => t.Name == tag.Name && t.Start == tag.Start));
                PushStructure("remove tag", before);
            });
        m.Show(screen);
    }

    private void InsertBlankAt(int at)
    {
        var before = Before();
        _doc.InsertFrame(at, new FrameSpec { Delay = _doc.Frames[Math.Clamp(at - 1, 0, _doc.FrameCount - 1)].Delay });
        _cur = at; _selStart = _selEnd = at;
        PushStructure("insert frame", before);
    }

    private void DeleteFrames()
    {
        int lo = Math.Min(_selStart, _selEnd), hi = Math.Max(_selStart, _selEnd);
        if (hi - lo + 1 >= _doc.FrameCount) { Say("a document needs at least one frame"); return; }
        var before = Before();
        for (int i = hi; i >= lo; i--) _doc.RemoveFrame(i);
        _cur = Math.Clamp(lo, 0, _doc.FrameCount - 1); _selStart = _selEnd = _cur;
        PushStructure("delete frames", before);
        Say($"deleted {hi - lo + 1} frame(s)");
    }

    /// <summary>Copies cels of every non-shared layer; shared layers are not per-frame.</summary>
    private void CopyFrames()
    {
        int lo = Math.Min(_selStart, _selEnd), hi = Math.Max(_selStart, _selEnd);
        _frameClipboard = new();
        for (int f = lo; f <= hi; f++)
        {
            var cels = new Dictionary<int, Cel>();
            for (int l = 0; l < _doc.Layers.Count; l++)
                if (!_doc.Layers[l].Shared && _doc.Layers[l].Cels.TryGetValue(f, out var c)) cels[l] = c.Clone();
            _frameClipboard.Add((_doc.Frames[f].Clone(), cels));
        }
        Say($"copied {hi - lo + 1} frame(s)");
    }

    private void PasteFrames()
    {
        if (_frameClipboard == null) return;
        var before = Before();
        int at = Math.Max(_selStart, _selEnd) + 1;
        foreach (var (spec, cels) in _frameClipboard)
        {
            _doc.InsertFrame(at, spec.Clone());
            foreach (var (l, c) in cels)
                if (l < _doc.Layers.Count && !_doc.Layers[l].Shared) _doc.Layers[l].Cels[at] = c.Clone();
            at++;
        }
        _selStart = Math.Max(_selStart, _selEnd) + 1; _selEnd = at - 1; _cur = _selStart;
        PushStructure("paste frames", before);
        Say($"pasted {_frameClipboard.Count} frame(s)");
    }

    #endregion

    #region audio

    private int SelectedTrackOrNew()
    {
        if (_timeline.SelectedTrack >= 0 && _timeline.SelectedTrack < _doc.Audio.Tracks.Count)
            return _timeline.SelectedTrack;
        return -1;   // ImportAudio makes one
    }

    private void AddTrack()
    {
        var before = _doc.Audio.Clone();
        int n = _doc.Audio.Tracks.Count + 1;
        _doc.Audio.Tracks.Add(new AudioTrack { Name = n == 1 ? "Dialogue" : $"Audio {n}" });
        _timeline.SelectedTrack = _doc.Audio.Tracks.Count - 1;
        _undo.Push(new AudioEdit("add track", before, _doc.Audio.Clone()));
        Touch();
        RefreshAll();
    }

    private void ChooseAudio()
    {
        using var dlg = new OpenFileDialog
        {
            Multiselect = true, Title = "Import audio",
            Filter = "Audio|" + string.Join(";", AudioIO.Extensions.Select(e => "*" + e)) + "|All files|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _ = ImportAudio(dlg.FileNames, SelectedTrackOrNew(), _timeline.PlayheadMs);
    }

    /// <summary>Decode off the UI thread, then place clips one after another from a time.</summary>
    private async Task ImportAudio(string[] files, int trackIndex, double atMs)
    {
        StopPlay();
        Say($"decoding {files.Length} audio file(s)…");
        Cursor = Cursors.AppStarting;
        var decoded = new List<(string path, float[]? samples, string? error)>();
        await Task.Run(() =>
        {
            foreach (var f in files)
            {
                try { decoded.Add((f, AudioIO.Decode(f), null)); }
                catch (Exception ex) { decoded.Add((f, null, ex.Message)); }
            }
        });
        Cursor = Cursors.Default;

        var before = _doc.Audio.Clone();
        if (trackIndex < 0 || trackIndex >= _doc.Audio.Tracks.Count)
        {
            _doc.Audio.Tracks.Add(new AudioTrack { Name = _doc.Audio.Tracks.Count == 0 ? "Dialogue" : $"Audio {_doc.Audio.Tracks.Count + 1}" });
            trackIndex = _doc.Audio.Tracks.Count - 1;
        }
        var track = _doc.Audio.Tracks[trackIndex];
        long added = 0;
        double at = atMs;
        var errors = new List<string>();
        foreach (var (path, samples, error) in decoded)
        {
            if (samples == null) { errors.Add($"{Path.GetFileName(path)}: {error}"); continue; }
            var src = _doc.Audio.AddSource(samples, Path.GetFileNameWithoutExtension(path), path);
            var clip = _doc.Audio.AddClip(track, src, at);
            at = clip.EndMs;
            added += src.Bytes;
        }
        if (added == 0) { Say("audio import failed: " + string.Join("; ", errors)); return; }

        _undo.Push(new AudioEdit("import audio", before, _doc.Audio.Clone(), added));
        _timeline.SelectedTrack = trackIndex;
        Touch();
        RefreshAll();
        Say(errors.Count > 0 ? "some audio failed: " + string.Join("; ", errors)
                             : $"added audio to '{track.Name}' at {atMs / 1000:0.000}s");
    }

    private void ShowLaneMenu(int lane, AudioClip? clip, double ms, Point screen)
    {
        var track = _doc.Audio.Tracks[lane];
        var m = new ContextMenuStrip();

        void Edit(string label, Action change)
        {
            var before = _doc.Audio.Clone();
            change();
            _undo.Push(new AudioEdit(label, before, _doc.Audio.Clone()));
            Touch();
            RefreshAll();
        }

        if (clip != null)
        {
            m.Items.Add("Split at playhead", null, (_, _) => Edit("split clip", () =>
            {
                if (_doc.Audio.Split(track, clip, _timeline.PlayheadMs) == null)
                    Say("put the playhead inside the clip to split it");
            }));
            m.Items.Add("Duplicate clip", null, (_, _) => Edit("duplicate clip", () =>
            {
                var c = clip.Clone();
                c.StartMs = clip.EndMs;
                track.Clips.Add(c);
            }));
            m.Items.Add("Delete clip", null, (_, _) => Edit("delete clip", () =>
            {
                track.Clips.Remove(clip);
                _doc.Audio.PruneSources();
            }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(clip.FadeInMs > 0 ? "Remove fade in" : "Fade in (100 ms)", null, (_, _) =>
                Edit("fade in", () => clip.FadeInMs = clip.FadeInMs > 0 ? 0 : Math.Min(100, clip.LengthMs / 2)));
            m.Items.Add(clip.FadeOutMs > 0 ? "Remove fade out" : "Fade out (100 ms)", null, (_, _) =>
                Edit("fade out", () => clip.FadeOutMs = clip.FadeOutMs > 0 ? 0 : Math.Min(100, clip.LengthMs / 2)));
            foreach (var (label, gain) in new[] { ("Gain 50%", 0.5f), ("Gain 100%", 1f), ("Gain 150%", 1.5f), ("Gain 200%", 2f) })
                m.Items.Add(label + (Math.Abs(clip.Gain - gain) < 0.01 ? "  ✓" : ""), null, (_, _) => Edit("clip gain", () => clip.Gain = gain));
            m.Items.Add(clip.Loop ? "Stop looping" : "Loop clip", null, (_, _) => Edit("loop clip", () => clip.Loop = !clip.Loop));
            m.Items.Add("Snap start to frame…", null, (_, _) => Edit("snap clip", () =>
            {
                int f = Playback.FrameAt(Delays(), (int)clip.StartMs);
                clip.StartMs = Playback.FrameStart(Delays(), f);
            }));
            m.Items.Add(new ToolStripSeparator());
        }

        m.Items.Add("Rename track…", null, (_, _) =>
        {
            using var dlg = new RenameDialog(track.Name);
            if (dlg.ShowDialog(this) == DialogResult.OK) Edit("rename track", () => track.Name = dlg.Value);
        });
        var kind = new ToolStripMenuItem("Track type");
        foreach (TrackKind k in Enum.GetValues<TrackKind>())
            kind.DropDownItems.Add(k + (track.Kind == k ? "  ✓" : ""), null, (_, _) => Edit("track type", () => track.Kind = k));
        m.Items.Add(kind);
        var vol = new ToolStripMenuItem("Track volume");
        foreach (var v in new[] { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f })
            vol.DropDownItems.Add($"{v * 100:0}%" + (Math.Abs(track.Volume - v) < 0.01 ? "  ✓" : ""), null,
                (_, _) => Edit("track volume", () => track.Volume = v));
        m.Items.Add(vol);
        m.Items.Add("Add track", null, (_, _) => AddTrack());
        m.Items.Add("Delete track", null, (_, _) => Edit("delete track", () =>
        {
            _doc.Audio.Tracks.Remove(track);
            _doc.Audio.PruneSources();
            _timeline.SelectedTrack = -1;
        }));
        m.Show(screen);
    }

    // ---------- recording ----------

    private void ToggleRecord()
    {
        if (_engine.Recording || _countdown > 0) { StopPlay(); return; }
        if (_micBox.SelectedIndex < 0 || !_micBox.Enabled) { Say("no microphone found"); return; }
        StopPlay();
        _recStartMs = _timeline.PlayheadMs;
        _countdown = 3;
        _recBtn.Text = "■ 3";
        Say("recording in 3…");
        var t = new System.Windows.Forms.Timer { Interval = 1000 };
        t.Tick += (_, _) =>
        {
            _countdown--;
            if (_countdown > 0) { _recBtn.Text = $"■ {_countdown}"; Say($"recording in {_countdown}…"); return; }
            t.Stop(); t.Dispose();
            BeginRecording();
        };
        t.Start();
    }

    private void BeginRecording()
    {
        try
        {
            // Play the animation (and any existing audio) from the playhead,
            // then start the mic and note the playhead at that instant.
            int f = Playback.FrameAt(Delays(), (int)_recStartMs);
            _cur = f;
            StartPlay(forRecording: true);
            _engine.StartRecording(_micBox.SelectedIndex);
            _recStartMs = _playAudio ? _engine.PositionMs : _rangeStartMs + Elapsed();
            _recBtn.Text = "■ Stop";
            Say("recording — press Stop or Space to finish");
        }
        catch (Exception ex)
        {
            _countdown = 0;
            _recBtn.Text = "● Rec";
            Say("could not start recording: " + ex.Message);
        }
    }

    private void FinishRecording()
    {
        bool wasCounting = _countdown > 0;
        _countdown = 0;
        _recBtn.Text = "● Rec";
        if (wasCounting && !_engine.Recording) { Say("recording cancelled"); return; }

        var samples = _engine.StopRecording();
        if (samples.Length < AudioFormat.Rate / 10) { Say("recording too short; nothing kept"); return; }

        var before = _doc.Audio.Clone();
        int lane = SelectedTrackOrNew();
        if (lane < 0)
        {
            _doc.Audio.Tracks.Add(new AudioTrack { Name = "Dialogue", Kind = TrackKind.Dialogue });
            lane = _doc.Audio.Tracks.Count - 1;
        }
        int take = _doc.Audio.Sources.Count + 1;
        var src = _doc.Audio.AddSource(samples, $"take {take}");
        _doc.Audio.AddClip(_doc.Audio.Tracks[lane], src, Math.Max(0, _recStartMs - RecordOffsetMs));
        _undo.Push(new AudioEdit("record", before, _doc.Audio.Clone(), src.Bytes));
        _timeline.SelectedTrack = lane;
        Touch();
        RefreshAll();
        Say($"recorded {src.DurationMs / 1000:0.0}s onto '{_doc.Audio.Tracks[lane].Name}' at {_recStartMs / 1000:0.000}s");
    }

    /// <summary>
    /// Shift applied to new recordings. Zero by default: the picture is driven
    /// by the output device's own position, so speech timed to it lands where
    /// spoken. Adjust if a particular microphone proves consistently late.
    /// </summary>
    internal double RecordOffsetMs = 0;

    #endregion

    #region test hooks

    internal void LoadForTest(string[] paths, int cols, int rows, Color key, int tolerance)
    {
        Import(paths);
        Slice(cols, rows);
        _key = key;
        _keySwatch.BackColor = key;
        _tol.Value = tolerance;
        _applyAll.Checked = true;
        RemoveKey();
    }

    internal int TestStroke(Tool tool, Color colour, int brush, params Point[] path)
    {
        SetTool(tool);
        _fg = colour;
        _brush.Value = brush;
        var o = CanvasOrigin();
        MouseEventArgs At(Point p) => new(
            MouseButtons.Left, 1, o.X + p.X * _zoom + _zoom / 2, o.Y + p.Y * _zoom + _zoom / 2, 0);

        CanvasDown(null, At(path[0]));
        for (int i = 1; i < path.Length; i++) CanvasMove(null, At(path[i]));
        CanvasUp(null, At(path[^1]));

        uint want = Surface.Pack(colour);
        // Observe the result without EnsureRaster creating a new paint object.
        var pixels = EditingMask && ActiveCelOrNull?.Mask is { } mask ? ImageOps.GetPixels(mask.Pixels) : LayerPixels();
        var s = new Surface(pixels, DocW, DocH);
        int n = 0;
        for (int y = 0; y < DocH; y++)
            for (int x = 0; x < DocW; x++)
                if (s.Get(x, y) == want) n++;
        return n;
    }

    internal void TestNewCanvas(int w, int h)
    {
        _undo.Clear();
        _doc.Dispose();
        _doc = new Document(w, h);
        _cur = 0;
        _activeLayer = 0;
        _selection = null;
        _zoom = 4;
        InvalidateComposite();
        RefreshAll();
    }

    internal void TestSelect(Rectangle r) => _selection = r.IsEmpty ? null : Selection.FromRect(DocW, DocH, r);
    internal void TestSelectFrames(int a, int b) { _selStart = a; _selEnd = b; _cur = a; RefreshAll(); }
    internal void TestMoveFrame(int from, int to) => MoveFrameTo(from, to);
    internal void TestCopyFrames() => CopyFrames();
    internal void TestPasteFrames() => PasteFrames();
    internal void TestDeleteFrames() => DeleteFrames();
    internal void TestTag(string name)
    {
        var before = Before();
        int lo = Math.Min(_selStart, _selEnd), hi = Math.Max(_selStart, _selEnd);
        _doc.Tags.Add(new AnimTag { Name = name, Start = lo, End = hi });
        PushStructure("tag frames", before);
    }
    internal Task TestImportAudio(string path, double ms) => ImportAudio(new[] { path }, -1, ms);
    internal void TestFinishRecording(float[] samples, double startMs)
    {
        // The recording path minus the microphone: same placement and undo.
        var before = _doc.Audio.Clone();
        int lane = SelectedTrackOrNew();
        if (lane < 0) { _doc.Audio.Tracks.Add(new AudioTrack { Name = "Dialogue" }); lane = _doc.Audio.Tracks.Count - 1; }
        var src = _doc.Audio.AddSource(samples, "take");
        _doc.Audio.AddClip(_doc.Audio.Tracks[lane], src, Math.Max(0, startMs - RecordOffsetMs));
        _undo.Push(new AudioEdit("record", before, _doc.Audio.Clone(), src.Bytes));
        RefreshAll();
    }
    internal void TestSetDelays(params int[] ms)
    {
        while (_doc.FrameCount < ms.Length) _doc.InsertFrame(_doc.FrameCount);
        for (int i = 0; i < ms.Length; i++) _doc.Frames[i].Delay = ms[i];
        RefreshAll();
    }
    internal int TestCurrentFrame => _cur;
    internal void TestAutosaveNow() { _dirty = true; Autosave(); }
    internal int TestHistoryCount => _history.Items.Count;
    internal int TestHistoryCurrent => _undo.DoneCount;
    internal void TestHistoryJump(int row) => JumpToHistory(row);
    internal bool TestSaveViaEditor(string path) { _path = path; return SaveProject(false); }
    internal void TestPlayFor(int ms, LoopMode mode, double speed = 1)
    {
        _playback.Mode = mode;
        _playback.Speed = speed;
        _cur = 0; _selStart = _selEnd = 0;
        StartPlay();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(2); }
        _play.Stop();   // freeze where it is, without resetting
        _clock.Stop();
        _engine.Stop();
    }
    internal int TestFrameCount => _doc.FrameCount;
    internal int TestLayerCount => _doc.LayerCount;
    internal void TestAddFrame() => AddBlankFrame();
    internal void TestAddLayer() => AddLayer();
    internal void TestUndo() => Undo();
    internal void TestRedo() => Redo();
    internal Document TestDocument => _doc;
    internal long TestUndoBytes => _undo.Used;
    internal void TestSetActiveLayer(int i) { _activeLayer = i; RefreshAll(); }
    internal void TestSave(string path) { FlintFormat.Save(_doc, path); _path = path; _dirty = false; }

    internal bool TestOpen(string path)
    {
        var r = FlintFormat.Load(path);
        if (!r.Ok) return false;
        _undo.Clear();
        _doc.Dispose();
        _doc = r.Document!;
        _cur = 0;
        _activeLayer = _doc.Layers.Count - 1;
        InvalidateComposite();
        RefreshAll();
        return true;
    }

    #endregion
}
