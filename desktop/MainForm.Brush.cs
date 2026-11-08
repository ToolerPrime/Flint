/* The Brush tool (B): soft and hard round brushes from BrushEngine.cs. */
namespace Flint;

public sealed partial class MainForm
{
    private BrushSettings _brushSettings = BrushSettings.Presets[1];
    private BrushStroke? _brushStroke;
    private byte[]? _brushOut;
    private ComboBox _brushPreset = null!;
    private NumericUpDown _brushSize = null!, _brushHardness = null!, _brushOpacity = null!, _brushFlow = null!;
    private bool _brushSync;
    private ComboBox _brushMode = null!;
    private readonly System.Windows.Forms.Timer _brushHold = new() { Interval = 30 };
    private DateTime _brushHoldLast;

    private readonly Dictionary<NumericUpDown, MiniSlider> _brushSliders = new();
    private readonly Dictionary<string, Bitmap> _brushPreviews = new();

    private Control[] BuildBrushOptions()
    {
        Label L(string text, int w) => new() { Text = text, Width = w, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
        NumericUpDown N(int w, decimal min, decimal max) => new() { Width = w, Height = 22, Minimum = min, Maximum = max, TextAlign = HorizontalAlignment.Right };
        _brushPreset = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, DropDownWidth = 230, AccessibleName = "Brush preset",
            DrawMode = DrawMode.OwnerDrawVariable, ItemHeight = 17, MaxDropDownItems = 12,
        };
        foreach (var b in BrushSettings.Presets) _brushPreset.Items.Add(b.Name);
        _brushPreset.MeasureItem += (_, e) => e.ItemHeight = e.Index < 0 ? 17 : 46;
        _brushPreset.DrawItem += DrawPresetItem;
        _brushSize = N(52, 1, (decimal)BrushSettings.MaxSize); _brushHardness = N(46, 0, 100); _brushOpacity = N(46, 0, 100); _brushFlow = N(46, 1, 100);
        _brushSize.AccessibleName = "Brush size"; _brushHardness.AccessibleName = "Brush hardness";
        _brushOpacity.AccessibleName = "Brush opacity"; _brushFlow.AccessibleName = "Brush flow";
        _tips.SetToolTip(_brushPreset, "Brush presets (from PhotoCraft's General brushes)");
        _tips.SetToolTip(_brushHardness, "Hardness: 100 is a crisp edge, 0 is very soft");
        _tips.SetToolTip(_brushOpacity, "Opacity: the most one stroke can cover. Going over your own stroke never gets darker than this");
        _tips.SetToolTip(_brushFlow, "Flow: how much each dab adds. Low flow builds up gently as you paint over an area");
        _brushPreset.SelectedIndexChanged += (_, _) => { if (!_brushSync && _brushPreset.SelectedIndex >= 0) SetBrush(BrushSettings.Presets[_brushPreset.SelectedIndex]); };
        void Changed(object? s, EventArgs e)
        {
            if (_brushSync) return;
            _brushSettings = _brushSettings with
            {
                Size = (float)_brushSize.Value, Hardness = (float)_brushHardness.Value / 100,
                Opacity = (float)_brushOpacity.Value / 100, Flow = (float)_brushFlow.Value / 100,
            };
        }
        var controls = new List<Control> { _brushPreset };
        foreach (var (num, label, lw, sw) in new[] { (_brushSize, "Size", 28, 70), (_brushHardness, "Hard", 32, 56), (_brushOpacity, "Opac", 34, 56), (_brushFlow, "Flow", 32, 56) })
        {
            var slider = new MiniSlider { Width = sw, Minimum = num.Minimum, Maximum = num.Maximum, Logarithmic = num == _brushSize, AccessibleName = num.AccessibleName + " slider" };
            num.ValueChanged += (_, _) => slider.Value = num.Value;
            slider.ValueChanged += (_, _) => num.Value = slider.Value;
            num.ValueChanged += Changed;
            _brushSliders[num] = slider;
            _tips.SetToolTip(slider, _tips.GetToolTip(num) is { Length: > 0 } t ? t : num.AccessibleName);
            controls.AddRange(new Control[] { L(label, lw), num, slider });
        }
        _brushMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 96, DropDownWidth = 130, MaxDropDownItems = 32, AccessibleName = "Brush blend mode" };
        _brushMode.Items.Add("Behind");
        foreach (var m in Enum.GetValues<BlendMode>()) _brushMode.Items.Add(m.ToString());
        _brushMode.SelectedIndexChanged += (_, _) =>
        {
            if (_brushSync || _brushMode.SelectedItem is not string name) return;
            _brushSettings = name == "Behind" ? _brushSettings with { Behind = true, Mode = BlendMode.Normal }
                : _brushSettings with { Behind = false, Mode = Enum.Parse<BlendMode>(name) };
        };
        _tips.SetToolTip(_brushMode, "Brush mode: how the paint mixes with what is already on the layer. Behind paints only where the layer is see-through; Erase rubs out");
        var more = new Button { Text = "Settings…", Width = 76, Height = 24, AccessibleName = "Brush settings" };
        more.Click += (_, _) => ShowBrushSettings();
        _tips.SetToolTip(more, "Brush settings: size, angle and colour jitter, scatter, noise, wet edges, airbrush build-up, smoothing");
        controls.AddRange(new Control[] { L("Mode", 44), _brushMode, more });
        _brushHold.Tick += (_, _) => BrushHoldTick();
        SetBrush(_brushSettings);
        return controls.ToArray();
    }

    /// <summary>A sample stroke for a preset, in the theme's text colour on transparent.</summary>
    internal Bitmap BrushPreview(BrushSettings b, int w, int h)
    {
        string key = $"{b.Name}|{w}x{h}|{Theme.Text.ToArgb()}";
        if (_brushPreviews.TryGetValue(key, out var hit)) return hit;
        var px = new byte[w * h * 4];
        var s = new BrushStroke(b with { Size = Math.Min(b.Size, h * 0.55f) }, (byte[])px.Clone(), w, h, Surface.Pack(Color.FromArgb(255, Theme.Text)), null);
        double mid = h / 2.0, amp = h * 0.18;
        s.Begin(8, mid);
        for (int k = 1; k <= 60; k++) s.MoveTo(8 + k * (w - 16) / 60.0, mid + amp * Math.Sin(k / 60.0 * Math.PI * 2));
        s.RenderInto(px);
        return _brushPreviews[key] = ImageOps.FromPixels(w, h, px);
    }

    private void DrawPresetItem(object? sender, DrawItemEventArgs e)
    {
        bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0, sel = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(sel && !edit ? Theme.AccentSoft(Theme.Panel2, 0.35f) : Theme.Panel2)) e.Graphics.FillRectangle(bg, e.Bounds);
        if (e.Index < 0) return;
        var b = BrushSettings.Presets[e.Index];
        var r = e.Bounds;
        if (edit)
        {
            TextRenderer.DrawText(e.Graphics, b.Name, e.Font, r, Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }
        TextRenderer.DrawText(e.Graphics, b.Name, e.Font, new Rectangle(r.X + 6, r.Y + 2, r.Width - 12, 15), Theme.Text, TextFormatFlags.EndEllipsis);
        e.Graphics.DrawImageUnscaled(BrushPreview(b, r.Width - 12, 26), r.X + 6, r.Y + 18);
    }

    private void SetBrush(BrushSettings b)
    {
        _brushSettings = b;
        _brushSync = true;
        _brushPreset.SelectedIndex = Array.FindIndex(BrushSettings.Presets, p => p.Name == b.Name);
        _brushSize.Value = (decimal)Math.Clamp(b.Size, 1, BrushSettings.MaxSize);
        _brushHardness.Value = (decimal)Math.Round(Math.Clamp(b.Hardness, 0, 1) * 100);
        _brushOpacity.Value = (decimal)Math.Round(Math.Clamp(b.Opacity, 0, 1) * 100);
        _brushFlow.Value = (decimal)Math.Max(1, Math.Round(Math.Clamp(b.Flow, 0, 1) * 100));
        if (_brushMode != null) _brushMode.SelectedItem = b.Behind ? "Behind" : b.Mode.ToString();
        _brushSync = false;
    }

    /// <summary>Image position with sub-pixel precision, so soft strokes are smooth.</summary>
    private (double x, double y) ToImageF(MouseEventArgs e)
    {
        var o = CanvasOrigin();
        return ((e.X - o.X) / (double)_zoom, (e.Y - o.Y) / (double)_zoom);
    }

    private void ShowBrushSettings()
    {
        using var dlg = new BrushSettingsDialog(_brushSettings, SetBrush);
        dlg.ShowDialog(this);
    }

    private void BrushDown(MouseEventArgs e, uint colour, RasterObject raster)
    {
        uint other = InkFor(e.Button == MouseButtons.Right ? MouseButtons.Left : MouseButtons.Right);
        _brushStroke = new BrushStroke(_brushSettings with { Seed = Environment.TickCount }, _strokeBase!, DocW, DocH, colour, _selection, other);
        _brushOut = (byte[])_strokeBase!.Clone();
        var (x, y) = ToImageF(e);
        _brushStroke.Begin(x, y);
        _brushStroke.RenderInto(_brushOut);
        Commit(new Surface(_brushOut, DocW, DocH), raster);
        _brushHoldLast = DateTime.UtcNow;
        if (_brushSettings.BuildUp && !Program.Headless) _brushHold.Start();
    }

    /// <summary>Airbrush build-up while the mouse is held still.</summary>
    private void BrushHoldTick()
    {
        if (_brushStroke == null || _brushOut == null || !_dragging) { _brushHold.Stop(); return; }
        var now = DateTime.UtcNow;
        _brushStroke.Hold((now - _brushHoldLast).TotalMilliseconds);
        _brushHoldLast = now;
        if (!_brushStroke.RenderInto(_brushOut).IsEmpty) Commit(new Surface(_brushOut, DocW, DocH), ActiveRaster());
    }

    private void BrushUp(MouseEventArgs e)
    {
        _brushHold.Stop();
        if (_brushStroke == null || _brushOut == null) return;
        var (x, y) = ToImageF(e);
        _brushStroke.End(x, y);
        if (!_brushStroke.RenderInto(_brushOut).IsEmpty) Commit(new Surface(_brushOut, DocW, DocH), ActiveRaster());
    }

    private void BrushMove(MouseEventArgs e, RasterObject raster)
    {
        if (_brushStroke == null || _brushOut == null) return;
        var (x, y) = ToImageF(e);
        _brushStroke.MoveTo(x, y);
        _brushHoldLast = DateTime.UtcNow;   // moving resets the airbrush hold
        if (_brushStroke.RenderInto(_brushOut).IsEmpty) return;
        Commit(new Surface(_brushOut, DocW, DocH), raster);
    }

    internal void TestSetBrush(BrushSettings b) => SetBrush(b);
    internal MiniSlider TestSizeSlider => _brushSliders[_brushSize];
    internal decimal TestSizeNumber => _brushSize.Value;

    /// <summary>The open preset list as it would be drawn, for checking by eye.</summary>
    internal Bitmap TestPresetList()
    {
        int w = _brushPreset.DropDownWidth, ih = 46, n = BrushSettings.Presets.Length;
        var bmp = new Bitmap(w, ih * n);
        using var g = Graphics.FromImage(bmp);
        for (int i = 0; i < n; i++)
            DrawPresetItem(_brushPreset, new DrawItemEventArgs(g, _brushPreset.Font, new Rectangle(0, i * ih, w, ih), i, i == 1 ? DrawItemState.Selected : DrawItemState.None));
        return bmp;
    }
    internal BrushSettings TestBrush => _brushSettings;
}
