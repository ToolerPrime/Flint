/* Frames view of the timeline panel: Fireworks' "Frames" (States) panel.
 * Every frame is the same size, in order, as a filmstrip that wraps when the
 * panel is tall enough and scrolls sideways otherwise. It reuses the timeline's
 * state, thumbnail cache and events, so selecting, reordering, renaming, the
 * context menu and delay edits all reach the host exactly as in Timeline view.
 */
using System.Drawing.Drawing2D;

namespace Flint;

public sealed partial class TimelinePanel
{
    public const int CellW = 92, CellH = 110, CellGap = 6, CellPad = 8;
    private const int TagStripH = 14, ThumbTop = 16, ThumbH = 64, DelayTop = 94;

    private bool _framesView;
    private int _scrollY;
    private TextBox? _editor;
    private int _editFrame;
    private bool _closingEditor;

    /// <summary>Test hook: treat clicks as Shift-clicks.</summary>
    internal bool TestExtend;

    public bool FramesView
    {
        get => _framesView;
        set
        {
            if (value == _framesView) return;
            CancelEdit();
            _framesView = value;
            _scrollX = _scrollY = 0;
            _hbar.Visible = true;
            UpdateScroll();
            Invalidate();
        }
    }

    private bool Wrapped => ClientSize.Height >= 2 * CellH + CellGap + 2 * CellPad;
    private int Cols => Wrapped ? Math.Max(1, (ClientSize.Width - 2 * CellPad + CellGap) / (CellW + CellGap)) : int.MaxValue;
    private bool Extend => TestExtend || (ModifierKeys & (Keys.Shift | Keys.Control)) != 0;

    private Rectangle CellRect(int i)
    {
        int cols = Cols;
        int col = Wrapped ? i % cols : i, row = Wrapped ? i / cols : 0;
        return new Rectangle(CellPad + col * (CellW + CellGap) - _scrollX, CellPad + row * (CellH + CellGap) - _scrollY, CellW, CellH);
    }
    private static Rectangle TagRectOf(Rectangle r) => new(r.X, r.Y, r.Width, TagStripH);
    private static Rectangle ThumbRectOf(Rectangle r) => new(r.X, r.Y + ThumbTop, r.Width, ThumbH);
    private static Rectangle DelayRectOf(Rectangle r) => new(r.X, r.Y + DelayTop, r.Width, 15);

    private int CellAt(Point p)
    {
        if (_doc == null) return -1;
        for (int i = 0; i < _doc.FrameCount; i++) if (CellRect(i).Contains(p)) return i;
        return -1;
    }

    private AnimTag? TagOf(int f) => _doc?.Tags.FirstOrDefault(t => f >= t.Start && f <= t.End);

    private void FramesUpdateScroll()
    {
        int n = _doc?.FrameCount ?? 0;
        if (Wrapped)
        {
            _hbar.Visible = false;
            int cols = Cols, rows = (n + cols - 1) / cols;
            int contentH = CellPad * 2 + Math.Max(0, rows * (CellH + CellGap) - CellGap);
            _scrollX = 0;
            _scrollY = Math.Clamp(_scrollY, 0, Math.Max(0, contentH - ClientSize.Height));
        }
        else
        {
            _hbar.Visible = true;
            int content = CellPad * 2 + n * (CellW + CellGap);
            int view = Math.Max(1, ClientSize.Width);
            _hbar.Maximum = Math.Max(0, content);
            _hbar.LargeChange = view;
            _hbar.SmallChange = 40;
            _hbar.Enabled = content > view;
            _scrollX = Math.Clamp(_scrollX, 0, Math.Max(0, content - view));
            _scrollY = 0;
            _hbar.Value = Math.Min(_scrollX, Math.Max(0, _hbar.Maximum));
        }
    }

    private void FramesReveal(double ms)
    {
        if (_doc == null) return;
        var r = CellRect(Playback.FrameAt(Delays, (int)ms));
        int view = ClientSize.Width, viewH = ClientSize.Height;
        if (Wrapped)
        {
            if (r.Top < 0) _scrollY += r.Top - CellPad;
            else if (r.Bottom > viewH) _scrollY += r.Bottom - viewH + CellPad;
            else return;
        }
        else
        {
            if (r.Left < 0) _scrollX += r.Left - CellPad;
            else if (r.Right > view) _scrollX += r.Right - view + CellPad;
            else return;
        }
        UpdateScroll();
        Invalidate();
    }

    // ---------- painting ----------

    private void PaintFramesView(Graphics g)
    {
        int bottom = ClientSize.Height - (_hbar.Visible ? _hbar.Height : 0);
        g.SetClip(new Rectangle(0, 0, ClientSize.Width, bottom));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int lo = Math.Min(SelStart, SelEnd), hi = Math.Max(SelStart, SelEnd);
        var d = Delays;
        using var small = new Font(Font.FontFamily, 7f);
        using var smallBold = new Font(Font.FontFamily, 7f, FontStyle.Bold);
        const TextFormatFlags tf = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        int cols = Cols;

        for (int i = 0; i < d.Length; i++)
        {
            var r = CellRect(i);
            if (r.Right < 0 || r.Left > ClientSize.Width || r.Bottom < 0 || r.Top > bottom) continue;
            bool sel = i >= lo && i <= hi && hi > lo, cur = i == Current;

            using (var card = Theme.Round(new Rectangle(r.X, r.Y + ThumbTop - 2, r.Width - 1, r.Height - ThumbTop + 1), 6))
            {
                using var fill = new SolidBrush(sel || cur ? Theme.AccentSoft(Theme.Panel3, cur ? 0.22f : 0.14f) : Theme.Panel3);
                g.FillPath(fill, card);
                using var pen = new Pen(cur ? Theme.Accent : sel ? Color.FromArgb(160, Theme.Accent) : Theme.Border, cur ? 2 : 1);
                g.DrawPath(pen, card);
            }

            // Tag strip above the frames a tag covers; its name sits on the first cell of each row.
            var tag = TagOf(i);
            if (tag != null)
            {
                bool first = i == tag.Start || (Wrapped && i % cols == 0);
                bool joinL = i > tag.Start && !(Wrapped && i % cols == 0);
                var tr = TagRectOf(r);
                tr.X -= joinL ? CellGap : 0; tr.Width += joinL ? CellGap : 0;
                using var tb = new SolidBrush(tag.Colour);
                g.FillRectangle(tb, tr);
                if (first) TextRenderer.DrawText(g, tag.Name, smallBold, new Rectangle(r.X + 4, r.Y, r.Width - 6, TagStripH), Color.White, tf);
            }

            var thumb = Thumb(i);
            var tbox = ThumbRectOf(r);
            tbox.Inflate(-4, -3);
            using (var well = new SolidBrush(Theme.Panel2)) g.FillRectangle(well, tbox);
            if (thumb != null)
            {
                float s = Math.Min(tbox.Width / (float)thumb.Width, tbox.Height / (float)thumb.Height);
                if (s > 1) s = (float)Math.Floor(s);
                int tw = Math.Max(1, (int)(thumb.Width * s)), th = Math.Max(1, (int)(thumb.Height * s));
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(thumb, tbox.X + (tbox.Width - tw) / 2, tbox.Y + (tbox.Height - th) / 2, tw, th);
                g.PixelOffsetMode = PixelOffsetMode.Default;
            }

            string name = _doc!.Frames[i].Name;
            string label = name.Length > 0 ? $"{i + 1}  {name}" : $"{i + 1}";
            TextRenderer.DrawText(g, label, small, new Rectangle(r.X + 6, r.Y + ThumbTop + ThumbH + 1, r.Width - 10, 14),
                cur ? Theme.Accent : Theme.Text, tf);
            TextRenderer.DrawText(g, $"{d[i]} ms", small, DelayRectOf(r) with { X = r.X + 6, Width = r.Width - 10 }, Theme.Muted, tf);
        }

        if (_drag == Drag.Reorder && _moved && _reorderTo >= 0 && d.Length > 0)
        {
            var m = _reorderTo >= d.Length ? CellRect(d.Length - 1) : CellRect(_reorderTo);
            int x = _reorderTo >= d.Length ? m.Right + CellGap / 2 : m.Left - CellGap / 2;
            using var mark = new Pen(Theme.Accent, 3);
            g.DrawLine(mark, x, m.Y + ThumbTop - 2, x, m.Bottom);
        }
        g.ResetClip();
    }

    // ---------- input ----------

    private void FramesDown(MouseEventArgs e)
    {
        Focus();
        _down = e.Location;
        _moved = false;
        _drag = Drag.None;
        int f = CellAt(e.Location);
        if (f < 0) return;
        var r = CellRect(f);
        if (e.Button == MouseButtons.Right) { FrameMenu?.Invoke(f, PointToScreen(e.Location)); return; }
        if (TagRectOf(r).Contains(e.Location) && TagOf(f) is { } tag) { TagClicked?.Invoke(tag); return; }
        if (DelayRectOf(r).Contains(e.Location) && !Extend)
        {
            FrameClicked?.Invoke(f, false);
            BeginDelayEdit(f);
            return;
        }
        _drag = Drag.Reorder;
        _dragFrame = f;
        FrameClicked?.Invoke(f, Extend);
    }

    private void FramesMove(MouseEventArgs e)
    {
        if (_drag != Drag.Reorder) { FramesCursor(e); return; }
        if (Math.Abs(e.X - _down.X) > 3 || Math.Abs(e.Y - _down.Y) > 3) _moved = true;
        if (!_moved) return;
        // Nearest cell by centre; before or after it by which side of the centre the pointer is on.
        int best = 0; double bestD = double.MaxValue;
        for (int i = 0; i < _doc!.FrameCount; i++)
        {
            var r = CellRect(i);
            double dx = e.X - (r.X + r.Width / 2.0), dy = e.Y - (r.Y + r.Height / 2.0);
            double dist = dx * dx + dy * dy;
            if (dist < bestD) { bestD = dist; best = i; }
        }
        var br = CellRect(best);
        _reorderTo = e.X > br.X + br.Width / 2 ? best + 1 : best;
        Invalidate();
    }

    private void FramesCursor(MouseEventArgs e)
    {
        int f = CellAt(e.Location);
        Cursor c = f >= 0 && DelayRectOf(CellRect(f)).Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        if (Cursor != c) Cursor = c;
    }

    private void FramesDoubleClick(MouseEventArgs e)
    {
        int f = CellAt(e.Location);
        if (f < 0 || DelayRectOf(CellRect(f)).Contains(e.Location)) return;
        CancelEdit();
        FrameDoubleClicked?.Invoke(f);
    }

    private void FramesWheel(MouseEventArgs e)
    {
        if (Wrapped) _scrollY -= e.Delta / 2; else _scrollX -= e.Delta / 2;
        _scrollX = Math.Max(0, _scrollX); _scrollY = Math.Max(0, _scrollY);
        UpdateScroll();
        Invalidate();
    }

    // ---------- inline delay edit ----------

    private void BeginDelayEdit(int f)
    {
        CancelEdit();
        var r = DelayRectOf(CellRect(f));
        _editFrame = f;
        _editor = new TextBox
        {
            Bounds = new Rectangle(r.X + 6, r.Y - 1, r.Width - 12, 18), BorderStyle = BorderStyle.FixedSingle,
            Text = _doc!.Frames[f].Delay.ToString(), TextAlign = HorizontalAlignment.Center,
            BackColor = Theme.Panel2, ForeColor = Theme.Accent, Font = new Font(Font.FontFamily, 7.5f),
        };
        _editor.KeyDown += (_, k) =>
        {
            if (k.KeyCode == Keys.Enter) { k.SuppressKeyPress = true; CommitEdit(); }
            else if (k.KeyCode == Keys.Escape) { k.SuppressKeyPress = true; CancelEdit(); }
        };
        _editor.LostFocus += (_, _) => CommitEdit();
        Controls.Add(_editor);
        _editor.Focus();
        _editor.SelectAll();
    }

    private void CancelEdit()
    {
        if (_editor == null || _closingEditor) return;
        _closingEditor = true;
        var ed = _editor; _editor = null;
        Controls.Remove(ed); ed.Dispose();
        _closingEditor = false;
    }

    /// <summary>Apply the typed delay (whole 10 ms, like a drag) as one undo step via DelayDragged.</summary>
    private void CommitEdit()
    {
        if (_editor == null || _closingEditor || _doc == null) return;
        string digits = new string(_editor.Text.Where(char.IsDigit).ToArray());
        int f = _editFrame;
        CancelEdit();
        if (!int.TryParse(digits, out int v) || f >= _doc.FrameCount) { Invalidate(); return; }
        int now = Math.Clamp((int)(Math.Round(v / 10.0) * 10), 10, 60000), old = _doc.Frames[f].Delay;
        if (now != old)
        {
            _doc.Frames[f].Delay = now;
            DelayDragged?.Invoke(f, old, now);
        }
        Invalidate();
    }

    // ---------- test hooks ----------

    internal Rectangle TestCell(int i) => CellRect(i);
    internal Rectangle TestDelayRect(int i) => DelayRectOf(CellRect(i));
    internal Rectangle TestThumbRect(int i) => ThumbRectOf(CellRect(i));
    internal bool TestEditorOpen => _editor != null;
    internal void TestType(string text, bool commit = true)
    {
        if (_editor == null) return;
        _editor.Text = text;
        if (commit) CommitEdit(); else CancelEdit();
    }
    internal void TestMouse(string kind, Point p, MouseButtons b = MouseButtons.Left, int clicks = 1)
    {
        var a = new MouseEventArgs(b, clicks, p.X, p.Y, 0);
        switch (kind)
        {
            case "down": OnMouseDown(a); break;
            case "move": OnMouseMove(a); break;
            case "up": OnMouseUp(a); break;
            case "dbl": OnMouseDoubleClick(a); break;
        }
    }
}
