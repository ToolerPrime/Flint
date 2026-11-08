/* Rulers, guides and snapping. Drawing here is UI only (nothing touches stored
 * pixels). Guides live in Document.Guides and every change is one undo step. */
namespace Flint;

public sealed partial class MainForm
{
    private const int RulerSize = 22;
    private bool _rulers, _guidesShown = true, _guidesLocked, _guidesSnap /* off by default, as in Fireworks */, _rulerHooked;
    private ToolStripMenuItem _miRulers = null!, _miShow = null!, _miLock = null!, _miSnap = null!;
    private Point _rulerMouse = new(-1, -1);

    private sealed class GuideDrag
    {
        public bool Vertical, Off;
        public int Index = -1, Pos;
        public IReadOnlyList<Guide> Old = Array.Empty<Guide>();
    }
    private GuideDrag? _gd;

    /// <summary>Space the canvas leaves for the rulers (0 when they are off).</summary>
    private int RulerPad => _rulers ? RulerSize : 0;

    private void BuildRulerMenu(ToolStripMenuItem view)
    {
        view.DropDownItems.Add(new ToolStripSeparator());
        _miRulers = new ToolStripMenuItem("&Rulers", null, (_, _) => SetRulers(!_rulers)) { ShortcutKeys = Keys.Control | Keys.Alt | Keys.R };
        view.DropDownItems.Add(_miRulers);
        var g = new ToolStripMenuItem("&Guides");
        _miShow = new ToolStripMenuItem("&Show guides", null, (_, _) => { _guidesShown = !_guidesShown; SyncGuideMenu(); _canvas.Invalidate(); }) { Checked = true };
        _miLock = new ToolStripMenuItem("&Lock guides", null, (_, _) => { _guidesLocked = !_guidesLocked; SyncGuideMenu(); });
        _miSnap = new ToolStripMenuItem("S&nap to guides", null, (_, _) => { _guidesSnap = !_guidesSnap; SyncGuideMenu(); });
        _miShow.Checked = true;
        g.DropDownItems.Add(_miShow);
        g.DropDownItems.Add(_miLock);
        g.DropDownItems.Add(_miSnap);
        g.DropDownItems.Add(new ToolStripSeparator());
        g.DropDownItems.Add("&Clear guides", null, (_, _) => ClearGuides());
        view.DropDownItems.Add(g);
    }

    private void SyncGuideMenu()
    {
        _miRulers.Checked = _rulers; _miShow.Checked = _guidesShown; _miLock.Checked = _guidesLocked; _miSnap.Checked = _guidesSnap;
    }

    private void SetRulers(bool on)
    {
        _rulers = on;
        SyncGuideMenu();
        _canvas.AutoScrollMinSize = new Size(DocW * _zoom + 40 + RulerPad, DocH * _zoom + 40 + RulerPad);
        _canvas.Invalidate();
    }

    private void SetGuides(IReadOnlyList<Guide> next, string label)
    {
        var prev = _doc.Guides;
        _doc.Guides = next;
        _undo.Push(new PropertyEdit(label, d => d.Guides = prev, d => d.Guides = next));
        Touch();
        _canvas.Invalidate();
    }

    private void ClearGuides()
    {
        if (_doc.Guides.Count == 0) { Say("no guides to clear"); return; }
        SetGuides(Array.Empty<Guide>(), "clear guides");
        Say("guides cleared");
    }

    // ---------- painting ----------

    /// <summary>Guides, then rulers on top. Called last from PaintCanvas.</summary>
    private void PaintRulersAndGuides(Graphics g, Point o)
    {
        if (!_rulerHooked) { _canvas.Scroll += (_, _) => _canvas.Invalidate(); _rulerHooked = true; }
        var size = _canvas.ClientSize;
        if (_guidesShown)
        {
            using var pen = new Pen(Color.FromArgb(230, 0, 200, 255));
            for (int i = 0; i < _doc.Guides.Count; i++)
            {
                var gd = _doc.Guides[i];
                if (_gd != null && _gd.Index == i) continue;       // drawn at its dragged position below
                DrawGuide(g, pen, gd.Vertical, gd.Pos, o, size);
            }
            if (_gd is { Off: false } d)
            {
                using var drag = new Pen(Color.FromArgb(255, 255, 255, 0));
                DrawGuide(g, drag, d.Vertical, d.Pos, o, size);
            }
        }
        if (!_rulers) return;

        int pad = RulerSize;
        using var back = new SolidBrush(Color.FromArgb(222, 222, 222));
        g.FillRectangle(back, 0, 0, size.Width, pad);
        g.FillRectangle(back, 0, 0, pad, size.Height);
        using var line = new Pen(Color.FromArgb(120, 120, 120));
        g.DrawLine(line, 0, pad - 1, size.Width, pad - 1);
        g.DrawLine(line, pad - 1, 0, pad - 1, size.Height);
        using var font = new Font("Segoe UI", 7f);
        using var ink = new SolidBrush(Color.FromArgb(40, 40, 40));
        var (step, minor) = RulerSteps();
        for (int axis = 0; axis < 2; axis++)
        {
            int origin = axis == 0 ? o.X : o.Y, extent = axis == 0 ? size.Width : size.Height;
            int first = (int)Math.Floor((pad - origin) / (double)_zoom) / minor * minor - minor;
            int last = (int)Math.Ceiling((extent - origin) / (double)_zoom);
            for (int i = first; i <= last; i += minor)
            {
                int s = origin + i * _zoom;
                if (s < pad) continue;
                bool major = i % step == 0;
                int len = major ? pad - 4 : (pad - 4) / 3;
                if (axis == 0) g.DrawLine(line, s, pad - 1, s, pad - 1 - len);
                else g.DrawLine(line, pad - 1, s, pad - 1 - len, s);
                if (!major) continue;
                if (axis == 0) g.DrawString(i.ToString(), font, ink, s + 1, 0);
                else
                {
                    var st = g.Save();
                    g.TranslateTransform(0, s - 1);
                    g.RotateTransform(-90);
                    g.DrawString(i.ToString(), font, ink, 0, 0);
                    g.Restore(st);
                }
            }
        }
        using var mark = new Pen(Color.Red);
        if (_rulerMouse.X >= pad) g.DrawLine(mark, _rulerMouse.X, 0, _rulerMouse.X, pad - 1);
        if (_rulerMouse.Y >= pad) g.DrawLine(mark, 0, _rulerMouse.Y, pad - 1, _rulerMouse.Y);
    }

    private void DrawGuide(Graphics g, Pen pen, bool vertical, int pos, Point o, Size size)
    {
        if (vertical) { int x = o.X + pos * _zoom; g.DrawLine(pen, x, RulerPad, x, size.Height); }
        else { int y = o.Y + pos * _zoom; g.DrawLine(pen, RulerPad, y, size.Width, y); }
    }

    /// <summary>Label spacing (at least ~50 screen pixels) and tick spacing, in image pixels.</summary>
    private (int step, int minor) RulerSteps()
    {
        int step = 1;
        foreach (int mag in new[] { 1, 10, 100, 1000, 10000 })
            foreach (int m in new[] { 1, 2, 5 })
                if (step * _zoom < 50) step = m * mag;
        return (step, step >= 10 ? step / 10 : 1);
    }

    // ---------- mouse ----------

    private int GuidePos(MouseEventArgs e, bool vertical)
    {
        var o = CanvasOrigin();
        return (int)Math.Round((vertical ? e.X - o.X : e.Y - o.Y) / (double)_zoom);
    }

    private bool GuideDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return false;
        int pad = RulerPad;
        if (_rulers && (e.X < pad || e.Y < pad))
        {
            if (e.X < pad && e.Y < pad) return true;                  // the corner does nothing
            if (_guidesLocked) { Say("guides are locked (View > Guides > Lock guides)"); return true; }
            bool vertical = e.X < pad;                                 // from the left ruler: a vertical guide
            _gd = new GuideDrag { Vertical = vertical, Index = -1, Pos = GuidePos(e, vertical), Old = _doc.Guides };
            _guidesShown = true; SyncGuideMenu();
            _canvas.Cursor = vertical ? Cursors.VSplit : Cursors.HSplit;
            _canvas.Invalidate();
            return true;
        }
        if (!_guidesShown || _guidesLocked || _doc.Guides.Count == 0) return false;
        var o = CanvasOrigin();
        int best = -1, bestD = 4;
        for (int i = 0; i < _doc.Guides.Count; i++)
        {
            var g = _doc.Guides[i];
            int d = Math.Abs(g.Vertical ? e.X - (o.X + g.Pos * _zoom) : e.Y - (o.Y + g.Pos * _zoom));
            if (d < bestD) { best = i; bestD = d; }
        }
        if (best < 0) return false;
        var hit = _doc.Guides[best];
        _gd = new GuideDrag { Vertical = hit.Vertical, Index = best, Pos = hit.Pos, Old = _doc.Guides };
        _canvas.Cursor = hit.Vertical ? Cursors.VSplit : Cursors.HSplit;
        return true;
    }

    private bool GuideMove(MouseEventArgs e)
    {
        if (_gd == null)
        {
            if (_rulers)
            {
                _rulerMouse = e.Location;
                _canvas.Invalidate(new Rectangle(0, 0, _canvas.ClientSize.Width, RulerSize));
                _canvas.Invalidate(new Rectangle(0, 0, RulerSize, _canvas.ClientSize.Height));
            }
            return false;
        }
        _gd.Pos = GuidePos(e, _gd.Vertical);
        _gd.Off = _gd.Vertical ? e.X < RulerPad : e.Y < RulerPad;
        _rulerMouse = e.Location;
        Say(_gd.Off ? "release to delete this guide" : $"guide {(_gd.Vertical ? "x" : "y")} = {_gd.Pos}");
        _canvas.Invalidate();
        return true;
    }

    private bool GuideUp(MouseEventArgs e)
    {
        if (_gd == null) return false;
        var d = _gd;
        _gd = null;
        _canvas.Cursor = Cursors.Default;
        var list = d.Old.ToList();
        var now = new Guide(d.Vertical, d.Pos);
        bool off = d.Off || (_rulers && (d.Vertical ? e.X < RulerPad : e.Y < RulerPad));
        string label;
        if (off) { if (d.Index < 0) { _canvas.Invalidate(); return true; } list.RemoveAt(d.Index); label = "delete guide"; }
        else if (d.Index < 0) { if (list.Contains(now)) { _canvas.Invalidate(); return true; } list.Add(now); label = "add guide"; }
        else { if (list[d.Index] == now || list.Contains(now)) { _canvas.Invalidate(); return true; } list[d.Index] = now; label = "move guide"; }
        SetGuides(list.ToArray(), label);
        Say(label == "delete guide" ? "guide deleted" : $"guide {(d.Vertical ? "x" : "y")} = {d.Pos}");
        return true;
    }

    // ---------- snapping ----------

    private static bool SnapsToGuides(Tool t) =>
        t is Tool.Marquee or Tool.EllipseSelect or Tool.Crop or Tool.Line or Tool.Rectangle or Tool.Ellipse or Tool.Gradient or Tool.Hitbox;

    private IEnumerable<Guide> ActiveGuides => _guidesShown ? _doc.Guides : Array.Empty<Guide>();

    /// <summary>ToImage, pulled onto a guide, the canvas edge or its centre when within ~6 screen pixels.</summary>
    private Point ToImageSnapped(MouseEventArgs e)
    {
        var p = ToImage(e.X, e.Y);
        if (!_guidesSnap || !SnapsToGuides(_tool)) return p;
        var o = CanvasOrigin();
        if (GuideSet.SnapAxis((e.X - o.X) / (double)_zoom, true, ActiveGuides, DocW, _zoom) is { } sx)
            p.X = GuideSet.EdgeToPixel(sx, _dragging ? _dragStart.X : null);
        if (GuideSet.SnapAxis((e.Y - o.Y) / (double)_zoom, false, ActiveGuides, DocH, _zoom) is { } sy)
            p.Y = GuideSet.EdgeToPixel(sy, _dragging ? _dragStart.Y : null);
        return p;
    }

    /// <summary>Move/transform: nudge the dragged box so its left, centre or right (top, middle, bottom) meets a guide.</summary>
    private void SnapMove(XformSession s)
    {
        s.GuideAligned = false;
        if (!_guidesSnap) return;
        var c = s.Corners();
        double l = c.Min(p => p.X), r = c.Max(p => p.X), t = c.Min(p => p.Y), b = c.Max(p => p.Y);
        double Best(bool vertical, double a, double mid, double z, int size)
        {
            double best = 0, bestAbs = double.MaxValue;
            foreach (var probe in new[] { a, mid, z })
                if (GuideSet.SnapAxis(probe, vertical, ActiveGuides, size, _zoom) is { } hit && Math.Abs(hit - probe) < bestAbs)
                { best = hit - probe; bestAbs = Math.Abs(best); }
            return best; // A guide (including a half-pixel canvas centre) takes priority over pixel snap.
        }
        s.Tx += Best(true, l, (l + r) / 2, r, DocW);
        s.Ty += Best(false, t, (t + b) / 2, b, DocH);
        s.GuideAligned = true;
    }

    // ---------- test hooks ----------

    internal bool TestRulersOn { get => _rulers; set => SetRulers(value); }
    internal int TestRulerPad => RulerPad;
    internal IReadOnlyList<Guide> TestGuides { get => _doc.Guides; set => SetGuides(value, "set guides"); }
    internal void TestGuideFlags(bool show, bool lck, bool snap) { _guidesShown = show; _guidesLocked = lck; _guidesSnap = snap; SyncGuideMenu(); }
    internal void TestClearGuides() => ClearGuides();
    internal void TestZoom(int z) { _zoom = z; RefreshAll(); }
    /// <summary>Screen position (canvas client coordinates) of an image position; edges are whole numbers.</summary>
    internal Point TestScreen(double ix, double iy) { var o = CanvasOrigin(); return new Point((int)Math.Round(o.X + ix * _zoom), (int)Math.Round(o.Y + iy * _zoom)); }
    internal void TestMouse(Tool tool, params Point[] screenPath)
    {
        SetTool(tool);
        MouseEventArgs At(Point p) => new(MouseButtons.Left, 1, p.X, p.Y, 0);
        CanvasDown(null, At(screenPath[0]));
        for (int i = 1; i < screenPath.Length; i++) CanvasMove(null, At(screenPath[i]));
        CanvasUp(null, At(screenPath[^1]));
    }
    internal int TestUndoCount => _undo.DoneCount;
}
