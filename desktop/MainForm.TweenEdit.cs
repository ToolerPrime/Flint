/* Editable tweens: the tween dialog (move straight or on a curve, scale,
 * rotate, fade, ease presets and a custom curve), Edit tween (change it and
 * every generated frame follows, including a new frame count) and Remove
 * tween (keep the frames as they are). */
using System.Drawing.Drawing2D;

namespace Flint;

public sealed partial class MainForm
{
    /// <summary>The tween on the active layer that the current frame belongs to, if any.</summary>
    private TweenSpan? TweenHere => ActiveLayer.Shared ? null : ActiveLayer.Tweens.FirstOrDefault(s => s.Covers(_cur));

    private void RunEditTween()
    {
        FinishFloat();
        StopPlay();
        if (TweenHere is not { } span) { Say("Go to a frame after a tween's first frame to edit it."); return; }
        using var edit = new TweenDialog(span.Spec, span.Frames, editing: true);
        if (edit.ShowDialog(this) == DialogResult.OK) EditTween(span, edit.Spec, edit.Frames);
    }

    private void RunTweenDialog()
    {
        FinishFloat();
        StopPlay();
        if (!CanTweenObject()) return;
        using var dlg = new TweenDialog(new TweenSpec { Dx = 32 }, 6, editing: false);
        if (dlg.ShowDialog(this) == DialogResult.OK) CreateTween(dlg.Spec, "create motion tween");
    }

    /// <summary>Change a tween: its generated frames are made again from the start frame's object.</summary>
    internal bool EditTween(TweenSpan span, TweenSpec spec, int frames)
    {
        var layer = ActiveLayer;
        if (layer.Shared || !layer.Tweens.Contains(span)) { Say("That tween is no longer on this layer."); return false; }
        if (layer.Cels.GetValueOrDefault(span.Start) is not { } startCel || span.ObjectIndex >= startCel.Objects.Count ||
            startCel.Objects[span.ObjectIndex] is not RasterObject baseObj)
        {
            Say("The tween's object is gone from its first frame; remove the tween instead.");
            return false;
        }
        if (span.StartCount >= 0 && startCel.Objects.Count != span.StartCount)
        {
            Say("Objects were added or deleted on the tween's first frame, so Flint cannot be sure which one it tweens. Remove the tween and create it again.");
            return false;
        }
        if (layer.Locked) { Say("The layer is locked."); return false; }
        if (!spec.Changes) { Say("Choose a move, scale, rotation or opacity change."); return false; }
        if (!TweenFits(span.Start + span.Frames, baseObj, spec, Math.Max(frames, 1))) return false;
        var before = Before();
        layer.Tweens.Remove(span);                   // put back after the frame count change, which would drop it
        int start = span.Start, old = span.Frames;
        for (int k = old + 1; k <= frames; k++)
        {
            int at = start + k;
            _doc.InsertFrame(at, _doc.Frames[start + old].Clone());
            foreach (var l in _doc.Layers)
                if (!l.Shared && l.Cels.TryGetValue(start + old, out var last)) { l.Cels[at] = last.Clone(); SymbolOps.AdvanceCel(_doc, l.Cels[at], k - old); }
        }
        for (int k = old; k > frames; k--) _doc.RemoveFrame(start + k);
        spec = spec.Clone();
        spec.NewFrames = frames;
        int idx = span.ObjectIndex;
        if (spec.OpacityFrom.HasValue) startCel.Objects[idx] = TweenStep((RasterObject)baseObj.CloneObject(), spec, 0);
        else if (baseObj.Opacity != span.BaseOpacity)
        {
            // The fade was turned off: the object gets its own opacity back.
            var own = (RasterObject)baseObj.CloneObject();
            own.Opacity = span.BaseOpacity;
            startCel.Objects[idx] = own;
        }
        var source = (RasterObject)startCel.Objects[idx];
        for (int k = 1; k <= frames; k++)
        {
            var cel = layer.EnsureCel(start + k);
            var step = TweenStep(SymbolOps.Advance(_doc, (RasterObject)source.CloneObject(), k), spec, k / (double)frames);
            if (idx < cel.Objects.Count) cel.Objects[idx] = step; else cel.Objects.Add(step);
        }
        layer.Tweens.Add(new TweenSpan(start, frames, idx, spec, span.BaseOpacity, startCel.Objects.Count));
        _cur = Math.Min(_cur, start + frames);
        _selStart = _selEnd = _cur;
        PushStructure("edit tween", before);
        Say($"tween updated: {frames} frame{(frames == 1 ? "" : "s")} after frame {start + 1}");
        return true;
    }

    /// <summary>Forget the tween; its frames stay as plain frames.</summary>
    internal bool RemoveTween()
    {
        if (TweenHere is not { } span) { Say("The current frame is not part of a tween on this layer."); return false; }
        var before = Before();
        ActiveLayer.Tweens.Remove(span);
        PushStructure("remove tween", before);
        Say("tween removed; its frames stay as they are");
        return true;
    }

    internal TweenSpan? TestTweenHere => TweenHere;
    internal void TestSetLayerShared(bool on) => SetLayerShared(on);
    internal bool TestCreateTween(TweenSpec spec) => CreateTween(spec, "create motion tween");

    // ---------- the dialog ----------

    private sealed class TweenDialog : ThemedForm
    {
        private readonly NumericUpDown _frames, _x, _y, _cx, _cy, _scale, _opFrom, _opTo, _rot;
        private readonly CheckBox _curve = new() { Text = "Curved path, bending towards", AutoSize = true };
        private readonly CheckBox _fade = new() { Text = "Change opacity", AutoSize = true };
        private readonly RadioButton _cw = new() { Text = "Clockwise", AutoSize = true, Checked = true };
        private readonly RadioButton _ccw = new() { Text = "Counter-clockwise", AutoSize = true };
        private readonly ComboBox _ease = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, AccessibleName = "Ease" };
        private readonly EaseEditor _curveBox = new() { Width = 170, Height = 170, AccessibleName = "Ease curve. Drag the two handles for a custom curve." };

        public int Frames => (int)_frames.Value;

        public TweenSpec Spec => new()
        {
            NewFrames = Frames, Dx = (double)_x.Value, Dy = (double)_y.Value,
            CurveX = _curve.Checked ? (double)_cx.Value : null, CurveY = _curve.Checked ? (double)_cy.Value : null,
            ScaleTo = (double)_scale.Value,
            OpacityFrom = _fade.Checked ? (double)_opFrom.Value : null, OpacityTo = _fade.Checked ? (double)_opTo.Value : null,
            Rotate = (double)_rot.Value * (_cw.Checked ? 1 : -1),
            Ease = _curveBox.Ease,
        };

        public TweenDialog(TweenSpec s, int frames, bool editing)
        {
            Text = editing ? "Edit tween" : "Create motion tween";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = ShowInTaskbar = false;
            var tips = Theme.MakeTip();
            Disposed += (_, _) => tips.Dispose();
            int y = 14;
            NumericUpDown N(string label, int left, decimal min, decimal max, decimal value, string hint, int places = 0)
            {
                var cap = new Label { Text = label, Left = left, Top = y + 3, Width = 130 };
                var n = new NumericUpDown { Left = left + 134, Top = y, Width = 80, Minimum = min, Maximum = max, DecimalPlaces = places,
                                            Value = Math.Clamp(value, min, max), AccessibleName = label.Replace("&", ""), TextAlign = HorizontalAlignment.Right };
                Controls.Add(cap); Controls.Add(n);
                tips.SetToolTip(n, hint); tips.SetToolTip(cap, hint);
                return n;
            }
            _frames = N(editing ? "&Frames after start" : "&New frames", 16, 1, MaxTweenFrames, frames, "Frames after the start frame. Editing can add or remove frames at the end of the tween."); y += 32;
            _x = N("&Horizontal (px)", 16, -MaxTweenOffset, MaxTweenOffset, (decimal)s.Dx, "Negative values move left."); y += 32;
            _y = N("&Vertical (px)", 16, -MaxTweenOffset, MaxTweenOffset, (decimal)s.Dy, "Negative values move up."); y += 32;
            _curve.Left = 16; _curve.Top = y + 2; _curve.Checked = s.CurveX.HasValue; Controls.Add(_curve); y += 26;
            _cx = N("   control x (px)", 16, -MaxTweenOffset, MaxTweenOffset, (decimal)(s.CurveX ?? s.Dx / 2), "Where the path bends towards, measured from the start."); y += 30;
            _cy = N("   control y (px)", 16, -MaxTweenOffset, MaxTweenOffset, (decimal)(s.CurveY ?? s.Dy / 2 - 24), "Negative is up."); y += 32;
            _scale = N("&Scale to (%)", 16, 1, 1000, (decimal)s.ScaleTo, "Size at the last frame."); y += 32;
            _rot = N("&Rotate (degrees)", 16, 0, 3600, (decimal)Math.Abs(s.Rotate), "Turn over the tween."); y += 30;
            _cw.Left = 150; _cw.Top = y; _ccw.Left = 240; _ccw.Top = y; _ccw.Checked = s.Rotate < 0;
            Controls.Add(_cw); Controls.Add(_ccw); y += 28;
            _fade.Left = 16; _fade.Top = y + 2; _fade.Checked = s.OpacityFrom.HasValue || s.OpacityTo.HasValue; Controls.Add(_fade); y += 26;
            _opFrom = N("   opacity from (%)", 16, 0, 100, (decimal)(s.OpacityFrom ?? 100), "Opacity at the start frame."); y += 30;
            _opTo = N("   opacity to (%)", 16, 0, 100, (decimal)(s.OpacityTo ?? 0), "Opacity at the last frame."); y += 36;

            // Ease column.
            Controls.Add(new Label { Text = "&Ease", Left = 260, Top = 17, AutoSize = true });
            foreach (EaseKind k in Enum.GetValues<EaseKind>()) _ease.Items.Add(Ease.Describe(k));
            _ease.Left = 300; _ease.Top = 14;
            Controls.Add(_ease);
            _curveBox.Left = 300; _curveBox.Top = 48;
            Controls.Add(_curveBox);
            Controls.Add(new Label { Text = "Custom: drag the two handles.", Left = 300, Top = 224, AutoSize = true, ForeColor = Theme.Muted });
            _curveBox.Ease = s.Ease;
            _ease.SelectedIndex = (int)s.Ease.Kind;
            _ease.SelectedIndexChanged += (_, _) =>
            {
                var k = (EaseKind)_ease.SelectedIndex;
                _curveBox.Ease = k == EaseKind.Custom ? new Ease(k, _curveBox.Ease.X1, _curveBox.Ease.Y1, _curveBox.Ease.X2, _curveBox.Ease.Y2) : new Ease(k);
            };
            _curveBox.Changed += () => { if (_ease.SelectedIndex != (int)EaseKind.Custom) _ease.SelectedIndex = (int)EaseKind.Custom; };

            void Sync() { _cx.Enabled = _cy.Enabled = _curve.Checked; _opFrom.Enabled = _opTo.Enabled = _fade.Checked; }
            _curve.CheckedChanged += (_, _) => Sync(); _fade.CheckedChanged += (_, _) => Sync();
            Sync();

            var ok = new Button { Text = editing ? "Update" : "Create", Left = 300, Top = y, Width = 84, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 392, Top = y, Width = 84, DialogResult = DialogResult.Cancel };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            ClientSize = new Size(490, y + 44);
        }
    }
}

/// <summary>Shows an ease curve; for Custom, two draggable handles set the cubic-bezier control points.</summary>
internal sealed class EaseEditor : Control
{
    private Ease _ease = Ease.Linear;
    private int _drag = -1;
    public event Action? Changed;

    public Ease Ease
    {
        get => _ease;
        set { _ease = value; Invalidate(); }
    }

    public EaseEditor()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = true;
    }

    // y from -0.5 to 1.5 so overshoot shows.
    private RectangleF Plot => new(12, 12, Width - 24, Height - 24);
    private PointF ToScreen(double x, double y) => new((float)(Plot.X + x * Plot.Width), (float)(Plot.Bottom - (y + 0.5) / 2 * Plot.Height));
    private (double x, double y) FromScreen(Point p) => ((p.X - Plot.X) / Plot.Width, (Plot.Bottom - p.Y) / Plot.Height * 2 - 0.5);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Panel2);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var grid = new Pen(Theme.Border);
        g.DrawRectangle(grid, Rectangle.Round(RectangleF.FromLTRB(ToScreen(0, 1).X, ToScreen(0, 1).Y, ToScreen(1, 0).X, ToScreen(1, 0).Y)));
        var pts = Enumerable.Range(0, 81).Select(i => ToScreen(i / 80.0, _ease.Apply(i / 80.0))).ToArray();
        using var curve = new Pen(Theme.Accent, 2);
        g.DrawLines(curve, pts);
        if (_ease.Kind != EaseKind.Custom) return;
        using var handle = new Pen(Theme.Muted);
        var h1 = ToScreen(_ease.X1, _ease.Y1); var h2 = ToScreen(_ease.X2, _ease.Y2);
        g.DrawLine(handle, ToScreen(0, 0), h1); g.DrawLine(handle, ToScreen(1, 1), h2);
        using var fill = new SolidBrush(Theme.Accent);
        g.FillEllipse(fill, h1.X - 5, h1.Y - 5, 10, 10); g.FillEllipse(fill, h2.X - 5, h2.Y - 5, 10, 10);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (_ease.Kind != EaseKind.Custom) { _ease = new Ease(EaseKind.Custom, _ease.X1, _ease.Y1, _ease.X2, _ease.Y2); Changed?.Invoke(); }
        var h1 = ToScreen(_ease.X1, _ease.Y1); var h2 = ToScreen(_ease.X2, _ease.Y2);
        double d1 = Math.Pow(e.X - h1.X, 2) + Math.Pow(e.Y - h1.Y, 2), d2 = Math.Pow(e.X - h2.X, 2) + Math.Pow(e.Y - h2.Y, 2);
        _drag = d1 <= d2 ? 1 : 2;
        OnMouseMove(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag < 0) return;
        var (x, y) = FromScreen(e.Location);
        x = Math.Round(Math.Clamp(x, 0, 1), 2); y = Math.Round(Math.Clamp(y, -0.5, 1.5), 2);
        _ease = _drag == 1 ? _ease with { X1 = x, Y1 = y } : _ease with { X2 = x, Y2 = y };
        Invalidate();
        Changed?.Invoke();
    }

    protected override void OnMouseUp(MouseEventArgs e) => _drag = -1;
}
