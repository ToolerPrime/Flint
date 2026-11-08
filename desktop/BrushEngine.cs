/* Painting brush, Photoshop style. Round tips with size, hardness, angle,
 * roundness, spacing, opacity and flow; a blend mode; and the Brush Settings
 * sections: shape dynamics (size, angle, roundness jitter), scattering, colour
 * dynamics, transfer (opacity and flow jitter), noise, wet edges, airbrush
 * build-up and smoothing.
 *
 * A stroke paints into its own coverage buffer: each dab builds up with flow
 * towards its opacity ceiling, and the buffer is blended onto the pre-stroke
 * pixels at the stroke opacity, so crossing your own stroke never darkens past
 * the opacity. Every random choice is a hash of the stroke seed and the dab
 * number, so a stroke always comes out the same.
 *
 * Tip falloff, hardness table, spacing, the build-up rule, noise, wet edges and
 * the General presets are translated from PhotoCraft (crates/paint/src/lib.rs,
 * render.rs, dynamics.rs, brush.rs, presets.rs), Copyright (c) 2026 ArtCraft
 * Team and the PhotoCraft contributors, MIT License. See CREDITS_LICENSES.md.
 *
 * Pixels are written as bytes (BGRA), never through GDI+. */
namespace Flint;

public sealed record BrushSettings(
    string Name = "Hard Round",
    float Size = 30,
    float Hardness = 1,     // 0 soft .. 1 hard
    float Opacity = 1,      // stroke ceiling, 0..1
    float Flow = 1,         // per-dab build-up, 0..1
    float Spacing = 0.1f,   // fraction of the diameter between dabs
    float Angle = 0,        // degrees, anticlockwise
    float Roundness = 1,    // 1 round .. 0.01 flat
    bool Aliased = false,   // hard pixel edges (pencil-like)
    BlendMode Mode = BlendMode.Normal,
    bool Behind = false,    // paint only where the layer is see-through
    // Shape dynamics (all 0..1)
    float SizeJitter = 0, float MinSize = 0,
    float AngleJitter = 0, bool FollowDirection = false,
    float RoundnessJitter = 0, float MinRoundness = 0.25f,
    // Scattering: Scatter is in brush diameters (0..10)
    float Scatter = 0, bool BothAxes = false, int Count = 1, float CountJitter = 0,
    // Colour dynamics (0..1): mix towards the secondary colour, hue / saturation / brightness jitter
    float FgBgJitter = 0, float HueJitter = 0, float SatJitter = 0, float BrightJitter = 0, bool ColourPerTip = true,
    // Transfer (0..1)
    float OpacityJitter = 0, float FlowJitter = 0,
    bool Noise = false, bool WetEdges = false,
    bool BuildUp = false, float BuildUpRate = 25,   // airbrush: dabs per second while held still
    float Smoothing = 0,                            // 0..1, steadies the line
    int Seed = 0)
{
    public const float MaxSize = 1000;

    public bool HasColourDynamics => FgBgJitter > 0 || HueJitter > 0 || SatJitter > 0 || BrightJitter > 0;

    /// <summary>Built-in presets (PhotoCraft's General group and Hard Pencil, plus a few using the dynamics). Pen-pressure variants come with tablet support.</summary>
    public static readonly BrushSettings[] Presets =
    {
        new("Hard Round", 30, 1, Spacing: 0.05f),
        new("Soft Round", 45, 0),
        new("Hard Round Fine", 5, 1, Spacing: 0.05f),
        new("Soft Round Large", 300, 0, Spacing: 0.15f),
        new("Airbrush Soft", 80, 0, Flow: 0.1f, BuildUp: true),
        new("Calligraphy Flat", 28, 1, Spacing: 0.05f, Angle: 45, Roundness: 0.2f),
        new("Hard Pencil", 3, 1, Spacing: 0.1f, Aliased: true),
        new("Spatter", 30, 0.9f, Spacing: 0.6f, SizeJitter: 0.8f, Scatter: 2.2f, BothAxes: true, Count: 3, CountJitter: 0.5f, OpacityJitter: 0.5f),
        new("Stipple", 6, 1, Spacing: 0.9f, SizeJitter: 0.5f, Scatter: 4f, BothAxes: true, Count: 4, Noise: true),
        new("Watercolour Wet Edges", 50, 0.3f, Opacity: 0.8f, Flow: 0.5f, WetEdges: true, SizeJitter: 0.15f),
        new("Chalk", 35, 0.8f, Spacing: 0.15f, Noise: true, AngleJitter: 1f, RoundnessJitter: 0.4f, FlowJitter: 0.5f),
        new("Confetti", 18, 1, Spacing: 1.2f, SizeJitter: 0.6f, Scatter: 3f, BothAxes: true, Count: 2, HueJitter: 1f, SatJitter: 0.2f),
    };
}

public static class BrushTip
{
    /// <summary>(core radius fraction, shoulder width fraction) at hardness 0, 0.1 .. 1.</summary>
    private static readonly (float core, float shoulder)[] Shape =
    {
        (0.00f, 0.664f), (0.14f, 0.58f), (0.28f, 0.50f), (0.40f, 0.44f), (0.52f, 0.36f), (0.63f, 0.29f),
        (0.70f, 0.24f), (0.78f, 0.19f), (0.84f, 0.15f), (0.89f, 0.11f), (1.00f, 0.0f),
    };

    private const float EdgePx = 1.2f;       // a hard tip keeps a ~1 px soft edge
    public const float SmallRadius = 3f;     // smaller tips are sampled by area (4x4), so thin lines don't bead

    /// <summary>Core radius and shoulder width in pixels for a round tip.</summary>
    public static (float core, float shoulder) TipShape(float radius, float hardness)
    {
        float h = (float.IsNaN(hardness) ? 0 : Math.Clamp(hardness, 0, 1)) * 10;
        int i = Math.Min((int)MathF.Floor(h), 9);
        float t = h - i;
        var (a, b) = (Shape[i], Shape[i + 1]);
        float r0 = a.core + (b.core - a.core) * t, s = a.shoulder + (b.shoulder - a.shoulder) * t;
        radius = float.IsFinite(radius) ? Math.Max(radius, 0) : 0;
        float edge = Math.Max(Math.Min(EdgePx, 0.3f * radius), 0.25f);
        return (Math.Max(Math.Min(r0 * radius, radius - edge), 0), Math.Max(Math.Max(s * radius, edge), 1e-3f));
    }

    /// <summary>exp(-u) as (1 - u/64)^64: close to exp, exactly 0 from u = 64.</summary>
    private static float SoftExp(float u)
    {
        float g = Math.Clamp(1 - u / 64, 0, 1);
        for (int k = 0; k < 6; k++) g *= g;
        return g;
    }

    public static float Falloff(float d, float core, float shoulder)
    {
        if (d <= core) return 1;
        float x = (d - core) / shoulder;
        return SoftExp(x * x);
    }

    public static float Reach(float core, float shoulder) => core + 2.6f * shoulder;

    /// <summary>Wet edges: the middle of a stroke keeps half the paint, the rim all of it (rn: 0 centre .. 1 rim).</summary>
    public static float WetFactor(float rn)
    {
        float t = Math.Clamp((rn - 0.5f) / 0.5f, 0, 1);
        return 0.5f + 0.5f * t * t * (3 - 2 * t);
    }

    /// <summary>A repeatable random number 0..1 from three integers.</summary>
    public static float Hash(int a, int b, int c)
    {
        ulong z = (ulong)(uint)a * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)b * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(uint)c * 0x165667B19E3779F9UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; z ^= z >> 31;
        return (z >> 40) / (float)(1UL << 24);
    }
}

public sealed class BrushStroke
{
    private readonly BrushSettings _b;
    private readonly byte[] _base;
    private readonly int _w, _h;
    // shortcut: full-canvas float buffers; fine for sprite-sized canvases, switch to sparse tiles for very large ones.
    private readonly float[] _cov;
    private readonly float[]? _rgb;     // per-pixel paint colour, only with colour dynamics
    private readonly float[]? _depth;   // wet edges: how deep inside a dab each pixel is
    private readonly uint _colour, _secondary;   // BGRA packed (Surface.Pack)
    private readonly (float r, float g, float b) _strokeColour;
    private readonly Selection? _sel;
    private double _x, _y, _toNext, _sx, _sy, _heldDabs;
    private float _direction;   // degrees, of the last segment
    private Rectangle _dirty = Rectangle.Empty;

    public int DabCount { get; private set; }

    public BrushStroke(BrushSettings b, byte[] basePixels, int w, int h, uint colour, Selection? selection, uint? secondary = null)
    {
        static float F(float v, float dflt, float lo, float hi) => Math.Clamp(float.IsFinite(v) ? v : dflt, lo, hi);
        _b = b with
        {
            Size = F(b.Size, 1, 1, BrushSettings.MaxSize), Hardness = F(b.Hardness, 1, 0, 1),
            Opacity = F(b.Opacity, 1, 0, 1), Flow = F(b.Flow, 1, 0, 1), Spacing = F(b.Spacing, 0.1f, 0.01f, 10),
            Roundness = F(b.Roundness, 1, 0.01f, 1), Angle = F(b.Angle, 0, -3600, 3600),
            SizeJitter = F(b.SizeJitter, 0, 0, 1), MinSize = F(b.MinSize, 0, 0, 1), AngleJitter = F(b.AngleJitter, 0, 0, 1),
            RoundnessJitter = F(b.RoundnessJitter, 0, 0, 1), MinRoundness = F(b.MinRoundness, 0.25f, 0.01f, 1),
            Scatter = F(b.Scatter, 0, 0, 10), Count = Math.Clamp(b.Count, 1, 16), CountJitter = F(b.CountJitter, 0, 0, 1),
            FgBgJitter = F(b.FgBgJitter, 0, 0, 1), HueJitter = F(b.HueJitter, 0, 0, 1), SatJitter = F(b.SatJitter, 0, 0, 1),
            BrightJitter = F(b.BrightJitter, 0, 0, 1), OpacityJitter = F(b.OpacityJitter, 0, 0, 1), FlowJitter = F(b.FlowJitter, 0, 0, 1),
            BuildUpRate = F(b.BuildUpRate, 25, 1, 200), Smoothing = F(b.Smoothing, 0, 0, 1),
        };
        _base = basePixels; _w = w; _h = h; _colour = colour; _secondary = secondary ?? colour; _sel = selection;
        _cov = new float[w * h];
        if (_b.HasColourDynamics) _rgb = new float[w * h * 3];
        if (_b.WetEdges) _depth = new float[w * h];
        _strokeColour = DabColour(-1);
    }

    private double Step => Math.Max(_b.Size * _b.Spacing, _b.Aliased ? 1 : 0.5);

    private float R(int dab, int stream) => BrushTip.Hash(_b.Seed, dab, stream);

    public void Begin(double x, double y) { _x = _sx = x; _y = _sy = y; Dab(x, y); _toNext = Step; }

    /// <summary>A pointer position: smoothing pulls the line towards it gently, then dabs are placed.</summary>
    public void MoveTo(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        double keep = _b.Smoothing * 0.92;   // fraction of the old position kept per sample
        _sx = _sx * keep + x * (1 - keep); _sy = _sy * keep + y * (1 - keep);
        LineTo(_sx, _sy);
    }

    /// <summary>End of the stroke: catch up to where the pointer actually stopped.</summary>
    public void End(double x, double y) { if (_b.Smoothing > 0) LineTo(x, y); }

    /// <summary>Airbrush build-up: keeps adding dabs where the pointer rests.</summary>
    public void Hold(double milliseconds)
    {
        if (!_b.BuildUp || !(milliseconds > 0)) return;
        _heldDabs += Math.Min(milliseconds, 1000) * _b.BuildUpRate / 1000;
        for (; _heldDabs >= 1; _heldDabs--) Dab(_x, _y);
    }

    /// <summary>Places dabs every <see cref="Step"/> pixels along the segment from the last point.</summary>
    public void LineTo(double x, double y)
    {
        double dx = x - _x, dy = y - _y, len = Math.Sqrt(dx * dx + dy * dy);
        if (!(len > 0) || !double.IsFinite(len)) return;
        _direction = (float)(Math.Atan2(-dy, dx) * 180 / Math.PI);
        double at = _toNext;
        int guard = 0;
        while (at <= len && guard++ < 100_000)
        {
            Dab(_x + dx * at / len, _y + dy * at / len);
            at += Step;
        }
        _toNext = at - len;
        _x = x; _y = y;
    }

    /// <summary>The paint colour for a dab (or the whole stroke when dab is -1), 0..255 channels.</summary>
    private (float r, float g, float b) DabColour(int dab)
    {
        float Ch(uint c, int shift) => (c >> shift) & 255;
        var (r, g, b) = (Ch(_colour, 16), Ch(_colour, 8), Ch(_colour, 0));
        if (!_b.HasColourDynamics) return (r, g, b);
        if (dab >= 0 && !_b.ColourPerTip) return _strokeColour;
        int k = dab + 1;
        float mix = _b.FgBgJitter * R(k, 11);
        r += (Ch(_secondary, 16) - r) * mix; g += (Ch(_secondary, 8) - g) * mix; b += (Ch(_secondary, 0) - b) * mix;
        var hsb = ColourMath.RgbToHsb(r / 255, g / 255, b / 255);
        double h = hsb[0] + (R(k, 12) * 2 - 1) * _b.HueJitter * 180;
        double s = Math.Clamp(hsb[1] + (R(k, 13) * 2 - 1) * _b.SatJitter, 0, 1);
        double v = Math.Clamp(hsb[2] + (R(k, 14) * 2 - 1) * _b.BrightJitter, 0, 1);
        var c = ColourMath.HsbToRgb((h % 360 + 360) % 360, s, v);
        return ((float)c[0] * 255, (float)c[1] * 255, (float)c[2] * 255);
    }

    private void Dab(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        int n = DabCount++;
        int copies = Math.Max(1, _b.Count - (int)Math.Round(_b.CountJitter * _b.Count * R(n, 1)));
        for (int c = 0; c < copies; c++)
        {
            int k = n * 16 + c;   // a separate random stream for each scattered copy
            double ox = 0, oy = 0;
            if (_b.Scatter > 0)
            {
                // across the stroke (and along it with both axes), up to Scatter brush diameters
                double a = _direction * Math.PI / 180, across = (R(k, 2) * 2 - 1) * _b.Scatter * _b.Size;
                ox = -Math.Sin(a) * across; oy = -Math.Cos(a) * across;
                if (_b.BothAxes) { double along = (R(k, 3) * 2 - 1) * _b.Scatter * _b.Size; ox += Math.Cos(a) * along; oy -= Math.Sin(a) * along; }
            }
            float size = _b.Size * Math.Max(_b.MinSize, 1 - _b.SizeJitter * R(k, 4));
            float angle = _b.Angle + (_b.FollowDirection ? _direction : 0) + (R(k, 5) - 0.5f) * 360 * _b.AngleJitter;
            float round = _b.Roundness * Math.Max(_b.MinRoundness, 1 - _b.RoundnessJitter * R(k, 6));
            float flow = _b.Flow * (1 - _b.FlowJitter * R(k, 7));
            float ceil = 1 - _b.OpacityJitter * R(k, 8);
            PaintTip(x + ox, y + oy, Math.Max(size, 1), angle, round, flow, ceil, DabColour(k));
        }
    }

    private void PaintTip(double x, double y, float size, float angleDeg, float roundness, float flow, float ceil, (float r, float g, float b) colour)
    {
        float r = size / 2, ro = Math.Max(roundness, 0.5f / r);
        float rm = Math.Max(r * ro, 0.5f);
        var (core, shoulder) = BrushTip.TipShape(rm, _b.Hardness);
        float reach = _b.Aliased ? rm : Math.Max(BrushTip.Reach(core, shoulder), rm);
        float cx = (float)x, cy = (float)y;
        if (_b.Aliased)
        {
            // Pencil-like: centre on a pixel for odd diameters, on a pixel corner for even ones.
            int n = Math.Max(1, (int)Math.Round(size));
            cx = n % 2 == 1 ? MathF.Floor(cx) + 0.5f : MathF.Round(cx);
            cy = n % 2 == 1 ? MathF.Floor(cy) + 0.5f : MathF.Round(cy);
        }
        float a = angleDeg * MathF.PI / 180, cs = MathF.Cos(a), sn = MathF.Sin(a);
        float span = r + reach - rm + 1;   // bounding box half-size, generous for rotation
        int x0 = Math.Max(0, (int)MathF.Floor(cx - span)), x1 = Math.Min(_w - 1, (int)MathF.Ceiling(cx + span));
        int y0 = Math.Max(0, (int)MathF.Floor(cy - span)), y1 = Math.Min(_h - 1, (int)MathF.Ceiling(cy + span));
        if (x0 > x1 || y0 > y1) return;
        bool small = !_b.Aliased && rm < BrushTip.SmallRadius;
        float reach2 = (reach + 0.5f) * (reach + 0.5f);

        // Coverage (0..1) and how far out towards the rim (0..1) at a point.
        (float v, float rn) At(float px, float py)
        {
            float dx = px - cx, dy = cy - py;                      // y up, so angles turn anticlockwise as in Photoshop
            float u = dx * cs + dy * sn, w = -dx * sn + dy * cs;   // into tip space
            float d2 = (u * ro) * (u * ro) + w * w, d = MathF.Sqrt(d2);
            float rn = Math.Min(1, d / rm);   // 0 at the centre, 1 at the tip's edge
            if (_b.Aliased) return (d2 < reach2 && d <= rm ? 1 : 0, rn);
            return (d2 >= reach2 ? 0 : BrushTip.Falloff(d, core, shoulder), rn);
        }

        for (int py = y0; py <= y1; py++)
            for (int px = x0; px <= x1; px++)
            {
                float v, rn;
                if (small)
                {
                    v = 0;
                    for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++) v += At(px + (sx + 0.5f) / 4, py + (sy + 0.5f) / 4).v;
                    v /= 16; rn = At(px + 0.5f, py + 0.5f).rn;
                }
                else (v, rn) = At(px + 0.5f, py + 0.5f);
                if (v <= 0) continue;
                if (_b.Noise && v < 1)
                {
                    float hard = BrushTip.Hash(px, py, 0x6E6F6973) < v ? 1 : 0;
                    v += (hard - v) * 0.7f;
                    if (v <= 0) continue;
                }
                int i = py * _w + px;
                float c0 = _cov[i];
                if (ceil > c0)
                {
                    float c1 = c0 + v * flow * (ceil - c0);   // build up towards the dab's ceiling
                    if (_rgb != null && c1 > 0)
                    {
                        float k = (c1 - c0) / c1;
                        _rgb[i * 3] += (colour.r - _rgb[i * 3]) * k; _rgb[i * 3 + 1] += (colour.g - _rgb[i * 3 + 1]) * k; _rgb[i * 3 + 2] += (colour.b - _rgb[i * 3 + 2]) * k;
                    }
                    _cov[i] = c1;
                }
                if (_depth != null) _depth[i] = Math.Max(_depth[i], 1 - rn);
            }
        _dirty = Rectangle.Union(_dirty.IsEmpty ? new Rectangle(x0, y0, 0, 0) : _dirty, Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1));
    }

    public float CoverageAt(int x, int y) => _cov[y * _w + x];

    /// <summary>
    /// Blends the stroke onto the pre-stroke pixels inside the area changed since the
    /// last call, writing into <paramref name="dst"/> (same size as the base). Returns that area.
    /// </summary>
    public Rectangle RenderInto(byte[] dst)
    {
        var r = _dirty; _dirty = Rectangle.Empty;
        if (r.IsEmpty) return r;
        float op = _b.Opacity, ca0 = (_colour >> 24) / 255f;
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
            {
                int i = y * _w + x, o = i * 4;
                dst[o] = _base[o]; dst[o + 1] = _base[o + 1]; dst[o + 2] = _base[o + 2]; dst[o + 3] = _base[o + 3];
                float a = _cov[i] * op;
                if (_depth != null) a *= BrushTip.WetFactor(1 - _depth[i]);
                if (a <= 0 || (_sel != null && !_sel.Contains(x, y))) continue;
                var (cr, cg, cb) = _rgb != null ? (_rgb[i * 3], _rgb[i * 3 + 1], _rgb[i * 3 + 2]) : _strokeColour;
                float sa = ca0 * a;
                if (_b.Behind)
                {
                    // the layer stays on top: the paint only shows through its see-through parts
                    float ba = dst[o + 3] / 255f, oa = ba + sa * (1 - ba);
                    if (oa <= 0) continue;
                    float k = sa * (1 - ba);
                    dst[o] = (byte)MathF.Round((dst[o] * ba + cb * k) / oa);
                    dst[o + 1] = (byte)MathF.Round((dst[o + 1] * ba + cg * k) / oa);
                    dst[o + 2] = (byte)MathF.Round((dst[o + 2] * ba + cr * k) / oa);
                    dst[o + 3] = (byte)MathF.Round(oa * 255);
                    continue;
                }
                int sa255 = (int)MathF.Round(sa * 255);
                if (sa255 <= 0) continue;
                Blending.Pixel(dst, o, (int)MathF.Round(cr), (int)MathF.Round(cg), (int)MathF.Round(cb), sa255, _b.Mode);
            }
        return r;
    }
}
