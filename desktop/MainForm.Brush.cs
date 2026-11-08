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

    private Control[] BuildBrushOptions()
    {
        Label L(string text, int w) => new() { Text = text, Width = w, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
        NumericUpDown N(int w, decimal min, decimal max) => new() { Width = w, Height = 22, Minimum = min, Maximum = max, TextAlign = HorizontalAlignment.Right };
        _brushPreset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, AccessibleName = "Brush preset" };
        foreach (var b in BrushSettings.Presets) _brushPreset.Items.Add(b.Name);
        _brushSize = N(58, 1, (decimal)BrushSettings.MaxSize); _brushHardness = N(52, 0, 100); _brushOpacity = N(52, 0, 100); _brushFlow = N(52, 1, 100);
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
        foreach (var n in new[] { _brushSize, _brushHardness, _brushOpacity, _brushFlow }) n.ValueChanged += Changed;
        SetBrush(_brushSettings);
        return new Control[] { _brushPreset, L("Size", 32), _brushSize, L("Hard", 34), _brushHardness, L("Opacity", 52), _brushOpacity, L("Flow", 34), _brushFlow };
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
        _brushSync = false;
    }

    /// <summary>Image position with sub-pixel precision, so soft strokes are smooth.</summary>
    private (double x, double y) ToImageF(MouseEventArgs e)
    {
        var o = CanvasOrigin();
        return ((e.X - o.X) / (double)_zoom, (e.Y - o.Y) / (double)_zoom);
    }

    private void BrushDown(MouseEventArgs e, uint colour, RasterObject raster)
    {
        _brushStroke = new BrushStroke(_brushSettings, _strokeBase!, DocW, DocH, colour, _selection);
        _brushOut = (byte[])_strokeBase!.Clone();
        var (x, y) = ToImageF(e);
        _brushStroke.Begin(x, y);
        _brushStroke.RenderInto(_brushOut);
        Commit(new Surface(_brushOut, DocW, DocH), raster);
    }

    private void BrushMove(MouseEventArgs e, RasterObject raster)
    {
        if (_brushStroke == null || _brushOut == null) return;
        var (x, y) = ToImageF(e);
        _brushStroke.LineTo(x, y);
        if (_brushStroke.RenderInto(_brushOut).IsEmpty) return;
        Commit(new Surface(_brushOut, DocW, DocH), raster);
    }

    internal void TestSetBrush(BrushSettings b) => SetBrush(b);
    internal BrushSettings TestBrush => _brushSettings;
}
