/* Trace bitmap: pixels to vector shapes, and back to pixels at any matrix.
 *
 * Tracing finds each colour's pixels, takes their boundary along the pixel-edge
 * grid (outer outlines and holes), and fills with the even-odd rule. With the
 * default options this is lossless: rendering the art at the identity matrix
 * gives back the exact original bytes. Smoothing then simplifies and rounds
 * the outlines so the art scales and rotates crisply.
 *
 * VectorArt is immutable after Trace: undo snapshots and objects share it.
 */
using System.Globalization;
using System.Text;

namespace Flint;

public readonly record struct VPt(double X, double Y);

/// <summary>All the pixels of one colour: closed polygons filled even-odd.</summary>
public sealed class VShape
{
    /// <summary>BGRA packed like Surface.Pack.</summary>
    public uint Colour;
    public List<List<VPt>> Contours = new();
    /// <summary>When set, the colour comes from this gradient at each sample (Colour is then only the flat fallback, e.g. for SVG).</summary>
    public VGradient? Gradient;
}

/// <summary>Stored by number in .flint files: new kinds are only ever appended.</summary>
public enum GradientKind { Linear, Circular, Elliptical, Rectangle, Cone, Starburst, Bars, Ripples, Waves, Satin, Folds, Diamond, Pinch }

/// <summary>
/// A gradient fill in the art's own space. Stops are straight-alpha BGRA (packed like Surface.Pack) at positions
/// 0..1, interpolated in premultiplied space. Points: P0 start/centre, P1 end (linear: end of the ramp; circular:
/// radius = |P1-P0|; elliptical: first radius vector), P2 the second radius vector's end (elliptical only).
/// Immutable after creation.
/// </summary>
public sealed class VGradient
{
    public GradientKind Kind;
    public double[] Pos = Array.Empty<double>();
    public uint[] Col = Array.Empty<uint>();
    public double X0, Y0, X1, Y1, X2, Y2;

    public const int LutSize = 512;

    /// <summary>The same gradient with its points moved through <paramref name="m"/>.</summary>
    public VGradient Mapped(Affine m)
    {
        var (a, b) = m.Apply(X0, Y0); var (c, d) = m.Apply(X1, Y1); var (e, f) = m.Apply(X2, Y2);
        return new VGradient { Kind = Kind, Pos = Pos, Col = Col, X0 = a, Y0 = b, X1 = c, Y1 = d, X2 = e, Y2 = f };
    }

    /// <summary>Straight BGRA for each of LutSize evenly spaced t values.</summary>
    public uint[] Lut()
    {
        var l = new uint[LutSize];
        if (Col.Length == 0) return l;
        for (int i = 0; i < LutSize; i++) l[i] = Colour(i / (double)(LutSize - 1));
        return l;
    }

    private uint Colour(double t)
    {
        int n = Col.Length;
        if (t <= Pos[0] || n == 1) return Col[0];
        if (t >= Pos[n - 1]) return Col[n - 1];
        int i = 1;
        while (Pos[i] < t) i++;
        double span = Pos[i] - Pos[i - 1], f = span < 1e-9 ? 1 : (t - Pos[i - 1]) / span;
        uint c0 = Col[i - 1], c1 = Col[i];
        // Premultiplied interpolation (verified: interpolating colour and opacity independently dropped FlashLite_Japan from 87% to 55%).
        double a0 = c0 >> 24, a1 = c1 >> 24, a = a0 + (a1 - a0) * f;
        if (a <= 0) return 0;
        uint o = (uint)Math.Round(a) << 24;
        for (int sh = 0; sh <= 16; sh += 8)
        {
            double v0 = ((c0 >> sh) & 255) * a0, v1 = ((c1 >> sh) & 255) * a1;
            o |= (uint)Math.Clamp((int)Math.Round((v0 + (v1 - v0) * f) / a), 0, 255) << sh;
        }
        return o;
    }

    /// <summary>
    /// The patterned kinds. They share one frame: the centre is P0, the first axis is U = P1-P0 and the second axis is
    /// V = P2-P0 (when P2 is missing or on the first axis, V is U turned a quarter turn, same length, so a plain two-point
    /// drag gives a circular frame). A point has coordinates (a, b) in that frame, and rho = sqrt(a*a + b*b), ang = atan2(b, a).
    /// Each kind turns them into a ramp position t (clamped to 0..1 by the caller; 0 = first colour):
    ///   Rectangle  t = max(|a|, |b|)                     square rings that reach the end colour at the box edge
    ///   Diamond    t = |a| + |b|                         rings of diamonds
    ///   Cone       t = |ang| / pi                        sweeps round the centre: start at +U, end at -U, mirrored so there is no seam
    ///   Starburst  t = tri(8 * ang / 2pi) * min(1, rho)  eight rays; tri(x) = |2*frac(x) - 1|; rays fade in from the centre
    ///   Bars       t = zig(|a|)                          VERIFIED against Fireworks' own gradients2.png (zig(x) = x folded back and forth: 0..1 up, 1..2 down)
    ///   Ripples    t = zig(rho)                          VERIFIED: concentric rings, one per radius out and back (fc_Ripple)
    ///   Pinch      t = |a * b|                           VERIFIED: hyperbolic X shape, the centre line of both axes is the start colour (fc_Pinch)
    ///   Rectangle, Cone are VERIFIED too (they matched Fireworks' pictures unchanged)
    ///   Waves      t = (1 - cos(2pi * (2a + 0.15 sin(2pi * 2b)))) / 2   soft stripes along U that wobble along V
    ///   Satin      t = (1 + cos(2pi a) cos(pi b)) / 2    two crossing soft folds, like draped cloth
    ///   Folds      t = tri(3a + 0.3b)                    sharp pleats along U, slightly slanted by V
    /// Waves, Satin, Folds, Starburst and Diamond are Flint's own approximations: Fireworks' real Waves, Satin and Folds are more
    /// complicated than these (see the pictures in gradients2.png), and Starburst does not exist in Fireworks 8.
    /// </summary>
    private double Pattern(double px, double py)
    {
        double ux = X1 - X0, uy = Y1 - Y0, vx = X2 - X0, vy = Y2 - Y0;
        if (Math.Abs(ux * vy - uy * vx) < 1e-9 * Math.Max(1, ux * ux + uy * uy)) { vx = -uy; vy = ux; }
        double det = ux * vy - uy * vx;
        if (Math.Abs(det) < 1e-12) return 1;
        double a = (px * vy - py * vx) / det, b = (ux * py - uy * px) / det;
        double rho = Math.Sqrt(a * a + b * b);
        static double Zig(double x) { double m = x - 2 * Math.Floor(x / 2); return m <= 1 ? m : 2 - m; }
        static double Tri(double x) => Math.Abs(2 * (x - Math.Floor(x)) - 1);
        switch (Kind)
        {
            case GradientKind.Rectangle: return Math.Max(Math.Abs(a), Math.Abs(b));
            case GradientKind.Diamond: return Math.Abs(a) + Math.Abs(b);
            case GradientKind.Cone: return Math.Abs(Math.Atan2(b, a)) / Math.PI;
            case GradientKind.Starburst: return Tri(8 * Math.Atan2(b, a) / (2 * Math.PI)) * Math.Min(1, rho);
            case GradientKind.Bars: return Zig(Math.Abs(a));
            case GradientKind.Ripples: return Zig(rho);
            case GradientKind.Pinch: return Math.Abs(a * b);
            case GradientKind.Waves: return (1 - Math.Cos(2 * Math.PI * (2 * a + 0.15 * Math.Sin(2 * Math.PI * 2 * b)))) / 2;
            case GradientKind.Satin: return (1 + Math.Cos(2 * Math.PI * a) * Math.Cos(Math.PI * b)) / 2;
            case GradientKind.Folds: return Tri(3 * a + 0.3 * b);
        }
        return 0;
    }

    /// <summary>Position along the ramp (0..1, clamped) of a point in art space.</summary>
    public double T(double x, double y)
    {
        double px = x - X0, py = y - Y0, t;
        if (Kind == GradientKind.Linear)
        {
            double dx = X1 - X0, dy = Y1 - Y0, l2 = dx * dx + dy * dy;
            t = l2 < 1e-12 ? 0 : (px * dx + py * dy) / l2;
        }
        else if (Kind == GradientKind.Circular)
        {
            double r = Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));
            t = r < 1e-9 ? 1 : Math.Sqrt(px * px + py * py) / r;
        }
        else if (Kind != GradientKind.Elliptical) t = Pattern(px, py);
        else
        {
            // coordinates of the point in the basis of the two radius vectors
            double ux = X1 - X0, uy = Y1 - Y0, vx = X2 - X0, vy = Y2 - Y0, det = ux * vy - uy * vx;
            if (Math.Abs(det) < 1e-12) t = 1;
            else
            {
                double a = (px * vy - py * vx) / det, b = (ux * py - uy * px) / det;
                t = Math.Sqrt(a * a + b * b);
            }
        }
        return Math.Clamp(t, 0, 1);
    }
}

/// <summary>Shapes in the source bitmap's pixel space; pixel corners are whole numbers.</summary>
public sealed class VectorArt
{
    public int W, H;
    public List<VShape> Shapes = new();
    /// <summary>Paint shapes in list order (later on top) instead of largest first. Off for traced art.</summary>
    public bool KeepOrder;
    public int PointCount { get { int n = 0; foreach (var s in Shapes) foreach (var c in s.Contours) n += c.Count; return n; } }
}

public sealed class TraceOptions
{
    /// <summary>0 = keep exact colours, else at most this many (pixels under half alpha are dropped).</summary>
    public int Colours = 0;
    /// <summary>Regions with fewer pixels are merged into their most common neighbouring colour.</summary>
    public int MinArea = 1;
    /// <summary>0 = exact pixel edges; up to 100 = rounder curves.</summary>
    public double Smoothing = 0;
    /// <summary>Douglas-Peucker tolerance in pixels (only used when smoothing).</summary>
    public double Simplify = 0;
}

public static class Tracer
{
    #region trace

    public static VectorArt Trace(byte[] bgra, int w, int h, TraceOptions o, CancellationToken ct = default)
    {
        var col = new uint[w * h];
        for (int i = 0; i < col.Length; i++)
            if (bgra[i * 4 + 3] != 0) col[i] = BitConverter.ToUInt32(bgra, i * 4);

        if (o.Colours > 0) Reduce(bgra, col, o.Colours);
        if (o.MinArea > 1) MergeSmall(col, w, h, o.MinArea);

        var art = new VectorArt { W = w, H = h };
        var groups = new Dictionary<uint, List<int>>();
        int vw = w + 1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                uint c = col[y * w + x];
                if (c == 0) continue;
                if (!groups.TryGetValue(c, out var edges)) groups[c] = edges = new List<int>();
                // Directions: 0 east, 1 south, 2 west, 3 north; the region is on the right of travel.
                if (y == 0 || col[(y - 1) * w + x] != c) edges.Add((y * vw + x) * 4);
                if (x == w - 1 || col[y * w + x + 1] != c) edges.Add((y * vw + x + 1) * 4 + 1);
                if (y == h - 1 || col[(y + 1) * w + x] != c) edges.Add(((y + 1) * vw + x + 1) * 4 + 2);
                if (x == 0 || col[y * w + x - 1] != c) edges.Add(((y + 1) * vw + x) * 4 + 3);
            }

        var mask = new byte[vw * (h + 1)];
        int[] delta = { 1, vw, -1, -vw };
        double tol = Math.Max(o.Simplify, o.Smoothing > 0 ? 0.45 + 0.5 * o.Smoothing / 100 : 0);
        foreach (var (c, edges) in groups)
        {
            ct.ThrowIfCancellationRequested();
            foreach (int e in edges) mask[e >> 2] |= (byte)(1 << (e & 3));
            var shape = new VShape { Colour = c };
            foreach (int e in edges)
            {
                int v0 = e >> 2, d0 = e & 3;
                if ((mask[v0] & (1 << d0)) == 0) continue;
                var vs = new List<int>();
                var ds = new List<int>();
                int v = v0, d = d0;
                while (true)
                {
                    mask[v] &= (byte)~(1 << d);
                    vs.Add(v); ds.Add(d);
                    int nv = v + delta[d];
                    int nd = -1;
                    // Right turn first keeps pixels that only touch at a corner as separate loops.
                    int tr = (d + 1) & 3, tl = (d + 3) & 3;
                    if ((mask[nv] & (1 << tr)) != 0) nd = tr;
                    else if ((mask[nv] & (1 << d)) != 0) nd = d;
                    else if ((mask[nv] & (1 << tl)) != 0) nd = tl;
                    if (nd < 0) break;
                    v = nv; d = nd;
                }
                var poly = new List<VPt>();
                for (int i = 0; i < vs.Count; i++)
                    if (ds[(i + vs.Count - 1) % vs.Count] != ds[i]) poly.Add(new VPt(vs[i] % vw, vs[i] / vw));
                if (poly.Count < 3) continue;
                shape.Contours.Add(o.Smoothing > 0 ? Smooth(poly, tol, o.Smoothing) : o.Simplify > 0 ? Simplify(poly, o.Simplify) : poly);
            }
            art.Shapes.Add(shape);
        }
        art.Shapes.Sort((a, b) => Area(b).CompareTo(Area(a)));
        return art;
    }

    /// <summary>Replace colours with a palette of at most n; drops pixels under half alpha.</summary>
    private static void Reduce(byte[] bgra, uint[] col, int n)
    {
        var hist = Quantizer.BuildHistogram(new[] { bgra }, out double total);
        var pal = Quantizer.Quantize(hist, total, n);
        if (pal.Count == 0) { Array.Clear(col); return; }
        var ix = new Quantizer.Indexer(pal);
        for (int i = 0; i < col.Length; i++)
        {
            if (col[i] == 0) continue;
            if (bgra[i * 4 + 3] < 128) { col[i] = 0; continue; }
            int k = ix.Nearest(bgra[i * 4 + 2], bgra[i * 4 + 1], bgra[i * 4]);
            col[i] = 0xFF000000u | (uint)(pal.R[k] << 16) | (uint)(pal.G[k] << 8) | pal.B[k];
        }
    }

    /// <summary>Recolour every 4-connected region under minArea pixels to its most common neighbouring colour (or to nothing).</summary>
    private static void MergeSmall(uint[] col, int w, int h, int minArea)
    {
        var label = new int[w * h];
        var stack = new Stack<int>();
        for (int pass = 0; pass < 8; pass++)
        {
            Array.Clear(label);
            var sizes = new List<int> { 0 };
            for (int s = 0; s < col.Length; s++)
            {
                if (label[s] != 0) continue;
                int id = sizes.Count, n = 0;
                label[s] = id; stack.Push(s);
                while (stack.Count > 0)
                {
                    int p = stack.Pop(); n++;
                    int x = p % w, y = p / w;
                    if (x > 0) Visit(p - 1);
                    if (x < w - 1) Visit(p + 1);
                    if (y > 0) Visit(p - w);
                    if (y < h - 1) Visit(p + w);
                }
                sizes.Add(n);
                void Visit(int q) { if (label[q] == 0 && col[q] == col[s]) { label[q] = id; stack.Push(q); } }
            }
            var tally = new Dictionary<int, Dictionary<uint, int>>();
            for (int p = 0; p < col.Length; p++)
            {
                int id = label[p];
                if (sizes[id] >= minArea) continue;
                int x = p % w, y = p / w;
                if (!tally.TryGetValue(id, out var t)) tally[id] = t = new Dictionary<uint, int>();
                void Add(int q) { if (label[q] != id) t[col[q]] = t.GetValueOrDefault(col[q]) + 1; }
                if (x > 0) Add(p - 1);
                if (x < w - 1) Add(p + 1);
                if (y > 0) Add(p - w);
                if (y < h - 1) Add(p + w);
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) t[0] = t.GetValueOrDefault(0u) + 1;   // the canvas edge counts as empty
            }
            if (tally.Count == 0) return;
            var pick = tally.ToDictionary(kv => kv.Key,
                kv => kv.Value.OrderByDescending(e => e.Value).ThenBy(e => e.Key).First().Key);
            for (int p = 0; p < col.Length; p++)
                if (pick.TryGetValue(label[p], out var c) && sizes[label[p]] < minArea) col[p] = c;
        }
    }

    private static double SignedArea(List<VPt> c)
    {
        double a = 0;
        for (int i = 0; i < c.Count; i++) { var p = c[i]; var q = c[(i + 1) % c.Count]; a += p.X * q.Y - q.X * p.Y; }
        return a / 2;
    }

    private static double Area(VShape s) => Math.Abs(s.Contours.Sum(SignedArea));

    #endregion

    #region smoothing

    private static List<VPt> Smooth(List<VPt> poly, double tol, double smoothing)
    {
        var p = Simplify(poly, tol);
        // Interior angles tighter than this stay sharp.
        double sharpBelow = (130 - 0.8 * smoothing) * Math.PI / 180;
        int iterations = smoothing <= 60 ? 1 : 2;
        var sharp = new bool[p.Count];
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[(i + p.Count - 1) % p.Count]; var b = p[i]; var c = p[(i + 1) % p.Count];
            double ax = a.X - b.X, ay = a.Y - b.Y, cx = c.X - b.X, cy = c.Y - b.Y;
            double l = Math.Sqrt((ax * ax + ay * ay) * (cx * cx + cy * cy));
            sharp[i] = l > 0 && Math.Acos(Math.Clamp((ax * cx + ay * cy) / l, -1, 1)) < sharpBelow;
        }
        for (int it = 0; it < iterations; it++)
        {
            var np = new List<VPt>();
            var ns = new List<bool>();
            int n = p.Count;
            for (int i = 0; i < n; i++)
            {
                var prev = p[(i + n - 1) % n]; var cur = p[i]; var next = p[(i + 1) % n];
                if (sharp[i]) { np.Add(cur); ns.Add(true); continue; }
                np.Add(new VPt(0.25 * prev.X + 0.75 * cur.X, 0.25 * prev.Y + 0.75 * cur.Y));
                np.Add(new VPt(0.75 * cur.X + 0.25 * next.X, 0.75 * cur.Y + 0.25 * next.Y));
                ns.Add(false); ns.Add(false);
            }
            p = np; sharp = ns.ToArray();
        }
        return p;
    }

    /// <summary>Douglas-Peucker on a closed polygon, split at the point farthest from the first.</summary>
    private static List<VPt> Simplify(List<VPt> c, double tol)
    {
        int n = c.Count;
        if (n < 5 || tol <= 0) return c;
        int far = 0; double best = -1;
        for (int i = 1; i < n; i++)
        {
            double d = Math.Pow(c[i].X - c[0].X, 2) + Math.Pow(c[i].Y - c[0].Y, 2);
            if (d > best) { best = d; far = i; }
        }
        var keep = new bool[n];
        keep[0] = keep[far] = true;
        var st = new Stack<(int a, int b)>();
        st.Push((0, far)); st.Push((far, n));   // index n means point 0 again
        while (st.Count > 0)
        {
            var (a, b) = st.Pop();
            var pa = c[a % n]; var pb = c[b % n];
            int m = -1; double md = tol;
            for (int i = a + 1; i < b; i++)
            {
                double d = SegDist(c[i % n], pa, pb);
                if (d > md) { md = d; m = i; }
            }
            if (m < 0) continue;
            keep[m % n] = true;
            st.Push((a, m)); st.Push((m, b));
        }
        var r = new List<VPt>();
        for (int i = 0; i < n; i++) if (keep[i]) r.Add(c[i]);
        return r.Count >= 3 ? r : c;
    }

    private static double SegDist(VPt p, VPt a, VPt b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        double t = l2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2, 0, 1);
        return Math.Sqrt(Math.Pow(p.X - (a.X + t * dx), 2) + Math.Pow(p.Y - (a.Y + t * dy), 2));
    }

    #endregion

    #region render

    /// <summary>
    /// Render the art through <paramref name="m"/> into a new straight-alpha
    /// BGRA buffer covering the transformed bounds. Returns its integer
    /// top-left in destination space, like Transform.Render. Not antialiased: a
    /// pixel is filled when its centre is inside. Antialiased: 4×4 samples per
    /// pixel; each sample belongs to the topmost shape covering it (the largest
    /// shape is drawn first) and the pixel is the average of its samples, so
    /// touching shapes never leave see-through seams.
    /// </summary>
    public static (byte[] px, int x, int y, int w, int h) Render(VectorArt art, Affine m, bool antialias, CancellationToken ct = default)
    {
        if (Math.Abs(m.A * m.D - m.B * m.C) < 1e-12) throw new InvalidOperationException("transform squashes the image flat");
        var (x0, y0, w, h) = Transform.Bounds(m, art.W, art.H);

        bool whole = true;
        foreach (var s in art.Shapes)
            foreach (var c in s.Contours)
                foreach (var p in c) whole &= p.X == Math.Round(p.X) && p.Y == Math.Round(p.Y);
        int N = antialias && !(m.IsIntegerTranslation && whole) ? 4 : 1;

        // Transformed edges per shape, lower end first so a shared edge gives the same crossings on both sides.
        var order = art.KeepOrder ? art.Shapes.ToList() : art.Shapes.OrderByDescending(Area).ToList();
        var shapeEdges = new List<(double ax, double ay, double bx, double by)>[order.Count];
        var minY = new double[order.Count]; var maxY = new double[order.Count];
        for (int k = 0; k < order.Count; k++)
        {
            var edges = shapeEdges[k] = new();
            minY[k] = double.MaxValue; maxY[k] = double.MinValue;
            foreach (var c in order[k].Contours)
                for (int i = 0; i < c.Count; i++)
                {
                    var a = m.Apply(c[i].X, c[i].Y); var b = m.Apply(c[(i + 1) % c.Count].X, c[(i + 1) % c.Count].Y);
                    a = (a.x - x0, a.y - y0); b = (b.x - x0, b.y - y0);
                    if (a.y == b.y) continue;
                    if (a.y > b.y) (a, b) = (b, a);
                    edges.Add((a.x, a.y, b.x, b.y));
                    minY[k] = Math.Min(minY[k], a.y); maxY[k] = Math.Max(maxY[k], b.y);
                }
        }

        // Gradient shapes: a colour table per shape, and the inverse matrix to find each sample's art-space position.
        var luts = new uint[order.Count][];
        Affine inv = Affine.Identity;
        for (int k = 0; k < order.Count; k++)
            if (order[k].Gradient is { } gr) { luts[k] = gr.Lut(); inv = m.Inverse(); }

        var dst = new byte[w * h * 4];
        int total = N * N, sw = w * N;
        // Bands of pixel rows keep the sample buffer small.
        int bandRows = (int)Math.Clamp(4_000_000L / ((long)sw * N), 1, h);
        var owner = new int[bandRows * N * sw];
        for (int by0 = 0; by0 < h; by0 += bandRows)
        {
            int by1 = Math.Min(h, by0 + bandRows);
            Array.Clear(owner, 0, (by1 - by0) * N * sw);
            for (int k = 0; k < order.Count; k++)
            {
                ct.ThrowIfCancellationRequested();
                if (shapeEdges[k].Count == 0 || maxY[k] <= by0 || minY[k] >= by1) continue;
                var cross = new List<double>?[(by1 - by0) * N];
                foreach (var (ax, ay, bx, by) in shapeEdges[k])
                {
                    int r0 = Math.Max(by0 * N, (int)Math.Ceiling(ay * N - 0.5)), r1 = Math.Min(by1 * N, (int)Math.Ceiling(by * N - 0.5));
                    for (int r = r0; r < r1; r++)
                        (cross[r - by0 * N] ??= new()).Add(ax + ((r + 0.5) / N - ay) * (bx - ax) / (by - ay));
                }
                for (int r = 0; r < cross.Length; r++)
                {
                    var xs = cross[r];
                    if (xs == null) continue;
                    xs.Sort();
                    for (int q = 0; q + 1 < xs.Count; q += 2)
                    {
                        int c0 = Math.Max(0, (int)Math.Ceiling(xs[q] * N - 0.5)), c1 = Math.Min(sw, (int)Math.Ceiling(xs[q + 1] * N - 0.5));
                        if (c1 > c0) Array.Fill(owner, k + 1, r * sw + c0, c1 - c0);
                    }
                }
            }
            for (int y = by0; y < by1; y++)
                for (int x = 0; x < w; x++)
                {
                    long sa = 0, sb = 0, sg = 0, sr = 0;
                    for (int j = 0; j < N; j++)
                        for (int i = 0; i < N; i++)
                        {
                            int k = owner[((y - by0) * N + j) * sw + x * N + i];
                            if (k == 0) continue;
                            uint c = order[k - 1].Colour;
                            if (luts[k - 1] is { } lut)
                            {
                                // Fireworks evaluates a gradient at the pixel corner, not its centre (measured on gradients.png: the start pixel is exactly the first colour)
                                var (gx, gy) = inv.Apply(x0 + x + (i + 0.5) / N - 0.5, y0 + y + (j + 0.5) / N - 0.5);
                                c = lut[(int)Math.Round(order[k - 1].Gradient!.T(gx, gy) * (VGradient.LutSize - 1))];
                            }
                            long a = c >> 24;
                            sa += a; sb += (c & 255) * a; sg += ((c >> 8) & 255) * a; sr += ((c >> 16) & 255) * a;
                        }
                    if (sa == 0) continue;
                    int o = (y * w + x) * 4;
                    dst[o] = (byte)(sb / sa); dst[o + 1] = (byte)(sg / sa); dst[o + 2] = (byte)(sr / sa);
                    dst[o + 3] = (byte)((sa + total / 2) / total);
                }
        }
        return (dst, x0, y0, w, h);
    }

    #endregion

    #region svg

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    public static string ToSvg(VectorArt art) => Svg(art, null, art.W, art.H, 255);

    /// <summary>The art as it sits on a canvas: through <paramref name="m"/> (x' = A·x + C·y + Tx), at the object's opacity.</summary>
    public static string ToSvg(VectorArt art, Affine m, int canvasW, int canvasH, byte opacity) => Svg(art, m, canvasW, canvasH, opacity);

    private static string Svg(VectorArt art, Affine? m, int cw, int ch, byte opacity)
    {
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{cw}\" height=\"{ch}\" viewBox=\"0 0 {cw} {ch}\">\n");
        if (m is { } t)
        {
            sb.Append($"<g transform=\"matrix({F(t.A)} {F(t.B)} {F(t.C)} {F(t.D)} {F(t.Tx)} {F(t.Ty)})\"");
            if (opacity < 255) sb.Append($" opacity=\"{F(opacity / 255.0)}\"");
            sb.Append(">\n");
        }
        foreach (var s in art.Shapes)
        {
            uint c = s.Colour;
            sb.Append($"<path fill=\"#{(c >> 16) & 255:X2}{(c >> 8) & 255:X2}{c & 255:X2}\"");
            if ((c >> 24) < 255) sb.Append($" fill-opacity=\"{F((c >> 24) / 255.0)}\"");
            sb.Append(" fill-rule=\"evenodd\" d=\"");
            foreach (var poly in s.Contours)
            {
                for (int i = 0; i < poly.Count; i++) sb.Append(i == 0 ? "M" : "L").Append(F(poly[i].X)).Append(' ').Append(F(poly[i].Y));
                sb.Append('Z');
            }
            sb.Append("\"/>\n");
        }
        if (m != null) sb.Append("</g>\n");
        return sb.Append("</svg>\n").ToString();
    }

    #endregion
}
