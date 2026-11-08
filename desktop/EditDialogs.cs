/* Small dialogs for the image-editor commands. */
namespace Flint;

/// <summary>Parameters for one filter, with live preview on the canvas.</summary>
public sealed class FilterDialog : ThemedForm
{
    private readonly List<NumericUpDown> _nums = new();
    private readonly CheckBox _preview, _all;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 150 };

    public event Action<double[], bool>? PreviewRequested;
    public double[] Values => _nums.Select(n => (double)n.Value).ToArray();
    public bool AllFrames => _all.Checked;

    public FilterDialog(FilterDef f, bool multiFrame)
    {
        Text = f.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        int y = 14;
        foreach (var p in f.Params)
        {
            Controls.Add(new Label { Text = p.Name, Left = 14, Top = y + 3, Width = 120 });
            var n = new NumericUpDown
            {
                Left = 140, Top = y, Width = 90, Minimum = (decimal)p.Min, Maximum = (decimal)p.Max,
                DecimalPlaces = p.Decimals, Increment = p.Decimals > 0 ? 0.1m : 1, Value = (decimal)p.Default,
                TextAlign = HorizontalAlignment.Right,
            };
            n.ValueChanged += (_, _) => Kick();
            _nums.Add(n);
            Controls.Add(n);
            y += 30;
        }
        _all = new MintCheckBox { Text = "All frames of this layer", Left = 14, Top = y + 4, Width = 220, Visible = multiFrame };
        _all.CheckedChanged += (_, _) => Kick();
        Controls.Add(_all);
        y += multiFrame ? 28 : 0;
        _preview = new MintCheckBox { Text = "Preview on the canvas", Left = 14, Top = y + 4, Width = 220, Checked = true };
        _preview.CheckedChanged += (_, _) => Kick();
        Controls.Add(_preview);
        y += 36;
        var ok = new Button { Text = "Apply", Left = 76, Top = y, Width = 76, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 158, Top = y, Width = 76, DialogResult = DialogResult.Cancel };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(248, y + 40);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            if (_preview.Checked) PreviewRequested?.Invoke(Values, AllFrames);
            else PreviewRequested?.Invoke(Array.Empty<double>(), false);
        };
        Shown += (_, _) => Kick();
        FormClosed += (_, _) => _debounce.Dispose();
    }

    private void Kick()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>The preview handler receives an empty array when preview is off: restore only.</summary>
    public static bool IsRestoreOnly(double[] v) => v.Length == 0;
}

public sealed class NumericTransformDialog : ThemedForm
{
    private readonly NumericUpDown _sx, _sy, _rot, _skew;
    private readonly CheckBox _lock;
    private readonly ComboBox _mode;
    public double ScaleX => (double)_sx.Value;
    public double ScaleY => (double)_sy.Value;
    public double Angle => (double)_rot.Value;
    public double SkewX => (double)_skew.Value;
    public Resample Mode => (Resample)_mode.SelectedIndex;

    public NumericTransformDialog(Resample mode)
    {
        Text = "Numeric transform";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        NumericUpDown Row(string label, int y, decimal min, decimal max, decimal val, int dec)
        {
            Controls.Add(new Label { Text = label, Left = 14, Top = y + 3, Width = 110 });
            var n = new NumericUpDown { Left = 130, Top = y, Width = 90, Minimum = min, Maximum = max, Value = val, DecimalPlaces = dec, TextAlign = HorizontalAlignment.Right };
            Controls.Add(n);
            return n;
        }
        _sx = Row("Width %", 14, 1, 10000, 100, 1);
        _sy = Row("Height %", 44, 1, 10000, 100, 1);
        _lock = new MintCheckBox { Text = "keep proportions", Left = 130, Top = 72, Width = 130, Checked = true };
        Controls.Add(_lock);
        _sx.ValueChanged += (_, _) => { if (_lock.Checked) _sy.Value = _sx.Value; };
        _rot = Row("Rotate °", 100, -360, 360, 0, 1);
        _skew = Row("Skew °", 130, -80, 80, 0, 1);
        Controls.Add(new Label { Text = "Resample", Left = 14, Top = 163, Width = 110 });
        _mode = new ComboBox { Left = 130, Top = 160, Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
        _mode.Items.AddRange(new object[] { "Nearest", "RotSprite", "Smooth" });
        _mode.SelectedIndex = (int)mode;
        Controls.Add(_mode);
        var ok = new Button { Text = "Apply", Left = 70, Top = 200, Width = 76, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 152, Top = 200, Width = 76, DialogResult = DialogResult.Cancel };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(244, 240);
    }
}

public sealed class TextDialog : ThemedForm
{
    private readonly TextBox _text;
    private readonly ComboBox _family;
    private readonly NumericUpDown _size;
    private readonly CheckBox _bold, _italic, _smooth;
    public string TextValue => _text.Text;
    public bool Smooth => _smooth.Checked;
    public Font FontValue => new(_family.Text, (float)_size.Value,
        (_bold.Checked ? FontStyle.Bold : 0) | (_italic.Checked ? FontStyle.Italic : 0), GraphicsUnit.Pixel);

    public TextDialog(Color colour)
    {
        Text = "Text";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        _text = new TextBox { Left = 14, Top = 14, Width = 330, Height = 70, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Text content" };
        Controls.Add(_text);
        _family = new ComboBox { Left = 14, Top = 94, Width = 200, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
        foreach (var f in FontFamily.Families) _family.Items.Add(f.Name);
        _family.Text = "Arial";
        Controls.Add(_family);
        Controls.Add(new Label { Text = "px", Left = 290, Top = 97, Width = 30 });
        _size = new NumericUpDown { Left = 222, Top = 94, Width = 64, Minimum = 4, Maximum = 512, Value = 16, TextAlign = HorizontalAlignment.Right };
        Controls.Add(_size);
        _bold = new MintCheckBox { Text = "Bold", Left = 14, Top = 124, Width = 60 };
        _italic = new MintCheckBox { Text = "Italic", Left = 80, Top = 124, Width = 64 };
        _smooth = new MintCheckBox { Text = "Smooth edges (off = hard pixels)", Left = 150, Top = 124, Width = 210, Checked = false };
        Controls.Add(_bold); Controls.Add(_italic); Controls.Add(_smooth);
        Controls.Add(new Label { Text = "Text stays editable. Use Object (Q) or the Objects tab to change its words, font, colour and size later.", Left = 14, Top = 150, Width = 330, Height = 32, ForeColor = SystemColors.GrayText });
        var swatch = new Panel { Left = 320, Top = 92, Width = 24, Height = 24, BackColor = colour, BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(swatch);
        var ok = new Button { Text = "Place", Left = 186, Top = 190, Width = 76, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 268, Top = 190, Width = 76, DialogResult = DialogResult.Cancel };
        Controls.Add(ok); Controls.Add(cancel);
        CancelButton = cancel;
        ClientSize = new Size(358, 228);
    }
}
