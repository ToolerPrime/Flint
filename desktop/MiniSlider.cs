/* A small themed horizontal slider for the options bar, kept in step with a
 * number box. Optional logarithmic scale for brush size (fine control at small
 * sizes, still reaching 1000). */
using System.Drawing.Drawing2D;

namespace Flint;

public sealed class MiniSlider : Control
{
    private decimal _value, _min, _max = 100;
    public bool Logarithmic { get; set; }
    public event EventHandler? ValueChanged;

    public decimal Minimum { get => _min; set { _min = value; Value = _value; } }
    public decimal Maximum { get => _max; set { _max = value; Value = _value; } }
    public decimal Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, _min, Math.Max(_min, _max));
            if (v == _value) return;
            _value = v; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public MiniSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(80, 20); Cursor = Cursors.Hand; TabStop = false;
    }

    /// <summary>0..1 position of a value along the track.</summary>
    public double Fraction(decimal v)
    {
        if (_max <= _min) return 0;
        if (Logarithmic && _min > 0) return Math.Log((double)v / (double)_min) / Math.Log((double)_max / (double)_min);
        return (double)((v - _min) / (_max - _min));
    }

    public decimal FromFraction(double t)
    {
        t = Math.Clamp(t, 0, 1);
        double v = Logarithmic && _min > 0 ? (double)_min * Math.Pow((double)_max / (double)_min, t) : (double)_min + t * (double)(_max - _min);
        return Math.Round((decimal)v);
    }

    private const int Pad = 6;
    private void SetFromMouse(int x) => Value = FromFraction((x - Pad) / (double)Math.Max(1, Width - 2 * Pad));

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Capture = true; SetFromMouse(e.X); } }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) SetFromMouse(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value = FromFraction(Fraction(_value) + Math.Sign(e.Delta) * 0.04);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int cy = Height / 2, w = Width - 2 * Pad;
        int x = Pad + (int)Math.Round(Fraction(_value) * w);
        using (var track = new Pen(Theme.Border, 3)) g.DrawLine(track, Pad, cy, Pad + w, cy);
        using (var fill = new Pen(Theme.Accent, 3)) g.DrawLine(fill, Pad, cy, x, cy);
        using var knob = new SolidBrush(Enabled ? Theme.Text : Theme.Muted);
        g.FillEllipse(knob, x - 5, cy - 5, 10, 10);
    }
}
