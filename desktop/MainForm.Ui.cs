/* Window layout: icon toolbar, grouped tool icons, the context options bar,
 * the right-hand tabs + collapsible Properties, and the one-row transport.
 * Split out of MainForm.cs; behaviour lives in the other partials. */
namespace Flint;

public sealed partial class MainForm
{
    private readonly ToolTip _tips = Theme.MakeTip();
    private readonly List<(ToolStripItem item, string key)> _tsIcons = new();
    private readonly Dictionary<Tool, Control[]> _toolOptions = new();
    private readonly List<Control> _optControls = new();
    private Label _optTitle = null!, _optNone = null!;

    private static FlatGroup Group(string title, int height, bool collapsible = false, string? key = null, bool collapsed = false)
    {
        var g = new FlatGroup
        {
            Text = title, Dock = DockStyle.Top, Height = height, Padding = new Padding(0, 4, 0, 4),
            Collapsible = collapsible, PersistKey = key,
        };
        return g;
    }

    private IconButton Ib(string key, string tip, Action act, int size = 30)
    {
        var b = new IconButton { IconKey = key, Size = new Size(size, size), AccessibleName = tip };
        b.Click += (_, _) => act();
        _tips.SetToolTip(b, tip);
        return b;
    }

    /// <summary>Centre a control vertically in a flow row of the given height.</summary>
    private static T Flow<T>(T c, int rowH, int left = 2) where T : Control
    {
        c.Margin = new Padding(left, Math.Max(0, (rowH - c.Height) / 2), 2, 0);
        return c;
    }

    // ---------- top toolbar ----------

    private ToolStrip BuildToolStrip()
    {
        var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 3, 6, 3), ImageScalingSize = new Size(18, 18) };
        void Item(string text, string key, string tip, Action a)
        {
            var it = new ToolStripButton(text, null, (_, _) => a())
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ToolTipText = tip,
                ImageAlign = ContentAlignment.MiddleLeft, Padding = new Padding(3, 0, 3, 0),
            };
            _tsIcons.Add((it, key));
            ts.Items.Add(it);
        }
        Item("New", "new", "New canvas (Ctrl+N)", NewCanvas);
        Item("Open", "open", "Open (Ctrl+O)", OpenProject);
        Item("Save", "save", "Save (Ctrl+S)", () => SaveProject(false));
        ts.Items.Add(new ToolStripSeparator());
        Item("Import", "import", "Import images (Ctrl+I)", ChooseFiles);
        Item("Export…", "export", "Export (Ctrl+E)", ExportAny);
        ts.Items.Add(new ToolStripSeparator());
        Item("Undo", "undo", "Undo (Ctrl+Z)", Undo);
        Item("Redo", "redo", "Redo (Ctrl+Y)", Redo);
        RefreshToolStripIcons();
        return ts;
    }

    private void RefreshToolStripIcons()
    {
        foreach (var (item, key) in _tsIcons) item.Image = ToolIcons.Get(key, 18, Theme.Text);
    }

    // ---------- left: grouped icon tools ----------

    private Panel BuildToolbox()
    {
        var box = new Panel { Dock = DockStyle.Left, Width = 92, Padding = new Padding(8, 4, 6, 4), AutoScroll = true };
        var info = new Dictionary<Tool, (string icon, string label, string tip)>
        {
            [Tool.ObjectSelect] = ("move", "Object", "Pointer (V, 0): pick and move objects; double-click to edit"),
            [Tool.Move] = ("move", "Move", "Move (press V twice): drag the layer, or the selected pixels"),
            [Tool.Transform] = ("transform", "Xform", "Scale / free transform (Q, Ctrl+T): scale, rotate"),
            [Tool.Crop] = ("crop", "Crop", "Crop (C): drag the area to keep, Enter"),
            [Tool.Marquee] = ("select-rect", "Sel", "Rectangle select (M). Shift adds, Alt subtracts"),
            [Tool.EllipseSelect] = ("select-ellipse", "Sel O", "Oval select (M twice)"),
            [Tool.Lasso] = ("lasso", "Lasso", "Lasso (L): draw round an area"),
            [Tool.Wand] = ("wand", "Wand", "Magic wand (W): click a colour"),
            [Tool.Eyedropper] = ("eyedropper", "Pick", "Eyedropper (I)"),
            [Tool.Pan] = ("pan", "Pan", "Pan (H)"),
            [Tool.Pencil] = ("pencil", "Pencil", "Pencil (B, Y)"),
            [Tool.Eraser] = ("eraser", "Eraser", "Eraser (E)"),
            [Tool.Fill] = ("fill", "Fill", "Paint bucket (G)"),
            [Tool.Gradient] = ("gradient", "Grad", "Gradient (G twice): drag"),
            [Tool.Clone] = ("clone", "Clone", "Rubber stamp (S): Alt+click the source, then paint"),
            [Tool.Text] = ("text", "Text", "Text (T): click where it goes"),
            [Tool.Line] = ("line", "Line", "Line (N)"),
            [Tool.Rectangle] = ("rect", "Rect", "Rectangle (U)"),
            [Tool.Ellipse] = ("ellipse", "Oval", "Oval (U twice)"),
            [Tool.Pivot] = ("pivot", "Pivot", "Pivot point for game export"),
            [Tool.Hitbox] = ("hitbox", "Hitbox", "Hitbox for game export"),
        };
        var sections = new (string title, Tool[] tools)[]
        {
            ("Select", new[] { Tool.ObjectSelect, Tool.Move, Tool.Transform, Tool.Crop, Tool.Marquee, Tool.EllipseSelect, Tool.Lasso, Tool.Wand }),
            ("Bitmap", new[] { Tool.Pencil, Tool.Eraser, Tool.Fill, Tool.Gradient, Tool.Clone, Tool.Eyedropper, Tool.Text }),
            ("Vector", new[] { Tool.Line, Tool.Rectangle, Tool.Ellipse }),
            ("Web / Game", new[] { Tool.Pivot, Tool.Hitbox }),
            ("View", new[] { Tool.Pan }),
        };
        var groups = new List<FlatGroup>();
        foreach (var (title, tools) in sections)
        {
            int rows = (tools.Length + 1) / 2;
            var g = Group(title, FlatGroup.HeaderH + 4 + rows * 28);
            for (int i = 0; i < tools.Length; i++)
            {
                var t = tools[i];
                var (icon, label, tip) = info[t];
                var b = new IconButton { IconKey = icon, Text = label, Size = new Size(26, 26), AccessibleName = tip, Left = i % 2 * 28, Top = FlatGroup.HeaderH + 2 + i / 2 * 28, IconSize = 18 };
                b.Click += (_, _) => SetTool(t);
                _tips.SetToolTip(b, tip);
                _toolButtons[t] = b;
                g.Controls.Add(b);
            }
            groups.Add(g);
        }


        // Top-docked groups stack in reverse add order.
        for (int i = groups.Count - 1; i >= 0; i--) box.Controls.Add(groups[i]);
        return box;
    }

    /// <summary>Foreground / background swatches and swap, at the right end of the options bar.</summary>
    private Panel BuildColourBox()
    {
        var p = new Panel { Dock = DockStyle.Right, Width = 118 };
        _fgSwatch = Swatch(8, 7, _fg, () => _fg, c => _fg = c);
        _bgSwatch = Swatch(42, 7, _bg, () => _bg, c => _bg = c);
        _fgSwatch.Size = _bgSwatch.Size = new Size(30, 24);
        _tips.SetToolTip(_fgSwatch, "Foreground colour (left mouse)");
        _tips.SetToolTip(_bgSwatch, "Secondary paint colour (right mouse; does not fill the canvas)");
        var swap = new IconButton { Text = "Swap", ShowText = true, Size = new Size(34, 24), Left = 78, Top = 7, Font = new Font(Font.FontFamily, 7.5f) };
        swap.Click += (_, _) => SetColours(_bg, _fg);
        _tips.SetToolTip(swap, "Swap foreground and background");
        p.Controls.Add(_fgSwatch);
        p.Controls.Add(_bgSwatch);
        p.Controls.Add(swap);
        return p;
    }

    private void StyleToolButtons()
    {
        foreach (var (k, b) in _toolButtons) ((IconButton)b).Active = k == _tool;
    }

    // ---------- context options bar ----------

    private Panel BuildOptionsBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 40, Tag = "optbar" };
        bar.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 0, bar.Height - 1, bar.Width, bar.Height - 1); };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(8, 0, 4, 1) };

        Label L(string text, int w) => new() { Text = text, Width = w, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
        _optTitle = new Label { Width = 78, Height = 20, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold), Tag = "keep" };
        _optTitle.ForeColor = Theme.Accent;
        _optNone = L("No options for this tool", 170); _optNone.Tag = "muted";

        var lSize = L("Size", 32);
        _brush = Num(0, 0, 50, 1, 1, 64);
        _filled = new MintCheckBox { Text = "filled", Width = 62, Height = 22 };
        _editableShapes = new MintCheckBox { Text = "editable", Width = 82, Height = 22, Checked = true };
        _tips.SetToolTip(_editableShapes, "Keep rectangles and ellipses editable. Pixel selections and mask painting use raster shapes.");
        var lTol = L("Tolerance", 62);
        _fillTol = Num(0, 0, 52, 0, 0, 255);
        BuildToolOptions(flow);   // creates _contiguous, _resample, _gradType
        _contiguous.Width = 90; _contiguous.Height = 22;
        var lRes = L("Resample", 58);
        _resample.Width = 96;
        var lGrad = L("Gradient", 54);
        _gradType.Width = 118;
        _tips.SetToolTip(_brush, "Brush / line size in pixels");
        _tips.SetToolTip(_fillTol, "Colour tolerance for fill and magic wand");
        _tips.SetToolTip(_filled, "Draw rectangles and ovals solid");

        flow.Controls.Add(Flow(_optTitle, 36, 4));
        var all = new Control[] { _optNone, lSize, _brush, _filled, _editableShapes, lTol, _fillTol, _contiguous, lRes, _resample, lGrad, _gradType };
        foreach (var c in all) flow.Controls.Add(Flow(c, 36));
        _optControls.AddRange(all);
        _toolOptions[Tool.Pencil] = new Control[] { lSize, _brush };
        _toolOptions[Tool.Eraser] = _toolOptions[Tool.Pencil];
        _toolOptions[Tool.Line] = _toolOptions[Tool.Pencil];
        _toolOptions[Tool.Clone] = _toolOptions[Tool.Pencil];
        _toolOptions[Tool.Rectangle] = new Control[] { lSize, _brush, _filled, _editableShapes };
        _toolOptions[Tool.Ellipse] = _toolOptions[Tool.Rectangle];
        _toolOptions[Tool.Fill] = new Control[] { lTol, _fillTol };
        _toolOptions[Tool.Wand] = new Control[] { lTol, _fillTol, _contiguous };
        _toolOptions[Tool.Gradient] = new Control[] { lGrad, _gradType };
        _toolOptions[Tool.Transform] = new Control[] { lRes, _resample };
        _toolOptions[Tool.Move] = _toolOptions[Tool.Transform];
        _toolOptions[Tool.ObjectSelect] = _toolOptions[Tool.Transform];
        bar.Controls.Add(flow);
        bar.Controls.Add(BuildColourBox());
        return bar;
    }

    private void UpdateOptionsBar()
    {
        if (_optTitle == null) return;
        _optTitle.Text = _toolButtons.TryGetValue(_tool, out var b) ? b.Text : "";
        _toolOptions.TryGetValue(_tool, out var show);
        foreach (var c in _optControls) c.Visible = show != null && Array.IndexOf(show, c) >= 0;
        _optNone.Visible = show == null;
    }

    // ---------- right: tabs + Properties ----------

    private Panel BuildSidePanel()
    {
        var side = new Panel { Dock = DockStyle.Right, Width = 282, Padding = new Padding(6) };

        var gExport = Group("Export settings", 150, true, "props.export");
        gExport.Controls.Add(Lbl("GIF colours", 8, 18, 72));
        _colors = Num(84, 18, 58, 256, 2, 256);
        gExport.Controls.Add(_colors);
        _dither = new MintCheckBox { Text = "dither", Left = 150, Top = 20, Width = 66 };
        gExport.Controls.Add(_dither);
        _loop = new MintCheckBox { Text = "loop", Left = 8, Top = 44, Width = 54, Checked = true };
        gExport.Controls.Add(_loop);
        _includeAudio = new MintCheckBox { Text = "audio", Left = 64, Top = 44, Width = 60, Checked = true };
        gExport.Controls.Add(_includeAudio);
        _rangeOnly = new MintCheckBox { Text = "selection", Left = 128, Top = 44, Width = 84 };
        gExport.Controls.Add(_rangeOnly);
        gExport.Controls.Add(Lbl("WebM crf", 8, 70, 62));
        _webmQuality = Num(74, 70, 52, 30, 0, 63);
        gExport.Controls.Add(_webmQuality);
        gExport.Controls.Add(Lbl("Sheet cols", 8, 98, 66));
        _sheetCols = Num(78, 98, 48, 0, 0, 999);
        gExport.Controls.Add(_sheetCols);
        gExport.Controls.Add(new Label
        {
            Text = "Video needs ffmpeg. WebM keeps alpha; MP4 does not.",
            Left = 134, Top = 66, Width = 114, Height = 76, ForeColor = SystemColors.GrayText,
        });

        var gKey = Group("Background removal", 106, true, "props.key");
        _keySwatch = Swatch(10, 20, _key, () => _key, c => _key = c);
        _keySwatch.Width = 28;
        _keySwatch.Height = 26;
        gKey.Controls.Add(_keySwatch);
        gKey.Controls.Add(Btn("Pick", 44, 20, 56, (_, _) => SetTool(Tool.Eyedropper)));
        gKey.Controls.Add(Btn("Remove", 106, 20, 142, (_, _) => RemoveKey()));
        gKey.Controls.Add(Lbl("Tolerance", 8, 52, 62));
        _tol = Num(74, 52, 56, 16, 0, 255);
        gKey.Controls.Add(_tol);
        _applyAll = new MintCheckBox { Text = "all frames", Left = 140, Top = 54, Width = 100, Checked = true };
        gKey.Controls.Add(_applyAll);
        gKey.Controls.Add(new Label
        {
            Text = "Applies to the active layer.",
            Left = 10, Top = 78, Width = 238, Height = 20, ForeColor = SystemColors.GrayText,
        });

        var gTrans = Group("Transform", 146, true, "props.transform");
        gTrans.Controls.Add(Btn("Flip H", 8, 20, 74, (_, _) => Flip(true)));
        gTrans.Controls.Add(Btn("Flip V", 88, 20, 74, (_, _) => Flip(false)));
        gTrans.Controls.Add(Btn("Trim", 168, 20, 74, (_, _) => Trim()));
        gTrans.Controls.Add(Lbl("Scale", 8, 52, 44));
        _scaleBy = Num(56, 52, 56, 2m, 0.05m, 32m, 2, 0.25m);
        gTrans.Controls.Add(_scaleBy);
        gTrans.Controls.Add(Btn("Apply", 118, 51, 56, (_, _) => ScaleAll()));
        _smooth = new MintCheckBox { Text = "smooth", Visible = false };
        gTrans.Controls.Add(_smooth);
        _imageQuality = new ComboBox { Left = 176, Top = 52, Width = 74, DropDownWidth = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        _imageQuality.Items.AddRange(Enum.GetNames<Resample>()); _imageQuality.SelectedIndex = 0;
        _imageQuality.SelectedIndexChanged += (_,_) => _smooth.Checked = _imageQuality.SelectedIndex == (int)Resample.Smooth;
        _tips.SetToolTip(_imageQuality, "Resize quality: Nearest keeps hard pixels; RotSprite improves pixel-art edges; Smooth blends pixels.");
        gTrans.Controls.Add(_imageQuality);
        gTrans.Controls.Add(Lbl("Canvas", 8, 84, 48));
        _rw = Num(56, 84, 54, 0, 0, 20000);
        _rh = Num(114, 84, 54, 0, 0, 20000);
        gTrans.Controls.Add(_rw);
        gTrans.Controls.Add(_rh);
        gTrans.Controls.Add(Btn("Set", 174, 83, 44, (_, _) => ResizeCanvas()));
        _center = new MintCheckBox { Text = "centre on resize", Left = 8, Top = 114, Width = 140, Checked = true };
        gTrans.Controls.Add(_center);

        var gCanvas = Group("Canvas background", 66, true, "props.canvas");
        gCanvas.Controls.Add(Lbl("Colour", 8, 28, 46));
        _canvasBackgroundSwatch = Swatch(58, 24, Color.White,
            () => CanvasBackgroundLayer?.CelAt(0)?.Objects.FirstOrDefault() is RasterObject r ? r.Pixels.GetPixel(0, 0) : Color.White,
            c => SetCanvasBackground(c));
        _canvasBackgroundSwatch.AccessibleName = "Canvas background colour";
        gCanvas.Controls.Add(_canvasBackgroundSwatch);
        gCanvas.Controls.Add(Btn("Transparent", 102, 26, 140, (_, _) => SetCanvasBackground(null)));
        _tips.SetToolTip(_canvasBackgroundSwatch, "Canvas colour: fills transparent areas on every frame. Undo restores the previous background.");

        // Layers tab: list, icon bar, opacity / blend / shared.
        var gLayers = new FlatGroup { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4) };
        _layers = new LayersPanel { Dock = DockStyle.Fill };
        _layers.ActiveChanged += i => { _activeLayer = i; RefreshAll(); };
        _layers.Changed += () => { Touch(); InvalidateComposite(); RefreshAll(); };
        _layers.MaskClicked += i => { _activeLayer = i; ToggleEditMask(); };

        var layerBar = new Panel { Dock = DockStyle.Bottom, Height = 66 };
        int lx = 0;
        foreach (var (key, tip, act) in new (string, string, Action)[]
        {
            ("add-layer", "Add layer", AddLayer), ("duplicate", "Duplicate layer", DuplicateLayer),
            ("delete-layer", "Delete layer", DeleteLayer), ("layer-up", "Move layer up", () => MoveLayer(1)),
            ("layer-down", "Move layer down", () => MoveLayer(-1)),
        })
        {
            var b = Ib(key, tip, act, 30);
            b.Left = lx; b.Top = 2;
            lx += 34;
            layerBar.Controls.Add(b);
        }
        layerBar.Controls.Add(Lbl("Opacity", 0, 38, 52));
        _layerOpacity = Num(54, 38, 46, 100, 0, 100);
        _layerOpacity.ValueChanged += (_, _) => { if (!_syncing) SetLayerOpacity((int)_layerOpacity.Value); };
        layerBar.Controls.Add(_layerOpacity);
        _blend = new ComboBox
        {
            Left = 104, Top = 39, Width = 74, DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 120,
        };
        _blend.Items.AddRange(Enum.GetNames<BlendMode>().Cast<object>().ToArray());
        _blend.SelectedIndexChanged += (_, _) => { if (!_syncing) SetLayerBlend(); };
        layerBar.Controls.Add(_blend);
        _sharedBox = new MintCheckBox { Text = "shared", Left = 184, Top = 40, Width = 62 };
        _sharedBox.CheckedChanged += (_, _) => { if (!_syncing) SetLayerShared(_sharedBox.Checked); };
        layerBar.Controls.Add(_sharedBox);
        gLayers.Controls.Add(_layers);
        gLayers.Controls.Add(layerBar);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            SplitterWidth = 5, FixedPanel = FixedPanel.None,
        };
        // Properties: the loose groups, now collapsible, in their own scroll area.
        var settings = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        foreach (var g in new[] { gExport, gKey, gCanvas, gTrans })
        {
            g.Collapsed = Theme.IsCollapsed(g.PersistKey!, g != gTrans && g != gCanvas);
            settings.Controls.Add(g);
        }
        var props = new Label { Text = "PROPERTIES", Dock = DockStyle.Top, Height = 22, Tag = "muted", TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold) };
        settings.Controls.Add(props);

        var tabs = new FlatTabs { Dock = DockStyle.Fill };
        var layersTab = new TabPage("Layers");
        layersTab.Controls.Add(gLayers);
        var historyTab = new TabPage("History");
        _history = new ListBox
        {
            Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 20,
            BorderStyle = BorderStyle.None,
        };
        _history.DrawItem += DrawHistoryItem;
        _undo.Pushed += () => { if (!_historyBusy) RefreshHistory(); };
        _history.SelectedIndexChanged += (_, _) => { if (!_historyBusy) JumpToHistory(_history.SelectedIndex); };
        historyTab.Controls.Add(_history);
        tabs.Add(layersTab, "duplicate");
        tabs.Add(BuildObjectsTab(), "rect");
        tabs.Add(historyTab, "history");
        tabs.Add(BuildLibraryTab(), "library");
        tabs.Add(BuildEffectsTab(), "effects");
        split.Panel1.Controls.Add(tabs);
        split.Panel2.Controls.Add(settings);
        split.Panel1MinSize = 150;
        split.Panel2MinSize = 120;
        side.Controls.Add(split);
        side.Resize += (_, _) =>
        {
            int want = Math.Max(split.Panel1MinSize, side.ClientSize.Height * 2 / 5);
            if (split.Height > want + split.Panel2MinSize && split.SplitterDistance != want && !_splitPlaced)
                split.SplitterDistance = want;
        };
        split.SplitterMoved += (_, _) => _splitPlaced = side.Visible && split.Focused;
        return side;
    }

    // ---------- bottom: one icon bar + timeline ----------

    private Panel BuildTransport()
    {
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 216, Padding = new Padding(6, 4, 6, 4) };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, Padding = new Padding(0, 2, 0, 0) };
        const int H = 36;
        void Add(Control c, int left = 2) => bar.Controls.Add(Flow(c, H, left));
        Control Sep() => new VSep { Width = 9, Height = 22 };
        Control Lab(string t, int w) => new Label { Text = t, Width = w, Height = 20, TextAlign = ContentAlignment.MiddleLeft };

        _viewSeg = new SegmentedControl(("Timeline", "audio"), ("Frames", "add-frame")) { Width = 176 };
        _tips.SetToolTip(_viewSeg, "Timeline: time-scaled frames and audio. Frames: equal-size frame strip.");
        _viewSeg.SelectedChanged += i =>
        {
            _timeline.FramesView = i == 1;
            if (!_viewQuiet) Theme.FramesView = i == 1;
            if (_bottom != null) RebuildStrip();
        };
        Add(_viewSeg, 0);
        Add(Sep(), 4);
        _playBtn = new IconButton
        {
            Text = "Play", Size = new Size(34, 30), IconResolver = () => _playBtn.Text == "Stop" ? "stop" : "play",
            AccessibleName = "Play / stop",
        };
        _playBtn.Click += (_, _) => TogglePlay();
        _tips.SetToolTip(_playBtn, "Play / stop (Space)");
        Add(_playBtn);

        _modeBox = new ComboBox { Width = 86, DropDownStyle = ComboBoxStyle.DropDownList };
        _modeBox.Items.AddRange(new object[] { "Loop", "Ping-pong", "Once" });
        _modeBox.SelectedIndex = 0;
        _modeBox.SelectedIndexChanged += (_, _) => _playback.Mode = (LoopMode)_modeBox.SelectedIndex;
        _tips.SetToolTip(_modeBox, "Playback mode");
        Add(_modeBox);
        _speedBox = new ComboBox { Width = 62, DropDownStyle = ComboBoxStyle.DropDownList };
        _speedBox.Items.AddRange(new object[] { "0.25×", "0.5×", "1×", "1.5×", "2×", "4×" });
        _speedBox.SelectedIndex = 2;
        _speedBox.SelectedIndexChanged += (_, _) =>
            _playback.Speed = new[] { 0.25, 0.5, 1, 1.5, 2, 4 }[_speedBox.SelectedIndex];
        _tips.SetToolTip(_speedBox, "Playback speed");
        Add(_speedBox);
        Add(Lab("Delay", 36), 6);
        _delay = Num(0, 0, 62, 100, 10, 60000, 0, 10);
        _delay.ValueChanged += (_, _) => { if (!_syncing) SetFrameDelay((int)_delay.Value); };
        _tips.SetToolTip(_delay, "Delay of the current frame, in milliseconds");
        Add(_delay);
        var all = new IconButton { Text = "All", ShowText = true, Size = new Size(36, 30) };
        all.Click += (_, _) => DelayAll();
        _tips.SetToolTip(all, "Use this delay for every frame");
        Add(all);
        Add(Sep(), 4);
        Add(Ib("prev", "Move frame earlier", () => MoveFrame(-1)));
        Add(Ib("next", "Move frame later", () => MoveFrame(1)));
        Add(Ib("add-frame", "Add blank frame", AddBlankFrame));
        Add(Ib("dup-frame", "Duplicate frame", DuplicateFrame));
        Add(Ib("delete-frame", "Delete frame", DeleteFrame));
        Add(Ib("tag", "Tag the selected frames…", TagSelection));
        Add(Sep(), 4);
        Add(Ib("zoom-out", "Zoom out (Ctrl+-)", () => Zoom(-1)));
        Add(Ib("zoom-in", "Zoom in (Ctrl++)", () => Zoom(1)));
        Add(Sep(), 4);

        // Onion skin: one toggle; its settings appear beside it only when on.
        _onionBox = new MintCheckBox { Text = "Onion", IconKey = "onion", Size = new Size(30, 30) };
        _onionBox.CheckedChanged += (_, _) => { _onionSet.Enabled = _onionBox.Checked; _canvas.Invalidate(); };
        _tips.SetToolTip(_onionBox, "Onion skin: show neighbouring frames faintly");
        Add(_onionBox);
        var onion = new List<Control>();
        void Extra(Control c, string tip, int left = 2) { _tips.SetToolTip(c, tip); onion.Add(c); Add(c, left); c.Visible = false; }
        _onionBefore = Num(0, 0, 40, 1, 0, 9);
        _onionBefore.ValueChanged += (_, _) => { _onionSet.Before = (int)_onionBefore.Value; _canvas.Invalidate(); };
        Extra(_onionBefore, "Onion: frames before");
        _onionAfter = Num(0, 0, 40, 0, 0, 9);
        _onionAfter.ValueChanged += (_, _) => { _onionSet.After = (int)_onionAfter.Value; _canvas.Invalidate(); };
        Extra(_onionAfter, "Onion: frames after");
        _onionOpacity = Num(0, 0, 48, 35, 5, 100);
        _onionOpacity.ValueChanged += (_, _) => { _onionSet.Opacity = (float)_onionOpacity.Value / 100f; _canvas.Invalidate(); };
        Extra(_onionOpacity, "Onion: opacity %");
        _onionTint = new MintCheckBox { Text = "tint", Width = 48, Height = 22, Checked = true };
        _onionTint.CheckedChanged += (_, _) => { _onionSet.Tint = _onionTint.Checked; _canvas.Invalidate(); };
        Extra(_onionTint, "Tint the onion frames (red before, green after)", 6);
        _onionBox.CheckedChanged += (_, _) => { foreach (var c in onion) c.Visible = _onionBox.Checked; };
        Add(Sep(), 4);

        Add(Ib("audio", "Import audio…", ChooseAudio));
        Add(Ib("add-layer", "Add audio track", AddTrack));
        _recBtn = new IconButton
        {
            Text = "● Rec", Size = new Size(30, 30), Danger = true,
            IconResolver = () => _recBtn.Text.StartsWith("●") ? "record" : "stop", AccessibleName = "Record",
        };
        _recBtn.Click += (_, _) => ToggleRecord();
        _tips.SetToolTip(_recBtn, "Record from the microphone");
        Add(_recBtn);
        _micBox = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        try
        {
            var mics = AudioEngine.Microphones();
            _micBox.Items.AddRange(mics.Cast<object>().ToArray());
            if (mics.Count > 0) _micBox.SelectedIndex = 0;
        }
        catch { /* no audio stack: recording stays disabled */ }
        if (_micBox.Items.Count == 0) { _micBox.Items.Add("no microphone"); _micBox.SelectedIndex = 0; _micBox.Enabled = false; _recBtn.Enabled = false; }
        _tips.SetToolTip(_micBox, "Microphone");
        Add(_micBox);
        _level = new ProgressBar { Width = 46, Height = 12, Maximum = 100 };
        Add(_level);

        _timeline = new TimelinePanel { Dock = DockStyle.Fill };
        WireTimeline();
        if (Theme.FramesView) SetFramesView(true, false);
        bottom.Controls.Add(_timeline);
        bottom.Controls.Add(bar);
        return bottom;
    }

    // ---------- theme ----------

    private void AddThemeMenu(ToolStripMenuItem view)
    {
        view.DropDownItems.Add(new ToolStripSeparator());
        var theme = new ToolStripMenuItem("&Theme");
        var dark = new ToolStripMenuItem("&Dark", null, (_, _) => Theme.Set(true)) { Checked = Theme.Dark };
        var light = new ToolStripMenuItem("&Light", null, (_, _) => Theme.Set(false)) { Checked = !Theme.Dark };
        theme.DropDownItems.Add(dark);
        theme.DropDownItems.Add(light);
        Theme.Changed += () => { dark.Checked = Theme.Dark; light.Checked = !Theme.Dark; };
        view.DropDownItems.Add(theme);
    }

    protected override void OnThemeChanged()
    {
        base.OnThemeChanged();
        RefreshToolStripIcons();
        _optTitle.ForeColor = Theme.Accent;
        StyleToolButtons();
        _canvas.Invalidate();
        _history.Invalidate();
    }
}

public sealed partial class MainForm
{
    private SegmentedControl _viewSeg = null!;

    /// <summary>Switch the bottom panel between Timeline and Frames view.</summary>
    private bool _viewQuiet;

    private void SetFramesView(bool frames, bool persist)
    {
        _viewQuiet = !persist;
        _viewSeg.SelectedIndex = frames ? 1 : 0;
        _viewQuiet = false;
    }

    internal void TestSetFramesView(bool frames) => SetFramesView(frames, false);
    internal TimelinePanel TestTimeline => _timeline;
    internal (int lo, int hi) TestSelRange => (Math.Min(_selStart, _selEnd), Math.Max(_selStart, _selEnd));
}
