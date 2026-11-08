/* A pixel selection: one bit per canvas pixel.
 *
 * Rectangles, ellipses, lasso polygons and magic-wand regions all become the
 * same mask, so every tool clips the same way and selections can be added to,
 * subtracted from and intersected. Hard-edged on purpose: sprite work wants
 * every pixel either in or out.
 */
namespace Flint;

public enum SelectMode { Replace, Add, Subtract, Intersect }

public sealed class Selection
{
    public readonly int W, H;
    private readonly bool[] _m;
    public Rectangle Bounds { get; private set; }

    public Selection(int w, int h) { W = w; H = h; _m = new bool[w * h]; }

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && _m[y * W + x];
    public bool IsEmpty => Bounds.IsEmpty;
    public int Count => _m.Count(b => b);

    public static Selection FromRect(int w, int h, Rectangle r)
    {
        var s = new Selection(w, h);
        r.Intersect(new Rectangle(0, 0, w, h));
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++) s._m[y * w + x] = true;
        s.Recalc();
        return s;
    }

    /// <summary>Pixels whose centres fall inside the ellipse inscribed in r.</summary>
    public static Selection FromEllipse(int w, int h, Rectangle r)
    {
        var s = new Selection(w, h);
        double cx = r.X + r.Width / 2.0, cy = r.Y + r.Height / 2.0, rx = r.Width / 2.0, ry = r.Height / 2.0;
        if (rx > 0 && ry > 0)
            for (int y = Math.Max(0, r.Top); y < Math.Min(h, r.Bottom); y++)
                for (int x = Math.Max(0, r.Left); x < Math.Min(w, r.Right); x++)
                {
                    double dx = (x + 0.5 - cx) / rx, dy = (y + 0.5 - cy) / ry;
                    if (dx * dx + dy * dy <= 1) s._m[y * w + x] = true;
                }
        s.Recalc();
        return s;
    }

    /// <summary>Lasso: pixels whose centres are inside the polygon (even-odd rule).</summary>
    public static Selection FromPolygon(int w, int h, IReadOnlyList<Point> pts)
    {
        var s = new Selection(w, h);
        if (pts.Count < 3) return s;
        var xs = new List<double>();
        for (int y = 0; y < h; y++)
        {
            double py = y + 0.5;
            xs.Clear();
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                double ay = a.Y + 0.5, by = b.Y + 0.5;
                if ((ay <= py && by > py) || (by <= py && ay > py))
                    xs.Add(a.X + 0.5 + (py - ay) / (by - ay) * (b.X - a.X));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
                for (int x = Math.Max(0, (int)Math.Ceiling(xs[k] - 0.5)); x < Math.Min(w, (int)Math.Ceiling(xs[k + 1] - 0.5)); x++)
                    s._m[y * w + x] = true;
        }
        // The outline itself counts, so a thin lasso stroke still selects what it touches.
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % pts.Count];
            int n = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            for (int t = 0; t <= n; t++)
            {
                int x = n == 0 ? a.X : a.X + (b.X - a.X) * t / n, y = n == 0 ? a.Y : a.Y + (b.Y - a.Y) * t / n;
                if (x >= 0 && y >= 0 && x < w && y < h) s._m[y * w + x] = true;
            }
        }
        s.Recalc();
        return s;
    }

    /// <summary>
    /// Magic wand on BGRA pixels: everything within <paramref name="tolerance"/>
    /// of the clicked colour (per channel, alpha included), touching it or
    /// anywhere when <paramref name="contiguous"/> is false.
    /// </summary>
    public static Selection Wand(byte[] px, int w, int h, int x, int y, int tolerance, bool contiguous)
    {
        var s = new Selection(w, h);
        if (x < 0 || y < 0 || x >= w || y >= h) return s;
        int o0 = (y * w + x) * 4;
        bool Like(int i)
        {
            int o = i * 4;
            // Fully transparent pixels match each other whatever colour they hide.
            if (px[o + 3] == 0 && px[o0 + 3] == 0) return true;
            for (int c = 0; c < 4; c++) if (Math.Abs(px[o + c] - px[o0 + c]) > tolerance) return false;
            return true;
        }
        if (!contiguous)
        {
            for (int i = 0; i < w * h; i++) if (Like(i)) s._m[i] = true;
        }
        else
        {
            var stack = new Stack<int>();
            stack.Push(y * w + x);
            s._m[y * w + x] = true;
            while (stack.Count > 0)
            {
                int p = stack.Pop(), px0 = p % w, py0 = p / w;
                void Try(int nx, int ny)
                {
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                    int q = ny * w + nx;
                    if (s._m[q] || !Like(q)) return;
                    s._m[q] = true;
                    stack.Push(q);
                }
                Try(px0 + 1, py0); Try(px0 - 1, py0); Try(px0, py0 + 1); Try(px0, py0 - 1);
            }
        }
        s.Recalc();
        return s;
    }

    /// <summary>Opaque pixels of a layer: the "select layer content" command.</summary>
    public static Selection FromAlpha(byte[] px, int w, int h)
    {
        var s = new Selection(w, h);
        for (int i = 0; i < w * h; i++) s._m[i] = px[i * 4 + 3] > 0;
        s.Recalc();
        return s;
    }

    /// <summary>Combine this new shape with the existing selection <paramref name="other"/>.</summary>
    public Selection Combine(Selection? other, SelectMode mode)
    {
        if (mode == SelectMode.Replace || other == null) return mode == SelectMode.Subtract ? new Selection(W, H) : this;
        var s = new Selection(W, H);
        for (int i = 0; i < _m.Length; i++)
            s._m[i] = mode switch
            {
                SelectMode.Add => other._m[i] || _m[i],
                SelectMode.Subtract => other._m[i] && !_m[i],
                _ => other._m[i] && _m[i],
            };
        s.Recalc();
        return s;
    }

    public Selection Invert()
    {
        var s = new Selection(W, H);
        for (int i = 0; i < _m.Length; i++) s._m[i] = !_m[i];
        s.Recalc();
        return s;
    }

    /// <summary>Grow (positive) or shrink (negative) by whole pixels, square-wise.</summary>
    public Selection Expand(int by)
    {
        var cur = this;
        for (int step = 0; step < Math.Abs(by); step++)
        {
            var s = new Selection(W, H);
            bool grow = by > 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    bool v = cur._m[y * W + x];
                    if (grow ? !v : v)
                    {
                        bool flip = false;
                        for (int dy = -1; dy <= 1 && !flip; dy++)
                            for (int dx = -1; dx <= 1 && !flip; dx++)
                            {
                                int nx = x + dx, ny = y + dy;
                                bool n = nx >= 0 && ny >= 0 && nx < W && ny < H && cur._m[ny * W + nx];
                                if (grow ? n : !n) flip = true;
                            }
                        v = grow ? flip : !flip;
                    }
                    s._m[y * W + x] = v;
                }
            s.Recalc();
            cur = s;
        }
        return cur;
    }

    /// <summary>A copy moved by dx, dy (pixels pushed off the canvas are lost).</summary>
    public Selection Offset(int dx, int dy)
    {
        var s = new Selection(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (_m[y * W + x] && x + dx >= 0 && y + dy >= 0 && x + dx < W && y + dy < H)
                    s._m[(y + dy) * W + x + dx] = true;
        s.Recalc();
        return s;
    }

    private void Recalc()
    {
        int minX = W, minY = H, maxX = -1, maxY = -1;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (_m[y * W + x])
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        Bounds = maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        _edges = null;
    }

    private List<(Point a, Point b)>? _edges;

    /// <summary>The outline as unit-grid line segments, merged into runs, for drawing marching ants.</summary>
    public List<(Point a, Point b)> Edges()
    {
        if (_edges != null) return _edges;
        var e = new List<(Point, Point)>();
        var b = Bounds;
        if (b.IsEmpty) return _edges = e;
        // Horizontal edges between row y-1 and y.
        for (int y = b.Top; y <= b.Bottom; y++)
        {
            int run = -1;
            for (int x = b.Left; x <= b.Right; x++)
            {
                bool edge = x < b.Right && Contains(x, y) != Contains(x, y - 1);
                if (edge && run < 0) run = x;
                if (!edge && run >= 0) { e.Add((new Point(run, y), new Point(x, y))); run = -1; }
            }
        }
        for (int x = b.Left; x <= b.Right; x++)
        {
            int run = -1;
            for (int y = b.Top; y <= b.Bottom; y++)
            {
                bool edge = y < b.Bottom && Contains(x, y) != Contains(x - 1, y);
                if (edge && run < 0) run = y;
                if (!edge && run >= 0) { e.Add((new Point(x, run), new Point(x, y))); run = -1; }
            }
        }
        return _edges = e;
    }
}
