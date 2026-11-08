/* Owns the dock columns around the canvas and every movable panel: docking,
 * floating, hiding, the drag highlight, and capturing / restoring layouts. */
namespace Flint;

public sealed class DockManager
{
    private readonly Form _owner;
    private readonly Control _host;
    private readonly Dictionary<DockSide, DockColumn> _columns = new();
    private readonly List<WorkspacePanel> _panels = new();
    private Form? _hint;
    private (DockSide side, int index)? _target;
    public WorkspaceLayout Default { get; set; } = new();
    public IReadOnlyList<WorkspacePanel> Panels => _panels;
    public DockColumn Column(DockSide side) => _columns[side];

    public DockManager(Form owner, Control host)
    {
        _owner = owner; _host = host;
        // Add order matters: the last added docks first. The bottom row runs the full width (the timeline needs it); left and right sit above it.
        _columns[DockSide.Bottom] = new DockColumn(DockSide.Bottom, 260, 120);
        _columns[DockSide.Right] = new DockColumn(DockSide.Right, 294, 120);
        _columns[DockSide.Left] = new DockColumn(DockSide.Left, 100, 60);
        foreach (var side in new[] { DockSide.Right, DockSide.Left, DockSide.Bottom }) host.Controls.Add(_columns[side]);
    }

    public WorkspacePanel Add(string title, Control content, Size minContent)
    {
        var p = new WorkspacePanel(this, title, content, minContent);
        _panels.Add(p);
        return p;
    }

    public WorkspacePanel? Find(string name) => _panels.FirstOrDefault(p => p.Name == name);

    private void Detach(WorkspacePanel p)
    {
        p.Column?.Remove(p);
        if (p.FloatingWindow is { } f)
        {
            p.FloatingWindow = null;
            f.Controls.Remove(p);
            f.Dispose();
        }
    }

    public void Dock(WorkspacePanel p, DockSide side, int index)
    {
        if (side is DockSide.Float) { Float(p); return; }
        if (side is DockSide.Hidden) { Hide(p); return; }
        if (p.Column?.Side == side)
        {
            // moving within the same column: the panel's own slot does not count
            int from = p.Column.Items.ToList().IndexOf(p);
            if (index > from) index--;
        }
        Detach(p);
        p.Visible = true;
        _columns[side].Insert(p, index);
        p.Home = (side, _columns[side].Items.ToList().IndexOf(p));
    }

    public void Float(WorkspacePanel p, Rectangle? bounds = null)
    {
        if (p.FloatingWindow != null) { p.FloatingWindow.Activate(); return; }
        var screen = p.Parent != null && p.IsHandleCreated ? p.RectangleToScreen(p.ClientRectangle) : new Rectangle(Cursor.Position, new Size(300, 400));
        if (p.Column != null) p.Home = (p.Column.Side, p.Column.Items.ToList().IndexOf(p));
        Detach(p);
        var r = bounds ?? new Rectangle(screen.Location, new Size(Math.Max(screen.Width, 240), Math.Max(screen.Height, 200)));
        var f = new ThemedForm
        {
            Text = p.Name, FormBorderStyle = FormBorderStyle.SizableToolWindow, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            Location = r.Location, ClientSize = r.Size,
            MinimumSize = new Size(Math.Min(p.MinContent.Width + 20, 600), p.MinContent.Height + WorkspacePanel.HeaderHeight + 40),
        };
        p.FloatingWindow = f;
        p.FoldedBeforeFloat = p.Collapsed;
        p.Collapsed = false;
        p.Dock = DockStyle.Fill; p.Visible = true;
        f.Controls.Add(p);
        f.FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing || _owner.IsDisposed || _owner.Disposing) return;
            e.Cancel = true;
            Dock(p, p.Home.side is DockSide.Left or DockSide.Right or DockSide.Bottom ? p.Home.side : DockSide.Right, p.Home.index);
            p.Collapsed = p.FoldedBeforeFloat;
        };
        f.Show(_owner);
    }

    public void Hide(WorkspacePanel p) { Detach(p); p.Visible = false; }

    /// <summary>Shows a hidden panel where it last was (or brings a floating one forward).</summary>
    public void Show(WorkspacePanel p)
    {
        if (p.FloatingWindow != null) { p.FloatingWindow.Activate(); return; }
        if (p.Column != null) { p.Collapsed = false; return; }
        var side = p.Home.side is DockSide.Left or DockSide.Right or DockSide.Bottom ? p.Home.side : DockSide.Right;
        Dock(p, side, p.Home.index);
        p.Collapsed = false;
    }

    public void Toggle(WorkspacePanel p) { if (p.IsHidden) Show(p); else Hide(p); }

    // ---------- dragging ----------

    public void BeginDrag(WorkspacePanel p)
    {
        if (p.FloatingWindow == null) Float(p);
        _hint ??= new HintForm();
    }

    public void DragTo(WorkspacePanel p, Point screen, Point grabOffset)
    {
        if (p.FloatingWindow is { } f)
            f.Location = new Point(screen.X - grabOffset.X - 8, screen.Y - grabOffset.Y - SystemInformation.ToolWindowCaptionHeight - 8);
        _target = TargetAt(screen);
        if (_target is { } t && _hint != null)
        {
            _hint.Bounds = HintBounds(t.side, t.index);
            if (!_hint.Visible) _hint.Show(_owner);
            _owner.Activate(); p.FloatingWindow?.BringToFront();
        }
        else _hint?.Hide();
    }

    public void EndDrag(WorkspacePanel p, Point screen)
    {
        _hint?.Hide();
        var t = TargetAt(screen);
        _target = null;
        if (t is { } target) Dock(p, target.side, target.index);
    }

    /// <summary>Docking target under a screen point: a column's panels, or within 48 px of an empty edge of the canvas area.</summary>
    public (DockSide side, int index)? TargetAt(Point screen)
    {
        var p = _host.PointToClient(screen);
        if (!_host.ClientRectangle.Contains(p)) return null;
        foreach (var col in _columns.Values)
            if (col.Visible && col.Bounds.Contains(p)) return (col.Side, col.IndexAt(col.PointToClient(screen)));
        const int edge = 48;
        var area = CanvasArea();
        if (p.X < area.Left + edge) return (DockSide.Left, _columns[DockSide.Left].Items.Count);
        if (p.X >= area.Right - edge) return (DockSide.Right, _columns[DockSide.Right].Items.Count);
        if (p.Y >= area.Bottom - edge) return (DockSide.Bottom, _columns[DockSide.Bottom].Items.Count);
        return null;
    }

    private Rectangle CanvasArea()
    {
        var r = _host.ClientRectangle;
        foreach (var col in _columns.Values.Where(c => c.Visible))
        {
            if (col.Side == DockSide.Left) { r.X += col.Width; r.Width -= col.Width; }
            else if (col.Side == DockSide.Right) r.Width -= col.Width;
        }
        if (_columns[DockSide.Bottom].Visible) r.Height -= _columns[DockSide.Bottom].Height;
        return r;
    }

    /// <summary>Screen rectangle showing where a drop would land.</summary>
    private Rectangle HintBounds(DockSide side, int index)
    {
        var col = _columns[side];
        Rectangle r;
        if (!col.Visible)
        {
            var area = CanvasArea();
            r = side switch
            {
                DockSide.Left => new Rectangle(area.Left, area.Top, 120, area.Height),
                DockSide.Right => new Rectangle(area.Right - 120, area.Top, 120, area.Height),
                _ => new Rectangle(area.Left, area.Bottom - 120, area.Width, 120),
            };
            return _host.RectangleToScreen(r);
        }
        var items = col.Items;
        int at = index < items.Count ? (col.Horizontal ? items[index].Left : items[index].Top) : (col.Horizontal ? col.ClientSize.Width : col.ClientSize.Height);
        r = col.Horizontal ? new Rectangle(Math.Max(0, at - 6), 0, 12, col.ClientSize.Height) : new Rectangle(0, Math.Max(0, at - 6), col.ClientSize.Width, 12);
        return col.RectangleToScreen(r);
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
        var l = new WorkspaceLayout
        {
            LeftWidth = _columns[DockSide.Left].Width, RightWidth = _columns[DockSide.Right].Width, BottomHeight = _columns[DockSide.Bottom].Height,
        };
        foreach (var p in _panels)
        {
            var s = new PanelState { Weight = p.Weight, Collapsed = p.Collapsed };
            if (p.Column is { } c) { s.Where = c.Side; s.Index = c.Items.ToList().IndexOf(p); }
            else if (p.FloatingWindow is { } f) { s.Where = DockSide.Float; s.FloatBounds = new[] { f.Left, f.Top, f.ClientSize.Width, f.ClientSize.Height }; }
            else s.Where = DockSide.Hidden;
            l.Panels[p.Name] = s;
        }
        return l;
    }

    /// <summary>Puts every panel where the layout says. Panels the layout doesn't mention go where the default puts them.</summary>
    public void Apply(WorkspaceLayout layout)
    {
        _host.SuspendLayout();
        foreach (var p in _panels) Detach(p);
        _columns[DockSide.Left].Width = Math.Max(60, layout.LeftWidth);
        _columns[DockSide.Right].Width = Math.Max(120, layout.RightWidth);
        _columns[DockSide.Bottom].Height = Math.Max(120, layout.BottomHeight);
        PanelState StateOf(WorkspacePanel p) =>
            layout.Panels.TryGetValue(p.Name, out var s) ? s : Default.Panels.TryGetValue(p.Name, out var d) ? d : new PanelState { Where = DockSide.Right, Index = 99 };
        foreach (var p in _panels.OrderBy(p => StateOf(p).Index))
        {
            var s = StateOf(p);
            p.Weight = s.Weight > 0 && double.IsFinite(s.Weight) ? s.Weight : 1;
            switch (s.Where)
            {
                case DockSide.Left or DockSide.Right or DockSide.Bottom:
                    Dock(p, s.Where, int.MaxValue); p.Collapsed = s.Collapsed; break;
                case DockSide.Float:
                    var b = s.FloatBounds is { Length: 4 } fb ? new Rectangle(fb[0], fb[1], Math.Max(fb[2], 200), Math.Max(fb[3], 150)) : (Rectangle?)null;
                    if (b is { } rb && !Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(rb))) b = null;   // keep it on a screen that still exists
                    if (Program.Headless) { Dock(p, DockSide.Right, int.MaxValue); break; }
                    Float(p, b); break;
                default:
                    Hide(p); p.Home = (DockSide.Right, int.MaxValue); break;
            }
        }
        _host.ResumeLayout(true);
        foreach (var c in _columns.Values) c.Relayout();
    }
}
