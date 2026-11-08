/* Themed controls: icon button, mint checkbox/toggle, flat section header
 * (optionally collapsible) and a flat tab strip. All read Theme at paint time. */
using System.Drawing.Drawing2D;

namespace Flint;

/// <summary>Square (or icon + text) flat button; Active shows the mint "selected" look.</summary>
public class IconButton : Button
{
    private bool _hot, _down, _active;
    public string? IconKey { get; set; }
    /// <summary>Picks the icon at paint time (e.g. play becomes stop).</summary>
    public Func<string?>? IconResolver { get; set; }
    public bool ShowText { get; set; }
    public bool Danger { get; set; }
    public int IconSize { get; set; } = 20;
    public bool Active { get => _active; set { _active = value; Invalidate(); } }

    public IconButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        Size = new Size(30, 30);
        FlatStyle = FlatStyle.Flat;
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(1, 1, Width - 3, Height - 3);
        Color fill = Theme.Panel, edge = Color.Empty;
        if (_active) { fill = Theme.AccentSoft(Theme.Panel, 0.22f); edge = Theme.Accent; }
        if (_down) fill = Theme.AccentSoft(Theme.Panel3, 0.3f);
        else if (_hot && Enabled) fill = _active ? Theme.AccentSoft(Theme.Panel, 0.32f) : Theme.Panel3;
        if (_active || _down || (_hot && Enabled))
        {
            using var path = Theme.Round(r, 6);
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
            if (edge != Color.Empty) { using var p = new Pen(Color.FromArgb(150, edge)); g.DrawPath(p, path); }
        }
        Color ink = !Enabled ? Theme.Mix(Theme.Muted, Theme.Panel, 0.55f)
            : _active ? Theme.Accent : Danger ? Theme.Danger : _hot ? Theme.Text : Theme.Muted;
        var key = IconResolver?.Invoke() ?? IconKey;
        int s = (int)Math.Round(IconSize * DeviceDpi / 96.0);
        bool text = ShowText && Text.Length > 0;
        int tw = text ? TextRenderer.MeasureText(Text, Font).Width : 0;
        int total = (key != null ? s : 0) + (key != null && text ? 5 : 0) + tw;
        int x = (Width - total) / 2;
        if (key != null)
        {
            g.DrawImage(ToolIcons.Get(key, s, ink), x, (Height - s) / 2, s, s);
            x += s + 5;
        }
        if (text)
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, tw + 2, Height), Enabled ? (_active ? Theme.Accent : Theme.Text) : ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }
}

/// <summary>Checkbox with a mint tick; with IconKey it becomes an icon toggle button.</summary>
public class MintCheckBox : CheckBox
{
    private bool _hot;
    public string? IconKey { get; set; }
    public int IconSize { get; set; } = 20;

    public MintCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (IconKey != null)
        {
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            if (Checked || _hot)
            {
                using var path = Theme.Round(r, 6);
                using var b = new SolidBrush(Checked ? Theme.AccentSoft(Theme.Panel, _hot ? 0.32f : 0.22f) : Theme.Panel3);
                g.FillPath(b, path);
                if (Checked) { using var p = new Pen(Color.FromArgb(150, Theme.Accent)); g.DrawPath(p, path); }
            }
            int s = (int)Math.Round(IconSize * DeviceDpi / 96.0);
            Color ink = Checked ? Theme.Accent : _hot ? Theme.Text : Theme.Muted;
            g.DrawImage(ToolIcons.Get(IconKey, s, ink), (Width - s) / 2, (Height - s) / 2, s, s);
            return;
        }
        int bx = 1, by = (Height - 14) / 2;
        var box = new Rectangle(bx, by, 14, 14);
        using (var path = Theme.Round(box, 3))
        {
            using var b = new SolidBrush(Checked ? (Enabled ? Theme.Accent : Theme.Muted) : Theme.Panel2);
            g.FillPath(b, path);
            using var p = new Pen(Checked ? Theme.AccentDark : _hot ? Theme.Accent : Theme.Border);
            g.DrawPath(p, path);
        }
        if (Checked)
        {
            using var p = new Pen(Theme.OnAccent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(p, new[] { new Point(bx + 3, by + 7), new Point(bx + 6, by + 10), new Point(bx + 11, by + 4) });
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(bx + 20, 0, Width - bx - 20, Height),
            Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Flat section header (replaces the grey GroupBox look); can collapse.</summary>
public class FlatGroup : GroupBox
{
    public const int HeaderH = 16;
    private bool _collapsed;
    private int _fullH;
    private readonly List<Control> _hidden = new();
    public bool Collapsible { get; set; }
    /// <summary>Key under which the collapsed state is remembered.</summary>
    public string? PersistKey { get; set; }

    public FlatGroup()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
    }

    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (value == _collapsed) return;
            _collapsed = value;
            if (value)
            {
                _fullH = Height;
                _hidden.Clear();
                foreach (Control c in Controls) if (c.Visible) { _hidden.Add(c); c.Visible = false; }
                Height = HeaderH + 6;
            }
            else
            {
                Height = Math.Max(_fullH, HeaderH + 6);
                foreach (var c in _hidden) c.Visible = true;
                _hidden.Clear();
            }
            Invalidate();
        }
    }

    public override Rectangle DisplayRectangle
    {
        get
        {
            int top = Text.Length > 0 ? HeaderH : 0;
            return new Rectangle(Padding.Left, top + Padding.Top, Math.Max(0, ClientSize.Width - Padding.Horizontal),
                Math.Max(0, ClientSize.Height - top - Padding.Vertical));
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Collapsible && e.Y < HeaderH)
        {
            Collapsed = !Collapsed;
            if (PersistKey != null) Theme.SetCollapsed(PersistKey, Collapsed);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = Collapsible && e.Y < HeaderH ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        if (Text.Length == 0) return;
        using var f = new Font(Font.FontFamily, 7.5f, FontStyle.Bold);
        string t = Text.ToUpperInvariant();
        int x = 0;
        if (Collapsible)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var p = new Pen(Theme.Muted, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (_collapsed) g.DrawLines(p, new[] { new Point(3, 4), new Point(7, 8), new Point(3, 12) });
            else g.DrawLines(p, new[] { new Point(1, 6), new Point(5, 10), new Point(9, 6) });
            x = 14;
        }
        var sz = TextRenderer.MeasureText(g, t, f, new Size(int.MaxValue, HeaderH), TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, t, f, new Point(x, (HeaderH - sz.Height) / 2), Theme.Muted, TextFormatFlags.NoPadding);
        using var line = new Pen(Theme.Border);
        int lx = x + sz.Width + 6;
        if (lx < Width) g.DrawLine(line, lx, HeaderH / 2, Width - 1, HeaderH / 2);
    }
}

/// <summary>Flat tab strip: icon + name on the selected tab, icon only on the rest.</summary>
public sealed class FlatTabs : Panel
{
    private sealed record Tab(string Title, string Icon, Panel Body);
    private readonly List<Tab> _tabs = new();
    private readonly List<Rectangle> _rects = new();
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Tag = "keep" };
    private readonly Panel _head = new() { Dock = DockStyle.Top, Height = 32, Tag = "slim" };
    private readonly ToolTip _tip = Theme.MakeTip();
    private int _sel, _hot = -1;

    public int SelectedIndex
    {
        get => _sel;
        set
        {
            if (value < 0 || value >= _tabs.Count) return;
            _sel = value;
            for (int i = 0; i < _tabs.Count; i++) _tabs[i].Body.Visible = i == value;
            _head.Invalidate();
        }
    }

    public FlatTabs()
    {
        Tag = "keep";
        Controls.Add(_content);
        Controls.Add(_head);
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(_head, true);
        _head.Paint += PaintHead;
        _head.MouseMove += (_, e) =>
        {
            int h = _rects.FindIndex(r => r.Contains(e.Location));
            if (h != _hot) { _hot = h; _head.Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; _tip.SetToolTip(_head, h >= 0 ? _tabs[h].Title : ""); _head.Invalidate(); }
        };
        _head.MouseLeave += (_, _) => { _hot = -1; _head.Invalidate(); };
        _head.MouseDown += (_, e) => { int h = _rects.FindIndex(r => r.Contains(e.Location)); if (h >= 0) SelectedIndex = h; };
    }

    /// <summary>Adopt a TabPage's contents as one tab.</summary>
    public void Add(TabPage page, string icon)
    {
        var body = new Panel { Dock = DockStyle.Fill, Visible = _tabs.Count == 0, Tag = "keep" };
        foreach (var c in page.Controls.Cast<Control>().ToArray()) body.Controls.Add(c);
        _content.Controls.Add(body);
        _tabs.Add(new Tab(page.Text, icon, body));

        _head.Invalidate();
    }

    private void PaintHead(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _rects.Clear();
        using var f = new Font(Font.FontFamily, 8.5f, FontStyle.Bold);
        int x = 2;
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool sel = i == _sel;
            int tw = sel ? TextRenderer.MeasureText(_tabs[i].Title, f).Width + 4 : 0;
            int w = 16 + 18 + (sel ? tw + 4 : 0);
            var r = new Rectangle(x, 3, w, 26);
            _rects.Add(r);
            if (sel || i == _hot)
            {
                using var path = Theme.Round(r, 6);
                using var b = new SolidBrush(sel ? Theme.AccentSoft(Theme.Panel, 0.16f) : Theme.Panel3);
                g.FillPath(b, path);
            }
            var ink = sel ? Theme.Accent : i == _hot ? Theme.Text : Theme.Muted;
            g.DrawImage(ToolIcons.Get(_tabs[i].Icon, 16, ink), r.X + 9, r.Y + 5, 16, 16);
            if (sel) TextRenderer.DrawText(g, _tabs[i].Title, f, new Rectangle(r.X + 29, r.Y, tw + 2, r.Height), Theme.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += w + 3;
        }
        using var line = new Pen(Theme.Border);
        g.DrawLine(line, 0, _head.Height - 1, _head.Width, _head.Height - 1);
    }
}

/// <summary>Thin vertical divider between toolbar groups.</summary>
public sealed class VSep : Control
{
    public VSep() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); TabStop = false; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Panel);
        using var p = new Pen(Theme.Border);
        e.Graphics.DrawLine(p, Width / 2, 2, Width / 2, Height - 3);
    }
}

/// <summary>Small two-or-more-way toggle ("Timeline | Frames"), mint on the selected segment.</summary>
public sealed class SegmentedControl : Control
{
    private readonly (string text, string icon)[] _items;
    private int _sel, _hot = -1;
    public event Action<int>? SelectedChanged;

    public SegmentedControl(params (string text, string icon)[] items)
    {
        _items = items;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(items.Length * 84 + 2, 28);
        TabStop = false;
    }

    public int SelectedIndex
    {
        get => _sel;
        set { if (value == _sel || value < 0 || value >= _items.Length) return; _sel = value; Invalidate(); SelectedChanged?.Invoke(value); }
    }

    private Rectangle Seg(int i) => new(1 + i * (Width - 2) / _items.Length, 1, (Width - 2) / _items.Length, Height - 2);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = -1;
        for (int i = 0; i < _items.Length; i++) if (Seg(i).Contains(e.Location)) h = i;
        if (h != _hot) { _hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { _hot = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        for (int i = 0; i < _items.Length; i++) if (Seg(i).Contains(e.Location)) SelectedIndex = i;
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var outer = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 7))
        {
            using var b = new SolidBrush(Theme.Panel2);
            g.FillPath(b, outer);
            using var p = new Pen(Theme.Border);
            g.DrawPath(p, outer);
        }
        for (int i = 0; i < _items.Length; i++)
        {
            var r = Seg(i);
            bool on = i == _sel;
            if (on || i == _hot)
            {
                var inner = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                using var path = Theme.Round(inner, 5);
                using var b = new SolidBrush(on ? Theme.AccentSoft(Theme.Panel2, 0.28f) : Theme.Panel3);
                g.FillPath(b, path);
            }
            var ink = on ? Theme.Accent : _hot == i ? Theme.Text : Theme.Muted;
            int tw = TextRenderer.MeasureText(_items[i].text, Font).Width;
            int x = r.X + (r.Width - (16 + 5 + tw)) / 2;
            g.DrawImage(ToolIcons.Get(_items[i].icon, 16, ink), x, r.Y + (r.Height - 16) / 2, 16, 16);
            TextRenderer.DrawText(g, _items[i].text, Font, new Rectangle(x + 21, r.Y, tw + 4, r.Height), ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
