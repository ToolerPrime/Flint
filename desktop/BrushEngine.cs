/* Painting brush: round tips with size, hardness, angle, roundness, spacing,
 * opacity and flow, Photoshop style. A stroke paints into its own coverage
 * buffer (each dab builds up with flow towards full coverage), and the buffer
 * is blended onto the pre-stroke pixels at the stroke opacity, so crossing your
 * own stroke never darkens past the opacity.
 *
 * Tip falloff, hardness table, spacing and the build-up rule are translated
 * from PhotoCraft (crates/paint/src/lib.rs, render.rs, dynamics.rs, presets.rs),
 * Copyright (c) 2026 ArtCraft Team and the PhotoCraft contributors, MIT License.
 * See CREDITS_LICENSES.md.
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
    float Angle = 0,        // degrees
    float Roundness = 1,    // 1 round .. 0.01 flat
    bool Aliased = false)   // hard pixel edges (pencil-like)
{
    public const float MaxSize = 1000;

    /// <summary>Built-in presets (PhotoCraft's General group, plus its Hard Pencil). Pen-pressure variants come with tablet support.</summary>
    public static readonly BrushSettings[] Presets =
    {
        new("Hard Round", 30, 1, Spacing: 0.05f),
        new("Soft Round", 45, 0),
        new("Hard Round Fine", 5, 1, Spacing: 0.05f),
        new("Soft Round Large", 300, 0, Spacing: 0.15f),
        new("Airbrush Soft", 80, 0, Flow: 0.1f),
        new("Calligraphy Flat", 28, 1, Spacing: 0.05f, Angle: 45, Roundness: 0.2f),
        new("Hard Pencil", 3, 1, Spacing: 0.1f, Aliased: true),
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
}

public sealed class BrushStroke
{
    private readonly BrushSettings _b;
    private readonly byte[] _base;
    private readonly int _w, _h;
    // shortcut: one float per document pixel; fine for sprite-sized canvases, switch to sparse tiles for very large ones.
    private readonly float[] _cov;
    private readonly uint _colour;   // BGRA packed (Surface.Pack)
    private readonly Selection? _sel;
    private double _x, _y, _toNext;
    private Rectangle _dirty = Rectangle.Empty;

    public int DabCount { get; private set; }

    public BrushStroke(BrushSettings b, byte[] basePixels, int w, int h, uint colour, Selection? selection)
    {
        _b = b with
        {
            Size = Math.Clamp(float.IsFinite(b.Size) ? b.Size : 1, 1, BrushSettings.MaxSize),
            Hardness = Math.Clamp(float.IsFinite(b.Hardness) ? b.Hardness : 1, 0, 1),
            Opacity = Math.Clamp(float.IsFinite(b.Opacity) ? b.Opacity : 1, 0, 1),
            Flow = Math.Clamp(float.IsFinite(b.Flow) ? b.Flow : 1, 0, 1),
            Spacing = Math.Clamp(float.IsFinite(b.Spacing) ? b.Spacing : 0.1f, 0.01f, 10),
            Roundness = Math.Clamp(float.IsFinite(b.Roundness) ? b.Roundness : 1, 0.01f, 1),
            Angle = float.IsFinite(b.Angle) ? b.Angle : 0,
        };
        _base = basePixels; _w = w; _h = h; _colour = colour; _sel = selection;
        _cov = new float[w * h];
    }

    private double Step => Math.Max(_b.Size * _b.Spacing, _b.Aliased ? 1 : 0.5);

    public void Begin(double x, double y) { _x = x; _y = y; Dab(x, y); _toNext = Step; }

    /// <summary>Places dabs every <see cref="Step"/> pixels along the segment from the last point.</summary>
    public void LineTo(double x, double y)
    {
        double dx = x - _x, dy = y - _y, len = Math.Sqrt(dx * dx + dy * dy);
        if (!(len > 0) || !double.IsFinite(len)) return;
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

    private void Dab(double x, double y)
    {
        DabCount++;
        float r = _b.Size / 2, ro = Math.Max(_b.Roundness, 0.5f / r);
        float rm = Math.Max(r * ro, 0.5f);
        var (core, shoulder) = BrushTip.TipShape(rm, _b.Hardness);
        float reach = _b.Aliased ? rm : Math.Max(BrushTip.Reach(core, shoulder), rm);
        float cx = (float)x, cy = (float)y;
        if (_b.Aliased)
        {
            // Pencil-like: centre on a pixel for odd diameters, on a pixel corner for even ones.
            int n = Math.Max(1, (int)Math.Round(_b.Size));
            cx = n % 2 == 1 ? MathF.Floor(cx) + 0.5f : MathF.Round(cx);
            cy = n % 2 == 1 ? MathF.Floor(cy) + 0.5f : MathF.Round(cy);
        }
        float a = _b.Angle * MathF.PI / 180, cs = MathF.Cos(a), sn = MathF.Sin(a);
        float span = r + reach - rm + 1;   // bounding box half-size, generous for rotation
        int x0 = Math.Max(0, (int)MathF.Floor(cx - span)), x1 = Math.Min(_w - 1, (int)MathF.Ceiling(cx + span));
        int y0 = Math.Max(0, (int)MathF.Floor(cy - span)), y1 = Math.Min(_h - 1, (int)MathF.Ceiling(cy + span));
        if (x0 > x1 || y0 > y1) return;
        bool small = !_b.Aliased && rm < BrushTip.SmallRadius;
        float reach2 = (reach + 0.5f) * (reach + 0.5f), flow = _b.Flow;

        float At(float px, float py)
        {
            float dx = px - cx, dy = cy - py;                      // y up, so angles turn anticlockwise as in Photoshop
            float u = dx * cs + dy * sn, v = -dx * sn + dy * cs;   // into tip space
            float d2 = (u * ro) * (u * ro) + v * v;
            if (_b.Aliased) return d2 < reach2 && MathF.Sqrt(d2) <= rm ? 1 : 0;
            return d2 >= reach2 ? 0 : BrushTip.Falloff(MathF.Sqrt(d2), core, shoulder);
        }

        for (int py = y0; py <= y1; py++)
            for (int px = x0; px <= x1; px++)
            {
                float v;
                if (small)
                {
                    v = 0;
                    for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++) v += At(px + (sx + 0.5f) / 4, py + (sy + 0.5f) / 4);
                    v /= 16;
                }
                else v = At(px + 0.5f, py + 0.5f);
                if (v <= 0) continue;
                int i = py * _w + px;
                _cov[i] += v * flow * (1 - _cov[i]);   // build up towards full coverage
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
        float cb = _colour & 255, cg = (_colour >> 8) & 255, cr = (_colour >> 16) & 255;
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
            {
                int i = y * _w + x, o = i * 4;
                float a = _cov[i] * op;
                if (a <= 0 || (_sel != null && !_sel.Contains(x, y)))
                {
                    dst[o] = _base[o]; dst[o + 1] = _base[o + 1]; dst[o + 2] = _base[o + 2]; dst[o + 3] = _base[o + 3];
                    continue;
                }
                float ba = _base[o + 3] / 255f;
                float sa = ca0 * a, oa = sa + ba * (1 - sa);
                if (oa <= 0) { dst[o] = dst[o + 1] = dst[o + 2] = dst[o + 3] = 0; continue; }
                float k = ba * (1 - sa);
                dst[o] = (byte)MathF.Round((cb * sa + _base[o] * k) / oa);
                dst[o + 1] = (byte)MathF.Round((cg * sa + _base[o + 1] * k) / oa);
                dst[o + 2] = (byte)MathF.Round((cr * sa + _base[o + 2] * k) / oa);
                dst[o + 3] = (byte)MathF.Round(oa * 255);
            }
        return r;
    }
}
