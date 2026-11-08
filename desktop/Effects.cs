/* Live effects on an object: drop and inner shadow, glow, bevel, blur, motion blur,
 * hue/saturation, colour fill, brightness/contrast.
 *
 * An effect list is applied, in order, to an object's rendered pixels (after the matrix),
 * so a moved or rotated object keeps its shadow and the same list is re-applied on every
 * re-render. Effects work on raw BGRA bytes, never GDI+. The bitmap grows to fit them
 * and is trimmed to what is visible afterwards. ObjectEffect is immutable after creation
 * (undo snapshots share it).
 *
 * Bevels, glows and shadows are approximations of Fireworks' look, not copies of its filters.
 */
namespace Flint;

public enum EffectKind { DropShadow, InnerShadow, Glow, InnerGlow, Bevel, GaussianBlur, MotionBlur, HueSat, ColorFill, BrightContrast }

public sealed class ObjectEffect
{
    public EffectKind Kind;
    /// <summary>Named numbers. Colours are BGRA packed like Surface.Pack (alpha in the top byte), stored as doubles.</summary>
    public Dictionary<string, double> P = new();

    public ObjectEffect(EffectKind kind, params (string, double)[] p)
    {
        Kind = kind;
        foreach (var (k, v) in p) P[k] = v;
    }

    /// <summary>Switched off in the Effects panel (stored as the number "off"; the effect stays in the list but does nothing).</summary>
    public bool Disabled => Get("off") != 0;

    /// <summary>A copy of this effect with one number changed (effects are never changed in place).</summary>
    public ObjectEffect With(string key, double value)
    {
        var e = new ObjectEffect(Kind);
        foreach (var (k, v) in P) e.P[k] = v;
        e.P[key] = value;
        return e;
    }

    public double Get(string k, double def = 0) => P.TryGetValue(k, out var v) ? v : def;
    public uint Colour(string k, uint def = 0xFF000000) => P.TryGetValue(k, out var v) ? (uint)v : def;
}

public static class ObjectEffects
{
    /// <summary>Apply every effect in order. Returns the new pixels and their top-left on the canvas.</summary>
    public static (byte[] px, int w, int h, int x, int y) Apply(IReadOnlyList<ObjectEffect> fx, byte[] px, int w, int h, int x, int y)
    {
        foreach (var e in fx)
        {
            if (e.Disabled) continue;
            int pad = Pad(e);
            if (pad > 0) { px = Expand(px, w, h, pad); w += 2 * pad; h += 2 * pad; x -= pad; y -= pad; }
            px = Run(e, px, w, h);
        }
        return Trim(px, w, h, x, y);
    }

    private static int Rb(double softness) => softness <= 0 ? 0 : Math.Max(1, (int)Math.Round(softness / 2.0));
    private static int RbGauss(double sigma) => sigma < 0.75 ? 0 : Math.Max(1, (int)Math.Round((-1 + Math.Sqrt(1 + 4 * sigma * sigma)) / 2));

    private static int Pad(ObjectEffect e) => e.Kind switch
    {
        EffectKind.DropShadow => (int)Math.Ceiling(Math.Abs(e.Get("dist"))) + 3 * Rb(e.Get("blur")) + 2,
        EffectKind.Glow => (int)Math.Ceiling(e.Get("width")) + 3 * Rb(e.Get("soft")) + 2,
        EffectKind.Bevel => e.Get("type") == 0 ? 0 : (int)Math.Ceiling(e.Get("width")) + 3,
        EffectKind.GaussianBlur => 3 * RbGauss(e.Get("radius")) + 1,
        EffectKind.MotionBlur => (int)Math.Ceiling(e.Get("dist") / 2) + 1,
        _ => 0,
    };

    private static byte[] Run(ObjectEffect e, byte[] p, int w, int h)
    {
        switch (e.Kind)
        {
            case EffectKind.DropShadow:
                {
                    var (dx, dy) = Offset(e.Get("dist"), e.Get("angle"));
                    return Adjust.DropShadow(p, w, h, dx, dy, 1.0, 2 * Rb(e.Get("blur")), e.Colour("colour"));
                }
            case EffectKind.InnerShadow: return InnerShadow(e, p, w, h);
            case EffectKind.Glow: return Glow(e, p, w, h, false);
            case EffectKind.InnerGlow: return Glow(e, p, w, h, true);
            case EffectKind.Bevel: return Bevel(e, p, w, h);
            case EffectKind.GaussianBlur:
                {
                    int rb = RbGauss(e.Get("radius"));
                    return rb == 0 ? p : Adjust.Blur(p, w, h, 2 * rb);
                }
            case EffectKind.MotionBlur: return MotionBlur(p, w, h, e.Get("angle"), e.Get("dist"));
            case EffectKind.HueSat:
                return e.Get("colorize") != 0 ? Adjust.Colourise(p, e.Get("hue"), e.Get("sat")) : Adjust.HueSatLight(p, e.Get("hue"), e.Get("sat"), e.Get("light"));
            case EffectKind.BrightContrast: return Adjust.BrightnessContrast(p, e.Get("bright"), e.Get("contrast"));
            case EffectKind.ColorFill: return ColorFill(p, e.Colour("colour"), e.Get("opacity", 100));
        }
        return p;
    }

    /// <summary>Screen offset for a distance and an angle in degrees (counter-clockwise from the right; 315 is down and right).</summary>
    public static (int dx, int dy) Offset(double dist, double angleDeg)
    {
        double a = angleDeg * Math.PI / 180;
        return ((int)Math.Round(dist * Math.Cos(a)), (int)Math.Round(-dist * Math.Sin(a)));
    }

    #region helpers

    private static byte[] Expand(byte[] p, int w, int h, int pad)
    {
        int nw = w + 2 * pad;
        var d = new byte[nw * (h + 2 * pad) * 4];
        for (int y = 0; y < h; y++) Buffer.BlockCopy(p, y * w * 4, d, ((y + pad) * nw + pad) * 4, w * 4);
        return d;
    }

    private static (byte[] px, int w, int h, int x, int y) Trim(byte[] p, int w, int h, int x, int y)
    {
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                if (p[(j * w + i) * 4 + 3] != 0) { if (i < x0) x0 = i; if (i > x1) x1 = i; if (j < y0) y0 = j; if (j > y1) y1 = j; }
        if (x1 < 0 || (x0 == 0 && y0 == 0 && x1 == w - 1 && y1 == h - 1)) return (p, w, h, x, y);
        int nw = x1 - x0 + 1, nh = y1 - y0 + 1;
        var d = new byte[nw * nh * 4];
        for (int j = 0; j < nh; j++) Buffer.BlockCopy(p, ((y0 + j) * w + x0) * 4, d, j * nw * 4, nw * 4);
        return (d, nw, nh, x + x0, y + y0);
    }

    private static byte[] Alpha(byte[] p)
    {
        var a = new byte[p.Length / 4];
        for (int i = 0; i < a.Length; i++) a[i] = p[i * 4 + 3];
        return a;
    }

    /// <summary>A plane as a solid colour whose alpha is the plane times <paramref name="strength"/> (0..1).</summary>
    private static byte[] Tint(byte[] plane, uint colour, double strength)
    {
        var d = new byte[plane.Length * 4];
        double k = strength * (colour >> 24) / 255.0;
        for (int i = 0; i < plane.Length; i++)
        {
            int a = (int)Math.Round(plane[i] * k);
            if (a == 0) continue;
            d[i * 4] = (byte)colour; d[i * 4 + 1] = (byte)(colour >> 8); d[i * 4 + 2] = (byte)(colour >> 16); d[i * 4 + 3] = (byte)a;
        }
        return d;
    }

    private static byte[] BlurPlane(byte[] plane, int w, int h, int rb)
    {
        if (rb <= 0) return plane;
        var rgba = new byte[plane.Length * 4];
        for (int i = 0; i < plane.Length; i++) rgba[i * 4 + 3] = plane[i];
        return Alpha(Adjust.Blur(rgba, w, h, 2 * rb));
    }

    /// <summary>Grow a plane by a disc of this radius (maximum, soft edge). Pixels outside the plane count as 0.</summary>
    private static byte[] Dilate(byte[] a, int w, int h, double r)
    {
        var offs = new List<(int dx, int dy, double k)>();
        int ri = (int)Math.Ceiling(r + 0.5);
        for (int dy = -ri; dy <= ri; dy++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                double k = Math.Clamp(r + 0.5 - Math.Sqrt(dx * dx + dy * dy), 0, 1);
                if (k > 0) offs.Add((dx, dy, k));
            }
        var d = new byte[a.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int v = a[y * w + x];
                if (v == 0) continue;
                foreach (var (dx, dy, k) in offs)
                {
                    int tx = x + dx, ty = y + dy;
                    if (tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                    int o = ty * w + tx, nv = (int)Math.Round(v * k);
                    if (nv > d[o]) d[o] = (byte)nv;
                }
            }
        return d;
    }

    /// <summary>Shrink a plane by a disc of this radius. The edge of the picture counts as empty.</summary>
    private static byte[] Erode(byte[] a, int w, int h, double r)
    {
        int pad = (int)Math.Ceiling(r) + 1, nw = w + 2 * pad, nh = h + 2 * pad;
        var inv = new byte[nw * nh];
        Array.Fill(inv, (byte)255);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) inv[(y + pad) * nw + x + pad] = (byte)(255 - a[y * w + x]);
        var dil = Dilate(inv, nw, nh, r);
        var d = new byte[a.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) d[y * w + x] = (byte)(255 - dil[(y + pad) * nw + x + pad]);
        return d;
    }

    private static byte[] Over(byte[] under, byte[] over, int w, int h)
    {
        var d = (byte[])under.Clone();
        ImageOps.Over(d, w, h, over, w, h, 0, 0, 255);
        return d;
    }

    #endregion

    #region effects

    private static byte[] InnerShadow(ObjectEffect e, byte[] p, int w, int h)
    {
        var (dx, dy) = Offset(e.Get("dist"), e.Get("angle"));
        int rb = Rb(e.Get("blur")), pad = Math.Max(Math.Abs(dx), Math.Abs(dy)) + 3 * rb + 2, nw = w + 2 * pad, nh = h + 2 * pad;
        var a = Alpha(p);
        var inv = new byte[nw * nh];   // 255 where there is no object, shifted by the offset; outside the picture there is none
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                int sx = x - pad - dx, sy = y - pad - dy;
                inv[y * nw + x] = sx < 0 || sy < 0 || sx >= w || sy >= h ? (byte)255 : (byte)(255 - a[sy * w + sx]);
            }
        inv = BlurPlane(inv, nw, nh, rb);
        var plane = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) plane[y * w + x] = (byte)(inv[(y + pad) * nw + x + pad] * a[y * w + x] / 255);
        return Over(p, Tint(plane, e.Colour("colour"), 1), w, h);
    }

    private static byte[] Glow(ObjectEffect e, byte[] p, int w, int h, bool inner)
    {
        var a = Alpha(p);
        double width = Math.Max(e.Get("width"), 0), op = e.Get("opacity", 100) / 100.0;
        int rb = Rb(e.Get("soft"));
        if (!inner)
        {
            var g = BlurPlane(Dilate(a, w, h, width), w, h, rb);
            return Over(Tint(g, e.Colour("colour"), op), p, w, h);   // glow behind the object
        }
        var er = Erode(a, w, h, width);
        var ring = new byte[a.Length];
        for (int i = 0; i < ring.Length; i++) ring[i] = (byte)Math.Max(0, a[i] - er[i]);
        ring = BlurPlane(ring, w, h, rb);
        for (int i = 0; i < ring.Length; i++) ring[i] = (byte)(ring[i] * a[i] / 255);
        return Over(p, Tint(ring, e.Colour("colour"), op), w, h);
    }

    /// <summary>
    /// A light-and-shade approximation. The object's alpha is blurred into a height map; where the
    /// slope faces the light the surface is lightened, where it faces away it is darkened.
    /// type 0 inner bevel (inside only), 1 outer bevel (outside only), 2 inset emboss, 3 raised emboss (both sides).
    /// </summary>
    private const double BevelGain = 0.45;   // tuned on FlashLite_Japan: a full-strength light/shade is far harsher than Fireworks' bevel
    private static byte[] Bevel(ObjectEffect e, byte[] p, int w, int h)
    {
        int type = (int)e.Get("type");
        double width = Math.Max(e.Get("width"), 0.5), contrast = Math.Clamp(e.Get("contrast", 75) / 100.0, 0, 1);
        int rb = Math.Max(1, (int)Math.Round(width / 2));
        double sigma = Math.Sqrt(rb * (rb + 1.0));
        double la = e.Get("light", 135) * Math.PI / 180, lx = Math.Cos(la), ly = -Math.Sin(la);
        double sign = type == 2 ? -1 : 1;
        var a = Alpha(p);
        var hm = BlurPlane(a, w, h, rb);
        uint hi = e.Colour("hilite", 0xFFFFFFFF), sh = e.Colour("shadow", 0xFF000000);
        var d = new byte[p.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                double mask = type switch { 0 => a[i] / 255.0, 1 => 1 - a[i] / 255.0, _ => 1 };
                if (mask <= 0) continue;
                double gx = (hm[y * w + Math.Min(x + 1, w - 1)] - hm[y * w + Math.Max(x - 1, 0)]) / 510.0;
                double gy = (hm[Math.Min(y + 1, h - 1) * w + x] - hm[Math.Max(y - 1, 0) * w + x]) / 510.0;
                double b = Math.Clamp(-(gx * lx + gy * ly) * sigma * BevelGain * sign, -1, 1);
                uint c = b >= 0 ? hi : sh;
                int al = (int)Math.Round(Math.Abs(b) * contrast * mask * (c >> 24));
                if (al == 0) continue;
                d[i * 4] = (byte)c; d[i * 4 + 1] = (byte)(c >> 8); d[i * 4 + 2] = (byte)(c >> 16); d[i * 4 + 3] = (byte)al;
            }
        return Over(p, d, w, h);
    }

    private static byte[] MotionBlur(byte[] p, int w, int h, double angleDeg, double dist)
    {
        int n = Math.Max(1, (int)Math.Round(dist));
        if (n <= 1) return p;
        double a = angleDeg * Math.PI / 180, ux = Math.Cos(a), uy = -Math.Sin(a);
        var d = new byte[p.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double sa = 0, sb = 0, sg = 0, sr = 0;
                for (int k = 0; k < n; k++)
                {
                    double t = k - (n - 1) / 2.0;
                    int sx = x + (int)Math.Round(t * ux), sy = y + (int)Math.Round(t * uy);
                    if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
                    int s = (sy * w + sx) * 4, al = p[s + 3];
                    sa += al; sb += p[s] * al; sg += p[s + 1] * al; sr += p[s + 2] * al;
                }
                if (sa < 0.5 * n) continue;
                int o = (y * w + x) * 4;
                d[o] = (byte)Math.Round(sb / sa); d[o + 1] = (byte)Math.Round(sg / sa); d[o + 2] = (byte)Math.Round(sr / sa);
                d[o + 3] = (byte)Math.Min(255, (int)Math.Round(sa / n));
            }
        return d;
    }

    private static byte[] ColorFill(byte[] p, uint colour, double opacity)
    {
        var d = (byte[])p.Clone();
        double k = Math.Clamp(opacity / 100.0, 0, 1);
        for (int i = 0; i < d.Length; i += 4)
        {
            if (d[i + 3] == 0) continue;
            d[i] = (byte)Math.Round(d[i] + ((byte)colour - d[i]) * k);
            d[i + 1] = (byte)Math.Round(d[i + 1] + ((byte)(colour >> 8) - d[i + 1]) * k);
            d[i + 2] = (byte)Math.Round(d[i + 2] + ((byte)(colour >> 16) - d[i + 2]) * k);
        }
        return d;
    }

    #endregion
}
