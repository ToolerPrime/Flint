/* Brush Settings window (Photoshop's Brush Settings panel, after PhotoCraft's
 * brush_panel.rs): tip shape, shape dynamics, scattering, colour dynamics,
 * transfer and the extras, with a live sample stroke. Every change applies at
 * once; Cancel puts the brush back as it was. */
namespace Flint;

public sealed class BrushSettingsDialog : ThemedForm
{
    private BrushSettings _s;
    private readonly BrushSettings _original;
    private readonly Action<BrushSettings> _apply;
    private readonly PictureBox _preview;
    private readonly List<Action> _refresh = new();
    private bool _sync;

    public BrushSettings Settings => _s;

    public BrushSettingsDialog(BrushSettings start, Action<BrushSettings> apply)
    {
        _s = _original = start; _apply = apply;
        Text = "Brush settings"; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false; ShowInTaskbar = false;
        var tips = Theme.MakeTip();

        _preview = new PictureBox { Left = 12, Top = 10, Width = 616, Height = 70, BorderStyle = BorderStyle.FixedSingle, Tag = "keep", BackColor = Color.White };
        Controls.Add(_preview);

        int col = 12, y = 92;
        void Section(string title) { Controls.Add(new Label { Text = title, Left = col, Top = y, Width = 290, Height = 18, Font = new Font(Font, FontStyle.Bold), ForeColor = Theme.Accent, Tag = "keep" }); y += 22; }
        void Number(string label, decimal min, decimal max, Func<BrushSettings, decimal> get, Func<BrushSettings, decimal, BrushSettings> set, string tip, string unit = "%")
        {
            Controls.Add(new Label { Text = label, Left = col, Top = y + 3, Width = 118 });
            var num = new NumericUpDown { Left = col + 120, Top = y, Width = 60, Minimum = min, Maximum = max, TextAlign = HorizontalAlignment.Right, AccessibleName = label };
            var slider = new MiniSlider { Left = col + 184, Top = y + 1, Width = 86, Minimum = min, Maximum = max, Logarithmic = label == "Spacing", AccessibleName = label + " slider" };
            Controls.Add(new Label { Text = unit, Left = col + 272, Top = y + 3, Width = 22 });
            num.ValueChanged += (_, _) => { slider.Value = num.Value; if (!_sync) Change(set(_s, num.Value)); };
            slider.ValueChanged += (_, _) => num.Value = slider.Value;
            tips.SetToolTip(num, tip); tips.SetToolTip(slider, tip);
            Controls.Add(num); Controls.Add(slider);
            _refresh.Add(() => num.Value = Math.Clamp(get(_s), min, max));
            y += 28;
        }
        void Toggle(string label, Func<BrushSettings, bool> get, Func<BrushSettings, bool, BrushSettings> set, string tip)
        {
            var box = new MintCheckBox { Text = label, Left = col, Top = y, Width = 280, Height = 22 };
            box.CheckedChanged += (_, _) => { if (!_sync) Change(set(_s, box.Checked)); };
            tips.SetToolTip(box, tip);
            Controls.Add(box);
            _refresh.Add(() => box.Checked = get(_s));
            y += 26;
        }
        static decimal P(float v) => (decimal)Math.Round(v * 100);
        static float F(decimal v) => (float)v / 100;

        Section("Brush tip");
        Number("Spacing", 1, 1000, s => P(s.Spacing), (s, v) => s with { Spacing = F(v) }, "Gap between dabs, as a percentage of the brush size");
        Number("Angle", -180, 180, s => (decimal)Math.Round(s.Angle), (s, v) => s with { Angle = (float)v }, "Tip angle, anticlockwise", "°");
        Number("Roundness", 1, 100, s => P(s.Roundness), (s, v) => s with { Roundness = F(v) }, "100 is round; lower makes a flat, calligraphy-style tip");
        Toggle("Hard pixel edges (pencil)", s => s.Aliased, (s, v) => s with { Aliased = v }, "No soft edge at all: every pixel is fully painted or untouched");
        Section("Shape dynamics");
        Number("Size jitter", 0, 100, s => P(s.SizeJitter), (s, v) => s with { SizeJitter = F(v) }, "Each dab is randomly smaller, by up to this much");
        Number("Minimum size", 0, 100, s => P(s.MinSize), (s, v) => s with { MinSize = F(v) }, "Size jitter never goes below this");
        Number("Angle jitter", 0, 100, s => P(s.AngleJitter), (s, v) => s with { AngleJitter = F(v) }, "Random turn of each dab (100% = any angle)");
        Toggle("Angle follows the stroke direction", s => s.FollowDirection, (s, v) => s with { FollowDirection = v }, "The tip turns as your stroke turns");
        Number("Roundness jitter", 0, 100, s => P(s.RoundnessJitter), (s, v) => s with { RoundnessJitter = F(v) }, "Each dab is randomly flatter, by up to this much");
        Section("Scattering");
        Number("Scatter", 0, 1000, s => P(s.Scatter), (s, v) => s with { Scatter = F(v) }, "How far dabs spread from the line, as a percentage of the brush size");
        Toggle("Both axes", s => s.BothAxes, (s, v) => s with { BothAxes = v }, "Spread along the line as well as across it");
        Number("Count", 1, 16, s => s.Count, (s, v) => s with { Count = (int)v }, "Dabs placed at each spacing step", "");
        Number("Count jitter", 0, 100, s => P(s.CountJitter), (s, v) => s with { CountJitter = F(v) }, "Randomly fewer dabs per step");

        int left = y; col = 330; y = 92;
        Section("Colour dynamics");
        Number("Fg/Bg jitter", 0, 100, s => P(s.FgBgJitter), (s, v) => s with { FgBgJitter = F(v) }, "Mix randomly towards the other colour swatch");
        Number("Hue jitter", 0, 100, s => P(s.HueJitter), (s, v) => s with { HueJitter = F(v) }, "Random hue shift (100% = anywhere on the colour wheel)");
        Number("Saturation jitter", 0, 100, s => P(s.SatJitter), (s, v) => s with { SatJitter = F(v) }, "Random change in how colourful each dab is");
        Number("Brightness jitter", 0, 100, s => P(s.BrightJitter), (s, v) => s with { BrightJitter = F(v) }, "Random change in how bright each dab is");
        Toggle("Change colour every dab", s => s.ColourPerTip, (s, v) => s with { ColourPerTip = v }, "Off: one random colour for the whole stroke");
        Section("Transfer");
        Number("Opacity jitter", 0, 100, s => P(s.OpacityJitter), (s, v) => s with { OpacityJitter = F(v) }, "Each dab's most coverage is randomly lower");
        Number("Flow jitter", 0, 100, s => P(s.FlowJitter), (s, v) => s with { FlowJitter = F(v) }, "Each dab adds a random amount less paint");
        Section("Other");
        Toggle("Noise", s => s.Noise, (s, v) => s with { Noise = v }, "Grainy soft edges");
        Toggle("Wet edges", s => s.WetEdges, (s, v) => s with { WetEdges = v }, "Paint gathers at the edges, like watercolour");
        Toggle("Build-up (airbrush)", s => s.BuildUp, (s, v) => s with { BuildUp = v }, "Keeps adding paint while you hold the mouse still");
        Number("Smoothing", 0, 100, s => P(s.Smoothing), (s, v) => s with { Smoothing = F(v) }, "Steadies shaky lines; higher lags further behind the pointer");

        int bottom = Math.Max(left, y) + 8;
        var reset = new Button { Text = "Reset to preset", Left = 12, Top = bottom, Width = 120 };
        reset.Click += (_, _) => { var p = BrushSettings.Presets.FirstOrDefault(b => b.Name == _s.Name); if (p != null) { Change(p with { Size = _s.Size }); Sync(); } };
        var ok = new Button { Text = "OK", Left = 452, Top = bottom, Width = 84, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 544, Top = bottom, Width = 84, DialogResult = DialogResult.Cancel };
        Controls.Add(reset); Controls.Add(ok); Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;
        FormClosed += (_, _) => { if (DialogResult != DialogResult.OK) _apply(_original); _preview.Image?.Dispose(); };
        ClientSize = new Size(640, bottom + 40);
        Sync();
    }

    private void Change(BrushSettings s) { _s = s; _apply(s); DrawPreview(); }

    private void Sync()
    {
        _sync = true;
        foreach (var r in _refresh) r();
        _sync = false;
        DrawPreview();
    }

    /// <summary>A sample stroke in black on white (or in colour with colour dynamics).</summary>
    internal void DrawPreview()
    {
        int w = Math.Max(1, _preview.Width - 2), h = Math.Max(1, _preview.Height - 2);
        var px = new byte[w * h * 4]; Array.Fill(px, (byte)255);
        uint ink = _s.HasColourDynamics ? Surface.Pack(Color.FromArgb(255, 220, 40, 40)) : Surface.Pack(Color.Black);
        var stroke = new BrushStroke(_s with { Size = Math.Min(_s.Size, h * 0.6f) }, (byte[])px.Clone(), w, h, ink, null, Surface.Pack(Color.FromArgb(255, 40, 80, 220)));
        stroke.Begin(16, h / 2.0);
        for (int k = 1; k <= 80; k++) stroke.MoveTo(16 + k * (w - 32) / 80.0, h / 2.0 + h * 0.22 * Math.Sin(k / 80.0 * Math.PI * 2));
        stroke.End(w - 16, h / 2.0);
        stroke.RenderInto(px);
        var old = _preview.Image;
        _preview.Image = ImageOps.FromPixels(w, h, px);
        old?.Dispose();
    }
}
