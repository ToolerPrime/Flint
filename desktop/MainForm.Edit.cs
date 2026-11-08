/* Image-editor tools: selections, move and free transform, crop, clipboard,
 * drop-to-canvas, the library, filters, gradient, text and clone.
 *
 * Placed, pasted and transformed images live as their own objects in the cel
 * and keep their original pixels, so moving, rotating and scaling them never
 * degrades them. Painting on a layer merges its objects into plain pixels
 * first, because the paint tools write one bitmap.
 */
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Flint;

public sealed partial class MainForm
{
    // ---------- edit state ----------
    private SelectMode _selMode;
    private Rectangle? _shapeDraft;            // marquee / ellipse select / crop while dragging
    private List<Point>? _lasso;
    private Rectangle? _cropRect;
    private XformSession? _xf;
    private XformDrag _xfDrag;
    private Point? _cloneSrc, _cloneOffset;
    private Point? _clipOrigin;
    private Size _clipSize;
    private ComboBox _resample = null!, _gradType = null!;
    private CheckBox _contiguous = null!;
    private LibraryPanel _library = null!;

    /// <summary>Headless tests set this instead of the real keyboard and clipboard.</summary>
    internal Keys? TestModifiers;
    internal Bitmap? TestClipboard;
    private Keys Mods => TestModifiers ?? ModifierKeys;

    private Resample CurrentResample => (Resample)Math.Max(0, _resample.SelectedIndex);

    /// <summary>One transform in progress: what is being moved and how far.</summary>
    private sealed class XformSession
    {
        public bool Individual, PixelGrid = true, GuideAligned;
        public Document Before = null!;
        public int Layer, Frame;
        public readonly List<(int index, RasterObject orig)> Targets = new();
        public RectangleF Box;
        public double Tx, Ty, Sx = 1, Sy = 1, Rot, SkewX;
        public Resample? ForceMode;
        public string Label = "transform";
        public bool Lifted;

        public double Cx => Box.X + Box.Width / 2.0;
        public double Cy => Box.Y + Box.Height / 2.0;

        public Affine Matrix
        {
            get
            {
                var m = Affine.Translate(-Cx, -Cy)
                    .Then(Affine.Scale(Sx, Sy))
                    .Then(Affine.Skew(SkewX, 0))
                    .Then(Affine.Rotate(Rot))
                    .Then(Affine.Translate(Cx + Tx, Cy + Ty));
                // Whole-number linear parts (quarter turns, flips, 2×) land on
                // the pixel grid only with a whole-number offset.
                bool integral = new[] { m.A, m.B, m.C, m.D }.All(v => v == Math.Round(v));
                return integral && PixelGrid && !GuideAligned ? m with { Tx = Math.Round(m.Tx), Ty = Math.Round(m.Ty) } : m;
            }
        }

        public bool IsIdentity => Matrix.IsIdentity;

        public PointF[] Corners()
        {
            var m = Matrix;
            var b = Box;
            return new[] { (b.Left, b.Top), (b.Right, b.Top), (b.Right, b.Bottom), (b.Left, b.Bottom) }
                .Select(p => { var (x, y) = m.Apply(p.Item1, p.Item2); return new PointF((float)x, (float)y); }).ToArray();
        }
    }

    private enum XformMode { None, Move, Scale, Rotate }

    private struct XformDrag
    {
        public XformMode Mode;
        public int Handle;           // 0..7: TL, T, TR, R, BR, B, BL, L
        public PointF Start;
        public double Tx, Ty, Sx, Sy, Rot;
    }

    private static readonly (int ux, int uy)[] HandleUnits =
        { (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0) };

    #region UI

    private void BuildEditMenus(MenuStrip menu, ToolStripMenuItem edit, ToolStripMenuItem layerMenu)
    {
        edit.DropDownItems.Add(new ToolStripSeparator());
        Add(edit, "Cu&t", Keys.Control | Keys.X, Cut);
        Add(edit, "&Copy", Keys.Control | Keys.C, () => Copy(false));
        edit.DropDownItems.Add("Copy &merged", null, (_, _) => Copy(true));
        Add(edit, "&Paste", Keys.Control | Keys.V, Paste);
        edit.DropDownItems.Add(new ToolStripSeparator());
        var xform = new ToolStripMenuItem("&Transform");
        Add(xform, "&Free transform", Keys.Control | Keys.T, () => SetTool(Tool.Transform));

        xform.DropDownItems.Add(new ToolStripSeparator());
        Add(xform, "Rotate 90° &clockwise", Keys.Control | Keys.Shift | Keys.D9, () => QuickTransform("rotate 90°", s => s.Rot = 90));
        Add(xform, "Rotate 90° &anticlockwise", Keys.Control | Keys.Shift | Keys.D7, () => QuickTransform("rotate −90°", s => s.Rot = -90));
        xform.DropDownItems.Add("Rotate &180°", null, (_, _) => QuickTransform("rotate 180°", s => s.Rot = 180));
        xform.DropDownItems.Add("Flip &horizontal", null, (_, _) => QuickTransform("flip horizontal", s => s.Sx = -1));
        xform.DropDownItems.Add("Flip &vertical", null, (_, _) => QuickTransform("flip vertical", s => s.Sy = -1));
        edit.DropDownItems.Add(xform);

        layerMenu.DropDownItems.Add(new ToolStripSeparator());
        layerMenu.DropDownItems.Add("Merge &objects into pixels", null, (_, _) => { if (BakeActiveCel(true)) RefreshAll(); });
        BuildMaskMenu(layerMenu);

        var select = new ToolStripMenuItem("&Select");
        Add(select, "&All", Keys.Control | Keys.A, SelectAll);
        Add(select, "&Deselect", Keys.Control | Keys.D, () => SetSelection(null, "selection cleared"));
        Add(select, "&Invert", Keys.Control | Keys.Shift | Keys.I, () =>
            SetSelection((_selection ?? new Selection(DocW, DocH)).Invert(), "selection inverted"));
        select.DropDownItems.Add("&Layer content", null, (_, _) =>
            SetSelection(Selection.FromAlpha(LayerPixels(), DocW, DocH), "selected this layer's pixels"));
        select.DropDownItems.Add(new ToolStripSeparator());
        select.DropDownItems.Add("&Grow by 1 pixel", null, (_, _) => { if (_selection != null) SetSelection(_selection.Expand(1), "grew the selection"); });
        select.DropDownItems.Add("&Shrink by 1 pixel", null, (_, _) => { if (_selection != null) SetSelection(_selection.Expand(-1), "shrank the selection"); });

        var image = new ToolStripMenuItem("&Image");
        image.DropDownItems.Add("Rotate canvas 90° &clockwise", null, (_, _) => RotateCanvas(1));
        image.DropDownItems.Add("Rotate canvas 90° &anticlockwise", null, (_, _) => RotateCanvas(3));
        image.DropDownItems.Add("Rotate canvas &180°", null, (_, _) => RotateCanvas(2));
        image.DropDownItems.Add("Flip canvas &horizontal", null, (_, _) => FlipCanvas(true));
        image.DropDownItems.Add("Flip canvas &vertical", null, (_, _) => FlipCanvas(false));
        image.DropDownItems.Add(new ToolStripSeparator());
        image.DropDownItems.Add("Crop to &selection", null, (_, _) =>
        {
            if (_selection is { IsEmpty: false } s) CropTo(s.Bounds); else Say("make a selection first");
        });
        BuildProductivityMenus(edit, image, xform);
        BuildTraceMenu(image);

        var view = new ToolStripMenuItem("&View");
        Add(view, "Zoom &in", Keys.Control | Keys.Oemplus, () => Zoom(1));
        Add(view, "Zoom &out", Keys.Control | Keys.OemMinus, () => Zoom(-1));
        Add(view, "&Actual pixels", Keys.Control | Keys.D1, () => { _zoom = 1; RefreshAll(); });
        Add(view, "&Fit on screen", Keys.Control | Keys.D0, () => { FitZoom(); RefreshAll(); });
        BuildFireworksShortcuts();
        BuildRulerMenu(view);
        BuildWorkspaceMenu(view);
        AddThemeMenu(view);

        var filters = new ToolStripMenuItem("Fil&ters");
        var adjust = new ToolStripMenuItem("&Adjust colour");
        foreach (var f in Adjust.All)
        {
            var target = f.Group == "Adjust" ? adjust : filters;
            target.DropDownItems.Add(f.Name + (f.Params.Length > 0 ? "…" : ""), null, (_, _) => RunFilterDialog(f));
        }
        adjust.DropDownItems.Add(new ToolStripSeparator());
        adjust.DropDownItems.Add("&Replace background colour with foreground…", null, (_, _) => RunFilterDialog(ReplaceDef()));
        filters.DropDownItems.Insert(0, adjust);
        filters.DropDownItems.Insert(1, new ToolStripSeparator());

        int at = menu.Items.IndexOf(edit) + 1;
        menu.Items.Insert(at, select);
        menu.Items.Insert(at + 1, image);
        menu.Items.Insert(at + 2, view);
        menu.Items.Insert(menu.Items.IndexOf(layerMenu) + 1, filters);
    }

    private FilterDef ReplaceDef() => new("Replace colour", "Adjust",
        new[] { new FilterParam("Tolerance", 0, 255, 0) },
        (p, w, h, a, c) => Adjust.ReplaceColour(p, Surface.Pack(_bg), c, (int)a[0]));


    private void BuildToolOptions(Control _)   // controls are placed by BuildOptionsBar
    {
        _contiguous = new MintCheckBox { Text = "contiguous", Checked = true };
        _resample = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        _resample.Items.AddRange(new object[] { "Nearest", "RotSprite", "Smooth" });
        _resample.SelectedIndex = 1;
        _gradType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        _gradType.Items.AddRange(new object[] { "Linear", "Radial", "Linear→clear", "Radial→clear",
            "Rectangle", "Cone", "Starburst", "Bars", "Ripples", "Waves", "Satin", "Folds", "Diamond", "Pinch" });
        _gradType.SelectedIndex = 0;
        _tips.SetToolTip(_contiguous, "Magic wand: only pixels touching the one you click");
        _tips.SetToolTip(_resample, "How rotate and scale pick pixels. RotSprite keeps pixel art crisp; Smooth blends.");
        _tips.SetToolTip(_gradType, "Gradient tool: foreground to background, or foreground fading out");
    }

    private TabPage BuildLibraryTab()
    {
        var page = new TabPage("Library");
        _library = new LibraryPanel { Dock = DockStyle.Fill };
        _library.PlaceRequested += (i, screen) =>
        {
            if (i < 0 || i >= _doc.Library.Count) return;
            var it = _doc.Library[i];
            var at = screen is { } s ? ToImage(_canvas.PointToClient(s).X, _canvas.PointToClient(s).Y) : VisibleCentre();
            PlaceImage(it.Pixels, it.Name, at, null, false);
        };
        _library.AddFilesRequested += files =>
        {
            int n = 0;
            foreach (var f in files)
            {
                try
                {
                    using var imp = Importer.Load(f);
                    AddToLibrary(imp.Frames[0].bmp, Path.GetFileNameWithoutExtension(f), f);
                    n++;
                }
                catch (Exception ex) { Say($"{Path.GetFileName(f)}: {ex.Message}"); }
            }
            if (n > 0) { Touch(); Say($"added {n} image{(n == 1 ? "" : "s")} to the library"); }
        };
        _library.AddSelectionRequested += () =>
        {
            var (bmp, _) = CopyRegion(false);
            if (bmp == null) { Say("nothing to add: this layer is empty there"); return; }
            AddToLibrary(bmp, $"Clip {_doc.Library.Count + 1}", null);
            Touch();
            Say("added the selection to the library");
        };
        _library.Changed += () => { Touch(); };
        page.Controls.Add(_library);
        return page;
    }

    #endregion

    #region selection

    private void SetSelection(Selection? s, string message)
    {
        _selection = s is { IsEmpty: true } ? null : s;
        if (_selection != null) SelectObject(-1);
        _canvas.Invalidate();
        Say(message);
    }

    private SelectMode ModeFromKeys()
    {
        var m = Mods;
        bool shift = (m & Keys.Shift) != 0, alt = (m & Keys.Alt) != 0;
        return shift && alt ? SelectMode.Intersect : shift ? SelectMode.Add : alt ? SelectMode.Subtract : SelectMode.Replace;
    }

    private void ApplyShape(Selection shape)
    {
        var next = shape.Combine(_selection, _selMode);
        SetSelection(next, next.IsEmpty ? "selection cleared"
            : $"selection {next.Bounds.Width}×{next.Bounds.Height}, {next.Count} pixels");
    }

    /// <summary>The active layer on this frame, flattened to canvas-sized straight-alpha bytes.</summary>
    private byte[] LayerPixels()
    {
        var px = new byte[DocW * DocH * 4];
        var cel = ActiveLayer.CelAt(_cur);
        if (cel == null) return px;
        foreach (var o in cel.Objects)
            if (o is RasterObject r && r.Visible)
                Blending.Over(px, DocW, DocH, ImageOps.GetPixels(r.Pixels), r.Width, r.Height, r.X, r.Y, r.Opacity, r.Blend);
        return px;
    }

    #endregion

    #region mouse

    /// <summary>Image coordinates with fractions, for smooth transform handles.</summary>
    private PointF ToImageF(int sx, int sy)
    {
        var o = CanvasOrigin();
        return new PointF((sx - o.X) / (float)_zoom, (sy - o.Y) / (float)_zoom);
    }

    private bool EditDown(MouseEventArgs e, Point p)
    {
        // Working on the canvas takes keyboard focus away from side-panel controls.
        if (!_canvas.Focused) _canvas.Focus();
        switch (_tool)
        {
            case Tool.Marquee:
            case Tool.EllipseSelect:
            case Tool.Crop:
                _selMode = ModeFromKeys();
                _dragging = true;
                _shapeDraft = new Rectangle(Clamp(p), Size.Empty);
                if (_tool == Tool.Crop) _cropRect = null;
                _canvas.Invalidate();
                return true;
            case Tool.Lasso:
                _selMode = ModeFromKeys();
                _dragging = true;
                _lasso = new List<Point> { p };
                return true;
            case Tool.Wand:
                _selMode = ModeFromKeys();
                if (!InDoc(p)) return true;
                ApplyShape(Selection.Wand(LayerPixels(), DocW, DocH, p.X, p.Y, (int)_fillTol.Value, _contiguous.Checked));
                return true;
            case Tool.ObjectSelect:
                if (!PickObject(p)) return true;
                if (e.Button == MouseButtons.Right) { _objects.ContextMenuStrip?.Show(_canvas, e.Location); return true; }
                goto case Tool.Move;
            case Tool.Move:
                if (!BeginTransform("move")) return true;
                _xfDrag = new XformDrag { Mode = XformMode.Move, Start = ToImageF(e.X, e.Y), Tx = _xf!.Tx, Ty = _xf.Ty };
                _dragging = true;
                return true;
            case Tool.Transform:
                if (_xf == null && !BeginTransform("transform")) return true;
                StartXformDrag(e);
                _dragging = true;
                return true;
            case Tool.Rectangle:
            case Tool.Ellipse:
                if (!_editableShapes.Checked || _selection != null || EditingMask) return false;
                if (ActiveLayer.Locked) { Say("This layer is locked."); return true; }
                FinishFloat(); StopPlay(); _primitiveDrawing = true; _dragging = true;
                _shapeDraft = Draw.FromPoints(Clamp(p), Clamp(p)); _canvas.Invalidate(); return true;
            case Tool.Text:
                if (SelectedObject?.TextSource != null && SelectedObject is { } text && new Rectangle(text.X, text.Y, text.Width, text.Height).Contains(p)) EditObjectProperties();
                else AddText(Clamp(p));
                return true;
            case Tool.Gradient:
            case Tool.Clone:
                if ((Mods & Keys.Alt) != 0 && _tool == Tool.Clone)
                {
                    _cloneSrc = Clamp(p);
                    Say($"clone source set at {p.X},{p.Y}. Now paint where you want the copy.");
                    return true;
                }
                if (_tool == Tool.Clone && _cloneSrc == null) { Say("Alt+click to choose where to copy from first"); return true; }
                if (ActiveLayer.Locked) { Say($"layer '{ActiveLayer.Name}' is locked"); return true; }
                EnsurePaintable();
                var raster = ActiveRaster();
                _recorder = new PaintRecorder(_doc, _activeLayer, ActiveFrameKey, raster);
                _strokeBase = ImageOps.GetPixels(raster.Pixels);
                _dragging = true;
                BeginStrokeCaches();
                if (_tool == Tool.Clone)
                {
                    _cloneOffset = new Point(_cloneSrc!.Value.X - p.X, _cloneSrc.Value.Y - p.Y);
                    var s = new Surface(ImageOps.GetPixels(raster.Pixels), DocW, DocH);
                    Draw.CloneDab(s, _strokeBase, p.X, p.Y, _cloneOffset.Value.X, _cloneOffset.Value.Y, (int)_brush.Value, _selection);
                    Commit(s, raster);
                }
                return true;
        }
        return false;
    }

    private bool EditMove(MouseEventArgs e, Point p)
    {
        if (!_dragging) return false;
        if (_primitiveDrawing)
        {
            _shapeDraft = Draw.FromPoints(Clamp(_dragStart), Clamp(p)); _canvas.Invalidate(); return true;
        }
        switch (_tool)
        {
            case Tool.Marquee:
            case Tool.EllipseSelect:
            case Tool.Crop:
                // From the press point each time: FromPoints normalises the box.
                _shapeDraft = Draw.FromPoints(Clamp(_dragStart), Clamp(p));
                _canvas.Invalidate();
                return true;
            case Tool.Lasso:
                if (_lasso != null && (_lasso.Count == 0 || _lasso[^1] != p)) _lasso.Add(p);
                _canvas.Invalidate();
                return true;
            case Tool.ObjectSelect:
            case Tool.Move:
            case Tool.Transform:
                DragXform(e);
                return true;
            case Tool.Gradient:
            {
                if (_strokeBase == null) return true;
                var raster = ActiveRaster();
                var s = new Surface((byte[])_strokeBase.Clone(), DocW, DocH);
                int kind = _gradType.SelectedIndex;
                uint c0 = Surface.Pack(_fg), c1 = kind >= 2 ? Surface.Pack(Color.FromArgb(0, _fg)) : Surface.Pack(_bg);
                bool clear = kind is 2 or 3;
                if (kind >= 4) c1 = Surface.Pack(_bg);   // the patterned kinds always run foreground to background
                if (EditingMask) { c0 = GreyInk(_fg); c1 = clear ? 0xFF000000 : GreyInk(_bg); }
                if (kind >= 4) Draw.Gradient(s, _dragStart, p, c0, c1, (GradientKind)(kind - 1), _selection);   // list index 4 is GradientKind.Rectangle (3)
                else Draw.Gradient(s, _dragStart, p, c0, c1, kind % 2 == 1, _selection);
                Commit(s, raster);
                return true;
            }
            case Tool.Clone:
            {
                if (_strokeBase == null || _cloneOffset is not { } off) return true;
                var raster = ActiveRaster();
                var s = new Surface(ImageOps.GetPixels(raster.Pixels), DocW, DocH);
                int n = Math.Max(Math.Abs(p.X - _dragLast.X), Math.Abs(p.Y - _dragLast.Y));
                for (int t = 1; t <= n; t++)
                {
                    int x = _dragLast.X + (p.X - _dragLast.X) * t / n, y = _dragLast.Y + (p.Y - _dragLast.Y) * t / n;
                    Draw.CloneDab(s, _strokeBase, x, y, off.X, off.Y, (int)_brush.Value, _selection);
                }
                Commit(s, raster);
                _dragLast = p;
                return true;
            }
        }
        return false;
    }

    private bool EditUp(MouseEventArgs e, Point p)
    {
        if (_primitiveDrawing)
        {
            _primitiveDrawing = false; _dragging = false; _shapeDraft = null;
            var box = Draw.FromPoints(Clamp(_dragStart), Clamp(p));
            try { AddEditableObject(EditableSources.Shape(new PrimitiveSpec(_tool == Tool.Ellipse, box.Width, box.Height, InkFor(e.Button), _filled.Checked, (int)_brush.Value), Affine.Translate(box.X, box.Y))); }
            catch (ArgumentException ex) { Say(ex.Message); }
            catch (InvalidOperationException ex) { Say(ex.Message); }
            _canvas.Invalidate(); return true;
        }
        switch (_tool)
        {
            case Tool.Marquee:
            case Tool.EllipseSelect:
                if (!_dragging) return true;
                _dragging = false;
                var r = _shapeDraft ?? Rectangle.Empty;
                _shapeDraft = null;
                // A click without a drag clears the selection, like every editor.
                if (r.Width <= 1 && r.Height <= 1)
                {
                    if (_selMode == SelectMode.Replace) SetSelection(null, "selection cleared");
                    else _canvas.Invalidate();
                    return true;
                }
                ApplyShape(_tool == Tool.Marquee ? Selection.FromRect(DocW, DocH, r) : Selection.FromEllipse(DocW, DocH, r));
                return true;
            case Tool.Crop:
                if (!_dragging) return true;
                _dragging = false;
                var c = _shapeDraft ?? Rectangle.Empty;
                _shapeDraft = null;
                _cropRect = c.Width > 1 || c.Height > 1 ? c : null;
                Say(_cropRect is { } cr ? $"crop to {cr.Width}×{cr.Height}: press Enter, or Esc to cancel" : "drag the area to keep");
                _canvas.Invalidate();
                return true;
            case Tool.Lasso:
                if (!_dragging) return true;
                _dragging = false;
                var pts = _lasso ?? new List<Point>();
                _lasso = null;
                if (pts.Count < 3) { if (_selMode == SelectMode.Replace) SetSelection(null, "selection cleared"); return true; }
                ApplyShape(Selection.FromPolygon(DocW, DocH, pts));
                return true;
            case Tool.ObjectSelect:
            case Tool.Move:
                if (!_dragging) return true;
                _dragging = false;
                CommitTransform();
                return true;
            case Tool.Transform:
                _dragging = false;
                _xfDrag.Mode = XformMode.None;
                return true;
            case Tool.Gradient:
            case Tool.Clone:
                if (!_dragging) return true;
                _dragging = false;
                FinishStroke(_tool == Tool.Gradient ? "gradient" : "clone");
                return true;
        }
        return false;
    }

    #endregion

    #region transform

    /// <summary>
    /// Start moving or transforming. With a selection, the selected pixels are
    /// lifted off the layer into their own object first; without one, every
    /// object on this layer and frame moves together.
    /// </summary>
    private bool BeginTransform(string label)
    {
        if (_xf != null) return true;
        if (ActiveLayer.Locked) { Say($"layer '{ActiveLayer.Name}' is locked"); return false; }
        StopPlay();
        var s = new XformSession { Before = Before(), Layer = _activeLayer, Frame = _cur, Label = label, PixelGrid = _pixelSnap };
        var cel = ActiveLayer.EnsureCel(_cur);

        if (_selection is { IsEmpty: false } sel)
        {
            BakeCel(cel);
            var baseObj = cel.EnsureRaster(DocW, DocH);
            int bi = cel.Objects.IndexOf(baseObj);
            var px = ImageOps.GetPixels(baseObj.Pixels);
            var b = sel.Bounds;
            var lifted = new byte[b.Width * b.Height * 4];
            bool any = false;
            for (int y = b.Top; y < b.Bottom; y++)
                for (int x = b.Left; x < b.Right; x++)
                {
                    if (!sel.Contains(x, y)) continue;
                    int so = (y * DocW + x) * 4, d = ((y - b.Y) * b.Width + x - b.X) * 4;
                    Buffer.BlockCopy(px, so, lifted, d, 4);
                    if (px[so + 3] != 0) any = true;
                    px[so] = px[so + 1] = px[so + 2] = px[so + 3] = 0;
                }
            if (!any)
            {
                RestoreDoc(s.Before);
                Say("nothing to move: the selection is empty on this layer");
                return false;
            }
            cel.Objects[bi] = baseObj.With(ImageOps.FromPixels(DocW, DocH, px));
            var obj = new RasterObject(ImageOps.FromPixels(b.Width, b.Height, lifted)) { Name = "Selection", X = b.X, Y = b.Y };
            cel.Objects.Insert(bi + 1, obj);
            s.Targets.Add((bi + 1, obj));
            s.Lifted = true;
            _selection = null;
        }
        else if ((_tool is Tool.ObjectSelect or Tool.Transform) && SelectedObject is { } selected)
        {
            foreach (int i in SelectedObjectIndices) s.Targets.Add((i,(RasterObject)cel.Objects[i]));
            s.Individual = true;
        }
        else
        {
            for (int i = 0; i < cel.Objects.Count; i++)
                if (cel.Objects[i] is RasterObject r && r.Visible && HasContent(r)) s.Targets.Add((i, r));
            if (s.Targets.Count == 0)
            {
                RestoreDoc(s.Before);
                Say("nothing to move on this layer and frame");
                return false;
            }
        }

        var box = RectangleF.Empty;
        foreach (var (_, r) in s.Targets)
        {
            var rb = Content(r);
            box = box.IsEmpty ? rb : RectangleF.Union(box, rb);
        }
        s.Box = box;
        _xf = s;
        InvalidateComposite();
        if (label == "transform")
            Say("Transform: drag inside to move, corners to scale (Shift keeps shape), outside to rotate. Enter applies, Esc cancels.");
        return true;
    }

    private static bool HasContent(RasterObject r)
    {
        var px = ImageOps.GetPixels(r.Pixels);
        for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) return true;
        return false;
    }

    /// <summary>The opaque part of an object, in canvas coordinates.</summary>
    private static RectangleF Content(RasterObject r)
    {
        var px = ImageOps.GetPixels(r.Pixels);
        var b = Sprites.ContentRect(px, r.Width, r.Height);
        if (b.IsEmpty) b = new Rectangle(0, 0, r.Width, r.Height);
        return new RectangleF(r.X + b.X, r.Y + b.Y, b.Width, b.Height);
    }

    private bool ApplyTransform()
    {
        if (_xf is not { } s) return false;
        var cel = _doc.Layers[s.Layer].EnsureCel(s.Frame);
        var m = s.Matrix;
        var mode = s.ForceMode ?? CurrentResample;
        try
        {
            var replacements = s.Targets.Select(t => (t.index, image: t.orig.Retransform(m, mode))).ToArray();
            foreach (var (i,image) in replacements) cel.Objects[i] = image;
        }
        catch (InvalidOperationException ex) { Say(ex.Message); return false; }
        InvalidateComposite();
        return true;
    }

    private void CommitTransform()
    {
        if (_xf is not { } s) return;
        _xf = null;
        _xfDrag.Mode = XformMode.None;
        if (s.IsIdentity && !s.Lifted)
        {
            RestoreDoc(s.Before);   // nothing moved; leave no step behind
            InvalidateComposite();
            return;
        }
        // A linked mask follows a whole-layer move by whole pixels. Anything else (a selection, scale, rotation) leaves the mask where it is.
        var mm = s.Matrix;
        if (!s.Lifted && !s.Individual && ActiveMaskLinked(s.Layer, s.Frame) is { } mcel && mm is { A: 1, B: 0, C: 0, D: 1 } && mm.Tx == Math.Round(mm.Tx) && mm.Ty == Math.Round(mm.Ty))
            mcel.Mask = Document.ReframeMask(mcel.Mask!, DocW, DocH, (int)mm.Tx, (int)mm.Ty);
        PushStructure(s.Label, s.Before);
        Say($"{s.Label} applied");
    }

    private Cel? ActiveMaskLinked(int layer, int frame) =>
        layer >= 0 && layer < _doc.Layers.Count && _doc.Layers[layer].MaskLinked && _doc.Layers[layer].CelAt(frame) is { Mask: not null } c ? c : null;

    private void CancelTransform()
    {
        if (_xf is not { } s) return;
        _xf = null;
        _xfDrag.Mode = XformMode.None;
        RestoreDoc(s.Before);
        InvalidateComposite();
        RefreshAll();
        Say("transform cancelled");
    }

    /// <summary>Put the document back to a snapshot without adding a history step.</summary>
    private void RestoreDoc(Document before) => new StructureEdit("", before, before).Undo(_doc);

    /// <summary>Commit any transform or crop in progress. Called before anything else changes the document.</summary>
    private void FinishFloat()
    {
        if (_xf != null) CommitTransform();
    }

    private void QuickTransform(string label, Action<XformSession> set)
    {
        FinishFloat();
        if (!BeginTransform(label)) return;
        set(_xf!);
        _xf!.ForceMode = Resample.Nearest;   // quarter turns and flips are exact
        ApplyTransform();
        CommitTransform();
    }

    private void NumericTransform()
    {
        FinishFloat();
        using var dlg = new NumericTransformDialog(CurrentResample);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        ApplyNumeric(dlg.ScaleX, dlg.ScaleY, dlg.Angle, dlg.SkewX, dlg.Mode);
    }

    private void ApplyNumeric(double scaleXPercent, double scaleYPercent, double angle, double skewX, Resample mode)
    {
        if (!BeginTransform("transform")) return;
        _xf!.Sx = scaleXPercent / 100; _xf.Sy = scaleYPercent / 100; _xf.Rot = angle; _xf.SkewX = skewX;
        _xf.ForceMode = mode;
        ApplyTransform();
        CommitTransform();
    }

    private void StartXformDrag(MouseEventArgs e)
    {
        var s = _xf!;
        var o = CanvasOrigin();
        var corners = s.Corners();
        var handles = HandlePoints(corners);
        var mouse = new PointF(e.X, e.Y);
        PointF Screen(PointF p) => new(o.X + p.X * _zoom, o.Y + p.Y * _zoom);
        int hit = -1;
        for (int i = 0; i < 8; i++)
        {
            var h = Screen(handles[i]);
            if (Math.Abs(h.X - mouse.X) <= 6 && Math.Abs(h.Y - mouse.Y) <= 6) { hit = i; break; }
        }
        _xfDrag = new XformDrag
        {
            Start = ToImageF(e.X, e.Y), Handle = hit,
            Tx = s.Tx, Ty = s.Ty, Sx = s.Sx, Sy = s.Sy, Rot = s.Rot,
            Mode = hit >= 0 ? XformMode.Scale : Inside(corners, ToImageF(e.X, e.Y)) ? XformMode.Move : XformMode.Rotate,
        };
    }

    private static PointF[] HandlePoints(PointF[] c) => new[]
    {
        c[0], Mid(c[0], c[1]), c[1], Mid(c[1], c[2]), c[2], Mid(c[2], c[3]), c[3], Mid(c[3], c[0]),
    };

    private static PointF Mid(PointF a, PointF b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static bool Inside(PointF[] poly, PointF p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    private void DragXform(MouseEventArgs e)
    {
        if (_xf is not { } s || _xfDrag.Mode == XformMode.None) return;
        var d = _xfDrag;
        var m = ToImageF(e.X, e.Y);
        s.PixelGrid = _pixelSnap;
        bool shift = (Mods & Keys.Shift) != 0;
        switch (d.Mode)
        {
            case XformMode.Move:
                s.Tx = PixelSnap.Coordinate(d.Tx + m.X - d.Start.X, _pixelSnap);
                s.Ty = PixelSnap.Coordinate(d.Ty + m.Y - d.Start.Y, _pixelSnap);
                SnapMove(s);
                break;
            case XformMode.Rotate:
            {
                double cx = s.Cx + s.Tx, cy = s.Cy + s.Ty;
                double a0 = Math.Atan2(d.Start.Y - cy, d.Start.X - cx), a1 = Math.Atan2(m.Y - cy, m.X - cx);
                double rot = d.Rot + (a1 - a0) * 180 / Math.PI;
                s.Rot = shift ? Math.Round(rot / 15) * 15 : Math.Round(rot, 1);
                break;
            }
            case XformMode.Scale:
            {
                // Pixel edges have a useful exact meaning for axis-aligned transforms.
                // Rotated and aspect-locked resizing keeps its existing geometry.
                if (_pixelSnap && !shift && Math.Abs(d.Rot / 90 - Math.Round(d.Rot / 90)) < 1e-8)
                    m = new PointF((float)PixelSnap.Coordinate(m.X, true), (float)PixelSnap.Coordinate(m.Y, true));
                var (ux, uy) = HandleUnits[d.Handle];
                double hw = s.Box.Width / 2.0, hh = s.Box.Height / 2.0;
                // Mouse in the box's own frame: centre at origin, unrotated, scaled.
                double r = -d.Rot * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
                double mx = m.X - (s.Cx + d.Tx), my = m.Y - (s.Cy + d.Ty);
                double lx = mx * cos - my * sin, ly = mx * sin + my * cos;
                // The opposite handle stays put.
                double ax = -ux * hw * d.Sx, ay = -uy * hh * d.Sy;
                double sx = d.Sx, sy = d.Sy;
                if (ux != 0) sx = (lx - ax) / (2 * ux * hw);
                if (uy != 0) sy = (ly - ay) / (2 * uy * hh);
                if (shift && ux != 0 && uy != 0)
                {
                    double k = Math.Max(Math.Abs(sx / d.Sx), Math.Abs(sy / d.Sy));
                    sx = Math.Sign(sx == 0 ? 1 : sx) * Math.Abs(d.Sx) * k;
                    sy = Math.Sign(sy == 0 ? 1 : sy) * Math.Abs(d.Sy) * k;
                }
                sx = Math.Abs(sx) < 0.01 ? 0.01 * (sx < 0 ? -1 : 1) : sx;
                sy = Math.Abs(sy) < 0.01 ? 0.01 * (sy < 0 ? -1 : 1) : sy;
                double ncx = ux != 0 ? ax + ux * hw * sx : 0, ncy = uy != 0 ? ay + uy * hh * sy : 0;
                double rr = d.Rot * Math.PI / 180, c2 = Math.Cos(rr), s2 = Math.Sin(rr);
                s.Sx = sx; s.Sy = sy;
                s.Tx = d.Tx + ncx * c2 - ncy * s2;
                s.Ty = d.Ty + ncx * s2 + ncy * c2;
                break;
            }
        }
        ApplyTransform();
        Say(d.Mode switch
        {
            XformMode.Move => $"move {s.Tx:+0;-0;0}, {s.Ty:+0;-0;0}",
            XformMode.Rotate => $"rotate {s.Rot:0.#}°",
            _ => $"scale {s.Sx * 100:0}% × {s.Sy * 100:0}%",
        });
    }

    private void Nudge(int dx, int dy)
    {
        if (_tool == Tool.ObjectSelect && SelectedObject == null) { Say("Pick an object first."); return; }
        bool fresh = _xf == null;
        if (!BeginTransform(_tool is Tool.Move or Tool.ObjectSelect ? "move" : "transform")) return;
        _xf!.Tx += dx; _xf.Ty += dy;
        ApplyTransform();
        if (fresh && (_tool is Tool.Move or Tool.ObjectSelect)) CommitTransform();
    }

    #endregion

    #region painting on objects

    /// <summary>
    /// Paint tools write one canvas-sized bitmap. If this layer holds placed or
    /// transformed images, merge them into that bitmap first (one undo step).
    /// </summary>
    private void EnsurePaintable()
    {
        FinishFloat();
        if (EditingMask) return;   // the tools write the mask, which is always plain pixels
        var cel = ActiveLayer.CelAt(_cur);
        if (cel == null || IsPlain(cel)) return;
        BakeActiveCel(false);
        Say("merged this layer's placed images into pixels so they can be painted");
    }

    private bool IsPlain(Cel cel) =>
        cel.Objects.All(o => o is RasterObject r && !r.IsTransformed && r.X == 0 && r.Y == 0 && r.Width == DocW && r.Height == DocH)
        && cel.Objects.Count <= 1;

    private bool BakeActiveCel(bool announce)
    {
        FinishFloat();
        var cel = ActiveLayer.CelAt(_cur);
        if (cel == null || IsPlain(cel)) { if (announce) Say("this layer is already plain pixels"); return false; }
        var before = Before();
        BakeCel(ActiveLayer.EnsureCel(_cur));
        _undo.Push(new StructureEdit("merge objects", before, _doc.Snapshot()));
        Touch();
        InvalidateComposite();
        if (announce) Say("merged objects into pixels");
        return true;
    }

    /// <summary>Flatten every visible object of a cel into one plain canvas bitmap (content past the edges is cut off).</summary>
    private void BakeCel(Cel cel)
    {
        if (IsPlain(cel)) return;
        var px = new byte[DocW * DocH * 4];
        var hidden = new List<DocObject>();
        foreach (var o in cel.Objects)
        {
            if (o is RasterObject r && r.Visible)
                Blending.Over(px, DocW, DocH, ImageOps.GetPixels(r.Pixels), r.Width, r.Height, r.X, r.Y, r.Opacity, r.Blend);
            else hidden.Add(o);
        }
        cel.Objects.Clear();
        cel.Objects.AddRange(hidden);
        cel.Objects.Add(new RasterObject(ImageOps.FromPixels(DocW, DocH, px)) { Name = "Bitmap" });
    }

    #endregion

    #region crop and canvas

    private void CropTo(Rectangle r)
    {
        FinishFloat();
        r.Intersect(new Rectangle(0, 0, DocW, DocH));
        if (r.Width < 1 || r.Height < 1) { Say("nothing to crop to"); return; }
        var before = Before();
        _doc.Resize(r.Width, r.Height, -r.X, -r.Y);
        ShiftMarkers(-r.X, -r.Y);
        _selection = null;
        _cropRect = null;
        PushStructure("crop", before);
        Say($"cropped to {r.Width}×{r.Height}");
    }

    private void ShiftMarkers(int dx, int dy)
    {
        foreach (var f in _doc.Frames)
        {
            if (f.Pivot is { } pv) f.Pivot = new Point(pv.X + dx, pv.Y + dy);
            for (int i = 0; i < f.Hitboxes.Count; i++)
            {
                var h = f.Hitboxes[i];
                h.Rect = new Rectangle(h.Rect.X + dx, h.Rect.Y + dy, h.Rect.Width, h.Rect.Height);
            }
        }
    }

    private void FitCanvasToContent() => FitCanvas(false);

    /// <summary>Quarter turns of the whole canvas, every layer and frame, exactly.</summary>
    private void RotateCanvas(int quarters)
    {
        FinishFloat();
        int k = ((quarters % 4) + 4) % 4;
        if (k == 0) return;
        int W = DocW, H = DocH;
        var m = Affine.Rotate(90 * k).Then(k switch
        {
            1 => Affine.Translate(H, 0),
            2 => Affine.Translate(W, H),
            _ => Affine.Translate(0, W),
        });
        var before = Before();
        TransformDocument(m, k % 2 == 1 ? H : W, k % 2 == 1 ? W : H);
        PushStructure($"rotate canvas {(k == 3 ? "−90" : (90 * k).ToString())}°", before);
    }

    private void FlipCanvas(bool horizontal)
    {
        FinishFloat();
        var m = horizontal ? Affine.Scale(-1, 1).Then(Affine.Translate(DocW, 0))
                           : Affine.Scale(1, -1).Then(Affine.Translate(0, DocH));
        var before = Before();
        TransformDocument(m, DocW, DocH);
        PushStructure(horizontal ? "flip canvas horizontal" : "flip canvas vertical", before);
    }

    /// <summary>Apply an exact (whole-pixel) matrix to every object, pivot and hitbox, then resize.</summary>
    private void TransformDocument(Affine m, int newW, int newH)
    {
        _doc.MapMasks(k =>
        {
            var t = Transform.Render(ImageOps.GetPixels(k.Pixels), k.Width, k.Height, m, Resample.Nearest);
            return t.w == newW && t.h == newH && t.x == 0 && t.y == 0 ? ImageOps.FromPixels(t.w, t.h, t.px) : null;
        });
        _doc.MapRasters(r =>
        {
            if (r.IsTransformed) return r.Retransform(m, r.Mode);
            var t = Transform.Render(ImageOps.GetPixels(r.Pixels), r.Width, r.Height, Affine.Translate(r.X, r.Y).Then(m), Resample.Nearest);
            return r.With(ImageOps.FromPixels(t.w, t.h, t.px), t.x, t.y);
        });
        foreach (var f in _doc.Frames)
        {
            if (f.Pivot is { } pv)
            {
                var (x, y) = m.Apply(pv.X + 0.5, pv.Y + 0.5);
                f.Pivot = new Point((int)Math.Floor(x), (int)Math.Floor(y));
            }
            for (int i = 0; i < f.Hitboxes.Count; i++)
            {
                var h = f.Hitboxes[i];
                var (x0, y0) = m.Apply(h.Rect.Left, h.Rect.Top);
                var (x1, y1) = m.Apply(h.Rect.Right, h.Rect.Bottom);
                h.Rect = Rectangle.FromLTRB((int)Math.Min(x0, x1), (int)Math.Min(y0, y1), (int)Math.Max(x0, x1), (int)Math.Max(y0, y1));
            }
        }
        _doc.Width = newW;
        _doc.Height = newH;
        _selection = null;
    }


    private Point VisibleCentre()
    {
        var c = ToImage(_canvas.ClientSize.Width / 2, _canvas.ClientSize.Height / 2);
        return new Point(Math.Clamp(c.X, 0, DocW - 1), Math.Clamp(c.Y, 0, DocH - 1));
    }

    #endregion

    #region clipboard, placing, library

    /// <summary>The selected pixels (or whole layer content) as a trimmed bitmap, and where it sat.</summary>
    private (Bitmap? bmp, Point at) CopyRegion(bool merged)
    {
        var px = merged ? _doc.RenderPixels(_cur) : LayerPixels();
        Rectangle r;
        if (_selection is { IsEmpty: false } sel)
        {
            for (int y = 0; y < DocH; y++)
                for (int x = 0; x < DocW; x++)
                    if (!sel.Contains(x, y)) { int o = (y * DocW + x) * 4; px[o] = px[o + 1] = px[o + 2] = px[o + 3] = 0; }
            r = Sprites.ContentRect(px, DocW, DocH);
        }
        else r = Sprites.ContentRect(px, DocW, DocH);
        if (r.IsEmpty) return (null, Point.Empty);
        return (ImageOps.FromPixels(r.Width, r.Height, ImageOps.CropRegion(px, DocW, r)), r.Location);
    }

    private void Copy(bool merged)
    {
        FinishFloat();
        var (bmp, at) = CopyRegion(merged);
        if (bmp == null) { Say("nothing to copy there"); return; }
        SetClipboardImage(bmp);
        _clipOrigin = at;
        _clipSize = bmp.Size;
        Say($"copied {bmp.Width}×{bmp.Height}{(merged ? " (all layers)" : "")}");
    }

    private void Cut()
    {
        if (ActiveLayer.Locked) { Say($"layer '{ActiveLayer.Name}' is locked"); return; }
        Copy(false);
        if (_clipOrigin != null) ClearSelection();
    }

    private void Paste()
    {
        FinishFloat();
        var bmp = GetClipboardImage();
        if (bmp == null) { Say("the clipboard has no image"); return; }
        Point centre;
        if (_clipOrigin is { } o && bmp.Size == _clipSize)
            centre = new Point(o.X + bmp.Width / 2, o.Y + bmp.Height / 2);   // back where it was copied from
        else centre = VisibleCentre();
        PlaceImage(bmp, "Pasted", centre, null, false);
    }

    private void SetClipboardImage(Bitmap bmp)
    {
        if (Program.Headless) { TestClipboard = ImageOps.Clone(bmp); return; }
        try
        {
            var data = new DataObject();
            var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);   // PNG keeps alpha for other apps
            data.SetData("PNG", false, ms);
            data.SetImage(bmp);
            Clipboard.SetDataObject(data, true);
        }
        catch (Exception ex) { Say("could not use the clipboard: " + ex.Message); }
    }

    private Bitmap? GetClipboardImage()
    {
        if (Program.Headless) return TestClipboard == null ? null : ImageOps.Clone(TestClipboard);
        try
        {
            if (Clipboard.GetData("PNG") is MemoryStream ms)
            {
                ms.Position = 0;
                using var b = new Bitmap(ms);
                return ImageOps.ReadExact(b);
            }
            if (Clipboard.ContainsFileDropList())
                foreach (string? f in Clipboard.GetFileDropList())
                    if (f != null && Importer.IsImage(f))
                    {
                        using var imp = Importer.Load(f);
                        return ImageOps.Clone(imp.Frames[0].bmp);
                    }
            if (Clipboard.GetImage() is Image img)
                using (img) return ImageOps.ReadExact(img);
        }
        catch (Exception ex) { Say("could not read the clipboard: " + ex.Message); }
        return null;
    }

    /// <summary>
    /// Put an image on a new layer of this frame, centred on a canvas point,
    /// as its own object so it can be moved and transformed without loss.
    /// </summary>
    private void PlaceImage(Bitmap bmp, string name, Point centre, string? source, bool addToLibrary)
    {
        FinishFloat();
        StopPlay();
        var before = Before();
        var layer = _doc.AddLayer(name, _activeLayer + 1);
        _activeLayer = _doc.Layers.IndexOf(layer);
        layer.EnsureCel(_cur).Objects.Add(new RasterObject(ImageOps.Clone(bmp))
        {
            Name = name, X = centre.X - bmp.Width / 2, Y = centre.Y - bmp.Height / 2,
        });
        if (addToLibrary) AddToLibrary(bmp, name, source);
        _selection = null;
        PushStructure($"place {name}", before);
        SetTool(Tool.Move);
        Say($"placed {name} ({bmp.Width}×{bmp.Height}) on a new layer. Drag to move; Ctrl+T to rotate or scale.");
    }

    private void AddToLibrary(Bitmap bmp, string name, string? source)
    {
        var px = ImageOps.GetPixels(bmp);
        foreach (var it in _doc.Library)
            if (it.Pixels.Width == bmp.Width && it.Pixels.Height == bmp.Height && ImageOps.GetPixels(it.Pixels).SequenceEqual(px))
                return;   // already there
        _doc.Library.Add(new LibraryItem(name, ImageOps.Clone(bmp), source));
        _library.Bind(_doc.Library);
    }

    /// <summary>
    /// Drop handling. One still image dropped on the canvas lands right there
    /// as a new layer and goes into the library. Shift, several files or an
    /// animation open the import dialog instead.
    /// </summary>
    private void HandleDrop(DragEventArgs e)
    {
        var client = _canvas.PointToClient(new Point(e.X, e.Y));
        bool overCanvas = _canvas.ClientRectangle.Contains(client);
        if (e.Data?.GetData(LibraryPanel.DragFormat) is string idx && int.TryParse(idx, out int li))
        {
            if (overCanvas && li >= 0 && li < _doc.Library.Count)
                PlaceImage(_doc.Library[li].Pixels, _doc.Library[li].Name, ToImage(client.X, client.Y), null, false);
            return;
        }
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
        // A Fireworks PNG opens as a document; Shift keeps the import dialog.
        if (files.Length == 1 && (Mods & Keys.Shift) == 0 && files[0].EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && Importer.HasFireworksChunks(files[0]))
        {
            if (ConfirmDiscard()) OpenFireworks(files[0]);
            return;
        }
        if (overCanvas && files.Length == 1 && (Mods & Keys.Shift) == 0 && Importer.IsImage(files[0])
            && TryPlaceFile(files[0], ToImage(client.X, client.Y)))
            return;
        DropFiles(files);
    }

    internal bool TryPlaceFile(string path, Point at)
    {
        ImportedFile imp;
        try { imp = Importer.Load(path); }
        catch (Exception ex) { Say($"{Path.GetFileName(path)}: {ex.Message}"); return true; }
        using (imp)
        {
            if (imp.Frames.Count != 1) return false;   // animations go through the dialog
            PlaceImage(imp.Frames[0].bmp, Path.GetFileNameWithoutExtension(path), at, path, true);
            if (imp.IsFireworksDocument) Say("placed the flattened picture from a Fireworks document (File > Open opens it with its layers)");
        }
        return true;
    }

    #endregion

    #region filters

    private void RunFilterDialog(FilterDef f)
    {
        FinishFloat();
        if (ActiveLayer.Locked) { Say($"layer '{ActiveLayer.Name}' is locked"); return; }
        var before = Before();
        if (f.Params.Length == 0)
        {
            int n = ApplyFilter(f, Array.Empty<double>(), false);
            if (n > 0) PushStructure(f.Name.ToLowerInvariant(), before); else Say("nothing on this layer to change");
            return;
        }
        using var dlg = new FilterDialog(f, _doc.FrameCount > 1);
        dlg.PreviewRequested += (args, all) =>
        {
            RestoreDoc(before);
            if (!FilterDialog.IsRestoreOnly(args)) ApplyFilter(f, args, all);
            InvalidateComposite();
        };
        var ok = dlg.ShowDialog(this) == DialogResult.OK;
        RestoreDoc(before);
        if (!ok) { InvalidateComposite(); RefreshAll(); return; }
        int changed = ApplyFilter(f, dlg.Values, dlg.AllFrames);
        if (changed > 0) PushStructure(f.Name.ToLowerInvariant(), before);
        else { InvalidateComposite(); Say("nothing on this layer to change"); }
    }

    /// <summary>Run a filter on the active layer (this frame, or all), inside the selection. Returns objects changed.</summary>
    internal int ApplyFilter(FilterDef f, double[] args, bool allFrames)
    {
        int key = ActiveFrameKey;
        var sel = _selection;
        uint colour = Surface.Pack(_fg);
        int grow = f.Grow?.Invoke(args) ?? 0;
        return _doc.MapRasters(r =>
        {
            bool isBase = r.X == 0 && r.Y == 0 && r.Width == DocW && r.Height == DocH;
            int pad = isBase ? 0 : grow;
            var src = pad > 0 ? ImageOps.Reframe(r.Pixels, r.Width + pad * 2, r.Height + pad * 2, pad, pad) : r.Pixels;
            int ox = r.X - pad, oy = r.Y - pad, w = src.Width, h = src.Height;
            var px = ImageOps.GetPixels(src);
            var result = f.Run(px, w, h, args, colour);
            if (sel != null) result = Adjust.Masked(px, result, w, h, (x, y) => sel.Contains(x + ox, y + oy));
            if (result.SequenceEqual(px) && pad == 0) return null;
            return r.With(ImageOps.FromPixels(w, h, result), ox, oy);
        }, li => li == _activeLayer, k => allFrames || k == key);
    }

    #endregion

    #region text

    private void AddText(Point at)
    {
        FinishFloat();
        StopPlay();
        if (ActiveLayer.Locked) { Say("This layer is locked."); return; }
        using var dlg = new TextDialog(_fg);
        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.TextValue)) return;
        using var font = dlg.FontValue;
        try
        {
            var source = new TextSource(dlg.TextValue, font.Name, font.Size, font.Bold, font.Italic, Surface.Pack(_fg), dlg.Smooth);
            var obj = EditableSources.Text(source, Affine.Translate(at.X, at.Y));
            obj.Name = "Text: " + (source.Text.Length > 16 ? source.Text[..16] + "…" : source.Text);
            AddEditableObject(obj);
        }
        catch (ArgumentException ex) { Say(ex.Message); }
        catch (InvalidOperationException ex) { Say(ex.Message); }
    }

    /// <summary>
    /// Draw text exactly: render white on black, read the grey level as
    /// coverage, and use the chosen colour with that alpha. Off = hard pixels.
    /// </summary>
    internal static Bitmap? RenderText(string text, Font font, Color colour, bool smooth)
    {
        var px = TextRender.Pixels(text, font, colour, smooth, out int w, out int h);
        var b = Sprites.ContentRect(px, w, h);
        if (b.IsEmpty) return null;
        return ImageOps.FromPixels(b.Width, b.Height, ImageOps.CropRegion(px, w, b));
    }

    #endregion

    #region overlay

    private void PaintEditOverlay(Graphics g, Point o)
    {
        g.SmoothingMode = SmoothingMode.None;
        PointF S(double x, double y) => new((float)(o.X + x * _zoom), (float)(o.Y + y * _zoom));

        if (_selection is { IsEmpty: false } sel)
        {
            using var black = new Pen(Color.Black);
            using var white = new Pen(Color.White) { DashStyle = DashStyle.Dash };
            foreach (var (a, b) in sel.Edges())
            {
                var pa = S(a.X, a.Y); var pb = S(b.X, b.Y);
                g.DrawLine(black, pa, pb);
                g.DrawLine(white, pa, pb);
            }
        }

        if (_shapeDraft is { } d && _dragging)
        {
            var r = new RectangleF(S(d.X, d.Y), new SizeF(d.Width * _zoom, d.Height * _zoom));
            using var pen = new Pen(Color.White) { DashStyle = DashStyle.Dash };
            if (_tool is Tool.EllipseSelect or Tool.Ellipse) g.DrawEllipse(pen, r); else g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }

        if (_lasso is { Count: > 1 } lp)
        {
            using var pen = new Pen(Color.White) { DashStyle = DashStyle.Dash };
            g.DrawLines(pen, lp.Select(p => S(p.X + 0.5, p.Y + 0.5)).ToArray());
        }

        if (_cropRect is { } c && _tool == Tool.Crop)
        {
            using var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            var whole = new RectangleF(S(0, 0), new SizeF(DocW * _zoom, DocH * _zoom));
            var keep = new RectangleF(S(c.X, c.Y), new SizeF(c.Width * _zoom, c.Height * _zoom));
            using var region = new Region(whole);
            region.Exclude(keep);
            g.FillRegion(shade, region);
            using var pen = new Pen(Color.White);
            g.DrawRectangle(pen, keep.X, keep.Y, keep.Width, keep.Height);
        }

        if (_xf is { } s && _tool == Tool.Transform)
        {
            var corners = s.Corners().Select(p => S(p.X, p.Y)).ToArray();
            using var pen = new Pen(Theme.Accent, 1);
            g.DrawPolygon(pen, corners);
            foreach (var h in HandlePoints(s.Corners()))
            {
                var hp = S(h.X, h.Y);
                g.FillRectangle(Brushes.White, hp.X - 4, hp.Y - 4, 8, 8);
                g.DrawRectangle(pen, hp.X - 4, hp.Y - 4, 8, 8);
            }
        }
    }

    #endregion

    #region keys

    /// <summary>
    /// Enter and Esc belong to an open transform or crop even when a side-panel
    /// button has keyboard focus (otherwise Enter would click that button).
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (TryProductivityShortcut(keyData)) return true;
        if ((keyData == Keys.Enter || keyData == Keys.Escape) && (_xf != null || _cropRect != null)
            && ActiveControl is not TextBoxBase)
        {
            EditKey(new KeyEventArgs(keyData));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Keys for the edit tools; true if handled.</summary>
    private bool EditKey(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                if (_xf == null && _tool == Tool.ObjectSelect && SelectedObject != null) { EditObjectProperties(); return true; }
                if (_xf != null) { CommitTransform(); return true; }
                if (_cropRect is { } c && _tool == Tool.Crop) { CropTo(c); return true; }
                return false;
            case Keys.Escape:
                if (_primitiveDrawing) { _primitiveDrawing = false; _dragging = false; _shapeDraft = null; _canvas.Invalidate(); return true; }
                if (_xf != null) { CancelTransform(); return true; }
                if (_cropRect != null) { _cropRect = null; _canvas.Invalidate(); Say("crop cancelled"); return true; }
                if (_lasso != null) { _lasso = null; _dragging = false; _canvas.Invalidate(); return true; }
                return false;
            case Keys.Left or Keys.Right or Keys.Up or Keys.Down when _tool is Tool.ObjectSelect or Tool.Move or Tool.Transform:
            {
                int step = e.Shift ? 10 : 1;
                Nudge(e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0,
                      e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0);
                return true;
            }
        }
        if (e.Control || e.Alt) return false;
        return FireworksToolKey(e.KeyCode);
    }

    #endregion

    #region test hooks

    internal Selection? TestSelection => _selection;
    internal void TestSetSelection(Selection? s) => _selection = s;
    internal int TestDrag(Tool tool, Keys mods, params Point[] path)
    {
        SetTool(tool);
        TestModifiers = mods;
        var o = CanvasOrigin();
        MouseEventArgs At(Point p) => new(MouseButtons.Left, 1, o.X + p.X * _zoom + _zoom / 2, o.Y + p.Y * _zoom + _zoom / 2, 0);
        CanvasDown(null, At(path[0]));
        for (int i = 1; i < path.Length; i++) CanvasMove(null, At(path[i]));
        CanvasUp(null, At(path[^1]));
        TestModifiers = null;
        return _undo.DoneCount;
    }
    internal void TestKey(Keys k) => OnKey(null, new KeyEventArgs(k));
    internal bool TestCmdKey(Keys k) { var m = new Message(); return ProcessCmdKey(ref m, k); }
    internal void TestFocusSidePanel() { foreach (Control c in Controls) FocusFirstButton(c); }
    private bool FocusFirstButton(Control c)
    {
        if (c is Button b && b.CanFocus) { b.Focus(); return true; }
        foreach (Control k in c.Controls) if (FocusFirstButton(k)) return true;
        return false;
    }
    internal bool TestTransforming => _xf != null;
    internal void TestTransformParams(double tx, double ty, double sx, double sy, double rot)
    {
        if (!BeginTransform("transform")) return;
        _xf!.Tx = tx; _xf.Ty = ty; _xf.Sx = sx; _xf.Sy = sy; _xf.Rot = rot;
        ApplyTransform();
    }
    internal void TestCommitTransform() => CommitTransform();
    internal void TestCancelTransform() => CancelTransform();
    internal void TestQuick(string what)
    {
        switch (what)
        {
            case "cw": QuickTransform("rotate 90°", s => s.Rot = 90); break;
            case "fliph": QuickTransform("flip horizontal", s => s.Sx = -1); break;
        }
    }
    internal void TestNumeric(double sx, double sy, double rot, Resample mode) => ApplyNumeric(sx, sy, rot, 0, mode);
    internal void TestRotateCanvas(int q) => RotateCanvas(q);
    internal void TestFlipCanvas(bool h) => FlipCanvas(h);
    internal void TestCrop(Rectangle r) => CropTo(r);
    internal void TestCopy(bool merged) => Copy(merged);
    internal void TestCut() => Cut();
    internal void TestPaste() => Paste();
    internal void TestPlace(Bitmap bmp, string name, Point at, bool library) => PlaceImage(bmp, name, at, null, library);
    internal int TestFilter(string name, params double[] args)
    {
        var before = Before();
        int n = ApplyFilter(Adjust.Find(name), args, false);
        if (n > 0) PushStructure(name, before);
        return n;
    }
    internal void TestText(string text, Point at, bool smooth)
    {
        using var font = new Font("Arial", 12);
        var bmp = RenderText(text, font, _fg, smooth)!;
        PlaceImage(bmp, "Text", new Point(at.X + bmp.Width / 2, at.Y + bmp.Height / 2), null, false);
    }
    internal void TestSetCloneSource(Point p) => _cloneSrc = p;
    internal void TestSetGradient(int kind) => _gradType.SelectedIndex = kind;
    internal void TestSetResample(Resample r) => _resample.SelectedIndex = (int)r;
    internal byte[] TestLayerPixels() => LayerPixels();
    internal Cel? TestActiveCel => ActiveLayer.CelAt(_cur);
    internal int TestLibraryCount => _doc.Library.Count;
    internal int TestActiveLayer => _activeLayer;
    internal void TestBake() => BakeActiveCel(true);
    internal void TestSetColours(Color fg, Color bg) { _fg = fg; _bg = bg; }

    #endregion
}
