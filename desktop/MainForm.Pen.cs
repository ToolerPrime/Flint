/* Pen (P) and Subselection (A) tools for bezier paths, plus the Combine Path
 * and Alter Path commands. Path geometry lives in PathSource.cs and
 * PathOps.cs; this file is the canvas interaction. Written for Flint
 * (behaviour after Fireworks 8; VectorCraft's pen documentation was a guide). */
using System.Collections.Immutable;
using System.Drawing.Drawing2D;

namespace Flint;

public sealed partial class MainForm
{
    // ---------- shared ----------

    /// <summary>The mouse in canvas pixels (fractions kept).</summary>
    private PointF ImageF(MouseEventArgs e)
    {
        var o = CanvasOrigin();
        return new PointF((e.X - o.X) / (float)_zoom, (e.Y - o.Y) / (float)_zoom);
    }

    private MintCheckBox? _pathSmooth;

    // ---------- Pen ----------

    private sealed class PenDraft
    {
        public List<PathNode> Nodes = new();
        public Document Doc = null!;
        public int Layer, Frame;
        public bool Dragging, Closing;
        public Point DownScreen;
        public PointF Mouse;
    }
    private PenDraft? _pen;

    /// <summary>A pen point: whole pixel corners (or the grid when it snaps).</summary>
    private VPt PenPoint(PointF f)
    {
        // An odd-width outline sits on pixel centres, so a 1 px line covers one row of pixels, not two halves.
        double half = !_filled.Checked && (int)_brush.Value % 2 == 1 ? 0.5 : 0;
        double x = Math.Round(f.X - half) + half, y = Math.Round(f.Y - half) + half;
        if (_doc.Grid.Snap) { x = _doc.Grid.Nearest(f.X, true) + half; y = _doc.Grid.Nearest(f.Y, false) + half; }
        return new VPt(x, y);
    }

    private bool PenDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return true;
        if (_pen != null && !ReferenceEquals(_pen.Doc, _doc)) { _pen = null; Say("the unfinished path was dropped: the document changed"); }
        if (_pen != null && (_pen.Layer != _activeLayer || _pen.Frame != _cur)) FinishPen(false);
        if (_pen == null && ActiveLayer.Locked) { Say("This layer is locked."); return true; }
        var f = ImageF(e);
        var pt = PenPoint(f);
        double near = 5.0 / _zoom;
        if (_pen == null)
        {
            _pen = new PenDraft { Doc = _doc, Layer = _activeLayer, Frame = _cur };
            _pen.Nodes.Add(new PathNode(pt, pt, pt));
        }
        else if (_pen.Nodes.Count >= 2 && Dist(_pen.Nodes[0].P, f) <= near) _pen.Closing = true;
        else if (Dist(_pen.Nodes[^1].P, f) <= near) _pen.Nodes[^1] = _pen.Nodes[^1] with { Out = _pen.Nodes[^1].P };   // make it a corner
        else _pen.Nodes.Add(new PathNode(pt, pt, pt));
        _pen.Dragging = true;
        _pen.DownScreen = e.Location;
        _pen.Mouse = f;
        Say(_pen.Closing ? "release to close the path" : $"{_pen.Nodes.Count} point{(_pen.Nodes.Count == 1 ? "" : "s")}. Click the first point to close; Enter or Esc to finish open; Backspace removes a point");
        _canvas.Invalidate();
        return true;
    }

    private static double Dist(VPt p, PointF f) => Math.Sqrt((p.X - f.X) * (p.X - f.X) + (p.Y - f.Y) * (p.Y - f.Y));

    private bool PenMove(MouseEventArgs e)
    {
        if (_pen == null) return false;
        _pen.Mouse = ImageF(e);
        if (_pen.Dragging && (Math.Abs(e.X - _pen.DownScreen.X) > 2 || Math.Abs(e.Y - _pen.DownScreen.Y) > 2))
        {
            // Dragging pulls out symmetric handles: a smooth point.
            int i = _pen.Closing ? 0 : _pen.Nodes.Count - 1;
            var n = _pen.Nodes[i];
            var m = new VPt(_pen.Mouse.X, _pen.Mouse.Y);
            if ((ModifierKeys & Keys.Shift) != 0) m = Snap45(n.P, m);
            _pen.Nodes[i] = n with { Out = m, In = new VPt(2 * n.P.X - m.X, 2 * n.P.Y - m.Y) };
        }
        _canvas.Invalidate();
        return true;
    }

    private static VPt Snap45(VPt from, VPt to)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y, len = Math.Sqrt(dx * dx + dy * dy);
        double a = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        return new VPt(from.X + len * Math.Cos(a), from.Y + len * Math.Sin(a));
    }

    private bool PenUp(MouseEventArgs e)
    {
        if (_pen == null) return false;
        _pen.Dragging = false;
        if (_pen.Closing) FinishPen(true);
        return true;
    }

    /// <summary>Add the drawn path (one undo step), or drop a draft too short to be a path.</summary>
    private void FinishPen(bool closed)
    {
        var p = _pen;
        _pen = null;
        _canvas.Invalidate();
        if (p == null || !ReferenceEquals(p.Doc, _doc)) return;
        if (p.Nodes.Count < 2) { Say("path cancelled: it needs two points or more"); return; }
        uint ink = Surface.Pack(_fg);
        bool filled = _filled.Checked;
        var src = new PathSource(ImmutableArray.Create(new SubPath(p.Nodes.ToImmutableArray(), closed)),
                                 filled ? ink : 0, filled ? 0 : ink, filled ? 0 : (double)_brush.Value);
        RasterObject obj;
        try { obj = PathSources.Create(src, Affine.Identity, _pathSmooth?.Checked != false ? Resample.Smooth : Resample.Nearest); }
        catch (ArgumentException ex) { Say(ex.Message); return; }
        // Added where it was drawn, then back to the layer and frame the user is on now.
        int keepLayer = _activeLayer, keepFrame = _cur;
        bool moved = keepLayer != p.Layer || keepFrame != p.Frame;
        _activeLayer = p.Layer; _cur = p.Frame;
        try { AddEditableObject(obj); }
        finally { if (moved) { _activeLayer = keepLayer; _cur = keepFrame; RefreshAll(); } }
    }

    /// <summary>Pen keys: Enter/Esc finish an open path, Backspace removes the last point.</summary>
    private bool PenKey(Keys k)
    {
        if (_pen == null) return false;
        if (k is Keys.Enter or Keys.Escape) { FinishPen(false); return true; }
        if (k == Keys.Back)
        {
            _pen.Nodes.RemoveAt(_pen.Nodes.Count - 1);
            if (_pen.Nodes.Count == 0) { _pen = null; Say("path cancelled"); }
            _canvas.Invalidate();
            return true;
        }
        return false;
    }

    // ---------- Subselection ----------

    private readonly HashSet<(int sub, int node)> _nodeSel = new();
    private RasterObject? _nodeSelFor;

    private sealed class PathDrag
    {
        public Document Before = null!;
        public int Index;
        public RasterObject Start = null!;      // the object as it was (undo restores it)
        public RasterObject Basis = null!;      // a primitive converted to a path, or Start itself
        public PathSource Src = null!;
        public Affine Inv;                       // canvas -> art
        public PointF StartMouse;
        public (int sub, int node, PathSources.Part part)? Handle;
        public bool Alt, Moved;
        public RectangleF? Marquee;
    }
    private PathDrag? _pathDrag;

    private (RasterObject obj, RasterObject? basis) _basisCache;

    private (int index, RasterObject obj, RasterObject basis, PathSource src)? SelectedPath()
    {
        if (SelectedObjectIndices.Length != 1 || ActiveLayer.CelAt(_cur) is not { } cel) return null;
        int i = SelectedObjectIndices[0];
        if (cel.Objects[i] is not RasterObject o || o.Symbol != null) return null;
        // A rectangle or oval is turned into a path for editing; remember it so painting does not redo it.
        if (!ReferenceEquals(_basisCache.obj, o)) _basisCache = (o, PathSources.AsPathObject(o));
        var basis = _basisCache.basis;
        return basis?.Vector?.Path is { } src ? (i, o, basis, src) : null;
    }

    private static PointF ToArt(Affine inv, PointF c) { var (x, y) = inv.Apply(c.X, c.Y); return new PointF((float)x, (float)y); }

    private bool SubDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return true;
        FinishFloat();
        var c = ImageF(e);
        bool shift = (ModifierKeys & Keys.Shift) != 0, alt = (ModifierKeys & Keys.Alt) != 0;
        if (SelectedPath() is { } sp && !ActiveLayer.Locked)
        {
            if (!ReferenceEquals(_nodeSelFor, sp.obj)) { _nodeSel.Clear(); _nodeSelFor = sp.obj; }
            var m = sp.basis.Matrix;
            var showHandles = new Func<int, int, bool>((si, ni) => _nodeSel.Contains((si, ni)));
            if (PathSources.NearestNode(sp.src, m, c.X, c.Y, 5.0 / _zoom, showHandles) is { } hit)
            {
                var drag = new PathDrag { Before = Before(), Index = sp.index, Start = sp.obj, Basis = sp.basis, Src = sp.src, Inv = m.Inverse(), StartMouse = c, Alt = alt };
                if (hit.part == PathSources.Part.Node)
                {
                    if (alt)
                    {
                        // Alt on a point: drag out new handles (a click alone makes it a corner).
                        drag.Handle = (hit.sub, hit.node, PathSources.Part.Out);
                        var n = sp.src.Subpaths[hit.sub].Nodes[hit.node];
                        drag.Src = sp.src with { Subpaths = sp.src.Subpaths.SetItem(hit.sub, sp.src.Subpaths[hit.sub] with { Nodes = sp.src.Subpaths[hit.sub].Nodes.SetItem(hit.node, n with { In = n.P, Out = n.P }) }) };
                        drag.Moved = n.HasIn || n.HasOut;
                        if (drag.Moved) ActiveLayer.CelAt(_cur)!.Objects[sp.index] = PathSources.Replace(sp.basis, drag.Src);
                    }
                    else
                    {
                        if (shift) { if (!_nodeSel.Remove((hit.sub, hit.node))) _nodeSel.Add((hit.sub, hit.node)); }
                        else if (!_nodeSel.Contains((hit.sub, hit.node))) { _nodeSel.Clear(); _nodeSel.Add((hit.sub, hit.node)); }
                    }
                }
                else drag.Handle = hit;
                _pathDrag = drag;
                _dragging = true;
                _canvas.Invalidate();
                return true;
            }
            if (PathSources.NearestSegment(sp.src, m, c.X, c.Y, 4.0 / _zoom) != null) { if (!shift) _nodeSel.Clear(); _canvas.Invalidate(); return true; }
        }
        // On an object (a path's body included): the Object tool picks and moves it. Empty space starts a box round points.
        var had = SelectedPath();
        if (PickObject(ToImage(e.X, e.Y)))
        {
            if (!(had is { } h0 && ReferenceEquals(SelectedObject, h0.obj))) { _nodeSel.Clear(); _nodeSelFor = SelectedObject; }
            return false;
        }
        if (had is { } h1) SelectObjects(new[] { h1.index }); else { _canvas.Invalidate(); return true; }
        if (!shift) _nodeSel.Clear();
        _pathDrag = new PathDrag { StartMouse = c, Marquee = new RectangleF(c, SizeF.Empty), Index = -1 };
        _dragging = true;
        _canvas.Invalidate();
        return true;
    }

    private bool SubMove(MouseEventArgs e)
    {
        if (_pathDrag is not { } d) return false;
        var c = ImageF(e);
        if (d.Marquee != null)
        {
            d.Marquee = RectangleF.FromLTRB(Math.Min(c.X, d.StartMouse.X), Math.Min(c.Y, d.StartMouse.Y), Math.Max(c.X, d.StartMouse.X), Math.Max(c.Y, d.StartMouse.Y));
            _canvas.Invalidate();
            return true;
        }
        // Drags move by whole canvas pixels (or grid steps), so points on whole pixels stay there.
        double ddx = Math.Round(c.X - d.StartMouse.X), ddy = Math.Round(c.Y - d.StartMouse.Y);
        if (_doc.Grid.Snap) { ddx = Math.Round(ddx / _doc.Grid.SpacingX) * _doc.Grid.SpacingX; ddy = Math.Round(ddy / _doc.Grid.SpacingY) * _doc.Grid.SpacingY; }
        var a0 = ToArt(d.Inv, d.StartMouse); var a1 = ToArt(d.Inv, new PointF((float)(d.StartMouse.X + ddx), (float)(d.StartMouse.Y + ddy)));
        PathSource edited;
        if (d.Handle is { } h)
        {
            var free = ToArt(d.Inv, c);     // handles move freely
            edited = PathSources.SetHandle(d.Src, h.sub, h.node, h.part, new VPt(free.X, free.Y), d.Alt || (ModifierKeys & Keys.Alt) != 0);
        }
        else
        {
            if (_nodeSel.Count == 0) return true;
            edited = PathSources.MoveNodes(d.Src, _nodeSel, a1.X - a0.X, a1.Y - a0.Y);
        }
        d.Moved = true;
        // Rebuilt from the start each move (never step by step); a new object, nothing changed in place.
        try { ActiveLayer.CelAt(_cur)!.Objects[d.Index] = PathSources.Replace(d.Basis, edited); }
        catch (ArgumentException ex) { Say(ex.Message); }
        InvalidateComposite();
        _canvas.Invalidate();
        return true;
    }

    private bool SubUp(MouseEventArgs e)
    {
        if (_pathDrag is not { } d) return false;
        _pathDrag = null;
        _dragging = false;
        if (d.Marquee is { } r && SelectedPath() is { } sp)
        {
            for (int si = 0; si < sp.src.Subpaths.Length; si++)
                for (int ni = 0; ni < sp.src.Subpaths[si].Nodes.Length; ni++)
                {
                    var p = sp.src.Subpaths[si].Nodes[ni].P;
                    var (x, y) = sp.basis.Matrix.Apply(p.X, p.Y);
                    if (r.Contains((float)x, (float)y)) _nodeSel.Add((si, ni));
                }
            Say($"{_nodeSel.Count} point{(_nodeSel.Count == 1 ? "" : "s")} selected");
        }
        else if (d.Moved)
        {
            _nodeSelFor = ActiveLayer.CelAt(_cur)!.Objects[d.Index] as RasterObject;
            PushStructure("edit path", d.Before);
        }
        _canvas.Invalidate();
        return true;
    }

    /// <summary>Subselection keys: Delete removes the selected points, arrows nudge them (Shift = 10), Esc cancels a drag.</summary>
    private bool SubKey(Keys k)
    {
        if (_tool != Tool.Subselect) return false;
        if (k == Keys.Escape && _pathDrag is { Index: >= 0 } d)
        {
            ActiveLayer.CelAt(_cur)!.Objects[d.Index] = d.Start;
            _pathDrag = null; _dragging = false;
            InvalidateComposite(); _canvas.Invalidate();
            return true;
        }
        if (_nodeSel.Count == 0 || SelectedPath() is not { } sp || !ReferenceEquals(_nodeSelFor, sp.obj) || ActiveLayer.Locked) return false;
        if ((k & (Keys.Control | Keys.Alt)) != 0) return false;
        var key = k & Keys.KeyCode;
        if (key is Keys.Delete or Keys.Back)
        {
            var before = Before();
            var cel = ActiveLayer.CelAt(_cur)!;
            var left = PathSources.DeleteNodes(sp.src, _nodeSel);
            if (left == null) cel.Objects.RemoveAt(sp.index);
            else cel.Objects[sp.index] = PathSources.Replace(sp.basis, left);
            _nodeSel.Clear();
            _nodeSelFor = left == null ? null : cel.Objects[sp.index] as RasterObject;
            if (left == null) SelectObject(-1);
            PushStructure("delete points", before);
            return true;
        }
        int step = (k & Keys.Shift) != 0 ? 10 : 1;
        (int dx, int dy) = key switch { Keys.Left => (-step, 0), Keys.Right => (step, 0), Keys.Up => (0, -step), Keys.Down => (0, step), _ => (0, 0) };
        if (dx == 0 && dy == 0) return false;
        var inv = sp.basis.Matrix.Inverse();
        var (ax, ay) = (inv.A * dx + inv.C * dy, inv.B * dx + inv.D * dy);
        var b2 = Before();
        ActiveLayer.CelAt(_cur)!.Objects[sp.index] = PathSources.Replace(sp.basis, PathSources.MoveNodes(sp.src, _nodeSel, ax, ay));
        _nodeSelFor = ActiveLayer.CelAt(_cur)!.Objects[sp.index] as RasterObject;
        PushStructure("nudge points", b2);
        return true;
    }

    /// <summary>Double-click a path's outline with Subselection: add a point there, shape unchanged.</summary>
    private bool SubDoubleClick(MouseEventArgs e)
    {
        if (_tool != Tool.Subselect || SelectedPath() is not { } sp || ActiveLayer.Locked) return false;
        var c = ImageF(e);
        if (PathSources.NearestSegment(sp.src, sp.basis.Matrix, c.X, c.Y, 4.0 / _zoom) is not { } seg) return false;
        var before = Before();
        ActiveLayer.CelAt(_cur)!.Objects[sp.index] = PathSources.Replace(sp.basis, PathSources.SplitAt(sp.src, seg.sub, seg.seg, seg.t));
        _nodeSel.Clear(); _nodeSel.Add((seg.sub, seg.seg + 1));
        _nodeSelFor = ActiveLayer.CelAt(_cur)!.Objects[sp.index] as RasterObject;
        PushStructure("add point", before);
        return true;
    }

    // ---------- overlay ----------

    private void PaintPathOverlay(Graphics g, Point o)
    {
        PointF S(VPt p, Affine m) { var (x, y) = m.Apply(p.X, p.Y); return new PointF(o.X + (float)x * _zoom, o.Y + (float)y * _zoom); }
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var line = new Pen(Color.FromArgb(230, 40, 170, 255), 1);
        using var handle = new Pen(Color.FromArgb(200, 255, 255, 255), 1);
        using var fill = new SolidBrush(Color.FromArgb(40, 170, 255));
        void Box(PointF p, bool solid, bool big = false)
        {
            float r = big ? 4.5f : 3.5f;
            if (solid) g.FillRectangle(fill, p.X - r, p.Y - r, 2 * r, 2 * r);
            else { g.FillRectangle(Brushes.White, p.X - r, p.Y - r, 2 * r, 2 * r); g.DrawRectangle(line, p.X - r, p.Y - r, 2 * r, 2 * r); }
        }
        void Segments(IReadOnlyList<PathNode> nodes, bool closed, Affine m)
        {
            int n = nodes.Count, segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = nodes[i]; var b = nodes[(i + 1) % n];
                g.DrawBezier(line, S(a.P, m), S(a.Out, m), S(b.In, m), S(b.P, m));
            }
        }
        if (_pen != null && ReferenceEquals(_pen.Doc, _doc))
        {
            var id = Affine.Identity;
            Segments(_pen.Nodes, false, id);
            if (!_pen.Dragging && _pen.Nodes.Count > 0)
            {
                using var dash = new Pen(Color.FromArgb(180, 40, 170, 255)) { DashStyle = DashStyle.Dash };
                var last = _pen.Nodes[^1];
                var mp = new VPt(_pen.Mouse.X, _pen.Mouse.Y);
                g.DrawBezier(dash, S(last.P, id), S(last.Out, id), S(mp, id), S(mp, id));
            }
            for (int i = 0; i < _pen.Nodes.Count; i++)
            {
                var n = _pen.Nodes[i];
                if (n.HasIn) { g.DrawLine(handle, S(n.P, id), S(n.In, id)); g.FillEllipse(Brushes.White, S(n.In, id).X - 2.5f, S(n.In, id).Y - 2.5f, 5, 5); }
                if (n.HasOut) { g.DrawLine(handle, S(n.P, id), S(n.Out, id)); g.FillEllipse(Brushes.White, S(n.Out, id).X - 2.5f, S(n.Out, id).Y - 2.5f, 5, 5); }
                Box(S(n.P, id), i > 0, i == 0 && _pen.Nodes.Count >= 2 && Dist(_pen.Nodes[0].P, _pen.Mouse) <= 5.0 / _zoom);
            }
        }
        if (_tool == Tool.Subselect && SelectedPath() is { } sp)
        {
            var m = sp.basis.Matrix;
            for (int si = 0; si < sp.src.Subpaths.Length; si++)
            {
                var s = sp.src.Subpaths[si];
                Segments(s.Nodes, s.Closed, m);
                for (int ni = 0; ni < s.Nodes.Length; ni++)
                {
                    var n = s.Nodes[ni];
                    bool sel = _nodeSel.Contains((si, ni)) && ReferenceEquals(_nodeSelFor, sp.obj);
                    if (sel)
                    {
                        if (n.HasIn) { g.DrawLine(handle, S(n.P, m), S(n.In, m)); g.FillEllipse(Brushes.White, S(n.In, m).X - 2.5f, S(n.In, m).Y - 2.5f, 5, 5); }
                        if (n.HasOut) { g.DrawLine(handle, S(n.P, m), S(n.Out, m)); g.FillEllipse(Brushes.White, S(n.Out, m).X - 2.5f, S(n.Out, m).Y - 2.5f, 5, 5); }
                    }
                    Box(S(n.P, m), sel);
                }
            }
        }
        if (_pathDrag?.Marquee is { } mq)
        {
            using var dash = new Pen(Color.White) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(dash, o.X + mq.X * _zoom, o.Y + mq.Y * _zoom, mq.Width * _zoom, mq.Height * _zoom);
        }
        g.SmoothingMode = old;
    }

    // ---------- Combine Path / Alter Path ----------

    private void AddPathMenus(ToolStripMenuItem edit)
    {
        var combine = new ToolStripMenuItem("Com&bine path");
        foreach (var (label, op) in new[] { ("&Union", CombineOp.Union), ("&Intersect", CombineOp.Intersect), ("&Punch", CombineOp.Punch), ("&Crop", CombineOp.Crop) })
            combine.DropDownItems.Add(label, null, (_, _) => CombinePaths(op));
        var alter = new ToolStripMenuItem("&Alter path");
        alter.DropDownItems.Add("&Simplify…", null, (_, _) => { if (AskNumber("Simplify", "Amount (pixels)", 1, 0.05m, 50) is { } v) AlterPaths("simplify", v); });
        alter.DropDownItems.Add("&Expand stroke…", null, (_, _) => { if (AskNumber("Expand stroke", "Width (pixels)", (double)Math.Max(1, (decimal)(SelectedPath()?.src.StrokeWidth ?? 4)), 0.5m, 512) is { } v) AlterPaths("expand", v); });
        alter.DropDownItems.Add("&Inset path…", null, (_, _) => { if (AskNumber("Inset path", "Pixels (negative grows it)", 2, -512, 512) is { } v) AlterPaths("inset", v); });
        edit.DropDownItems.Add(combine);
        edit.DropDownItems.Add(alter);
    }

    private double? AskNumber(string title, string label, double def, decimal min, decimal max)
    {
        if (SuppressPrompts || Program.Headless) return def;
        using var f = new ThemedForm
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(300, 96),
        };
        var n = new NumericUpDown { Left = 180, Top = 14, Width = 90, DecimalPlaces = 2, Minimum = min, Maximum = max, Value = Math.Clamp((decimal)def, min, max), AccessibleName = label };
        var ok = new Button { Text = "OK", Left = 110, Top = 54, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 200, Top = 54, Width = 80, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { new Label { Text = label, Left = 16, Top = 17, AutoSize = true }, n, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(this) == DialogResult.OK ? (double)n.Value : null;
    }

    /// <summary>The selected objects that are paths (rectangles and ovals count, turned into paths), bottom first.</summary>
    private List<(int index, RasterObject obj, RasterObject basis)>? SelectedPaths(string what, int min)
    {
        FinishFloat();
        var cel = ActiveLayer.CelAt(_cur);
        var idx = SelectedObjectIndices.OrderBy(i => i).ToArray();
        if (cel == null || idx.Length < min) { Say($"{what}: select {(min == 1 ? "a path" : $"{min} or more paths")} (rectangles and ovals count)."); return null; }
        if (ActiveLayer.Locked) { Say("The layer is locked."); return null; }
        var list = new List<(int, RasterObject, RasterObject)>();
        foreach (int i in idx)
        {
            if (cel.Objects[i] is not RasterObject o || o.Symbol != null || PathSources.AsPathObject(o) is not { } basis)
            { Say($"{what} works on paths, rectangles and ovals; '{cel.Objects[i].Name}' is not one."); return null; }
            list.Add((i, o, basis));
        }
        return list;
    }

    internal bool CombinePaths(CombineOp op)
    {
        if (SelectedPaths(op.ToString(), 2) is not { } sel) return false;
        var inputs = sel.Select(s => PathOps.FillPolygons(s.basis.Vector!.Path!, s.basis.Matrix)).ToList();
        List<PathSource?> results;
        try
        {
            var bottomFill = sel[0].basis.Vector!.Path!;
            results = PathOps.Combine(op, inputs, bottomFill.HasFill ? bottomFill.Fill : bottomFill.Stroke);
        }
        catch (Clipper2Lib.ClipperLibException ex) { Say("These paths could not be combined: " + ex.Message); return false; }
        if (op is CombineOp.Union or CombineOp.Intersect && results[0] == null) { Say("Nothing would be left."); return false; }
        // Every new object is made before the document changes, so a failure leaves it untouched.
        var made = new List<RasterObject?>();
        try
        {
            if (op is CombineOp.Union or CombineOp.Intersect) made.Add(Restyle(sel[0].basis, results[0]!, sel[0].basis.Vector!.Path!));
            else for (int k = 0; k < sel.Count - 1; k++) made.Add(results[k] is { } r ? Restyle(sel[k].basis, r, sel[k].basis.Vector!.Path!) : null);
        }
        catch (ArgumentException ex) { Say(ex.Message); return false; }
        var before = Before();
        var cel = ActiveLayer.CelAt(_cur)!;
        if (op is CombineOp.Union or CombineOp.Intersect)
        {
            foreach (var s in sel.AsEnumerable().Reverse()) cel.Objects.RemoveAt(s.index);
            cel.Objects.Insert(sel[0].index, made[0]!);
            SelectObjects(new[] { sel[0].index });
        }
        else
        {
            // The top-most selected path cuts each of the others, which keep their own look.
            cel.Objects.RemoveAt(sel[^1].index);
            for (int k = sel.Count - 2; k >= 0; k--)
                if (made[k] is { } r) cel.Objects[sel[k].index] = r; else cel.Objects.RemoveAt(sel[k].index);
            SelectObject(-1);
        }
        PushStructure(op.ToString().ToLowerInvariant() + " paths", before);
        Say(op switch { CombineOp.Union => "united into one path", CombineOp.Intersect => "kept where they overlap", CombineOp.Punch => "punched the top path out", _ => "cropped to the top path" });
        return true;
    }

    /// <summary>A result (in canvas space) with the style of <paramref name="like"/>: its fill (or stroke colour), stroke, name, look and groups.</summary>
    private static RasterObject Restyle(RasterObject like, PathSource canvasResult, PathSource style)
    {
        // The input's own fill and stroke (an outline-only path stays an outline).
        var src = canvasResult with { Fill = style.Fill, Stroke = style.Stroke, StrokeWidth = style.StrokeWidth };
        var o = PathSources.Create(src, Affine.Identity, like.Mode, like.Name);
        if (like.Effects is { Count: > 0 }) o = o.WithEffectList(like.Effects);
        if (like.GroupPath.Count > 0) o = o.WithGroups(like.GroupPath);
        o.Visible = like.Visible; o.Opacity = like.Opacity; o.Blend = like.Blend;
        return o;
    }

    internal bool AlterPaths(string what, double amount)
    {
        if (SelectedPaths(what switch { "simplify" => "Simplify", "expand" => "Expand stroke", _ => "Inset path" }, 1) is not { } sel) return false;
        var outs = new List<(int index, RasterObject? obj)>();
        foreach (var (index, _, basis) in sel)
        {
            var src = basis.Vector!.Path!;
            RasterObject? next;
            try
            {
                next = what switch
                {
                    "simplify" => PathSources.Replace(basis, PathOps.Simplify(src, amount)),
                    "expand" => PathOps.ExpandStroke(src, basis.Matrix, amount, src.HasStroke ? src.Stroke : src.Fill) is { } ex ? Restyle(basis, ex, src with { Fill = src.HasStroke ? src.Stroke : src.Fill, Stroke = 0, StrokeWidth = 0 }) : null,
                    _ => PathOps.Offset(PathOps.FillPolygons(src, basis.Matrix), -amount, src.Fill) is { } off ? Restyle(basis, off, src) : null,
                };
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { Say(ex.Message); return false; }
            outs.Add((index, next));
        }
        if (outs.All(o => o.obj == null)) { Say("Nothing would be left."); return false; }
        var before = Before();
        var cel = ActiveLayer.CelAt(_cur)!;
        foreach (var (index, obj) in outs.OrderByDescending(o => o.index))
            if (obj == null) cel.Objects.RemoveAt(index); else cel.Objects[index] = obj;
        SelectObjects(outs.Where(o => o.obj != null).Select(o => o.index - outs.Count(x => x.obj == null && x.index < o.index)));
        PushStructure(what + " path", before);
        return true;
    }

    // ---------- path style ----------

    /// <summary>Change a path's fill and stroke (double-click it with Object, or Objects > Edit).</summary>
    internal bool SetPathStyle(uint fill, uint stroke, double width)
    {
        if (SelectedPath() is not { } sp || sp.obj.Vector?.Path == null || ActiveLayer.Locked) return false;
        RasterObject next;
        try { next = PathSources.Replace(sp.basis, sp.src with { Fill = fill, Stroke = stroke, StrokeWidth = width }); }
        catch (ArgumentException ex) { Say(ex.Message); return false; }
        var before = Before();
        ActiveLayer.CelAt(_cur)!.Objects[sp.index] = next;
        PushStructure("path style", before);
        return true;
    }

    private void EditPathStyle()
    {
        if (SelectedPath() is not { } sp) return;
        var src = sp.src;
        using var f = new ThemedForm
        {
            Text = "Path", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(300, 160),
        };
        uint fill = src.Fill, stroke = src.Stroke;
        Button Swatch(int top, uint c, Action<uint> set, string name)
        {
            var b = new Button { Left = 110, Top = top, Width = 60, BackColor = Color.FromArgb(255, Color.FromArgb(unchecked((int)(c | 0xFF000000)))), AccessibleName = name };
            b.Click += (_, _) => { if (ColourPickerDialog.Pick(f, b.BackColor, name) is { } col) { b.BackColor = col; set(Surface.Pack(col)); } };
            f.Controls.Add(b);
            return b;
        }
        var hasFill = new CheckBox { Text = "Fill", Left = 16, Top = 16, AutoSize = true, Checked = src.HasFill };
        var hasStroke = new CheckBox { Text = "Stroke", Left = 16, Top = 50, AutoSize = true, Checked = src.HasStroke };
        Swatch(12, src.HasFill ? src.Fill : 0xFF000000, c => fill = c, "Fill colour");
        Swatch(46, src.HasStroke ? src.Stroke : 0xFF000000, c => stroke = c, "Stroke colour");
        var w = new NumericUpDown { Left = 110, Top = 82, Width = 60, DecimalPlaces = 1, Minimum = 0.5m, Maximum = 512, Value = (decimal)Math.Clamp(src.StrokeWidth > 0 ? src.StrokeWidth : 1, 0.5, 512), AccessibleName = "Stroke width" };
        var ok = new Button { Text = "OK", Left = 110, Top = 120, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 200, Top = 120, Width = 80, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { hasFill, hasStroke, new Label { Text = "Stroke width", Left = 16, Top = 85, AutoSize = true }, w, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        if (!hasFill.Checked && !hasStroke.Checked) { Say("A path needs a fill or a stroke."); return; }
        SetPathStyle(hasFill.Checked ? fill : 0, hasStroke.Checked ? stroke : 0, hasStroke.Checked ? (double)w.Value : 0);
    }

    // ---------- test hooks ----------
    internal bool TestPathKey(Keys k) => PenKey(k) || SubKey(k);
    internal bool TestSubDoubleClick(Point screen) => SubDoubleClick(new MouseEventArgs(MouseButtons.Left, 2, screen.X, screen.Y, 0));
    internal IReadOnlyCollection<(int sub, int node)> TestNodeSelection => _nodeSel;
    internal bool TestPenActive => _pen != null;
    internal void TestDragStart(Tool tool, Point down, Point move)
    {
        SetTool(tool);
        CanvasDown(null, new MouseEventArgs(MouseButtons.Left, 1, down.X, down.Y, 0));
        CanvasMove(null, new MouseEventArgs(MouseButtons.Left, 0, move.X, move.Y, 0));
    }
    internal void TestDragEnd(Point up) => CanvasUp(null, new MouseEventArgs(MouseButtons.Left, 1, up.X, up.Y, 0));
}
