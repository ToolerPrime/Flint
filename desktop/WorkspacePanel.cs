/* Movable interface panels, Fireworks style.
 *
 * A page is one piece of interface (Layers, History, the toolbar...). Pages live
 * in groups: a group shows one page, or several as tabs. Groups stack in dock
 * columns on any side of the canvas (several columns per side are allowed),
 * float in their own windows, or fold up to their title bar. Toolbars are
 * "strip" pages: no title bar, just a grip, sized to their content.
 * DockManager does the docking, dragging and layouts. */
namespace Flint;

public enum DockSide { Left, Right, Bottom, Top, Float, Hidden }

/// <summary>One group's place in a layout.</summary>
public sealed record GroupState
{
    public string[] Pages { get; set; } = Array.Empty<string>();
    public string? Active { get; set; }
    public string? Title { get; set; }
    public DockSide Where { get; set; }
    public int Column { get; set; }          // 0 = outermost column on that side
    public int Index { get; set; }           // position within the column
    public double Weight { get; set; } = 1;
    public bool Collapsed { get; set; }
    public int[]? FloatBounds { get; set; }  // x, y, width, height
}

/// <summary>A whole workspace arrangement (saved as JSON by name).</summary>
public sealed class WorkspaceLayout
{
    public List<GroupState> Groups { get; set; } = new();
    /// <summary>Column widths (left/right) or row heights (top/bottom), outermost first, keyed by side name.</summary>
    public Dictionary<string, int[]> Extents { get; set; } = new();
    public string[] FoldedSides { get; set; } = Array.Empty<string>();
}

/// <summary>One piece of interface that can sit in any group.</summary>
public sealed class PanelPage
{
    public string Name { get; }
    public Control Content { get; }
    public Size Min { get; }
    /// <summary>Toolbars: no title bar, a grip instead, and this fixed height.</summary>
    public int StripHeight { get; }
    public bool IsStrip => StripHeight > 0;
    public WorkspacePanel? Group { get; internal set; }
    internal (DockSide side, int column) Home { get; set; } = (DockSide.Right, int.MaxValue);
    public PanelPage(string name, Control content, Size min, int stripHeight = 0)
    {
        Name = name; Content = content; Min = min; StripHeight = stripHeight;
        content.Dock = DockStyle.Fill;
    }
}

/// <summary>A panel group: a title bar (or tabs, or a toolbar grip) over one page's content.</summary>
public sealed class WorkspacePanel : Panel
{
    public const int HeaderHeight = 25, GripWidth = 10;
    private readonly DockManager _manager;
    private readonly List<PanelPage> _pages = new();
    private readonly Panel _header, _body, _tabs;
    private readonly Button _arrow, _menuButton;
    private readonly Label _grip;
    private readonly ContextMenuStrip _menu = new();
    private bool _collapsed;
    private string? _title;

    public IReadOnlyList<PanelPage> Pages => _pages;
    public PanelPage? Active { get; private set; }
    public double Weight { get; set; } = 1;
    public DockColumn? Column { get; internal set; }
    public Form? FloatingWindow { get; internal set; }
    internal bool FoldedBeforeFloat { get; set; }
    internal (DockSide side, int column, int index) Home { get; set; } = (DockSide.Right, int.MaxValue, int.MaxValue);
    public bool IsStrip => _pages.Count == 1 && _pages[0].IsStrip;
    /// <summary>The group's name: set by Rename, otherwise its pages' names.</summary>
    public string Title { get => _title ?? string.Join(" / ", _pages.Select(p => p.Name)); set { _title = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); Rebuild(); } }
    public bool HasCustomTitle => _title != null;

    public Size MinContent
    {
        get
        {
            if (IsStrip) return new Size(_pages[0].Min.Width, _pages[0].StripHeight);
            return new Size(_pages.Count == 0 ? 100 : _pages.Max(p => p.Min.Width), _pages.Count == 0 ? 60 : _pages.Max(p => p.Min.Height));
        }
    }

    public bool Collapsed
    {
        get => _collapsed && !IsStrip;
        set
        {
            if (_collapsed == value) return;
            _collapsed = value;
            _body.Visible = !Collapsed;
            _arrow.Text = Collapsed ? "▸" : "▾";
            Column?.Relayout();
        }
    }

    public WorkspacePanel(DockManager manager)
    {
        _manager = manager;
        _body = new Panel { Dock = DockStyle.Fill };
        _header = new Panel { Dock = DockStyle.Top, Height = HeaderHeight, Tag = "slim" };
        _grip = new Label { Dock = DockStyle.Left, Width = GripWidth, Cursor = Cursors.SizeAll, Tag = "slim", AccessibleName = "Move panel group" };
        _grip.Paint += (_, e) =>
        {
            using var b = new SolidBrush(Theme.Muted);
            for (int y = 6; y < _grip.Height - 4; y += 4) { e.Graphics.FillRectangle(b, 3, y, 2, 2); e.Graphics.FillRectangle(b, 6, y + 2, 2, 2); }
        };
        _arrow = new Button { Text = "▾", Dock = DockStyle.Left, Width = 22, FlatStyle = FlatStyle.Flat, TabStop = false, AccessibleName = "Fold panel group" };
        _arrow.FlatAppearance.BorderSize = 0;
        _arrow.Click += (_, _) => Collapsed = !Collapsed;
        _tabs = new Panel { Dock = DockStyle.Fill, Tag = "slim" };
        _menuButton = new Button { Text = "…", Dock = DockStyle.Right, Width = 26, TabStop = false };
        _menu.Opening += (_, _) => BuildMenu();
        _menuButton.Click += (_, _) => _menu.Show(_menuButton, new Point(0, _menuButton.Height));
        _header.Controls.Add(_tabs); _header.Controls.Add(_menuButton); _header.Controls.Add(_arrow); _header.Controls.Add(_grip);
        Controls.Add(_body); Controls.Add(_header);
        HookDrag(_grip, () => _manager.DragGroupFrom(this));
        Disposed += (_, _) => _menu.Dispose();
    }

    /// <summary>Starts a drag after the mouse moves a little with the button held: the callback says what is being dragged.</summary>
    private void HookDrag(Control c, Func<WorkspacePanel?> start)
    {
        Point down = default, offset = default; bool pressed = false, dragging = false; WorkspacePanel? moving = null;
        c.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { pressed = true; down = c.PointToScreen(e.Location); offset = e.Location; c.Capture = true; } };
        c.MouseMove += (_, e) =>
        {
            if (!pressed || e.Button != MouseButtons.Left) return;
            var p = c.PointToScreen(e.Location);
            if (!dragging && Math.Abs(p.X - down.X) + Math.Abs(p.Y - down.Y) < SystemInformation.DragSize.Width * 2) return;
            if (!dragging) { moving = start(); dragging = moving != null; c.Capture = true; if (!dragging) return; }
            _manager.DragTo(moving!, p, offset);
        };
        c.MouseUp += (_, e) =>
        {
            c.Capture = false; pressed = false;
            if (!dragging || moving == null) return;
            dragging = false;
            _manager.EndDrag(moving, c.PointToScreen(e.Location));
            moving = null;
        };
    }

    internal void AddPage(PanelPage page, bool activate = true)
    {
        if (!_pages.Contains(page)) _pages.Add(page);
        page.Group = this;
        if (activate || Active == null) Activate(page); else Rebuild();
    }

    internal void RemovePage(PanelPage page)
    {
        if (!_pages.Remove(page)) return;
        page.Group = null;
        if (page.Content.Parent == _body) _body.Controls.Remove(page.Content);
        if (Active == page) { Active = null; if (_pages.Count > 0) { Activate(_pages[0]); return; } }
        Rebuild();
    }

    public void Activate(PanelPage page)
    {
        if (!_pages.Contains(page)) return;
        if (Active != null && Active != page && Active.Content.Parent == _body) _body.Controls.Remove(Active.Content);
        Active = page;
        if (page.Content.Parent != _body) _body.Controls.Add(page.Content);
        page.Content.Visible = true;
        Rebuild();
    }

    /// <summary>Lays out the title bar: a grip only for toolbars; otherwise grip, fold arrow, a title or tabs, and the menu.</summary>
    private void Rebuild()
    {
        bool strip = IsStrip;
        _header.Dock = strip ? DockStyle.Left : DockStyle.Top;
        if (strip) { _header.Width = GripWidth; _arrow.Visible = _menuButton.Visible = _tabs.Visible = false; _grip.Dock = DockStyle.Fill; _body.Visible = true; }
        else { _header.Height = HeaderHeight; _arrow.Visible = _menuButton.Visible = _tabs.Visible = true; _grip.Dock = DockStyle.Left; }
        _grip.ContextMenuStrip = _menu;
        _body.Tag = strip ? "slim" : null;
        foreach (Control c in _tabs.Controls.Cast<Control>().ToList()) { _tabs.Controls.Remove(c); c.Dispose(); }
        if (!strip)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = Padding.Empty, Margin = Padding.Empty, Tag = "slim" };
            foreach (var p in _pages)
            {
                var page = p;
                bool on = page == Active;
                var tab = new Label
                {
                    Text = _pages.Count == 1 && HasCustomTitle ? Title : page.Name, AutoSize = true, Height = HeaderHeight, Padding = new Padding(4, 5, 4, 0),
                    Margin = new Padding(0), Cursor = _pages.Count > 1 ? Cursors.Hand : Cursors.SizeAll, Tag = on ? "keep" : "slim",
                    ForeColor = on ? Theme.Text : Theme.Muted, BackColor = on && _pages.Count > 1 ? Theme.Panel3 : Color.Transparent,
                    AccessibleName = _pages.Count > 1 ? $"{page.Name} tab" : $"Move {page.Name} panel",
                };
                tab.Click += (_, _) => Activate(page);
                tab.DoubleClick += (_, _) => { if (_pages.Count == 1) Collapsed = !Collapsed; };
                tab.ContextMenuStrip = _menu;
                // Dragging a tab moves just that page (out of a multi-page group); a lone page's title moves the whole group.
                HookDrag(tab, () => _pages.Count > 1 ? _manager.DragPageOut(page) : _manager.DragGroupFrom(this));
                row.Controls.Add(tab);
            }
            _tabs.Controls.Add(row);
        }
        if (FloatingWindow != null) FloatingWindow.Text = Title;
        _menuButton.AccessibleName = $"{Title} panel options";
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();
        var page = Active;
        if (!IsStrip && page != null)
        {
            var with = new ToolStripMenuItem($"Group {page.Name} with");
            foreach (var g in _manager.Groups.Where(g => g != this && !g.IsStrip && (g.Column != null || g.FloatingWindow != null)))
            {
                var target = g;
                with.DropDownItems.Add(target.Title, null, (_, _) => _manager.MovePage(page, target));
            }
            if (_pages.Count > 1)
            {
                if (with.DropDownItems.Count > 0) with.DropDownItems.Add(new ToolStripSeparator());
                with.DropDownItems.Add("New panel group", null, (_, _) => _manager.SplitPage(page));
            }
            with.Enabled = with.DropDownItems.Count > 0;
            _menu.Items.Add(with);
            _menu.Items.Add("Rename panel group…", null, (_, _) => _manager.RenameGroup(this));
            _menu.Items.Add(new ToolStripSeparator());
        }
        _menu.Items.Add("Float", null, (_, _) => _manager.Float(this));
        foreach (var side in new[] { DockSide.Left, DockSide.Right, DockSide.Top, DockSide.Bottom })
            _menu.Items.Add($"Dock {side.ToString().ToLowerInvariant()}", null, (_, _) => _manager.DockNewColumnOrEnd(this, side));
        if (!IsStrip) _menu.Items.Add(Collapsed ? "Expand" : "Fold up", null, (_, _) => Collapsed = !Collapsed);
        if (Column != null && Column.Side != DockSide.Top) _menu.Items.Add($"Fold away the {Column.Side.ToString().ToLowerInvariant()} side", null, (_, _) => _manager.FoldSide(Column.Side, true));
        _menu.Items.Add(new ToolStripSeparator());
        if (_pages.Count > 1 && page != null) _menu.Items.Add($"Close {page.Name}", null, (_, _) => _manager.HidePage(page));
        _menu.Items.Add(_pages.Count > 1 ? "Close panel group" : "Close panel", null, (_, _) => _manager.HideGroup(this));
    }

    /// <summary>Height (side columns) or width (top/bottom rows) when folded up.</summary>
    public int FoldedExtent(bool horizontal) => horizontal ? 150 : HeaderHeight;
    /// <summary>The header's screen rectangle: dropping a dragged panel here groups it in as a tab.</summary>
    public Rectangle HeaderScreenBounds => IsStrip || !_header.IsHandleCreated ? Rectangle.Empty : _header.RectangleToScreen(_header.ClientRectangle);
}

/// <summary>A stack of groups docked on one side of the canvas, with dividers between them and a grip on its inner edge.</summary>
public sealed class DockColumn : Panel
{
    public const int Bar = 5;
    private readonly List<WorkspacePanel> _items = new();
    private readonly List<Control> _bars = new();
    private readonly Panel _grip;
    private readonly DockManager _manager;
    public DockSide Side { get; }
    public IReadOnlyList<WorkspacePanel> Items => _items;
    public bool Horizontal => Side is DockSide.Bottom or DockSide.Top;
    /// <summary>Set once you drag the column's edge, so Flint stops sizing it automatically.</summary>
    public bool UserSized { get; set; }
    /// <summary>A row holding only toolbars: sized to them, no resize grip.</summary>
    public bool StripOnly => _items.Count > 0 && _items.All(i => i.IsStrip);

    public DockColumn(DockManager manager, DockSide side, int extent)
    {
        _manager = manager; Side = side;
        Dock = side switch { DockSide.Left => DockStyle.Left, DockSide.Right => DockStyle.Right, DockSide.Top => DockStyle.Top, _ => DockStyle.Bottom };
        if (Horizontal) Height = extent; else Width = extent;
        _grip = new Panel
        {
            Tag = "slim", Cursor = Horizontal ? Cursors.HSplit : Cursors.VSplit, Width = Bar, Height = Bar,
            Dock = side switch { DockSide.Left => DockStyle.Right, DockSide.Right => DockStyle.Left, DockSide.Top => DockStyle.Bottom, _ => DockStyle.Top },
            AccessibleName = $"Resize these panels (double-click to fold the {side.ToString().ToLowerInvariant()} side away)",
        };
        int start = 0; Point down = default;
        _grip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { _grip.Capture = true; down = _grip.PointToScreen(e.Location); start = Extent; } };
        _grip.MouseMove += (_, e) =>
        {
            if (!_grip.Capture) return;
            var p = _grip.PointToScreen(e.Location);
            int delta = Side switch { DockSide.Left => p.X - down.X, DockSide.Right => down.X - p.X, DockSide.Top => p.Y - down.Y, _ => down.Y - p.Y };
            if (delta == 0) return;
            Extent = start + delta; UserSized = true;
        };
        _grip.MouseUp += (_, _) => _grip.Capture = false;
        _grip.DoubleClick += (_, _) => { if (Side != DockSide.Top) _manager.FoldSide(Side, true); };
        Controls.Add(_grip);
    }

    public int MinExtent => StripOnly ? _items.Max(i => i.MinContent.Height)
        : _items.Count == 0 ? 60 : Math.Max(60, Horizontal ? _items.Max(i => i.Collapsed ? 0 : i.MinContent.Height + WorkspacePanel.HeaderHeight) : _items.Max(i => i.MinContent.Width));

    /// <summary>Width (side columns) or height (top/bottom rows), kept within sensible limits.</summary>
    public int Extent
    {
        get => Horizontal ? Height : Width;
        set
        {
            if (StripOnly) { Height = MinExtent; return; }
            int room = Parent == null ? 0 : (Horizontal ? Parent.Height : Parent.Width);
            int max = room < 700 ? 4000 : Math.Max(MinExtent, room - 300);   // the window may not be laid out yet at startup
            int v = Math.Clamp(value, Math.Min(MinExtent, max), max);
            if (Horizontal) Height = v; else Width = v;
        }
    }

    public void Insert(WorkspacePanel p, int index)
    {
        _items.Remove(p);
        _items.Insert(Math.Clamp(index, 0, _items.Count), p);
        p.Column = this; p.Dock = DockStyle.None;
        if (p.Parent != this) Controls.Add(p);
        RebuildBars();
    }

    public void Remove(WorkspacePanel p)
    {
        if (!_items.Remove(p)) return;
        Controls.Remove(p); p.Column = null;
        RebuildBars();
    }

    private void RebuildBars()
    {
        foreach (var b in _bars) { Controls.Remove(b); b.Dispose(); }
        _bars.Clear();
        _grip.Visible = !StripOnly;
        Tag = StripOnly ? "slim" : null;   // a toolbar row is short on purpose (layout check)
        for (int i = 0; i + 1 < _items.Count; i++)
        {
            int at = i;
            var bar = new Panel { Tag = "slim", Cursor = Horizontal ? Cursors.VSplit : Cursors.HSplit, AccessibleName = "Resize panels" };
            Point down = default; double wa = 0, wb = 0;
            bar.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                var (ia, ib) = Neighbours(at);
                if (ia < 0 || ib < 0) return;
                bar.Capture = true; down = bar.PointToScreen(e.Location); wa = _items[ia].Weight; wb = _items[ib].Weight;
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
        if (StripOnly) Height = MinExtent;
        Relayout();
    }

    /// <summary>The nearest unfolded groups before and after the divider following group <paramref name="at"/> (-1 if none).</summary>
    private (int a, int b) Neighbours(int at)
    {
        int ia = at, ib = at + 1;
        while (ia >= 0 && _items[ia].Collapsed) ia--;
        while (ib < _items.Count && _items[ib].Collapsed) ib++;
        return (ia, ib < _items.Count ? ib : -1);
    }

    /// <summary>Moving the divider after group <paramref name="at"/> by <paramref name="delta"/> pixels shifts weight between the open groups either side of it.</summary>
    internal void DragBar(int at, int delta, double wa, double wb)
    {
        var (ia, ib) = Neighbours(at);
        if (ia < 0 || ib < 0) return;
        var a = _items[ia]; var b = _items[ib];
        int total = Area().length;
        double sum = _items.Where(i => !i.Collapsed).Sum(i => i.Weight);
        if (total <= 0 || sum <= 0) return;
        double pxPerWeight = total / sum, pa = wa * pxPerWeight + delta, pb = wb * pxPerWeight - delta;
        int MinOf(WorkspacePanel g) => Horizontal ? g.MinContent.Width : g.MinContent.Height + WorkspacePanel.HeaderHeight;
        int minA = MinOf(a), minB = MinOf(b);
        if (pa < minA) { pb -= minA - pa; pa = minA; }
        if (pb < minB) { pa -= minB - pb; pb = minB; }
        if (pa < minA) return;
        a.Weight = pa / pxPerWeight; b.Weight = pb / pxPerWeight;
        Relayout();
    }

    /// <summary>The rectangle the groups share and its length along the stacking direction (minus dividers and folded groups).</summary>
    private (Rectangle rect, int length) Area()
    {
        var r = ClientRectangle;
        if (_grip.Visible)
            switch (Side)
            {
                case DockSide.Left: r.Width -= Bar; break;
                case DockSide.Right: r.X += Bar; r.Width -= Bar; break;
                case DockSide.Top: r.Height -= Bar; break;
                default: r.Y += Bar; r.Height -= Bar; break;
            }
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

    /// <summary>Where a group dropped at a point (in this column's coordinates) would go.</summary>
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
