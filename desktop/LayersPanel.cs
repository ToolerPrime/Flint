/* The layer stack, drawn top-down like every editor since Photoshop 3.
 * Owner-drawn rather than a ListView so the visibility toggle, name, blend and
 * opacity all fit one compact row. */
namespace Flint;

public sealed class LayersPanel : Panel
{
    private Document? _doc;
    private int _active;
    private readonly List<Rectangle> _rows = new();

    public event Action<int>? ActiveChanged;
    public event Action? Changed;
    /// <summary>The small mask box on a layer row was clicked (layer index).</summary>
    public event Action<int>? MaskClicked;
    private int _frame;
    private bool _editMask;
    internal void RaiseMaskClicked(int layer) => MaskClicked?.Invoke(layer);

    public const int RowHeight = 26;

    public LayersPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoScroll = true;
        BackColor = Theme.Panel2;
    }

    public void Bind(Document? doc, int active, int frame = 0, bool editMask = false)
    {
        _doc = doc;
        _active = active;
        _frame = frame;
        _editMask = editMask;
        Invalidate();
    }

    public int ActiveLayer => _active;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        _rows.Clear();
        if (_doc == null) return;

        using var nameFont = new Font(Font.FontFamily, 8.25f);
        using var smallFont = new Font(Font.FontFamily, 6.75f);

        // Top of the list is the top of the stack, so iterate backwards.
        int y = AutoScrollPosition.Y;
        for (int i = _doc.Layers.Count - 1; i >= 0; i--)
        {
            var l = _doc.Layers[i];
            var row = new Rectangle(0, y, ClientSize.Width, RowHeight);
            _rows.Add(new Rectangle(0, y, ClientSize.Width, RowHeight));

            bool sel = i == _active;
            if (sel)
            {
                using var bg = new SolidBrush(Theme.AccentSoft(Theme.Panel2, 0.2f));
                g.FillRectangle(bg, row);
                using var strip = new SolidBrush(Theme.Accent);
                g.FillRectangle(strip, 0, y, 3, RowHeight);
            }
            Color quiet = sel ? Theme.Accent : Theme.Muted;

            // Eye: open when visible, struck through when hidden.
            g.DrawImage(ToolIcons.Get(l.Visible ? "eye" : "eye-off", 16, l.Visible ? (sel ? Theme.Accent : Theme.Text) : Theme.Muted), 5, y + 5, 16, 16);

            using var ink = new SolidBrush(sel ? Theme.Accent : Theme.Text);
            using var dim = new SolidBrush(Theme.Muted);
            g.DrawString(l.Name, nameFont, ink, 26, y + 3);

            var bits = new List<string>();
            if (l.Shared) bits.Add("shared");
            if (l.Blend != BlendMode.Normal) bits.Add(l.Blend.ToString().ToLowerInvariant());
            if (l.Opacity != 255) bits.Add($"{l.Opacity * 100 / 255}%");
            if (bits.Count > 0)
                g.DrawString(string.Join(" · ", bits), smallFont, dim, 28, y + 14);

            // Mask icon: only when this frame has a mask. Mint = its pixels are being edited; red slash = disabled.
            if (l.CelAt(_frame)?.Mask != null)
            {
                var mb = MaskRect(y);
                bool editing = sel && _editMask;
                g.DrawImage(ToolIcons.Get("mask", 16, editing ? Theme.Accent : Theme.Muted), mb.X, mb.Y - 2, 16, 16);
                using var pen = new Pen(Theme.Danger, 1.5f);
                if (!l.MaskEnabled) g.DrawLine(pen, mb.Left, mb.Bottom, mb.Right, mb.Top);
            }

            // Lock icon on the right: closed padlock when locked.
            var lockBox = LockRect(y);
            g.DrawImage(ToolIcons.Get(l.Locked ? "lock" : "unlock", 16, l.Locked ? (sel ? Theme.Accent : Theme.Text) : Theme.Mix(Theme.Muted, Theme.Panel2, 0.45f)),
                lockBox.X - 2, y + 5, 16, 16);

            using (var line = new Pen(Theme.Border))
                g.DrawLine(line, 0, y + RowHeight - 1, ClientSize.Width, y + RowHeight - 1);

            y += RowHeight;
        }

        AutoScrollMinSize = new Size(0, _doc.Layers.Count * RowHeight);
    }

    private Rectangle LockRect(int rowY) => new(ClientSize.Width - 22, rowY + 5, 12, 15);
    private Rectangle MaskRect(int rowY) => new(ClientSize.Width - 44, rowY + 6, 16, 13);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_doc == null) return;
        int index = IndexAt(e.Y);
        if (index < 0) return;

        int rowY = AutoScrollPosition.Y + (_doc.Layers.Count - 1 - index) * RowHeight;
        if (LockRect(rowY).Contains(e.Location) || e.X >= ClientSize.Width - 26)
        {
            _doc.Layers[index].Locked = !_doc.Layers[index].Locked;
            Changed?.Invoke();
            Invalidate();
            return;
        }

        if (_doc.Layers[index].CelAt(_frame)?.Mask != null && MaskRect(rowY).Contains(e.Location))
        {
            MaskClicked?.Invoke(index);
            return;
        }

        if (e.X < 24)
        {
            _doc.Layers[index].Visible = !_doc.Layers[index].Visible;
            Changed?.Invoke();
            Invalidate();
            return;
        }
        _active = index;
        ActiveChanged?.Invoke(index);
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (_doc == null || e.X < 24) return;
        int index = IndexAt(e.Y);
        if (index < 0) return;
        using var dlg = new RenameDialog(_doc.Layers[index].Name);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _doc.Layers[index].Name = dlg.Value;
        Changed?.Invoke();
        Invalidate();
    }

    private int IndexAt(int mouseY)
    {
        if (_doc == null) return -1;
        int offset = mouseY - AutoScrollPosition.Y;
        int fromTop = offset / RowHeight;
        if (fromTop < 0 || fromTop >= _doc.Layers.Count) return -1;
        return _doc.Layers.Count - 1 - fromTop;   // list is drawn top-down
    }
}

public sealed class RenameDialog : ThemedForm
{
    private readonly TextBox _box;
    public string Value => _box.Text.Trim();

    public RenameDialog(string current)
    {
        Text = "Rename";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(300, 96);

        _box = new TextBox { Left = 14, Top = 18, Width = 272, Text = current };
        _box.SelectAll();
        Controls.Add(_box);

        var ok = new Button { Text = "OK", Left = 130, Top = 54, Width = 72, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 210, Top = 54, Width = 76, DialogResult = DialogResult.Cancel };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
