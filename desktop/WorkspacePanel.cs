namespace Flint;

/// <summary>Reparents the same controls between the canvas surround and an owned tool window.</summary>
public sealed class WorkspacePanel : Panel
{
    private readonly Form _owner;
    private readonly Control _host;
    private readonly DockStyle _home;
    private readonly Size _homeSize;
    private readonly bool _wide;
    private readonly int _dockOrder;
    private readonly Label _title;
    private readonly Panel _grip;
    private Point _down, _offset;
    private bool _dragging, _resizing;
    private int _startSize;
    public Form? FloatingWindow { get; private set; }
    public Control Content { get; }
    public bool UserSized { get; private set; }

    public WorkspacePanel(Form owner, Control host, string title, Control content, DockStyle home, Size size)
    {
        _owner = owner; _host = host; _home = home; _homeSize = size; _wide = home == DockStyle.Bottom;
        _dockOrder = host.Controls.Count;
        Content = content; Name = title; Size = size;
        var header = new Panel { Dock = DockStyle.Top, Height = 25, Tag = "slim" };
        _title = new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0), Cursor = Cursors.SizeAll, AccessibleName = $"Move {title} panel" };
        var menuButton = new Button { Text = "…", Dock = DockStyle.Right, Width = 26, AccessibleName = $"{title} panel options" };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Float panel", null, (_, _) => Float());
        if (!_wide)
        {
            menu.Items.Add("Dock left", null, (_, _) => DockTo(DockStyle.Left));
            menu.Items.Add("Dock right", null, (_, _) => DockTo(DockStyle.Right));
        }
        else menu.Items.Add("Dock bottom", null, (_, _) => DockTo(DockStyle.Bottom));
        menu.Items.Add("Reset panel", null, (_, _) => ResetPanel());
        _title.ContextMenuStrip = menu;
        menuButton.Click += (_, _) => menu.Show(menuButton, new Point(0, menuButton.Height));
        header.Controls.Add(_title); header.Controls.Add(menuButton);
        _grip = new Panel { Width = 5, Height = 5, Tag = "slim" };
        content.Dock = DockStyle.Fill;
        Controls.Add(content); Controls.Add(header); Controls.Add(_grip);
        _title.DoubleClick += (_, _) => { if (FloatingWindow == null) Float(); else DockTo(_home); };
        _title.MouseDown += (_, e) => { if (e.Button != MouseButtons.Left) return; _down = _title.PointToScreen(e.Location); _offset = e.Location; _title.Capture = true; };
        _title.MouseMove += (_, e) =>
        {
            if (!_title.Capture || e.Button != MouseButtons.Left) return;
            var p = _title.PointToScreen(e.Location);
            if (!_dragging && Math.Abs(p.X - _down.X) + Math.Abs(p.Y - _down.Y) < SystemInformation.DragSize.Width) return;
            _dragging = true;
            if (FloatingWindow == null) { Float(); _title.Capture = true; }
            FloatingWindow!.Location = new Point(p.X - _offset.X, p.Y - _offset.Y - SystemInformation.ToolWindowCaptionHeight);
        };
        _title.MouseUp += (_, e) =>
        {
            _title.Capture = false;
            if (!_dragging) return;
            _dragging = false;
            var p = _host.PointToClient(_title.PointToScreen(e.Location));
            if (!_host.ClientRectangle.Contains(p)) return;
            if (_wide && p.Y >= _host.Height - 40) DockTo(DockStyle.Bottom);
            else if (!_wide && p.X < 40) DockTo(DockStyle.Left);
            else if (!_wide && p.X >= _host.Width - 40) DockTo(DockStyle.Right);
        };
        _grip.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _resizing = true; _grip.Capture = true; _down = _grip.PointToScreen(e.Location);
            _startSize = Dock == DockStyle.Bottom ? Height : Width;
        };
        _grip.MouseMove += (_, e) =>
        {
            if (!_resizing) return;
            UserSized = true;
            var p = _grip.PointToScreen(e.Location);
            if (Dock == DockStyle.Bottom) Height = Math.Clamp(_startSize + _down.Y - p.Y, 200, Math.Max(200, _host.Height - 200));
            else Width = Math.Clamp(_startSize + (Dock == DockStyle.Left ? p.X - _down.X : _down.X - p.X),
                _homeSize.Width, Math.Max(_homeSize.Width, _host.Width - 400));
        };
        _grip.MouseUp += (_, _) => { _resizing = false; _grip.Capture = false; };
        Disposed += (_, _) => menu.Dispose();
        DockTo(home);
    }

    public void Float()
    {
        if (FloatingWindow != null) { FloatingWindow.Activate(); return; }
        var screen = RectangleToScreen(ClientRectangle);
        Parent?.Controls.Remove(this);
        var f = new ThemedForm { Text = Name, FormBorderStyle = FormBorderStyle.SizableToolWindow,
            ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            Location = screen.Location, ClientSize = Size,
            MinimumSize = new Size(_wide ? 1050 : _homeSize.Width, _wide ? 220 : 260) };
        FloatingWindow = f;
        Dock = DockStyle.Fill; _grip.Visible = false;
        f.Controls.Add(this);
        f.FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing || _owner.IsDisposed || _owner.Disposing) return;
            e.Cancel = true;
            DockTo(_home);
        };
        f.Show(_owner);
    }

    public void DockTo(DockStyle side)
    {
        if (_wide ? side != DockStyle.Bottom : side is not (DockStyle.Left or DockStyle.Right))
            throw new ArgumentException("Unsupported panel edge", nameof(side));
        if (FloatingWindow is { } f)
        {
            FloatingWindow = null;
            f.Controls.Remove(this);
            f.Dispose();
        }
        Dock = side; Size = _homeSize;
        _grip.Dock = side == DockStyle.Left ? DockStyle.Right : side == DockStyle.Right ? DockStyle.Left : DockStyle.Top;
        _grip.Cursor = side == DockStyle.Bottom ? Cursors.HSplit : Cursors.VSplit;
        _grip.Visible = true;
        _host.Controls.Add(this);
        _host.Controls.SetChildIndex(this, Math.Min(_dockOrder, _host.Controls.Count - 1));
        _host.PerformLayout();
    }

    public void ResetPanel() { UserSized = false; DockTo(_home); }
}
