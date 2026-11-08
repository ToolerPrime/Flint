/* Blend modes on straight-alpha BGRA bytes.
 *
 * Formulas are from the W3C "Compositing and Blending Level 1" specification (public spec):
 *   Cs' = (1 - ab) * Cs + ab * B(Cb, Cs)        the source colour, adjusted where there is a backdrop
 *   ao  = as + ab * (1 - as)                    then ordinary source-over
 *   Co  = (as * Cs' + (1 - as) * ab * Cb) / ao
 * The separable modes work per channel; Hue, Saturation, Color and Luminosity use the spec's
 * Lum / Sat / SetLum / SetSat helpers. Add and Subtract are Flint's own (clamped sum and difference).
 *
 * Fireworks' three extra modes only ever act on what is already there (they never add coverage):
 *   Invert  the backdrop is inverted under the source's alpha (colour of the source is ignored)
 *   Tint    the backdrop moves half way towards the source colour, in proportion to the backdrop's saturation
 *           (HSV) and the source's alpha: a grey or white backdrop is left alone. Fitted to Fireworks' own blends.png
 *           (orange under blue gives the 50/50 mix; white stays white)
 *   Erase   the backdrop's alpha is reduced by the source's alpha (the source colour is ignored)
 * Nothing here touches GDI+, so results are exact bytes.
 */
namespace Flint;

public static class Blending
{
    /// <summary>Blend a canvas-sized (or smaller, placed) source onto <paramref name="d"/>.</summary>
    public static void Over(byte[] d, int dw, int dh, byte[] s, int sw, int sh, int ox, int oy, int opacity, BlendMode mode)
    {
        if (opacity <= 0) return;
        if (mode == BlendMode.Normal) { ImageOps.Over(d, dw, dh, s, sw, sh, ox, oy, opacity); return; }
        for (int y = Math.Max(0, oy); y < Math.Min(dh, oy + sh); y++)
            for (int x = Math.Max(0, ox); x < Math.Min(dw, ox + sw); x++)
            {
                int si = ((y - oy) * sw + (x - ox)) * 4, di = (y * dw + x) * 4;
                int sa = s[si + 3];
                if (opacity < 255) sa = (sa * opacity + 127) / 255;
                if (sa == 0) continue;
                Pixel(d, di, s[si + 2], s[si + 1], s[si], sa, mode);
            }
    }

    /// <summary>Combine two same-sized buffers: the source layer over the target.</summary>
    public static void Layer(byte[] d, byte[] s, BlendMode mode, int w, int h) => Over(d, w, h, s, w, h, 0, 0, 255, mode);

    /// <summary>One pixel: source (r,g,b) with alpha <paramref name="sa"/> 1..255 onto d[di..di+3] (BGRA).</summary>
    public static void Pixel(byte[] d, int di, int sr, int sg, int sb, int sa, BlendMode mode)
    {
        int ab255 = d[di + 3];
        double a_s = sa / 255.0, ab = ab255 / 255.0;
        double bb = d[di] / 255.0, bg = d[di + 1] / 255.0, br = d[di + 2] / 255.0;
        double cr = sr / 255.0, cg = sg / 255.0, cb = sb / 255.0;

        if (mode == BlendMode.Erase)
        {
            d[di + 3] = (byte)Math.Round(ab255 * (1 - a_s));
            return;
        }
        if (mode == BlendMode.Dissolve)
        {
            // Each pixel is either fully painted or untouched, with the alpha as the chance.
            uint h = (uint)di * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            if ((h & 0xFFFF) / 65536.0 >= a_s) return;
            d[di] = (byte)sb; d[di + 1] = (byte)sg; d[di + 2] = (byte)sr; d[di + 3] = 255;
            return;
        }
        if (ab255 == 0 && mode is BlendMode.Invert or BlendMode.Tint) return;   // nothing to act on
        if (mode == BlendMode.Invert)
        {
            d[di] = Byte(bb + (1 - 2 * bb) * a_s); d[di + 1] = Byte(bg + (1 - 2 * bg) * a_s); d[di + 2] = Byte(br + (1 - 2 * br) * a_s);
            return;
        }
        if (mode == BlendMode.Tint)
        {
            double mx = Math.Max(br, Math.Max(bg, bb)), mn = Math.Min(br, Math.Min(bg, bb));
            double tk = (mx <= 0 ? 0 : (mx - mn) / mx) * 0.5 * a_s;   // half the source, in proportion to how colourful the backdrop is
            d[di] = Byte(bb + (cb - bb) * tk); d[di + 1] = Byte(bg + (cg - bg) * tk); d[di + 2] = Byte(br + (cr - br) * tk);
            return;
        }
        if (ab255 == 0)
        {
            d[di] = (byte)sb; d[di + 1] = (byte)sg; d[di + 2] = (byte)sr; d[di + 3] = (byte)sa;
            return;
        }
        Mix(mode, br, bg, bb, cr, cg, cb, out double mr, out double mg, out double mb);
        double ar = (1 - ab) * cr + ab * mr, ag = (1 - ab) * cg + ab * mg, ab2 = (1 - ab) * cb + ab * mb;
        double ao = a_s + ab * (1 - a_s);
        double k = (1 - a_s) * ab;
        d[di] = Byte((a_s * ab2 + k * bb) / ao);
        d[di + 1] = Byte((a_s * ag + k * bg) / ao);
        d[di + 2] = Byte((a_s * ar + k * br) / ao);
        d[di + 3] = Byte(ao);
    }

    private static byte Byte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);

    /// <summary>B(Cb, Cs) for opaque colours, all channels 0..1.</summary>
    public static void Mix(BlendMode m, double br, double bg, double bb, double sr, double sg, double sb, out double r, out double g, out double b)
    {
        switch (m)
        {
            case BlendMode.DarkerColor:
                (r, g, b) = Lum(br, bg, bb) <= Lum(sr, sg, sb) ? (br, bg, bb) : (sr, sg, sb); return;
            case BlendMode.LighterColor:
                (r, g, b) = Lum(br, bg, bb) >= Lum(sr, sg, sb) ? (br, bg, bb) : (sr, sg, sb); return;
            case BlendMode.Hue:
                (r, g, b) = SetLum(SetSat((sr, sg, sb), Sat(br, bg, bb)), Lum(br, bg, bb)); return;
            case BlendMode.Saturation:
                (r, g, b) = SetLum(SetSat((br, bg, bb), Sat(sr, sg, sb)), Lum(br, bg, bb)); return;
            case BlendMode.Color:
                (r, g, b) = SetLum((sr, sg, sb), Lum(br, bg, bb)); return;
            case BlendMode.Luminosity:
                (r, g, b) = SetLum((br, bg, bb), Lum(sr, sg, sb)); return;
        }
        r = Sep(m, br, sr); g = Sep(m, bg, sg); b = Sep(m, bb, sb);
    }

    private static double Sep(BlendMode m, double cb, double cs) => m switch
    {
        BlendMode.Multiply => cb * cs,
        BlendMode.Screen => cb + cs - cb * cs,
        BlendMode.Overlay => HardLight(cs, cb),
        BlendMode.Darken => Math.Min(cb, cs),
        BlendMode.Lighten => Math.Max(cb, cs),
        BlendMode.ColorDodge => cb == 0 ? 0 : cs >= 1 ? 1 : Math.Min(1, cb / (1 - cs)),
        BlendMode.ColorBurn => cb >= 1 ? 1 : cs <= 0 ? 0 : 1 - Math.Min(1, (1 - cb) / cs),
        BlendMode.HardLight => HardLight(cb, cs),
        BlendMode.SoftLight => SoftLight(cb, cs),
        BlendMode.Difference => Math.Abs(cb - cs),
        BlendMode.Exclusion => cb + cs - 2 * cb * cs,
        BlendMode.Add => Math.Min(1, cb + cs),
        BlendMode.Subtract => Math.Max(0, cb - cs),
        BlendMode.LinearBurn => Math.Max(0, cb + cs - 1),
        BlendMode.VividLight => cs <= 0.5 ? Burn(cb, 2 * cs) : Dodge(cb, 2 * cs - 1),
        BlendMode.LinearLight => Math.Clamp(cb + 2 * cs - 1, 0, 1),
        BlendMode.PinLight => cs <= 0.5 ? Math.Min(cb, 2 * cs) : Math.Max(cb, 2 * cs - 1),
        BlendMode.HardMix => cb + cs >= 1 ? 1 : 0,
        BlendMode.Divide => cs <= 0 ? (cb > 0 ? 1 : 0) : Math.Min(1, cb / cs),
        _ => cs,
    };

    private static double Dodge(double cb, double cs) => cb == 0 ? 0 : cs >= 1 ? 1 : Math.Min(1, cb / (1 - cs));
    private static double Burn(double cb, double cs) => cb >= 1 ? 1 : cs <= 0 ? 0 : 1 - Math.Min(1, (1 - cb) / cs);

    private static double HardLight(double cb, double cs) =>
        cs <= 0.5 ? cb * 2 * cs : cb + (2 * cs - 1) - cb * (2 * cs - 1);

    private static double SoftLight(double cb, double cs)
    {
        if (cs <= 0.5) return cb - (1 - 2 * cs) * cb * (1 - cb);
        double d = cb <= 0.25 ? ((16 * cb - 12) * cb + 4) * cb : Math.Sqrt(cb);
        return cb + (2 * cs - 1) * (d - cb);
    }

    private static double Lum(double r, double g, double b) => 0.3 * r + 0.59 * g + 0.11 * b;
    private static double Sat(double r, double g, double b) => Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));

    private static (double, double, double) ClipColor((double r, double g, double b) c)
    {
        double l = Lum(c.r, c.g, c.b), n = Math.Min(c.r, Math.Min(c.g, c.b)), x = Math.Max(c.r, Math.Max(c.g, c.b));
        if (n < 0) { c.r = l + (c.r - l) * l / (l - n); c.g = l + (c.g - l) * l / (l - n); c.b = l + (c.b - l) * l / (l - n); }
        if (x > 1) { c.r = l + (c.r - l) * (1 - l) / (x - l); c.g = l + (c.g - l) * (1 - l) / (x - l); c.b = l + (c.b - l) * (1 - l) / (x - l); }
        return c;
    }

    private static (double, double, double) SetLum((double r, double g, double b) c, double l)
    {
        double d = l - Lum(c.r, c.g, c.b);
        return ClipColor((c.r + d, c.g + d, c.b + d));
    }

    private static (double, double, double) SetSat((double r, double g, double b) c, double s)
    {
        double mx = Math.Max(c.r, Math.Max(c.g, c.b)), mn = Math.Min(c.r, Math.Min(c.g, c.b));
        if (mx <= mn) return (0, 0, 0);
        double k = s / (mx - mn);
        return ((c.r - mn) * k, (c.g - mn) * k, (c.b - mn) * k);
    }
}
