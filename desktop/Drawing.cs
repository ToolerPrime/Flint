/* Pixel-level drawing primitives.
 *
 * These work on a raw BGRA buffer rather than GDI+ so that every tool lands on
 * exact pixel boundaries. GDI+ antialiases and offsets by half a pixel, which
 * is wrong for sprite work: a one-pixel pencil line must set exactly the
 * pixels Bresenham names, with no soft edges.
 */
namespace Flint;

public enum Tool
{
    Pencil, Eraser, Fill, Line, Rectangle, Ellipse, Marquee, Eyedropper, Pan, Pivot, Hitbox,
    Move, Transform, Crop, EllipseSelect, Lasso, Wand, Gradient, Text, Clone, ObjectSelect,
}

public sealed class Surface
{
    public readonly byte[] Px;   // BGRA
    public readonly int W, H;

    public Surface(byte[] px, int w, int h) { Px = px; W = w; H = h; }

    public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

    public uint Get(int x, int y)
    {
        if (!In(x, y)) return 0;
        int o = (y * W + x) * 4;
        return (uint)(Px[o] | (Px[o + 1] << 8) | (Px[o + 2] << 16) | (Px[o + 3] << 24));
    }

    public void Set(int x, int y, uint bgra, Selection? clip = null)
    {
        if (!In(x, y)) return;
        if (clip != null && !clip.Contains(x, y)) return;
        int o = (y * W + x) * 4;
        Px[o] = (byte)bgra;
        Px[o + 1] = (byte)(bgra >> 8);
        Px[o + 2] = (byte)(bgra >> 16);
        Px[o + 3] = (byte)(bgra >> 24);
    }

    public static uint Pack(Color c) =>
        (uint)(c.B | (c.G << 8) | (c.R << 16) | (c.A << 24));

    public static Color Unpack(uint v) => Color.FromArgb(
        (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
}

public static class Draw
{
    /// <summary>A square nib, so size 1 is exactly one pixel.</summary>
    public static void Dot(Surface s, int x, int y, int size, uint colour, Selection? clip)
    {
        if (size <= 1) { s.Set(x, y, colour, clip); return; }
        int half = size / 2;
        for (int dy = 0; dy < size; dy++)
            for (int dx = 0; dx < size; dx++)
                s.Set(x - half + dx, y - half + dy, colour, clip);
    }

    /// <summary>Bresenham, so the stroke is identical in every direction.</summary>
    public static void Line(Surface s, int x0, int y0, int x1, int y1, int size, uint colour, Selection? clip)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Dot(s, x0, y0, size, colour, clip);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public static void Rect(Surface s, Rectangle r, int size, uint colour, bool filled, Selection? clip)
    {
        r = Normalise(r);
        if (filled)
        {
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++)
                    s.Set(x, y, colour, clip);
            return;
        }
        Line(s, r.Left, r.Top, r.Right - 1, r.Top, size, colour, clip);
        Line(s, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1, size, colour, clip);
        Line(s, r.Left, r.Top, r.Left, r.Bottom - 1, size, colour, clip);
        Line(s, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1, size, colour, clip);
    }

    /// <summary>Midpoint ellipse, inscribed in the rectangle.</summary>
    public static void Ellipse(Surface s, Rectangle r, int size, uint colour, bool filled, Selection? clip)
    {
        r = Normalise(r);
        if (r.Width < 1 || r.Height < 1) return;

        // Work in a doubled space so odd and even spans both land correctly.
        int a = r.Width - 1, b = r.Height - 1;
        int x0 = r.Left, y0 = r.Top;
        double rx = a / 2.0, ry = b / 2.0;
        double cx = x0 + rx, cy = y0 + ry;

        if (filled)
        {
            for (int y = r.Top; y <= r.Bottom - 1; y++)
            {
                double ddy = (y - cy) / (ry == 0 ? 1 : ry);
                double inside = 1 - ddy * ddy;
                if (inside < 0) continue;
                double span = rx * Math.Sqrt(inside);
                int xa = (int)Math.Round(cx - span), xb = (int)Math.Round(cx + span);
                for (int x = xa; x <= xb; x++) s.Set(x, y, colour, clip);
            }
            return;
        }

        // Outline: sample each row and each column so there are no gaps on
        // steep sections, which a row-only sweep would leave.
        for (int y = r.Top; y <= r.Bottom - 1; y++)
        {
            double ddy = (y - cy) / (ry == 0 ? 1 : ry);
            double inside = 1 - ddy * ddy;
            if (inside < 0) continue;
            double span = rx * Math.Sqrt(inside);
            Dot(s, (int)Math.Round(cx - span), y, size, colour, clip);
            Dot(s, (int)Math.Round(cx + span), y, size, colour, clip);
        }
        for (int x = r.Left; x <= r.Right - 1; x++)
        {
            double ddx = (x - cx) / (rx == 0 ? 1 : rx);
            double inside = 1 - ddx * ddx;
            if (inside < 0) continue;
            double span = ry * Math.Sqrt(inside);
            Dot(s, x, (int)Math.Round(cy - span), size, colour, clip);
            Dot(s, x, (int)Math.Round(cy + span), size, colour, clip);
        }
    }

    /// <summary>
    /// Scanline flood fill with tolerance. Iterative, so a large region cannot
    /// blow the stack the way the obvious recursive version does.
    /// </summary>
    public static int Fill(Surface s, int x, int y, uint colour, int tolerance, Selection? clip)
    {
        if (!s.In(x, y)) return 0;
        var area = clip?.Bounds ?? new Rectangle(0, 0, s.W, s.H);
        if (!area.Contains(x, y)) return 0;

        uint target = s.Get(x, y);
        if (target == colour && tolerance == 0) return 0;

        int limit = tolerance * tolerance * 4;
        bool Match(int px, int py)
        {
            if (!area.Contains(px, py) || (clip != null && !clip.Contains(px, py))) return false;
            uint v = s.Get(px, py);
            if (v == colour) return false;           // already filled, stop
            if (tolerance == 0) return v == target;
            int db = (int)(v & 255) - (int)(target & 255);
            int dg = (int)((v >> 8) & 255) - (int)((target >> 8) & 255);
            int dr = (int)((v >> 16) & 255) - (int)((target >> 16) & 255);
            int da = (int)((v >> 24) & 255) - (int)((target >> 24) & 255);
            return dr * dr + dg * dg + db * db + da * da <= limit;
        }

        int painted = 0;
        var stack = new Stack<(int x, int y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (sx, sy) = stack.Pop();
            if (!Match(sx, sy)) continue;

            int left = sx, right = sx;
            while (Match(left - 1, sy)) left--;
            while (Match(right + 1, sy)) right++;

            for (int i = left; i <= right; i++)
            {
                s.Set(i, sy, colour, clip);
                painted++;
            }
            for (int i = left; i <= right; i++)
            {
                if (Match(i, sy - 1)) stack.Push((i, sy - 1));
                if (Match(i, sy + 1)) stack.Push((i, sy + 1));
            }
        }
        return painted;
    }

    /// <summary>
    /// Linear or radial gradient from c0 at a to c1 at b, laid over what is
    /// there (straight-alpha "over"), inside the selection.
    /// </summary>
    public static void Gradient(Surface s, Point a, Point b, uint c0, uint c1, bool radial, Selection? clip)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        double len = Math.Sqrt(len2);
        for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                if (clip != null && !clip.Contains(x, y)) continue;
                double t;
                if (len2 < 1e-9) t = 0;
                else if (radial) t = Math.Sqrt((x - a.X) * (x - a.X) + (y - a.Y) * (y - a.Y)) / len;
                else t = ((x - a.X) * dx + (y - a.Y) * dy) / len2;
                t = Math.Clamp(t, 0, 1);
                uint c = Lerp(c0, c1, t);
                int o = (y * s.W + x) * 4;
                Over(s.Px, o, c);
            }
    }

    /// <summary>
    /// Any gradient kind from c0 at the start to c1 at the end, laid over what is there. a is the centre/start, b the end of
    /// the first axis; the second axis is derived (see VGradient). Linear and Circular give the same bytes as the overload above.
    /// </summary>
    public static void Gradient(Surface s, Point a, Point b, uint c0, uint c1, GradientKind kind, Selection? clip)
    {
        var g = new VGradient { Kind = kind, X0 = a.X, Y0 = a.Y, X1 = b.X, Y1 = b.Y, X2 = a.X, Y2 = a.Y };
        for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                if (clip != null && !clip.Contains(x, y)) continue;
                Over(s.Px, (y * s.W + x) * 4, Lerp(c0, c1, g.T(x, y)));
            }
    }

    private static uint Lerp(uint c0, uint c1, double t)
    {
        double a0 = (c0 >> 24) / 255.0, a1 = (c1 >> 24) / 255.0;
        double a = a0 + (a1 - a0) * t;
        if (a <= 0) return 0;
        uint r = 0;
        for (int sh = 0; sh < 24; sh += 8)
        {
            // Interpolate premultiplied so a fade to clear keeps its colour.
            double v = (((c0 >> sh) & 255) * a0 * (1 - t) + ((c1 >> sh) & 255) * a1 * t) / a;
            r |= (uint)Math.Clamp((int)Math.Round(v), 0, 255) << sh;
        }
        return r | ((uint)Math.Clamp((int)Math.Round(a * 255), 0, 255) << 24);
    }

    private static void Over(byte[] d, int o, uint c)
    {
        int sa = (int)(c >> 24);
        if (sa == 0) return;
        if (sa == 255) { d[o] = (byte)c; d[o + 1] = (byte)(c >> 8); d[o + 2] = (byte)(c >> 16); d[o + 3] = 255; return; }
        int da = d[o + 3];
        int oa = sa + da * (255 - sa) / 255;
        for (int k = 0; k < 3; k++)
        {
            int sc = (int)((c >> (8 * k)) & 255);
            d[o + k] = (byte)((sc * sa + d[o + k] * da * (255 - sa) / 255) / Math.Max(1, oa));
        }
        d[o + 3] = (byte)oa;
    }

    /// <summary>Clone stamp: copy a brush-sized square from the stroke's starting pixels at an offset.</summary>
    public static void CloneDab(Surface s, byte[] source, int x, int y, int ox, int oy, int size, Selection? clip)
    {
        int half = size / 2;
        for (int dy = 0; dy < Math.Max(1, size); dy++)
            for (int dx = 0; dx < Math.Max(1, size); dx++)
            {
                int tx = x - half + dx, ty = y - half + dy, sx = tx + ox, sy = ty + oy;
                if (!s.In(tx, ty) || !s.In(sx, sy)) continue;
                if (clip != null && !clip.Contains(tx, ty)) continue;
                Buffer.BlockCopy(source, (sy * s.W + sx) * 4, s.Px, (ty * s.W + tx) * 4, 4);
            }
    }

    public static Rectangle Normalise(Rectangle r)
    {
        int x = Math.Min(r.Left, r.Right), y = Math.Min(r.Top, r.Bottom);
        return new Rectangle(x, y, Math.Abs(r.Width), Math.Abs(r.Height));
    }

    public static Rectangle FromPoints(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);
}
