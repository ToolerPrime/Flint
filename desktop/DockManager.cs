/* Owns the docking around the canvas: columns on each side (several allowed,
 * outermost first), panel groups and their pages, floating windows, folded
 * sides, the drag highlight, and capturing / restoring layouts. */
namespace Flint;

public enum DropKind { Tab, Insert, NewColumn }

public readonly record struct DropTarget(DropKind Kind, DockSide Side, int Column, int Index, WorkspacePanel? Group);

public sealed class DockManager
{
    private static readonly DockSide[] Sides = { DockSide.Top, DockSide.Bottom, DockSide.Left, DockSide.Right };
    private readonly Form _owner;
    private readonly Control _host;
    private readonly Dictionary<DockSide, List<DockColumn>> _sides = Sides.ToDictionary(s => s, _ => new List<DockColumn>());
    private readonly Dictionary<DockSide, Panel> _rails = new();
    private readonly HashSet<DockSide> _folded = new();
    private readonly List<PanelPage> _pages = new();
    private readonly List<WorkspacePanel> _groups = new();
    private Form? _hint;

    public WorkspaceLayout Default { get; set; } = new();
    /// <summary>Asks for a name (for Rename panel group); returns null on Cancel.</summary>
    public Func<string, string?> Prompt { get; set; } = s => s;
    public IReadOnlyList<PanelPage> Pages => _pages;
    public IReadOnlyList<WorkspacePanel> Groups => _groups;
    public IReadOnlyList<DockColumn> Columns(DockSide side) => _sides[side];
    public bool IsFolded(DockSide side) => _folded.Contains(side);
    public PanelPage? FindPage(string name) => _pages.FirstOrDefault(p => p.Name == name);
    public event Action? Changed;

    public DockManager(Form owner, Control host)
    {
        _owner = owner; _host = host;
        foreach (var side in new[] { DockSide.Left, DockSide.Right, DockSide.Bottom })
        {
            var s = side;
            var rail = new Panel
            {
                Dock = side == DockSide.Left ? DockStyle.Left : side == DockSide.Right ? DockStyle.Right : DockStyle.Bottom,
                Width = 12, Height = 12, Visible = false, Cursor = Cursors.Hand, Tag = "slim", AccessibleName = $"Show the {side.ToString().ToLowerInvariant()} panels",
            };
            rail.Paint += (_, e) =>
            {
                using var b = new SolidBrush(Theme.Accent);
                var c = new Point(rail.Width / 2, rail.Height / 2);
                Point[] tri = s switch
                {
                    DockSide.Left => new[] { new Point(c.X - 2, c.Y - 5), new Point(c.X + 3, c.Y), new Point(c.X - 2, c.Y + 5) },
                    DockSide.Right => new[] { new Point(c.X + 2, c.Y - 5), new Point(c.X - 3, c.Y), new Point(c.X + 2, c.Y + 5) },
                    _ => new[] { new Point(c.X - 5, c.Y + 2), new Point(c.X, c.Y - 3), new Point(c.X + 5, c.Y + 2) },
                };
                e.Graphics.FillPolygon(b, tri);
            };
            rail.Click += (_, _) => FoldSide(s, false);
            _rails[side] = rail;
            host.Controls.Add(rail);
        }
    }

    public PanelPage AddPage(string name, Control content, Size min, int stripHeight = 0)
    {
        var p = new PanelPage(name, content, min, stripHeight);
        _pages.Add(p);
        return p;
    }

    private WorkspacePanel NewGroup(params PanelPage[] pages)
    {
        var g = new WorkspacePanel(this);
        foreach (var p in pages)
        {
            var from = p.Group;
            from?.RemovePage(p);
            if (from != null && from.Pages.Count == 0) Discard(from);
            g.AddPage(p);
        }
        _groups.Add(g);
        return g;
    }

    // ---------- structure ----------

    private DockColumn NewColumn(DockSide side, int at)
    {
        var col = new DockColumn(this, side, side switch { DockSide.Left => 110, DockSide.Right => 294, DockSide.Top => 40, _ => 260 });
        var list = _sides[side];
        list.Insert(Math.Clamp(at, 0, list.Count), col);
        if (_folded.Contains(side)) col.Visible = false;
        _host.Controls.Add(col);
        return col;
    }

    /// <summary>Takes a group out of its column or floating window (empty columns are removed).</summary>
    private void Detach(WorkspacePanel g)
    {
        if (g.Column is { } c)
        {
            c.Remove(g);
            if (c.Items.Count == 0) { _sides[c.Side].Remove(c); _host.Controls.Remove(c); c.Dispose(); }
        }
        if (g.FloatingWindow is { } f)
        {
            g.FloatingWindow = null;
            f.Controls.Remove(g);
            f.Dispose();
        }
    }

    private void Discard(WorkspacePanel g)
    {
        Detach(g);
        foreach (var p in g.Pages.ToList()) g.RemovePage(p);
        _groups.Remove(g);
        g.Dispose();
    }

    private static bool Docked(DockSide side) => side is DockSide.Left or DockSide.Right or DockSide.Top or DockSide.Bottom;

    /// <summary>Docks a group: into an existing column at an index, or into a new column at a column position (0 = outermost).</summary>
    public void DockGroup(WorkspacePanel g, DockSide side, int column, int index, bool newColumn)
    {
        if (!Docked(side)) { Float(g); return; }
        var list = _sides[side];
        DockColumn? target = !newColumn && list.Count > 0 ? list[Math.Clamp(column, 0, list.Count - 1)] : null;
        if (target != null && g.Column == target)
        {
            int from = target.Items.ToList().IndexOf(g);
            if (index > from && index != int.MaxValue) index--;
        }
        if (newColumn && g.Column is { } own && own.Side == side && own.Items.Count == 1 && list.IndexOf(own) < column) column--;   // its own column disappears
        Detach(g);
        if (target != null && !list.Contains(target)) target = null;
        target ??= NewColumn(side, newColumn ? column : list.Count);
        g.Visible = true;
        target.Insert(g, index);
        if (_folded.Contains(side)) FoldSide(side, false);
        Rearrange();
        Changed?.Invoke();
    }

    /// <summary>"Dock left/right/top/bottom" from a menu: toolbars get their own row; panels go to the end of the innermost column.</summary>
    public void DockNewColumnOrEnd(WorkspacePanel g, DockSide side)
    {
        var list = _sides[side];
        var inner = list.LastOrDefault(c => c != g.Column || c.Items.Count > 1);
        bool fits = inner != null && !g.IsStrip && !inner.StripOnly;
        if (fits) DockGroup(g, side, list.IndexOf(inner!), int.MaxValue, false);
        else DockGroup(g, side, list.Count, 0, true);
    }

    public void Float(WorkspacePanel g, Rectangle? bounds = null)
    {
        if (g.FloatingWindow != null) { g.FloatingWindow.Activate(); return; }
        var screen = g.Parent != null && g.IsHandleCreated ? g.RectangleToScreen(g.ClientRectangle) : new Rectangle(Cursor.Position, new Size(300, 400));
        if (Program.Headless && bounds == null) screen.Location = new Point(-6000, -6000);
        if (g.Column is { } c) g.Home = (c.Side, _sides[c.Side].IndexOf(c), c.Items.ToList().IndexOf(g));
        Detach(g);
        var min = g.MinContent;
        var size = g.IsStrip ? new Size(Math.Max(screen.Width, 420), min.Height) : new Size(Math.Max(screen.Width, 240), Math.Max(screen.Height, 200));
        var r = bounds ?? new Rectangle(screen.Location, size);
        var f = new ThemedForm
        {
            Text = g.Title, FormBorderStyle = FormBorderStyle.SizableToolWindow, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            Location = r.Location, ClientSize = r.Size,
            MinimumSize = g.IsStrip ? new Size(200, min.Height + 30) : new Size(Math.Min(min.Width + 20, 600), min.Height + WorkspacePanel.HeaderHeight + 40),
        };
        g.FloatingWindow = f;
        g.FoldedBeforeFloat = g.Collapsed;
        g.Collapsed = false;
        g.Dock = DockStyle.Fill; g.Visible = true;
        f.Controls.Add(g);
        f.FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing || _owner.IsDisposed || _owner.Disposing) return;
            e.Cancel = true;
            var (side, col, idx) = g.Home;
            if (!Docked(side)) side = g.IsStrip ? DockSide.Top : DockSide.Right;
            bool exists = col < _sides[side].Count && !g.IsStrip;
            DockGroup(g, side, exists ? col : Math.Min(col, _sides[side].Count), idx, !exists);
            g.Collapsed = g.FoldedBeforeFloat;
        };
        f.Show(_owner);
        Rearrange();
        Changed?.Invoke();
    }

    // ---------- pages and groups ----------

    /// <summary>Moves a page into another group as a tab (Fireworks' "Group ... with").</summary>
    public void MovePage(PanelPage page, WorkspacePanel target)
    {
        if (page.Group == target || target.IsStrip || page.IsStrip) return;
        var from = page.Group;
        from?.RemovePage(page);
        target.AddPage(page);
        if (from != null && from.Pages.Count == 0) Discard(from);
        target.Column?.Relayout();
        Rearrange();
        Changed?.Invoke();
    }

    /// <summary>Takes a page out of its group into a new group just after it.</summary>
    public void SplitPage(PanelPage page)
    {
        var from = page.Group;
        if (from == null || from.Pages.Count < 2) return;
        var g = NewGroup(page);
        if (from.Column is { } c) DockGroup(g, c.Side, _sides[c.Side].IndexOf(c), c.Items.ToList().IndexOf(from) + 1, false);
        else Float(g);
    }

    public void RenameGroup(WorkspacePanel g)
    {
        if (Prompt(g.Title) is { } name) { g.Title = name; Changed?.Invoke(); }
    }

    public void HidePage(PanelPage page)
    {
        var g = page.Group;
        if (g == null) return;
        if (g.Column is { } c) page.Home = (c.Side, _sides[c.Side].IndexOf(c));
        else page.Home = (g.Home.side, g.Home.column);
        if (g.Pages.Count == 1) Discard(g); else g.RemovePage(page);
        Rearrange();
        Changed?.Invoke();
    }

    public void HideGroup(WorkspacePanel g) { foreach (var p in g.Pages.ToList()) HidePage(p); }

    /// <summary>Shows a hidden page where it last was (or brings its group forward and unfolds it).</summary>
    public void ShowPage(PanelPage page)
    {
        if (page.Group is { } g)
        {
            g.Activate(page);
            if (g.FloatingWindow != null) g.FloatingWindow.Activate(); else g.Collapsed = false;
            if (g.Column != null && _folded.Contains(g.Column.Side)) FoldSide(g.Column.Side, false);
            return;
        }
        var ng = NewGroup(page);
        var (side, col) = page.Home;
        if (!Docked(side)) side = page.IsStrip ? DockSide.Top : DockSide.Right;
        var list = _sides[side];
        if (page.IsStrip || list.Count == 0) DockGroup(ng, side, page.IsStrip ? Math.Min(col, list.Count) : list.Count, 0, true);
        else DockGroup(ng, side, Math.Clamp(col, 0, list.Count - 1), int.MaxValue, false);
    }

    public bool IsShowing(PanelPage page) => page.Group != null && !(page.Group.Column != null && _folded.Contains(page.Group.Column.Side));

    public void TogglePage(PanelPage page) { if (IsShowing(page)) HidePage(page); else ShowPage(page); }

    /// <summary>Folds a whole side away (a thin strip with an arrow brings it back), like Fireworks' dock handle.</summary>
    public void FoldSide(DockSide side, bool fold)
    {
        if (side is not (DockSide.Left or DockSide.Right or DockSide.Bottom)) return;
        if (fold && _sides[side].Count == 0) return;
        if (fold) _folded.Add(side); else _folded.Remove(side);
        foreach (var c in _sides[side]) c.Visible = !fold;
        _rails[side].Visible = fold;
        Rearrange();
        Changed?.Invoke();
    }

    /// <summary>Docking order: toolbars across the top, the bottom row full width, then the side columns (outermost first), then the canvas.</summary>
    private void Rearrange()
    {
        var seq = new List<Control>();
        foreach (var side in Sides)
        {
            seq.AddRange(_sides[side]);
            if (_rails.TryGetValue(side, out var rail)) seq.Add(rail);
        }
        _host.SuspendLayout();
        int total = _host.Controls.Count;
        for (int k = 0; k < seq.Count; k++)
            if (_host.Controls.Contains(seq[k])) _host.Controls.SetChildIndex(seq[k], total - 1 - k);
        _host.ResumeLayout(true);
        foreach (var c in _sides.Values.SelectMany(l => l)) c.Relayout();
    }

    // ---------- dragging ----------

    /// <summary>Starting to drag a group by its grip or lone title: it floats under the pointer.</summary>
    public WorkspacePanel? DragGroupFrom(WorkspacePanel g)
    {
        if (g.FloatingWindow == null) Float(g);
        _hint ??= new HintForm();
        return g;
    }

    /// <summary>Starting to drag one tab out of a group: it becomes its own floating group.</summary>
    public WorkspacePanel? DragPageOut(PanelPage page)
    {
        var from = page.Group;
        if (from == null) return null;
        var at = from.IsHandleCreated ? from.RectangleToScreen(from.ClientRectangle) : new Rectangle(Cursor.Position, new Size(260, 300));
        var g = NewGroup(page);
        Float(g, new Rectangle(Cursor.Position.X - 40, Cursor.Position.Y - 12, Math.Max(240, at.Width), Math.Max(200, at.Height)));
        _hint ??= new HintForm();
        return g;
    }

    private System.Windows.Forms.Timer? _follow;

    /// <summary>Drags a group with the mouse until the left button is let go, then drops it (Esc cancels the docking).</summary>
    public void FollowMouse(WorkspacePanel g, Point grabOffset)
    {
        _follow?.Stop(); _follow?.Dispose();
        _follow = new System.Windows.Forms.Timer { Interval = 15 };
        _follow.Tick += (_, _) =>
        {
            var p = Control.MousePosition;
            bool held = (Control.MouseButtons & MouseButtons.Left) != 0;
            if (g.IsDisposed || !held || (Control.ModifierKeys == Keys.None && IsKeyDown(Keys.Escape)))
            {
                _follow!.Stop();
                if (g.IsDisposed) { _hint?.Hide(); return; }
                if (held) _hint?.Hide(); else EndDrag(g, p);   // Esc: leave it floating where it is
                return;
            }
            DragTo(g, p, grabOffset);
        };
        _follow.Start();
        DragTo(g, Control.MousePosition, grabOffset);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private static bool IsKeyDown(Keys k) => (GetAsyncKeyState((int)k) & 0x8000) != 0;

    public void DragTo(WorkspacePanel g, Point screen, Point grabOffset)
    {
        if (g.FloatingWindow is { } f)
            f.Location = new Point(screen.X - grabOffset.X - 8, screen.Y - grabOffset.Y - SystemInformation.ToolWindowCaptionHeight - 8);
        var t = TargetAt(screen, g);
        if (t is { } target && _hint != null)
        {
            _hint.Bounds = HintBounds(target);
            if (!_hint.Visible) _hint.Show(_owner);
            g.FloatingWindow?.BringToFront();
        }
        else _hint?.Hide();
    }

    public void EndDrag(WorkspacePanel g, Point screen)
    {
        _hint?.Hide();
        if (TargetAt(screen, g) is { } t) Drop(g, t);
    }

    public void Drop(WorkspacePanel g, DropTarget t)
    {
        switch (t.Kind)
        {
            case DropKind.Tab when t.Group != null:
                foreach (var p in g.Pages.ToList()) MovePage(p, t.Group);
                break;
            case DropKind.Insert: DockGroup(g, t.Side, t.Column, t.Index, false); break;
            default: DockGroup(g, t.Side, t.Column, 0, true); break;
        }
    }

    /// <summary>
    /// What dropping <paramref name="dragging"/> at a screen point would do: group it in as a tab (over a title bar),
    /// insert it in a column, start a new column at a column's outer or inner edge, or dock at an empty edge of the canvas area.
    /// </summary>
    public DropTarget? TargetAt(Point screen, WorkspacePanel? dragging = null)
    {
        bool strip = dragging?.IsStrip == true;
        if (!strip)
            foreach (var g in _groups)
                if (g != dragging && !g.IsStrip && (g.Column?.Visible ?? g.FloatingWindow != null) && g.HeaderScreenBounds.Contains(screen))
                    return new DropTarget(DropKind.Tab, g.Column?.Side ?? DockSide.Float, 0, 0, g);
        var p = _host.PointToClient(screen);
        if (!_host.ClientRectangle.Contains(p)) return null;
        const int edgeZone = 22;
        foreach (var side in Sides)
        {
            var list = _sides[side];
            for (int ci = 0; ci < list.Count; ci++)
            {
                var col = list[ci];
                if (!col.Visible || !col.Bounds.Contains(p)) continue;
                var lp = col.PointToClient(screen);
                int across = col.Horizontal ? lp.Y : lp.X, size = col.Horizontal ? col.Height : col.Width;
                bool outerIsStart = side is DockSide.Left or DockSide.Top;   // the outer edge is the one nearer the window edge
                int toOuter = outerIsStart ? across : size - across, toInner = size - toOuter;
                bool mustBeOwnRow = strip || col.StripOnly;   // toolbars never share a row with panels or each other
                if (toOuter < edgeZone || (mustBeOwnRow && toOuter <= toInner)) return new DropTarget(DropKind.NewColumn, side, ci, 0, null);
                if (toInner < edgeZone || mustBeOwnRow) return new DropTarget(DropKind.NewColumn, side, ci + 1, 0, null);
                return new DropTarget(DropKind.Insert, side, ci, col.IndexAt(lp), null);
            }
        }
        const int edge = 48;
        var area = CanvasArea();
        if (p.Y < area.Top + edge) return new DropTarget(DropKind.NewColumn, DockSide.Top, _sides[DockSide.Top].Count, 0, null);
        if (p.Y >= area.Bottom - edge) return new DropTarget(DropKind.NewColumn, DockSide.Bottom, _sides[DockSide.Bottom].Count, 0, null);
        if (strip) return null;
        if (p.X < area.Left + edge) return new DropTarget(DropKind.NewColumn, DockSide.Left, _sides[DockSide.Left].Count, 0, null);
        if (p.X >= area.Right - edge) return new DropTarget(DropKind.NewColumn, DockSide.Right, _sides[DockSide.Right].Count, 0, null);
        return null;
    }

    /// <summary>The canvas area: the host minus every visible column and rail.</summary>
    private Rectangle CanvasArea()
    {
        var r = _host.ClientRectangle;
        foreach (Control c in _host.Controls)
        {
            if (!c.Visible || c.Dock == DockStyle.Fill) continue;
            var b = c.Bounds;
            switch (c.Dock)
            {
                case DockStyle.Left: r = Rectangle.FromLTRB(Math.Max(r.Left, b.Right), r.Top, r.Right, r.Bottom); break;
                case DockStyle.Right: r = Rectangle.FromLTRB(r.Left, r.Top, Math.Min(r.Right, b.Left), r.Bottom); break;
                case DockStyle.Top: r = Rectangle.FromLTRB(r.Left, Math.Max(r.Top, b.Bottom), r.Right, r.Bottom); break;
                case DockStyle.Bottom: r = Rectangle.FromLTRB(r.Left, r.Top, r.Right, Math.Min(r.Bottom, b.Top)); break;
            }
        }
        return r;
    }

    /// <summary>Screen rectangle showing where a drop would land.</summary>
    private Rectangle HintBounds(DropTarget t)
    {
        if (t.Kind == DropKind.Tab && t.Group != null) return t.Group.HeaderScreenBounds;
        var list = _sides[t.Side];
        if (t.Kind == DropKind.Insert)
        {
            var col = list[t.Column];
            var items = col.Items;
            int at = t.Index < items.Count ? (col.Horizontal ? items[t.Index].Left : items[t.Index].Top) : (col.Horizontal ? col.ClientSize.Width : col.ClientSize.Height);
            var r = col.Horizontal ? new Rectangle(Math.Max(0, at - 6), 0, 12, col.ClientSize.Height) : new Rectangle(0, Math.Max(0, at - 6), col.ClientSize.Width, 12);
            return col.RectangleToScreen(r);
        }
        // new column: a band along the edge where it will appear
        Rectangle band;
        bool before = t.Column < list.Count && list[t.Column].Visible;
        var refCol = before ? list[t.Column] : list.LastOrDefault(c => c.Visible);
        if (refCol != null)
        {
            var b = refCol.Bounds;
            bool outer = before;   // before = on this column's outer side; otherwise on the innermost column's inner side
            band = t.Side switch
            {
                DockSide.Left => outer ? new Rectangle(b.Left, b.Top, 14, b.Height) : new Rectangle(b.Right - 14, b.Top, 14, b.Height),
                DockSide.Right => outer ? new Rectangle(b.Right - 14, b.Top, 14, b.Height) : new Rectangle(b.Left, b.Top, 14, b.Height),
                DockSide.Top => outer ? new Rectangle(b.Left, b.Top, b.Width, 12) : new Rectangle(b.Left, b.Bottom - 12, b.Width, 12),
                _ => outer ? new Rectangle(b.Left, b.Bottom - 12, b.Width, 12) : new Rectangle(b.Left, b.Top, b.Width, 12),
            };
        }
        else
        {
            var a = CanvasArea();
            band = t.Side switch
            {
                DockSide.Left => new Rectangle(a.Left, a.Top, 90, a.Height),
                DockSide.Right => new Rectangle(a.Right - 90, a.Top, 90, a.Height),
                DockSide.Top => new Rectangle(a.Left, a.Top, a.Width, 40),
                _ => new Rectangle(a.Left, a.Bottom - 90, a.Width, 90),
            };
        }
        return _host.RectangleToScreen(band);
    }

    private sealed class HintForm : Form
    {
        public HintForm()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Accent; Opacity = 0.45; TopMost = true;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x20; return cp; }   // no-activate, tool window, click-through
        }
    }

    // ---------- layouts ----------

    public WorkspaceLayout Capture()
    {
        var l = new WorkspaceLayout { FoldedSides = _folded.Select(s => s.ToString()).ToArray() };
        foreach (var side in Sides)
        {
            var list = _sides[side];
            l.Extents[side.ToString()] = list.Select(c => c.Extent).ToArray();
            for (int ci = 0; ci < list.Count; ci++)
                for (int i = 0; i < list[ci].Items.Count; i++)
                    l.Groups.Add(StateOf(list[ci].Items[i], side, ci, i));
        }
        foreach (var g in _groups.Where(g => g.FloatingWindow != null))
        {
            var s = StateOf(g, DockSide.Float, 0, 0);
            var f = g.FloatingWindow!;
            s.FloatBounds = new[] { f.Left, f.Top, f.ClientSize.Width, f.ClientSize.Height };
            l.Groups.Add(s);
        }
        return l;
    }

    private static GroupState StateOf(WorkspacePanel g, DockSide side, int column, int index) => new()
    {
        Pages = g.Pages.Select(p => p.Name).ToArray(), Active = g.Active?.Name, Title = g.HasCustomTitle ? g.Title : null,
        Where = side, Column = column, Index = index, Weight = g.Weight, Collapsed = g.Collapsed,
    };

    /// <summary>Puts every page where the layout says. Pages it doesn't mention go where the default puts them; unknown names are ignored.</summary>
    public void Apply(WorkspaceLayout layout)
    {
        _host.SuspendLayout();
        foreach (var g in _groups.ToList()) Discard(g);
        foreach (var side in Sides) { foreach (var c in _sides[side]) { _host.Controls.Remove(c); c.Dispose(); } _sides[side].Clear(); }
        _folded.Clear();
        foreach (var rail in _rails.Values) rail.Visible = false;

        var placed = new HashSet<string>();
        List<GroupState> Usable(WorkspaceLayout l) => (l.Groups ?? new()).Where(s => s?.Pages != null && s.Pages.Any(n => FindPage(n) != null)).ToList();
        var states = Usable(layout);
        // pages the layout forgot: take their place from the default layout (only the missing pages)
        var missing = _pages.Where(p => !states.Any(s => s.Pages.Contains(p.Name))).Select(p => p.Name).ToHashSet();
        foreach (var d in Usable(Default))
        {
            var names = d.Pages.Where(missing.Contains).ToArray();
            if (names.Length > 0) states.Add(new GroupState { Pages = names, Where = d.Where, Column = d.Where == DockSide.Top ? d.Column : int.MaxValue, Index = int.MaxValue, Weight = d.Weight, Collapsed = d.Collapsed });
        }
        var extents = layout.Extents ?? new();
        foreach (var side in Sides)
        {
            var mine = states.Where(s => s.Where == side).ToList();
            if (mine.Count == 0) continue;
            int[]? ex = extents.TryGetValue(side.ToString(), out var e) ? e : null;
            int columns = Math.Max(ex?.Length ?? 0, mine.Max(s => s.Column == int.MaxValue ? 0 : Math.Clamp(s.Column, 0, 50)) + 1);
            for (int ci = 0; ci < columns; ci++) NewColumn(side, ci);
            foreach (var s in mine.OrderBy(s => s.Column).ThenBy(s => s.Index))
            {
                var g = Build(s, placed);
                if (g == null) continue;
                var list = _sides[side];
                var col = list[Math.Clamp(s.Column == int.MaxValue ? list.Count - 1 : s.Column, 0, list.Count - 1)];
                if (g.IsStrip && col.Items.Count > 0) col = NewColumn(side, list.IndexOf(col) + 1);   // toolbars get their own row
                else if (!g.IsStrip && col.StripOnly) col = NewColumn(side, list.IndexOf(col) + 1);
                g.Visible = true;
                col.Insert(g, int.MaxValue);
                g.Collapsed = s.Collapsed;
            }
            foreach (var c in _sides[side].Where(c => c.Items.Count == 0).ToList()) { _sides[side].Remove(c); _host.Controls.Remove(c); c.Dispose(); }
            if (ex != null)
                for (int ci = 0; ci < Math.Min(ex.Length, _sides[side].Count); ci++)
                    if (ex[ci] > 0) _sides[side][ci].Extent = ex[ci];
        }
        foreach (var s in states.Where(s => !Docked(s.Where)))
        {
            if (s.Where == DockSide.Hidden) continue;
            var g = Build(s, placed);
            if (g == null) continue;
            Rectangle? b = s.FloatBounds is { Length: 4 } fb ? new Rectangle(fb[0], fb[1], Math.Max(fb[2], 150), Math.Max(fb[3], 40)) : null;
            if (b is { } rb && !Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(rb))) b = null;   // keep it on a screen that still exists
            if (Program.Headless) { DockNewColumnOrEnd(g, g.IsStrip ? DockSide.Top : DockSide.Right); continue; }
            Float(g, b);
        }
        // anything still unplaced (e.g. a toolbar the layout tried to put in a tab group) and not deliberately hidden goes where the default puts it
        var hidden = states.Where(s => s.Where == DockSide.Hidden).SelectMany(s => s.Pages).ToHashSet();
        foreach (var page in _pages.Where(p => p.Group == null && !hidden.Contains(p.Name)).ToList())
        {
            var d = Usable(Default).FirstOrDefault(s => s.Pages.Contains(page.Name));
            var side = d != null && Docked(d.Where) ? d.Where : page.IsStrip ? DockSide.Top : DockSide.Right;
            var g = NewGroup(page);
            if (page.IsStrip) DockGroup(g, side, d?.Column ?? 0, 0, true); else DockNewColumnOrEnd(g, side);
        }
        foreach (var name in layout.FoldedSides ?? Array.Empty<string>())
            if (Enum.TryParse<DockSide>(name, out var side)) FoldSide(side, true);
        _host.ResumeLayout(true);
        Rearrange();
        Changed?.Invoke();
    }

    private WorkspacePanel? Build(GroupState s, HashSet<string> placed)
    {
        var pages = s.Pages.Select(FindPage).Where(p => p != null && !placed.Contains(p.Name)).Cast<PanelPage>().ToList();
        if (pages.Count == 0) return null;
        if (pages.Count > 1) pages = pages.Where(p => !p.IsStrip).ToList() is { Count: > 0 } rest ? rest : pages.Take(1).ToList();   // a toolbar never shares a group
        foreach (var p in pages) placed.Add(p.Name);
        var g = NewGroup(pages.ToArray());
        g.Activate(pages.FirstOrDefault(p => p.Name == s.Active) ?? pages[0]);   // the saved tab, else the first
        if (s.Title != null) g.Title = s.Title;
        g.Weight = s.Weight > 0 && double.IsFinite(s.Weight) ? s.Weight : 1;
        return g;
    }
}
