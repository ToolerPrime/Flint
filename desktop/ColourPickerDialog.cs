/* Photoshop-style colour picker: a 2D colour field and a slider for one chosen
 * channel (H, S, B, R, G or B), new / current swatches, HSB, RGB and hex fields,
 * a web-colour warning, and a screen eyedropper.
 *
 * Layout and colour maths translated from PhotoCraft
 * (crates/ui-egui/src/dialogs/color_picker_ui.rs, crates/color/src/convert.rs),
 * Copyright (c) 2026 ArtCraft Team and the PhotoCraft contributors, MIT License.
 * See CREDITS_LICENSES.md. */
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Flint;

/// <summary>Colour maths for the picker. Channels are 0..1, hue is degrees 0..360.</summary>
public static class ColourMath
{
    public static double[] HsbToRgb(double h, double s, double v)
    {
        double c = v * s, hp = ((h % 360 + 360) % 360) / 60, x = c * (1 - Math.Abs(hp % 2 - 1)), m = v - c;
        var (r, g, b) = (int)hp switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x),
            3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return new[] { r + m, g + m, b + m };
    }

    /// <summary>Hue is 0 for greys (callers keep the previous hue themselves).</summary>
    public static double[] RgbToHsb(double r, double g, double b)
    {
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn;
        double h = d <= 0 ? 0 : mx == r ? ((g - b) / d % 6 + 6) % 6 : mx == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return new[] { 60 * h, mx > 0 ? d / mx : 0, mx };
    }

    /// <summary>Colour at field position (x, y), 0..1 with y down, for a mode with the slider at z.</summary>
    public static double[] FieldColour(char mode, double z, double x, double y) => mode switch
    {
        's' => HsbToRgb(x * 360, z, 1 - y),
        'v' => HsbToRgb(x * 360, 1 - y, z),
        'r' => new[] { z, 1 - y, x },
        'g' => new[] { 1 - y, z, x },
        'b' => new[] { x, 1 - y, z },
        _ => HsbToRgb(z * 360, x, 1 - y),
    };

    /// <summary>Field position and slider value of a colour (inverse of <see cref="FieldColour"/>).</summary>
    public static (double x, double y, double z) Locate(char mode, double[] hsb, double[] rgb)
    {
        double h = hsb[0] / 360;
        return mode switch
        {
            's' => (h, 1 - hsb[2], hsb[1]),
            'v' => (h, 1 - hsb[1], hsb[2]),
            'r' => (rgb[2], 1 - rgb[1], rgb[0]),
            'g' => (rgb[2], 1 - rgb[0], rgb[1]),
            'b' => (rgb[0], 1 - rgb[1], rgb[2]),
            _ => (hsb[1], 1 - hsb[2], h),
        };
    }

    /// <summary>Colour for slider position z, other channels from the current colour (hue slider: full colour).</summary>
    public static double[] SliderColour(char mode, double z, double[] hsb, double[] rgb) => mode switch
    {
        's' => HsbToRgb(hsb[0], z, hsb[2]),
        'v' => HsbToRgb(hsb[0], hsb[1], z),
        'r' => new[] { z, rgb[1], rgb[2] },
        'g' => new[] { rgb[0], z, rgb[2] },
        'b' => new[] { rgb[0], rgb[1], z },
        _ => HsbToRgb(z * 360, 1, 1),
    };

    public static byte Q(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    public static string Hex(double[] c) => $"{Q(c[0]):x2}{Q(c[1]):x2}{Q(c[2]):x2}";

    /// <summary>Six hex digits, with or without '#'; null if not.</summary>
    public static double[]? ParseHex(string s)
    {
        var h = s.Trim().TrimStart('#');
        if (h.Length != 6 || !h.All(Uri.IsHexDigit)) return null;
        return new[] { Convert.ToByte(h[..2], 16) / 255.0, Convert.ToByte(h[2..4], 16) / 255.0, Convert.ToByte(h[4..], 16) / 255.0 };
    }

    /// <summary>The nearest web colour (channels in steps of 51), or null if it already is one.</summary>
    public static double[]? NotWebSafe(double[] c)
    {
        var web = c.Select(v => Math.Round(Math.Clamp(v, 0, 1) * 5) / 5).ToArray();
        return Hex(web) == Hex(c) ? null : web;
    }
}

public sealed class ColourPickerDialog : ThemedForm
{
    private const int FieldSize = 256;
    private static char _lastMode = 'h';

    private readonly int _alpha;
    private readonly double[] _orig;
    private double[] _rgb;
    private double[] _hsb;   // kept separately so hue and saturation survive greys and black
    private char _mode = _lastMode;
    private bool _sync;

    private readonly PickArea _field, _slider;
    private readonly Panel _swatch, _webChip;
    private readonly Label _webLabel;
    private readonly Dictionary<char, NumericUpDown> _num = new();
    private readonly Dictionary<char, RadioButton> _radio = new();
    private readonly TextBox _hex;

    public Color Colour => Color.FromArgb(_alpha, ColourMath.Q(_rgb[0]), ColourMath.Q(_rgb[1]), ColourMath.Q(_rgb[2]));

    /// <summary>Shows the picker; returns the chosen colour (alpha kept from <paramref name="start"/>) or null on Cancel.</summary>
    public static Color? Pick(IWin32Window? owner, Color start, string title = "Colour picker")
    {
        using var dlg = new ColourPickerDialog(start, title);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.Colour : null;
    }

    public ColourPickerDialog(Color start, string title = "Colour picker")
    {
        Text = title; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false; ShowInTaskbar = false; KeyPreview = true;
        _alpha = start.A;
        _orig = new[] { start.R / 255.0, start.G / 255.0, start.B / 255.0 };
        _rgb = (double[])_orig.Clone();
        _hsb = ColourMath.RgbToHsb(_rgb[0], _rgb[1], _rgb[2]);

        _field = new PickArea { Left = 12, Top = 12, Width = FieldSize, Height = FieldSize, AccessibleName = "Colour field" };
        _field.Paint += PaintField;
        _field.Picked += (x, y) => FieldPicked(x / (double)(FieldSize - 1), y / (double)(FieldSize - 1));
        _slider = new PickArea { Left = 274, Top = 6, Width = 34, Height = FieldSize + 12, AccessibleName = "Channel slider" };
        _slider.Paint += PaintSlider;
        _slider.Picked += (_, y) => SliderPicked(1 - Math.Clamp((y - 6) / (double)(FieldSize - 1), 0, 1));
        Controls.Add(_field); Controls.Add(_slider);

        Controls.Add(new Label { Text = "new", Left = 322, Top = 10, Width = 64, TextAlign = ContentAlignment.MiddleCenter });
        _swatch = new Panel { Left = 322, Top = 30, Width = 64, Height = 72, Cursor = Cursors.Hand, Tag = "keep", AccessibleName = "New and current colour" };
        _swatch.Paint += PaintSwatch;
        _swatch.MouseDown += (_, e) => { if (e.Y >= _swatch.Height / 2) SetRgb(_orig); };
        Controls.Add(_swatch);
        Controls.Add(new Label { Text = "current", Left = 322, Top = 104, Width = 64, TextAlign = ContentAlignment.MiddleCenter });
        var tips = Theme.MakeTip();
        tips.SetToolTip(_swatch, "Bottom half: the colour you started with. Click it to go back.");

        _webChip = new Panel { Left = 394, Top = 68, Width = 14, Height = 14, BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand, Tag = "keep" };
        _webLabel = new Label { Text = "web", Left = 390, Top = 50, Width = 40, Height = 16 };
        _webChip.Click += (_, _) => { if (ColourMath.NotWebSafe(_rgb) is { } w) SetRgb(w); };
        tips.SetToolTip(_webChip, "Not a web colour: click for the nearest web colour");
        Controls.Add(_webChip); Controls.Add(_webLabel);

        var ok = new Button { Text = "OK", Left = 450, Top = 12, Width = 90, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 450, Top = 44, Width = 90, DialogResult = DialogResult.Cancel };
        Controls.Add(ok); Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;

        void Row(char key, string label, string unit, int x, int y, int max)
        {
            var radio = new RadioButton { Text = label, Left = x, Top = y + 2, Width = 38, Checked = key == _mode };
            radio.CheckedChanged += (_, _) => { if (radio.Checked) { _mode = _lastMode = key; Redraw(); } };
            var num = new NumericUpDown { Left = x + 40, Top = y, Width = 58, Minimum = 0, Maximum = max, TextAlign = HorizontalAlignment.Right };
            num.AccessibleName = label.TrimEnd(':') + " " + unit;
            num.ValueChanged += (_, _) => NumberChanged(key);
            Controls.Add(radio); Controls.Add(num);
            Controls.Add(new Label { Text = unit, Left = x + 100, Top = y + 3, Width = 18 });
            _radio[key] = radio; _num[key] = num;
        }
        Row('h', "H:", "°", 322, 134, 360); Row('s', "S:", "%", 322, 164, 100); Row('v', "B:", "%", 322, 194, 100);
        Row('r', "R:", "", 448, 134, 255); Row('g', "G:", "", 448, 164, 255); Row('b', "B:", "", 448, 194, 255);

        Controls.Add(new Label { Text = "#", Left = 332, Top = 241, Width = 14 });
        _hex = new TextBox { Left = 362, Top = 238, Width = 80, MaxLength = 7, AccessibleName = "Hex colour" };
        _hex.TextChanged += (_, _) => { if (!_sync && ColourMath.ParseHex(_hex.Text) is { } c) SetRgb(c, fromHex: true); };
        _hex.Leave += (_, _) => Sync();
        Controls.Add(_hex);

        var eyedrop = new Button { Text = "Pick from screen", Left = 12, Top = 278, Width = 140, AccessibleName = "Pick a colour from the screen" };
        tips.SetToolTip(eyedrop, "Hold the mouse button down on this, drag anywhere on screen (the canvas too), and let go on the colour you want");
        eyedrop.MouseDown += (_, _) => { eyedrop.Capture = true; Cursor = Cursors.Cross; };
        eyedrop.MouseMove += (_, _) => { if (eyedrop.Capture) SetRgb(ScreenColour(Control.MousePosition)); };
        eyedrop.MouseUp += (_, _) => { eyedrop.Capture = false; Cursor = Cursors.Default; };
        Controls.Add(eyedrop);

        ClientSize = new Size(552, 316);
        Sync();
    }

    private void FieldPicked(double x, double y)
    {
        x = Math.Clamp(x, 0, 1); y = Math.Clamp(y, 0, 1);
        var (_, _, z) = ColourMath.Locate(_mode, _hsb, _rgb);
        switch (_mode)
        {
            case 'h': SetHsb(z * 360, x, 1 - y); break;
            case 's': SetHsb(x * 360, z, 1 - y); break;
            case 'v': SetHsb(x * 360, 1 - y, z); break;
            default: SetRgb(ColourMath.FieldColour(_mode, z, x, y)); break;
        }
    }

    private void SliderPicked(double z)
    {
        switch (_mode)
        {
            case 'h': SetHsb(z * 360, _hsb[1], _hsb[2]); break;
            case 's': SetHsb(_hsb[0], z, _hsb[2]); break;
            case 'v': SetHsb(_hsb[0], _hsb[1], z); break;
            default: SetRgb(ColourMath.SliderColour(_mode, z, _hsb, _rgb)); break;
        }
    }

    private void NumberChanged(char key)
    {
        if (_sync) return;
        double v = (double)_num[key].Value;
        switch (key)
        {
            case 'h': SetHsb(v, _hsb[1], _hsb[2]); break;
            case 's': SetHsb(_hsb[0], v / 100, _hsb[2]); break;
            case 'v': SetHsb(_hsb[0], _hsb[1], v / 100); break;
            default:
                var c = (double[])_rgb.Clone(); c["rgb".IndexOf(key)] = v / 255; SetRgb(c); break;
        }
    }

    internal void SetHsb(double h, double s, double v)
    {
        _hsb = new[] { Math.Clamp(h, 0, 360), Math.Clamp(s, 0, 1), Math.Clamp(v, 0, 1) };
        _rgb = ColourMath.HsbToRgb(_hsb[0], _hsb[1], _hsb[2]);
        Sync();
    }

    internal void SetRgb(double[] c, bool fromHex = false)
    {
        _rgb = c.Select(x => Math.Clamp(x, 0, 1)).ToArray();
        var hsb = ColourMath.RgbToHsb(_rgb[0], _rgb[1], _rgb[2]);
        if (hsb[2] <= 0) { hsb[0] = _hsb[0]; hsb[1] = _hsb[1]; }   // black: keep hue and saturation
        else if (hsb[1] <= 0) hsb[0] = _hsb[0];                       // grey: keep hue
        _hsb = hsb;
        Sync(fromHex);
    }

    internal void SetMode(char mode) => _radio[mode].Checked = true;
    internal double[] TestHsb => _hsb;

    private void Sync(bool fromHex = false)
    {
        _sync = true;
        void Set(char k, double v) => _num[k].Value = Math.Clamp((decimal)Math.Round(v), _num[k].Minimum, _num[k].Maximum);
        Set('h', _hsb[0]); Set('s', _hsb[1] * 100); Set('v', _hsb[2] * 100);
        Set('r', _rgb[0] * 255); Set('g', _rgb[1] * 255); Set('b', _rgb[2] * 255);
        if (!fromHex) _hex.Text = ColourMath.Hex(_rgb);
        var web = ColourMath.NotWebSafe(_rgb);
        _webChip.Visible = _webLabel.Visible = web != null;
        if (web != null) _webChip.BackColor = Color.FromArgb(ColourMath.Q(web[0]), ColourMath.Q(web[1]), ColourMath.Q(web[2]));
        _sync = false;
        Redraw();
    }

    private void Redraw() { _field.Invalidate(); _slider.Invalidate(); _swatch.Invalidate(); }

    private static Color C(double[] c) => Color.FromArgb(ColourMath.Q(c[0]), ColourMath.Q(c[1]), ColourMath.Q(c[2]));

    /// <summary>Fills a bitmap pixel by pixel (BGRA bytes, no GDI+ drawing).</summary>
    private static Bitmap Render(int w, int h, Func<int, int, double[]> colour)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = colour(x, y); int i = (y * w + x) * 4;
                px[i] = ColourMath.Q(c[2]); px[i + 1] = ColourMath.Q(c[1]); px[i + 2] = ColourMath.Q(c[0]); px[i + 3] = 255;
            }
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        return bmp;
    }

    private void PaintField(object? sender, PaintEventArgs e)
    {
        var (fx, fy, fz) = ColourMath.Locate(_mode, _hsb, _rgb);
        using var bmp = Render(FieldSize, FieldSize, (x, y) => ColourMath.FieldColour(_mode, fz, x / (double)(FieldSize - 1), y / (double)(FieldSize - 1)));
        e.Graphics.DrawImageUnscaled(bmp, 0, 0);
        int mx = (int)Math.Round(fx * (FieldSize - 1)), my = (int)Math.Round(fy * (FieldSize - 1));
        bool light = _hsb[2] > 0.6 && _hsb[1] < 0.4;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(light ? Color.Black : Color.White, 1.5f);
        e.Graphics.DrawEllipse(pen, mx - 5, my - 5, 10, 10);
    }

    private void PaintSlider(object? sender, PaintEventArgs e)
    {
        var (_, _, z) = ColourMath.Locate(_mode, _hsb, _rgb);
        using var bmp = Render(18, FieldSize, (_, y) => ColourMath.SliderColour(_mode, 1 - y / (double)(FieldSize - 1), _hsb, _rgb));
        e.Graphics.DrawImageUnscaled(bmp, 8, 6);
        int sy = 6 + (int)Math.Round((1 - z) * (FieldSize - 1));
        using var ink = new SolidBrush(Theme.Text);
        e.Graphics.FillPolygon(ink, new[] { new Point(7, sy), new Point(1, sy - 4), new Point(1, sy + 4) });
        e.Graphics.FillPolygon(ink, new[] { new Point(27, sy), new Point(33, sy - 4), new Point(33, sy + 4) });
    }

    private void PaintSwatch(object? sender, PaintEventArgs e)
    {
        int h = _swatch.Height / 2;
        using (var b = new SolidBrush(C(_rgb))) e.Graphics.FillRectangle(b, 0, 0, _swatch.Width, h);
        using (var b = new SolidBrush(C(_orig))) e.Graphics.FillRectangle(b, 0, h, _swatch.Width, _swatch.Height - h);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, _swatch.Width - 1, _swatch.Height - 1);
    }

    private static double[] ScreenColour(Point p)
    {
        using var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(p, Point.Empty, new Size(1, 1));
        var c = bmp.GetPixel(0, 0);
        return new[] { c.R / 255.0, c.G / 255.0, c.B / 255.0 };
    }

    /// <summary>A control that reports the press and every drag position, clamped to its area.</summary>
    private sealed class PickArea : Control
    {
        public event Action<int, int>? Picked;
        public PickArea() { DoubleBuffered = true; Cursor = Cursors.Cross; SetStyle(ControlStyles.Selectable, false); }
        private void Report(MouseEventArgs e) => Picked?.Invoke(Math.Clamp(e.X, 0, Width - 1), Math.Clamp(e.Y, 0, Height - 1));
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Capture = true; Report(e); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture && e.Button == MouseButtons.Left) Report(e); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }
    }
}
