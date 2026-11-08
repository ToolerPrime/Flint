/* Colour adjustments and filters, on straight-alpha BGRA bytes.
 *
 * Each filter is a pure function: pixels in, new pixels out. The editor
 * applies the result only where the selection allows, and through a structure
 * edit, so undo restores the exact original bytes. Adjustments never touch
 * alpha unless that is their job (outline, shadow, blur).
 */
namespace Flint;

public sealed record FilterParam(string Name, double Min, double Max, double Default, int Decimals = 0);

/// <summary>
/// A filter the Filters menu can run. <see cref="Grow"/> says how many pixels
/// the result may spill past the original edges (blur, outline, shadow), so a
/// placed object can be padded before filtering instead of being clipped.
/// </summary>
public sealed record FilterDef(string Name, string Group, FilterParam[] Params,
                               Func<byte[], int, int, double[], uint, byte[]> Run,
                               Func<double[], int>? Grow = null);

public static class Adjust
{
    public static readonly FilterDef[] All =
    {
        new("Invert", "Adjust", Array.Empty<FilterParam>(), (p, w, h, a, c) => Invert(p)),
        new("Desaturate", "Adjust", Array.Empty<FilterParam>(), (p, w, h, a, c) => HueSatLight(p, 0, -100, 0)),
        new("Brightness / contrast", "Adjust",
            new[] { new FilterParam("Brightness", -100, 100, 0), new FilterParam("Contrast", -100, 100, 0) },
            (p, w, h, a, c) => BrightnessContrast(p, a[0], a[1])),
        new("Hue / saturation / lightness", "Adjust",
            new[] { new FilterParam("Hue", -180, 180, 0), new FilterParam("Saturation", -100, 100, 0), new FilterParam("Lightness", -100, 100, 0) },
            (p, w, h, a, c) => HueSatLight(p, a[0], a[1], a[2])),
        new("Levels", "Adjust",
            new[] { new FilterParam("Input black", 0, 254, 0), new FilterParam("Input white", 1, 255, 255),
                    new FilterParam("Gamma", 0.1, 9.99, 1, 2), new FilterParam("Output black", 0, 255, 0), new FilterParam("Output white", 0, 255, 255) },
            (p, w, h, a, c) => Levels(p, (int)a[0], (int)a[1], a[2], (int)a[3], (int)a[4])),
        new("Colourise", "Adjust",
            new[] { new FilterParam("Hue", 0, 359, 30), new FilterParam("Saturation", 0, 100, 50) },
            (p, w, h, a, c) => Colourise(p, a[0], a[1])),
        new("Posterize", "Adjust", new[] { new FilterParam("Levels", 2, 64, 4) }, (p, w, h, a, c) => Posterize(p, (int)a[0])),
        new("Threshold", "Adjust", new[] { new FilterParam("Level", 0, 255, 128) }, (p, w, h, a, c) => Threshold(p, (int)a[0])),

        new("Blur", "Filter", new[] { new FilterParam("Radius", 1, 50, 2) },
            (p, w, h, a, c) => Blur(p, w, h, (int)a[0]), a => (int)a[0]),
        new("Sharpen (unsharp mask)", "Filter",
            new[] { new FilterParam("Radius", 1, 20, 1), new FilterParam("Amount %", 1, 500, 100), new FilterParam("Threshold", 0, 255, 0) },
            (p, w, h, a, c) => Unsharp(p, w, h, (int)a[0], a[1] / 100, (int)a[2])),
        new("Outline", "Filter",
            new[] { new FilterParam("Thickness", 1, 16, 1), new FilterParam("Corners (0/1)", 0, 1, 1) },
            (p, w, h, a, c) => Outline(p, w, h, (int)a[0], a[1] >= 0.5, c), a => (int)a[0]),
        new("Drop shadow", "Filter",
            new[] { new FilterParam("Offset X", -50, 50, 2), new FilterParam("Offset Y", -50, 50, 2),
                    new FilterParam("Opacity %", 0, 100, 50), new FilterParam("Blur", 0, 20, 0) },
            (p, w, h, a, c) => DropShadow(p, w, h, (int)a[0], (int)a[1], a[2] / 100, (int)a[3], c),
            a => Math.Max(Math.Abs((int)a[0]), Math.Abs((int)a[1])) + (int)a[3]),
        new("Pixelate", "Filter", new[] { new FilterParam("Cell size", 2, 64, 4) }, (p, w, h, a, c) => Pixelate(p, w, h, (int)a[0])),
        new("Add noise", "Filter", new[] { new FilterParam("Amount", 1, 128, 16), new FilterParam("Seed", 0, 9999, 1) },
            (p, w, h, a, c) => Noise(p, (int)a[0], (int)a[1])),
    };

    public static FilterDef Find(string name) => All.First(f => f.Name == name);

    /// <summary>Keep the original wherever <paramref name="inside"/> says no.</summary>
    public static byte[] Masked(byte[] orig, byte[] result, int w, int h, Func<int, int, bool>? inside)
    {
        if (inside == null) return result;
        var o = (byte[])orig.Clone();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (inside(x, y)) Buffer.BlockCopy(result, (y * w + x) * 4, o, (y * w + x) * 4, 4);
        return o;
    }

    private static byte[] PerPixel(byte[] p, Func<int, int, int, (int b, int g, int r)> f)
    {
        var d = (byte[])p.Clone();
        for (int i = 0; i < d.Length; i += 4)
        {
            if (d[i + 3] == 0) continue;
            var (b, g, r) = f(d[i], d[i + 1], d[i + 2]);
            d[i] = Clamp(b); d[i + 1] = Clamp(g); d[i + 2] = Clamp(r);
        }
        return d;
    }

    private static byte Clamp(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);
    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);

    public static byte[] Invert(byte[] p) => PerPixel(p, (b, g, r) => (255 - b, 255 - g, 255 - r));

    public static byte[] BrightnessContrast(byte[] p, double brightness, double contrast)
    {
        double k = contrast >= 0 ? 1 + contrast / 100 * 3 : 1 + contrast / 100;   // up to 4× or down to flat
        double add = brightness / 100 * 255;
        var lut = new byte[256];
        for (int v = 0; v < 256; v++) lut[v] = Clamp((v - 127.5) * k + 127.5 + add);
        return PerPixel(p, (b, g, r) => (lut[b], lut[g], lut[r]));
    }

    public static byte[] Levels(byte[] p, int inBlack, int inWhite, double gamma, int outBlack, int outWhite)
    {
        var lut = new byte[256];
        double span = Math.Max(1, inWhite - inBlack);
        for (int v = 0; v < 256; v++)
        {
            double t = Math.Clamp((v - inBlack) / span, 0, 1);
            t = Math.Pow(t, 1 / gamma);
            lut[v] = Clamp(outBlack + t * (outWhite - outBlack));
        }
        return PerPixel(p, (b, g, r) => (lut[b], lut[g], lut[r]));
    }

    public static byte[] Posterize(byte[] p, int levels)
    {
        var lut = new byte[256];
        for (int v = 0; v < 256; v++) lut[v] = Clamp(Math.Round(v / 255.0 * (levels - 1)) * 255.0 / (levels - 1));
        return PerPixel(p, (b, g, r) => (lut[b], lut[g], lut[r]));
    }

    public static byte[] Threshold(byte[] p, int level) => PerPixel(p, (b, g, r) =>
    {
        int v = (r * 299 + g * 587 + b * 114) / 1000 >= level ? 255 : 0;
        return (v, v, v);
    });

    public static byte[] HueSatLight(byte[] p, double hue, double sat, double light) => hue == 0 && sat == 0 && light == 0
        ? (byte[])p.Clone()   // HSL round trips can drift by one; no change means no change
        : PerPixel(p, (b, g, r) =>
    {
        RgbToHsl(r, g, b, out double h, out double s, out double l);
        h = (h + hue / 360 + 1) % 1;
        s = sat >= 0 ? s + (1 - s) * sat / 100 * (s > 0 ? 1 : 0) : s * (1 + sat / 100);
        l = light >= 0 ? l + (1 - l) * light / 100 : l * (1 + light / 100);
        HslToRgb(h, Math.Clamp(s, 0, 1), Math.Clamp(l, 0, 1), out int rr, out int gg, out int bb);
        return (bb, gg, rr);
    });

    public static byte[] Colourise(byte[] p, double hue, double sat) => PerPixel(p, (b, g, r) =>
    {
        RgbToHsl(r, g, b, out _, out _, out double l);
        HslToRgb(hue / 360, sat / 100, l, out int rr, out int gg, out int bb);
        return (bb, gg, rr);
    });

    public static byte[] ReplaceColour(byte[] p, uint from, uint to, int tolerance)
    {
        var d = (byte[])p.Clone();
        for (int i = 0; i < d.Length; i += 4)
        {
            bool match = true;
            for (int c = 0; c < 4 && match; c++)
                if (Math.Abs(d[i + c] - (byte)(from >> (8 * c))) > tolerance) match = false;
            if (!match) continue;
            d[i] = (byte)to; d[i + 1] = (byte)(to >> 8); d[i + 2] = (byte)(to >> 16); d[i + 3] = (byte)(to >> 24);
        }
        return d;
    }

    /// <summary>Box blur run three times (close to Gaussian), in premultiplied alpha.</summary>
    public static byte[] Blur(byte[] p, int w, int h, int radius)
    {
        var f = new float[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            float a = p[i * 4 + 3];
            f[i * 4] = p[i * 4] * a; f[i * 4 + 1] = p[i * 4 + 1] * a; f[i * 4 + 2] = p[i * 4 + 2] * a; f[i * 4 + 3] = a;
        }
        int r = Math.Max(1, radius / 2 + (radius % 2));
        for (int pass = 0; pass < 3; pass++)
        {
            f = BoxH(f, w, h, r);
            f = BoxV(f, w, h, r);
        }
        var d = new byte[p.Length];
        for (int i = 0; i < w * h; i++)
        {
            float a = f[i * 4 + 3];
            if (a < 0.5f) continue;
            d[i * 4] = Clamp(f[i * 4] / a); d[i * 4 + 1] = Clamp(f[i * 4 + 1] / a); d[i * 4 + 2] = Clamp(f[i * 4 + 2] / a);
            d[i * 4 + 3] = Clamp(a);
        }
        return d;
    }

    private static float[] BoxH(float[] s, int w, int h, int r)
    {
        var d = new float[s.Length];
        Parallel.For(0, h, y =>
        {
            var acc = new double[4];
            int n = 2 * r + 1;
            for (int x = -r; x <= r; x++) Add(acc, s, w, h, x, y, 1);
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                for (int c = 0; c < 4; c++) d[o + c] = (float)(acc[c] / n);
                Add(acc, s, w, h, x + r + 1, y, 1);
                Add(acc, s, w, h, x - r, y, -1);
            }
        });
        return d;
    }

    private static float[] BoxV(float[] s, int w, int h, int r)
    {
        var d = new float[s.Length];
        Parallel.For(0, w, x =>
        {
            var acc = new double[4];
            int n = 2 * r + 1;
            for (int y = -r; y <= r; y++) Add(acc, s, w, h, x, y, 1);
            for (int y = 0; y < h; y++)
            {
                int o = (y * w + x) * 4;
                for (int c = 0; c < 4; c++) d[o + c] = (float)(acc[c] / n);
                Add(acc, s, w, h, x, y + r + 1, 1);
                Add(acc, s, w, h, x, y - r, -1);
            }
        });
        return d;
    }

    private static void Add(double[] acc, float[] s, int w, int h, int x, int y, int sign)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;   // outside counts as transparent
        int o = (y * w + x) * 4;
        for (int c = 0; c < 4; c++) acc[c] += sign * s[o + c];
    }

    public static byte[] Unsharp(byte[] p, int w, int h, int radius, double amount, int threshold)
    {
        var blur = Blur(p, w, h, radius);
        var d = (byte[])p.Clone();
        for (int i = 0; i < d.Length; i += 4)
        {
            if (d[i + 3] == 0) continue;
            for (int c = 0; c < 3; c++)
            {
                int diff = p[i + c] - blur[i + c];
                if (Math.Abs(diff) < threshold) continue;
                d[i + c] = Clamp(p[i + c] + diff * amount);
            }
        }
        return d;
    }

    /// <summary>A hard outline of <paramref name="colour"/> around every opaque area, outside it.</summary>
    public static byte[] Outline(byte[] p, int w, int h, int thickness, bool corners, uint colour)
    {
        var solid = new bool[w * h];
        for (int i = 0; i < w * h; i++) solid[i] = p[i * 4 + 3] > 0;
        var d = (byte[])p.Clone();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (solid[y * w + x]) continue;
                bool near = false;
                for (int dy = -thickness; dy <= thickness && !near; dy++)
                    for (int dx = -thickness; dx <= thickness && !near; dx++)
                    {
                        if (!corners && Math.Abs(dx) + Math.Abs(dy) > thickness) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h && solid[ny * w + nx]) near = true;
                    }
                if (!near) continue;
                int o = (y * w + x) * 4;
                d[o] = (byte)colour; d[o + 1] = (byte)(colour >> 8); d[o + 2] = (byte)(colour >> 16); d[o + 3] = (byte)(colour >> 24);
            }
        return d;
    }

    /// <summary>A shadow of the shape, offset and tinted, placed under the original.</summary>
    public static byte[] DropShadow(byte[] p, int w, int h, int dx, int dy, double opacity, int blur, uint colour)
    {
        var sh = new byte[p.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = x - dx, sy = y - dy;
                if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
                int a = p[(sy * w + sx) * 4 + 3];
                if (a == 0) continue;
                int o = (y * w + x) * 4;
                sh[o] = (byte)colour; sh[o + 1] = (byte)(colour >> 8); sh[o + 2] = (byte)(colour >> 16);
                sh[o + 3] = Clamp(a * opacity * ((colour >> 24) / 255.0));
            }
        if (blur > 0) sh = Blur(sh, w, h, blur);
        ImageOps.Over(sh, w, h, p, w, h, 0, 0, 255);
        return sh;
    }

    public static byte[] Pixelate(byte[] p, int w, int h, int cell)
    {
        var d = new byte[p.Length];
        for (int by = 0; by < h; by += cell)
            for (int bx = 0; bx < w; bx += cell)
            {
                double b = 0, g = 0, r = 0, a = 0; int n = 0;
                for (int y = by; y < Math.Min(h, by + cell); y++)
                    for (int x = bx; x < Math.Min(w, bx + cell); x++)
                    {
                        int o = (y * w + x) * 4; double pa = p[o + 3];
                        b += p[o] * pa; g += p[o + 1] * pa; r += p[o + 2] * pa; a += pa; n++;
                    }
                if (a <= 0) continue;
                byte B = Clamp(b / a), G = Clamp(g / a), R = Clamp(r / a), A = Clamp(a / n);
                for (int y = by; y < Math.Min(h, by + cell); y++)
                    for (int x = bx; x < Math.Min(w, bx + cell); x++)
                    {
                        int o = (y * w + x) * 4;
                        d[o] = B; d[o + 1] = G; d[o + 2] = R; d[o + 3] = A;
                    }
            }
        return d;
    }

    public static byte[] Noise(byte[] p, int amount, int seed)
    {
        var rng = new Random(seed);
        return PerPixel(p, (b, g, r) =>
        {
            int n = rng.Next(-amount, amount + 1);
            return (b + n, g + n, r + n);
        });
    }

    public static void RgbToHsl(int r, int g, int b, out double h, out double s, out double l)
    {
        double R = r / 255.0, G = g / 255.0, B = b / 255.0;
        double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
        l = (max + min) / 2;
        if (max == min) { h = 0; s = 0; return; }
        double d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max == R) h = (G - B) / d + (G < B ? 6 : 0);
        else if (max == G) h = (B - R) / d + 2;
        else h = (R - G) / d + 4;
        h /= 6;
    }

    public static void HslToRgb(double h, double s, double l, out int r, out int g, out int b)
    {
        if (s == 0) { r = g = b = (int)Math.Round(l * 255); return; }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        static double F(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1 / 2.0) return q;
            if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
            return p;
        }
        r = (int)Math.Round(F(p, q, h + 1 / 3.0) * 255);
        g = (int)Math.Round(F(p, q, h) * 255);
        b = (int)Math.Round(F(p, q, h - 1 / 3.0) * 255);
    }
}
