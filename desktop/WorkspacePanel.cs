/* Movable interface panels, Fireworks style. Panels stack in dock columns on
 * the left, right or bottom of the canvas, with dividers to resize them; each
 * can fold up to its title bar, float in its own window, or be hidden. Drag a
 * panel by its title: a highlight shows where it will dock when you let go.
 * Layouts can be captured, restored and saved by name (DockManager). */

namespace Flint;

public enum DockSide { Left, Right, Bottom, Float, Hidden }

/// <summary>One panel's place in a layout.</summary>
public sealed class PanelState
{
    public DockSide Where { get; set; }
    public int Index { get; set; }
    public double Weight { get; set; } = 1;
    public bool Collapsed { get; set; }
    public int[]? FloatBounds { get; set; }   // x, y, width, height
}

/// <summary>A whole workspace arrangement (saved as JSON by name).</summary>
public sealed class WorkspaceLayout
{
    public Dictionary<string, PanelState> Panels { get; set; } = new();
    public int LeftWidth { get; set; } = 100;
    public int RightWidth { get; set; } = 294;
    public int BottomHeight { get; set; } = 260;
}

public sealed class WorkspacePanel : Panel
{
    public const int HeaderHeight = 25;
    private readonly DockManager _manager;
    private readonly Panel _header;
    private readonly Button _arrow;
    private readonly Label _title;
    private bool _collapsed;
    private Point _down, _offset;
    private bool _pressed, _dragging;

    public Control Content { get; }
    public Size MinContent { get; }
    public double Weight { get; set; } = 1;
    public DockColumn? Column { get; internal set; }
    public Form? FloatingWindow { get; internal set; }
    public bool IsHidden => Column == null && FloatingWindow == null;
    /// <summary>Where a floating panel returns when its window is closed.</summary>
    internal (DockSide side, int index) Home { get; set; }
    /// <summary>Folded state to restore when a floating panel goes back to its dock.</summary>
    internal bool FoldedBeforeFloat { get; set; }

    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value) return;
            _collapsed = value;
            Content.Visible = !value;
            _arrow.Text = value ? "▸" : "▾";
            Column?.Relayout();
        }
    }

    public WorkspacePanel(DockManager manager, string title, Control content, Size minContent)
    {
        _manager = manager; Name = title; Content = content; MinContent = minContent;
        _header = new Panel { Dock = DockStyle.Top, Height = HeaderHeight, Tag = "slim" };
        _arrow = new Button { Text = "▾", Dock = DockStyle.Left, Width = 24, FlatStyle = FlatStyle.Flat, AccessibleName = $"Fold {title} panel", TabStop = false };
        _arrow.FlatAppearance.BorderSize = 0;
        _arrow.Click += (_, _) => Collapsed = !Collapsed;
        _title = new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 0, 0, 0), Cursor = Cursors.SizeAll, AccessibleName = $"Move {title} panel" };
        var menuButton = new Button { Text = "…", Dock = DockStyle.Right, Width = 26, AccessibleName = $"{title} panel options", TabStop = false };
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            menu.Items.Add("Float panel", null, (_, _) => _manager.Float(this));
            foreach (var side in new[] { DockSide.Left, DockSide.Right, DockSide.Bottom })
                menu.Items.Add($"Dock {side.ToString().ToLowerInvariant()}", null, (_, _) => _manager.Dock(this, side, int.MaxValue));
            menu.Items.Add(Collapsed ? "Expand" : "Fold up", null, (_, _) => Collapsed = !Collapsed);
            menu.Items.Add("Close panel", null, (_, _) => _manager.Hide(this));
        };
        _title.ContextMenuStrip = menu;
        menuButton.Click += (_, _) => menu.Show(menuButton, new Point(0, menuButton.Height));
        _header.Controls.Add(_title); _header.Controls.Add(menuButton); _header.Controls.Add(_arrow);
        content.Dock = DockStyle.Fill;
        Controls.Add(content); Controls.Add(_header);
        _title.DoubleClick += (_, _) => Collapsed = !Collapsed;
        _title.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _down = _title.PointToScreen(e.Location); _offset = e.Location; _pressed = true; _title.Capture = true;
        };
        _title.MouseMove += (_, e) =>
        {
            if (!_pressed || e.Button != MouseButtons.Left) return;
            var p = _title.PointToScreen(e.Location);
            if (!_dragging && Math.Abs(p.X - _down.X) + Math.Abs(p.Y - _down.Y) < SystemInformation.DragSize.Width * 2) return;
            if (!_dragging) { _dragging = true; _manager.BeginDrag(this); _title.Capture = true; }
            _manager.DragTo(this, p, _offset);
        };
        _title.MouseUp += (_, e) =>
        {
            _title.Capture = false; _pressed = false;
            if (!_dragging) return;
            _dragging = false;
            _manager.EndDrag(this, _title.PointToScreen(e.Location));
        };
        Disposed += (_, _) => menu.Dispose();
    }

    /// <summary>Height (in a side column) or width (in the bottom row) when folded up.</summary>
    public int FoldedExtent(bool horizontal) => horizontal ? 150 : HeaderHeight;
}

/// <summary>A stack of panels docked on one side of the canvas, with dividers between them and a grip on its inner edge.</summary>
public sealed class DockColumn : Panel
{
    public const int Bar = 5;
    private readonly List<WorkspacePanel> _items = new();
    private readonly List<Control> _bars = new();
    private readonly Panel _grip;
    private readonly int _minExtent;
    public DockSide Side { get; }
    public IReadOnlyList<WorkspacePanel> Items => _items;
    public bool Horizontal => Side == DockSide.Bottom;
    /// <summary>Set once you drag the column's edge, so Flint stops sizing it automatically.</summary>
    public bool UserSized { get; set; }

    public DockColumn(DockSide side, int extent, int minExtent)
    {
        Side = side; _minExtent = minExtent;
        Dock = side == DockSide.Left ? DockStyle.Left : side == DockSide.Right ? DockStyle.Right : DockStyle.Bottom;
        if (Horizontal) Height = extent; else Width = extent;
        _grip = new Panel
        {
            Tag = "slim", Cursor = Horizontal ? Cursors.HSplit : Cursors.VSplit,
            Dock = side == DockSide.Left ? DockStyle.Right : side == DockSide.Right ? DockStyle.Left : DockStyle.Top,
            Width = Bar, Height = Bar, AccessibleName = $"Resize the {side.ToString().ToLowerInvariant()} panels",
        };
        int start = 0; Point down = default;
        _grip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { _grip.Capture = true; down = _grip.PointToScreen(e.Location); start = Extent; } };
        _grip.MouseMove += (_, e) =>
        {
            if (!_grip.Capture) return;
            var p = _grip.PointToScreen(e.Location);
            int delta = Side == DockSide.Left ? p.X - down.X : Side == DockSide.Right ? down.X - p.X : down.Y - p.Y;
            Extent = start + delta; UserSized = true;
        };
        _grip.MouseUp += (_, _) => _grip.Capture = false;
        Controls.Add(_grip);
        Visible = false;
    }

    /// <summary>Width (side columns) or height (bottom row), kept within sensible limits.</summary>
    public int Extent
    {
        get => Horizontal ? Height : Width;
        set
        {
            int max = Parent == null ? 4000 : Math.Max(_minExtent, (Horizontal ? Parent.Height : Parent.Width) - 300);
            int v = Math.Clamp(value, _minExtent, max);
            if (Horizontal) Height = v; else Width = v;
        }
    }

    public void Insert(WorkspacePanel p, int index)
    {
        _items.Remove(p);
        _items.Insert(Math.Clamp(index, 0, _items.Count), p);
        p.Column = this; p.Dock = DockStyle.None;
        if (p.Parent != this) Controls.Add(p);
        Visible = true;
        RebuildBars();
    }

    public void Remove(WorkspacePanel p)
    {
        if (!_items.Remove(p)) return;
        Controls.Remove(p); p.Column = null;
        Visible = _items.Count > 0;
        RebuildBars();
    }

    private void RebuildBars()
    {
        foreach (var b in _bars) { Controls.Remove(b); b.Dispose(); }
        _bars.Clear();
        for (int i = 0; i + 1 < _items.Count; i++)
        {
            int at = i;
            var bar = new Panel { Tag = "slim", Cursor = Horizontal ? Cursors.VSplit : Cursors.HSplit, AccessibleName = "Resize panels" };
            int start = 0; Point down = default; double wa = 0, wb = 0;
            bar.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                var (ia, ib) = Neighbours(at);
                if (ia < 0 || ib < 0) return;
                bar.Capture = true; down = bar.PointToScreen(e.Location); wa = _items[ia].Weight; wb = _items[ib].Weight; start = 0;
            };
            bar.MouseMove += (_, e) =>
            {
                if (!bar.Capture) return;
                var p = bar.PointToScreen(e.Location);
                DragBar(at, Horizontal ? p.X - down.X : p.Y - down.Y, wa, wb);
            };
            bar.MouseUp += (_, _) => bar.Capture = false;
            _bars.Add(bar); Controls.Add(bar);
        }
        Relayout();
    }

    /// <summary>The nearest unfolded panels before and after the divider following panel <paramref name="at"/> (-1 if none).</summary>
    private (int a, int b) Neighbours(int at)
    {
        int ia = at, ib = at + 1;
        while (ia >= 0 && _items[ia].Collapsed) ia--;
        while (ib < _items.Count && _items[ib].Collapsed) ib++;
        return (ia, ib < _items.Count ? ib : -1);
    }

    /// <summary>Moving the divider after panel <paramref name="at"/> by <paramref name="delta"/> pixels shifts weight between the open panels either side of it (folded ones are skipped).</summary>
    internal void DragBar(int at, int delta, double wa, double wb)
    {
        var (ia, ib) = Neighbours(at);
        if (ia < 0 || ib < 0) return;
        var a = _items[ia]; var b = _items[ib];
        int total = Area().length;
        double sum = _items.Where(i => !i.Collapsed).Sum(i => i.Weight);
        if (total <= 0 || sum <= 0) return;
        double pxPerWeight = total / sum, pa = wa * pxPerWeight + delta, pb = wb * pxPerWeight - delta;
        int minA = Horizontal ? a.MinContent.Width : a.MinContent.Height + WorkspacePanel.HeaderHeight;
        int minB = Horizontal ? b.MinContent.Width : b.MinContent.Height + WorkspacePanel.HeaderHeight;
        if (pa < minA) { pb -= minA - pa; pa = minA; }
        if (pb < minB) { pa -= minB - pb; pb = minB; }
        if (pa < minA) return;
        a.Weight = pa / pxPerWeight; b.Weight = pb / pxPerWeight;
        Relayout();
    }

    /// <summary>The rectangle the panels share and its length along the stacking direction (minus dividers and folded panels).</summary>
    private (Rectangle rect, int length) Area()
    {
        var r = ClientRectangle;
        if (Side == DockSide.Left) r.Width -= Bar; else if (Side == DockSide.Right) { r.X += Bar; r.Width -= Bar; } else { r.Y += Bar; r.Height -= Bar; }
        int len = (Horizontal ? r.Width : r.Height) - Bar * Math.Max(0, _items.Count - 1)
            - _items.Where(i => i.Collapsed).Sum(i => i.FoldedExtent(Horizontal));
        return (r, Math.Max(0, len));
    }

    public void Relayout()
    {
        if (_items.Count == 0) return;
        SuspendLayout();
        var (r, len) = Area();
        double sum = _items.Where(i => !i.Collapsed).Sum(i => Math.Max(0.01, i.Weight));
        int pos = Horizontal ? r.X : r.Y, expandedLeft = _items.Count(i => !i.Collapsed), used = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            var p = _items[i];
            int size;
            if (p.Collapsed) size = p.FoldedExtent(Horizontal);
            else
            {
                expandedLeft--;
                size = expandedLeft == 0 ? len - used : (int)Math.Round(len * Math.Max(0.01, p.Weight) / sum);
                used += size;
            }
            p.Bounds = Horizontal ? new Rectangle(pos, r.Y, size, r.Height) : new Rectangle(r.X, pos, r.Width, size);
            pos += size;
            if (i < _bars.Count)
            {
                _bars[i].Bounds = Horizontal ? new Rectangle(pos, r.Y, Bar, r.Height) : new Rectangle(r.X, pos, r.Width, Bar);
                pos += Bar;
            }
        }
        ResumeLayout(false);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

    /// <summary>Where a panel dropped at a point (in this column's coordinates) would go.</summary>
    public int IndexAt(Point p)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var b = _items[i].Bounds;
            if (Horizontal ? p.X < b.X + b.Width / 2 : p.Y < b.Y + b.Height / 2) return i;
        }
        return _items.Count;
    }
}
