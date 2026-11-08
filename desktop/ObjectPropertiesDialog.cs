namespace Flint;

public sealed class ObjectPropertiesDialog : ThemedForm
{
    private readonly RasterObject _original;
    private readonly TextBox _name, _text;
    private readonly NumericUpDown _x, _y, _width, _height, _stroke, _size;
    private readonly ComboBox _family;
    private readonly CheckBox _filled, _bold, _italic, _smooth;
    private uint _colour;
    public string ObjectName => _name.Text;
    public int PositionX => (int)_x.Value;
    public int PositionY => (int)_y.Value;
    public PrimitiveSpec? Primitive => _original.Primitive is { } s
        ? s with { Width = (int)_width.Value, Height = (int)_height.Value, Stroke = (int)_stroke.Value, Filled = _filled.Checked, Colour = _colour } : null;
    public TextSource? TextSource => _original.TextSource == null ? null
        : new(_text.Text, _family.Text, (float)_size.Value, _bold.Checked, _italic.Checked, _colour, _smooth.Checked);

    public ObjectPropertiesDialog(RasterObject obj)
    {
        _original = obj;
        Text = "Object properties";
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false; ShowInTaskbar = false;
        void Label(string text, int y) => Controls.Add(new Label { Text = text, Left = 14, Top = y + 4, Width = 80 });
        NumericUpDown Number(int x, int y, int value, int min, int max) => new()
            { Left = x, Top = y, Width = 86, Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max) };
        Label("Name", 14); _name = new TextBox { Left = 98, Top = 14, Width = 228, Text = obj.Name }; Controls.Add(_name);
        Label("Position X / Y", 46);
        _x = Number(98, 46, obj.X, -1_000_000, 1_000_000); _y = Number(196, 46, obj.Y, -1_000_000, 1_000_000);
        Controls.Add(_x); Controls.Add(_y);
        _width = Number(98, 82, obj.Primitive?.Width ?? obj.Width, 1, 16384);
        _height = Number(196, 82, obj.Primitive?.Height ?? obj.Height, 1, 16384);
        _width.AccessibleName = "Shape width"; _height.AccessibleName = "Shape height";
        _stroke = Number(98, 114, obj.Primitive?.Stroke ?? 1, 1, 512);
        _filled = new MintCheckBox { Left = 196, Top = 116, Width = 100, Text = "Filled", Checked = obj.Primitive?.Filled ?? false };
        var t = obj.TextSource;
        _colour = obj.Primitive?.Colour ?? t?.Colour ?? 0xFF000000;
        _text = new TextBox { Left = 14, Top = 84, Width = 312, Height = 90, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Text = t?.Text ?? "", AccessibleName = "Text content" };
        _family = new ComboBox { Left = 98, Top = 186, Width = 228, DropDownStyle = ComboBoxStyle.DropDown, Text = t?.Family ?? "Arial" };
        foreach (var font in FontFamily.Families) _family.Items.Add(font.Name);
        _size = Number(98, 218, (int)(t?.Size ?? 16), 4, 512);
        _size.DecimalPlaces = 1; _size.Value = (decimal)(t?.Size ?? 16);
        _bold = new MintCheckBox { Left = 14, Top = 252, Width = 68, Text = "Bold", Checked = t?.Bold ?? false };
        _italic = new MintCheckBox { Left = 88, Top = 252, Width = 68, Text = "Italic", Checked = t?.Italic ?? false };
        _smooth = new MintCheckBox { Left = 164, Top = 252, Width = 154, Text = "Smooth edges", Checked = t?.Smooth ?? false };
        int bottom = 84;
        if (obj.Primitive != null)
        {
            Label("Size W / H", 82); Controls.Add(_width); Controls.Add(_height);
            Label("Stroke width", 114); Controls.Add(_stroke); Controls.Add(_filled); bottom = 150;
        }
        if (t != null)
        {
            Controls.Add(_text); Label("Font", 186); Controls.Add(_family);
            Label("Size in pixels", 218); Controls.Add(_size);
            Controls.Add(_bold); Controls.Add(_italic); Controls.Add(_smooth); bottom = 286;
        }
        if (obj.Primitive != null || t != null)
        {
            Label("Colour", bottom);
            var colour = new Button { Left = 98, Top = bottom, Width = 86, Height = 26, Text = "Choose…" };
            colour.Click += (_, _) =>
            {
                if (ColourPickerDialog.Pick(this, Color.FromArgb(unchecked((int)_colour)), "Object colour") is { } c) _colour = Surface.Pack(c);
            };
            Controls.Add(colour); bottom += 38;
        }
        var ok = new Button { Text = "Apply", Left = 162, Top = bottom, Width = 76 };
        ok.Click += (_, _) =>
        {
            try { Primitive?.Validate(); TextSource?.Validate(); DialogResult = DialogResult.OK; }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "Object properties"); }
        };
        var cancel = new Button { Text = "Cancel", Left = 248, Top = bottom, Width = 76, DialogResult = DialogResult.Cancel };
        Controls.Add(ok); Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;
        ClientSize = new Size(340, bottom + 44);
    }
}
