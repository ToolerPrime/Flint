/* Geometric transforms on raw BGRA bytes.
 *
 * Every transform maps a source bitmap through an affine matrix into a new
 * bitmap, sampling each destination pixel centre back in the source. Three
 * samplers:
 *
 *   Nearest    exact source colours; perfect for 90° turns, flips, whole-number
 *              scales and moves.
 *   RotSprite  pixel-art rotation: the source is upscaled 8× with Scale2x
 *              (which follows edges instead of blurring them), then sampled.
 *              Output still uses only the source's own colours, but diagonal
 *              edges stay clean instead of going jagged.
 *   Smooth     bilinear, in premultiplied alpha so edges do not darken.
 *
 * Objects keep their original pixels and the accumulated matrix (see
 * RasterObject), so rotating 10° five times re-renders once from the original
 * at 50° instead of degrading five times.
 */
namespace Flint;

public enum Resample { Nearest, RotSprite, Smooth }

/// <summary>x' = A·x + C·y + Tx, y' = B·x + D·y + Ty.</summary>
public readonly record struct Affine(double A, double B, double C, double D, double Tx, double Ty)
{
    public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

    public static Affine Translate(double x, double y) => new(1, 0, 0, 1, x, y);
    public static Affine Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);
    public static Affine Skew(double kxDeg, double kyDeg) =>
        new(1, Math.Tan(kyDeg * Math.PI / 180), Math.Tan(kxDeg * Math.PI / 180), 1, 0, 0);

    /// <summary>Clockwise on screen (y points down), in degrees.</summary>
    public static Affine Rotate(double deg)
    {
        // Exact values for quarter turns, so 90° is pixel-exact.
        double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        double q = deg / 90;
        if (Math.Abs(q - Math.Round(q)) < 1e-12)
        {
            int k = (((int)Math.Round(q)) % 4 + 4) % 4;
            (c, s) = k switch { 0 => (1d, 0d), 1 => (0d, 1d), 2 => (-1d, 0d), _ => (0d, -1d) };
        }
        return new(c, s, -s, c, 0, 0);
    }

    /// <summary>This, then <paramref name="next"/>.</summary>
    public Affine Then(Affine next) => new Affine(
        next.A * A + next.C * B,
        next.B * A + next.D * B,
        next.A * C + next.C * D,
        next.B * C + next.D * D,
        next.A * Tx + next.C * Ty + next.Tx,
        next.B * Tx + next.D * Ty + next.Ty).Snapped();

    /// <summary>The same transform applied about a point instead of the origin.</summary>
    public Affine About(double cx, double cy) => Translate(-cx, -cy).Then(this).Then(Translate(cx, cy));

    public Affine Inverse()
    {
        double det = A * D - B * C;
        if (Math.Abs(det) < 1e-12) throw new InvalidOperationException("transform squashes the image flat");
        double ia = D / det, ib = -B / det, ic = -C / det, id = A / det;
        return new Affine(ia, ib, ic, id, -(ia * Tx + ic * Ty), -(ib * Tx + id * Ty)).Snapped();
    }

    public (double x, double y) Apply(double x, double y) => (A * x + C * y + Tx, B * x + D * y + Ty);

    public bool IsIntegerTranslation =>
        A == 1 && B == 0 && C == 0 && D == 1 && Tx == Math.Round(Tx) && Ty == Math.Round(Ty);

    public bool IsIdentity => this == Identity;

    /// <summary>
    /// Round entries that are within floating noise of a whole number or
    /// half, so rotating +30° then −30° lands back on exactly the identity.
    /// </summary>
    private Affine Snapped()
    {
        static double S(double v)
        {
            double r = Math.Round(v * 2) / 2;
            return Math.Abs(v - r) < 1e-9 ? r : v;
        }
        return new(S(A), S(B), S(C), S(D), S(Tx), S(Ty));
    }

    public double[] ToArray() => new[] { A, B, C, D, Tx, Ty };
    public static Affine FromArray(double[] v) => new(v[0], v[1], v[2], v[3], v[4], v[5]);
}

public static class Transform
{
    /// <summary>Largest output we will allocate, in pixels.</summary>
    public const long MaxPixels = 64L * 1024 * 1024;
    /// <summary>RotSprite upscales 8× in each direction; above this source size it falls back to Nearest.</summary>
    public const long RotSpriteMaxSource = 1024L * 1024;

    /// <summary>
    /// Integer bounds of a sw×sh rectangle through <paramref name="m"/>, checked in
    /// doubles before any cast so a huge scale throws instead of overflowing.
    /// </summary>
    public static (int x0, int y0, int w, int h) Bounds(Affine m, double sw, double sh)
    {
        var pts = new[] { m.Apply(0, 0), m.Apply(sw, 0), m.Apply(0, sh), m.Apply(sw, sh) };
        double minX = Math.Floor(pts.Min(p => p.x) + 1e-9), maxX = Math.Ceiling(pts.Max(p => p.x) - 1e-9);
        double minY = Math.Floor(pts.Min(p => p.y) + 1e-9), maxY = Math.Ceiling(pts.Max(p => p.y) - 1e-9);
        double fw = Math.Max(1, maxX - minX), fh = Math.Max(1, maxY - minY);
        if (double.IsNaN(fw) || double.IsNaN(fh) || fw > int.MaxValue || fh > int.MaxValue || fw * fh > MaxPixels
            || Math.Abs(minX) > int.MaxValue || Math.Abs(minY) > int.MaxValue)
            throw new InvalidOperationException($"the result would be {fw:0}×{fh:0} pixels, too large");
        return ((int)minX, (int)minY, (int)fw, (int)fh);
    }

    /// <summary>
    /// Render <paramref name="src"/> (sw×sh, pixel corners at integer
    /// coordinates) through <paramref name="m"/>. Returns the pixels and the
    /// integer position of their top-left corner in destination space.
    /// </summary>
    public static (byte[] px, int x, int y, int w, int h, Resample used) Render(
        byte[] src, int sw, int sh, Affine m, Resample mode, Func<byte[]>? up8 = null)
    {
        var (x0, y0, w, h) = Bounds(m, sw, sh);

        // Fast exact path: whole-pixel moves.
        if (m.IsIntegerTranslation) return ((byte[])src.Clone(), (int)m.Tx, (int)m.Ty, sw, sh, Resample.Nearest);

        if (mode == Resample.RotSprite && (long)sw * sh > RotSpriteMaxSource) mode = Resample.Nearest;

        var inv = m.Inverse();
        var dst = new byte[w * h * 4];
        byte[]? up = mode == Resample.RotSprite ? (up8?.Invoke() ?? Up8(src, sw, sh)) : null;
        int uw = sw * 8, uh = sh * 8;

        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                var (u, v) = inv.Apply(x0 + x + 0.5, y0 + y + 0.5);
                int o = (y * w + x) * 4;
                switch (mode)
                {
                    case Resample.Nearest:
                    {
                        int sx = (int)Math.Floor(u + 1e-9), sy = (int)Math.Floor(v + 1e-9);
                        if (sx < 0 || sy < 0 || sx >= sw || sy >= sh) break;
                        Buffer.BlockCopy(src, (sy * sw + sx) * 4, dst, o, 4);
                        break;
                    }
                    case Resample.RotSprite:
                    {
                        int sx = (int)Math.Floor(u * 8 + 1e-9), sy = (int)Math.Floor(v * 8 + 1e-9);
                        if (sx < 0 || sy < 0 || sx >= uw || sy >= uh) break;
                        Buffer.BlockCopy(up!, (sy * uw + sx) * 4, dst, o, 4);
                        break;
                    }
                    default:
                        Bilinear(src, sw, sh, u - 0.5, v - 0.5, dst, o);
                        break;
                }
            }
        });
        return (dst, x0, y0, w, h, mode);
    }

    private static void Bilinear(byte[] s, int w, int h, double u, double v, byte[] d, int o)
    {
        int ix = (int)Math.Floor(u), iy = (int)Math.Floor(v);
        double fx = u - ix, fy = v - iy;
        double b = 0, g = 0, r = 0, a = 0;
        for (int k = 0; k < 4; k++)
        {
            int sx = ix + (k & 1), sy = iy + (k >> 1);
            double wgt = ((k & 1) == 1 ? fx : 1 - fx) * ((k >> 1) == 1 ? fy : 1 - fy);
            if (wgt == 0 || sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
            int p = (sy * w + sx) * 4;
            double pa = s[p + 3] * wgt;
            a += pa;
            b += s[p] * pa; g += s[p + 1] * pa; r += s[p + 2] * pa;
        }
        if (a < 0.5) return;
        d[o] = (byte)Math.Clamp(Math.Round(b / a), 0, 255);
        d[o + 1] = (byte)Math.Clamp(Math.Round(g / a), 0, 255);
        d[o + 2] = (byte)Math.Clamp(Math.Round(r / a), 0, 255);
        d[o + 3] = (byte)Math.Clamp(Math.Round(a), 0, 255);
    }

    /// <summary>Three rounds of Scale2x: 8× larger, edges followed, colours unchanged.</summary>
    public static byte[] Up8(byte[] src, int w, int h)
    {
        var a = Scale2x(src, w, h);
        var b = Scale2x(a, w * 2, h * 2);
        return Scale2x(b, w * 4, h * 4);
    }

    /// <summary>EPX / Scale2x: doubles size, copying a neighbour's colour where two neighbours agree on an edge.</summary>
    public static byte[] Scale2x(byte[] src, int w, int h)
    {
        int W = w * 2;
        var dst = new byte[W * h * 2 * 4];
        uint Px(int x, int y) => BitConverter.ToUInt32(src, ((Math.Clamp(y, 0, h - 1) * w) + Math.Clamp(x, 0, w - 1)) * 4);
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                uint p = Px(x, y), A = Px(x, y - 1), B = Px(x + 1, y), C = Px(x - 1, y), D = Px(x, y + 1);
                uint e0 = p, e1 = p, e2 = p, e3 = p;
                if (A != D && C != B)
                {
                    if (C == A) e0 = A;
                    if (A == B) e1 = B;
                    if (D == C) e2 = C;
                    if (B == D) e3 = D;
                }
                Put(dst, W, x * 2, y * 2, e0);
                Put(dst, W, x * 2 + 1, y * 2, e1);
                Put(dst, W, x * 2, y * 2 + 1, e2);
                Put(dst, W, x * 2 + 1, y * 2 + 1, e3);
            }
        });
        return dst;
    }

    private static void Put(byte[] d, int w, int x, int y, uint v)
    {
        int o = (y * w + x) * 4;
        d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); d[o + 2] = (byte)(v >> 16); d[o + 3] = (byte)(v >> 24);
    }

    /// <summary>Quarter turns clockwise, exact.</summary>
    public static (byte[] px, int w, int h) Rotate90(byte[] src, int w, int h, int quarters)
    {
        int k = ((quarters % 4) + 4) % 4;
        if (k == 0) return ((byte[])src.Clone(), w, h);
        var r = Render(src, w, h, Affine.Rotate(90 * k), Resample.Nearest);
        return (r.px, r.w, r.h);
    }
}
