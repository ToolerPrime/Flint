/* Combine Path (Union, Intersect, Punch, Crop) and Alter Path (Simplify,
 * Expand Stroke, Inset Path).
 *
 * Booleans and offsets work on a high-resolution mask (up to 8 samples per
 * pixel each way): each input is rendered with Tracer.Render, the masks are
 * combined (or grown/shrunk with an exact distance transform), the result is
 * traced back to outlines with Tracer.Trace, and the outlines become smooth
 * bezier paths: Douglas-Peucker finds real corners, PathFit (translated from
 * VectorCraft) fits curves between them. Straight edges on whole pixels come
 * back exact; curved edges move by at most about a tenth of a pixel.
 * shortcut: mask-based, so tiny details below 1/8 px are lost; a polygon
 * clipper (e.g. Clipper2) would be exact if a dependency is ever approved.
 * Written for Flint. */
using System.Collections.Immutable;

namespace Flint;

public enum CombineOp { Union, Intersect, Punch, Crop }

public static class PathOps
{
    /// <summary>A path's fill outlines in canvas space (open parts count as closed).</summary>
    public static List<List<VPt>> FillPolygons(PathSource src, Affine artToCanvas) =>
        src.Mapped(artToCanvas).Subpaths.Select(s => PathSources.Flatten(s)).Where(p => p.Count >= 3).ToList();

    private sealed class Mask
    {
        public int X0, Y0, W, H, S;          // canvas origin of the mask, size in samples, samples per pixel
        public bool[] Bits = Array.Empty<bool>();
    }

    private static Mask NewMask(RectangleF canvasBounds, double pad)
    {
        int x0 = (int)Math.Floor(canvasBounds.Left - pad) - 1, y0 = (int)Math.Floor(canvasBounds.Top - pad) - 1;
        int x1 = (int)Math.Ceiling(canvasBounds.Right + pad) + 1, y1 = (int)Math.Ceiling(canvasBounds.Bottom + pad) + 1;
        int w = Math.Max(1, x1 - x0), h = Math.Max(1, y1 - y0);
        int s = 8;
        while (s > 1 && (long)w * s * h * s > 16_000_000) s /= 2;
        if ((long)w * s * h * s > 16_000_000) throw new InvalidOperationException("these paths are too large to combine");
        return new Mask { X0 = x0, Y0 = y0, W = w * s, H = h * s, S = s, Bits = new bool[w * s * h * s] };
    }

    /// <summary>Set the samples whose centres are inside the polygons (even-odd), via the exact vector renderer.</summary>
    private static bool[] Raster(Mask m, IEnumerable<VShape> shapes)
    {
        var art = new VectorArt { W = m.W, H = m.H, KeepOrder = true };
        var toMask = Affine.Translate(-m.X0, -m.Y0).Then(Affine.Scale(m.S, m.S));
        foreach (var sh in shapes)
            art.Shapes.Add(new VShape { Colour = 0xFFFFFFFF, Contours = sh.Contours.Select(c => c.Select(p => { var (x, y) = toMask.Apply(p.X, p.Y); return new VPt(x, y); }).ToList()).ToList() });
        var r = Tracer.Render(art, Affine.Identity, false);
        var bits = new bool[m.W * m.H];
        for (int y = 0; y < r.h; y++)
            for (int x = 0; x < r.w; x++)
            {
                int mx = x + r.x, my = y + r.y;
                if (mx >= 0 && my >= 0 && mx < m.W && my < m.H && r.px[(y * r.w + x) * 4 + 3] != 0) bits[my * m.W + mx] = true;
            }
        return bits;
    }

    private static RectangleF Bounds(IEnumerable<List<VPt>> polys)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var c in polys) foreach (var p in c) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        return x0 == double.MaxValue ? RectangleF.Empty : RectangleF.FromLTRB((float)x0, (float)y0, (float)x1, (float)y1);
    }

    /// <summary>
    /// Combine fill outlines (canvas space). Union/Intersect fold all inputs; Punch and Crop use the
    /// last input as the cutter on each of the others. Returns one result per output (Punch/Crop: per lower input).
    /// </summary>
    public static List<PathSource?> Combine(CombineOp op, IReadOnlyList<List<List<VPt>>> inputs, uint fill)
    {
        var all = inputs.SelectMany(i => i).ToList();
        var mask = NewMask(Bounds(all), 0);
        var masks = inputs.Select(i => Raster(mask, new[] { new VShape { Contours = i } })).ToList();
        bool[] Fold(Func<bool, bool, bool> f, IEnumerable<bool[]> ms) => ms.Aggregate((a, b) => a.Zip(b, f).ToArray());
        if (op is CombineOp.Union or CombineOp.Intersect)
        {
            Func<bool, bool, bool> f = op == CombineOp.Union ? (a, b) => a || b : (a, b) => a && b;
            return new List<PathSource?> { Trace(mask, Fold(f, masks), fill) };
        }
        var cutter = masks[^1];
        Func<bool, bool, bool> cut = op == CombineOp.Punch ? (a, b) => a && !b : (a, b) => a && b;
        return masks.Take(masks.Count - 1).Select(m => Trace(mask, m.Zip(cutter, cut).ToArray(), fill)).ToList();
    }

    /// <summary>Grow (positive) or shrink (negative) fill outlines by <paramref name="by"/> canvas pixels; round corners when growing.</summary>
    public static PathSource? Offset(List<List<VPt>> polys, double by, uint fill)
    {
        var mask = NewMask(Bounds(polys), Math.Max(0, by) + 1);
        var bits = Raster(mask, new[] { new VShape { Contours = polys } });
        double r = Math.Abs(by) * mask.S;
        // Squared distance (in samples) from each sample to the nearest sample of the other kind (float: half the memory).
        var dist = DistanceSquared(bits, mask.W, mask.H, by > 0);
        var res = new bool[bits.Length];
        for (int i = 0; i < res.Length; i++) res[i] = by > 0 ? bits[i] || dist[i] <= r * r : bits[i] && dist[i] > r * r;
        return Trace(mask, res, fill);
    }

    /// <summary>A stroke's outline as a filled path (canvas space).</summary>
    public static PathSource? ExpandStroke(PathSource src, Affine artToCanvas, double width, uint fill)
    {
        var mapped = src.Mapped(artToCanvas);
        var shapes = new List<VShape>();
        foreach (var s in mapped.Subpaths)
        {
            var pts = PathSources.Flatten(s);
            if (pts.Count >= 2) FireworksPng.Stroke(pts, s.Closed, width, 0xFFFFFFFF, shapes);
        }
        if (shapes.Count == 0) return null;
        // Round ends and joins at every width (the shared stroker skips them below 2 px).
        foreach (var s in mapped.Subpaths)
            foreach (var n in s.Nodes) shapes.Add(new VShape { Contours = { Disc(n.P, width / 2) } });
        var mask = NewMask(Bounds(shapes.SelectMany(s => s.Contours)), 1);
        // One render: each piece fills even-odd on its own, so overlapping pieces simply add up.
        return Trace(mask, Raster(mask, shapes), fill);
    }

    private static List<VPt> Disc(VPt c, double r)
    {
        int n = Math.Clamp((int)Math.Ceiling(r * 6), 12, 96);
        return Enumerable.Range(0, n).Select(k => new VPt(c.X + r * Math.Cos(2 * Math.PI * k / n), c.Y + r * Math.Sin(2 * Math.PI * k / n))).ToList();
    }

    /// <summary>Fewer points: each part is refitted within <paramref name="tolerance"/> pixels, keeping real corners.</summary>
    public static PathSource Simplify(PathSource src, double tolerance)
    {
        var subs = src.Subpaths.Select(s =>
        {
            var pts = PathSources.Flatten(s, Math.Min(0.05, tolerance / 4));
            var fit = ToSubPath(pts, s.Closed, tolerance);
            return fit != null && fit.Nodes.Length < s.Nodes.Length ? fit : s;
        }).ToImmutableArray();
        return src with { Subpaths = subs };
    }

    // ---------- outlines back to paths ----------

    private static PathSource? Trace(Mask m, bool[] bits, uint fill)
    {
        if (!bits.Any(b => b)) return null;
        var bgra = new byte[bits.Length * 4];
        for (int i = 0; i < bits.Length; i++) if (bits[i]) bgra[i * 4] = bgra[i * 4 + 1] = bgra[i * 4 + 2] = bgra[i * 4 + 3] = 255;
        var art = Tracer.Trace(bgra, m.W, m.H, new TraceOptions());
        var subs = new List<SubPath>();
        double k = 1.0 / m.S;
        foreach (var shape in art.Shapes)
            foreach (var c in shape.Contours)
            {
                var pts = c.Select(p => new VPt(m.X0 + p.X * k, m.Y0 + p.Y * k)).ToList();
                if (ToSubPath(pts, true, 0.1, 0.75 * k) is { } sp) subs.Add(sp);
            }
        return subs.Count == 0 ? null : new PathSource(subs.ToImmutableArray(), fill, 0, 0);
    }

    /// <summary>
    /// A polyline as a bezier subpath: Douglas-Peucker (within <paramref name="corner"/>) finds the shape's
    /// real corners, then the points between corners are fitted with cubics within <paramref name="tolerance"/>.
    /// </summary>
    public static SubPath? ToSubPath(List<VPt> pts, bool closed, double tolerance, double corner = -1, bool rotated = false)
    {
        if (corner < 0) corner = tolerance * 0.75;
        if (closed && pts.Count > 1 && pts[0] == pts[^1]) pts = pts.Take(pts.Count - 1).ToList();
        if (pts.Count < (closed ? 3 : 2)) return null;
        var loop = closed ? pts.Append(pts[0]).ToList() : pts;
        var keep = Dp(loop, corner);
        var dp = keep.Select(i => loop[i]).ToList();
        // A corner: the outline turns more than 35 degrees, measured over a window of about ten times the
        // corner tolerance either side, so a mask's tiny stair steps never count; the sharpest turn in a cluster wins.
        int n = dp.Count, m = closed ? n - 1 : n;
        double perimeter = 0;
        for (int i = 0; i + 1 < n; i++) perimeter += Math.Sqrt((dp[i + 1].X - dp[i].X) * (dp[i + 1].X - dp[i].X) + (dp[i + 1].Y - dp[i].Y) * (dp[i + 1].Y - dp[i].Y));
        double win = Math.Min(10 * corner, perimeter / 8);   // small shapes: a window round the whole outline would blur every corner
        VPt Walk(int i, int dir)
        {
            double acc = 0; int j = i;
            for (int steps = 0; steps < m; steps++)
            {
                int k = closed ? ((j + dir) % m + m) % m : j + dir;
                if (k < 0 || k >= m) break;
                acc += Math.Sqrt((dp[k].X - dp[j].X) * (dp[k].X - dp[j].X) + (dp[k].Y - dp[j].Y) * (dp[k].Y - dp[j].Y));
                j = k;
                if (acc >= win) break;
            }
            return dp[j];
        }
        var turn = new double[m];
        for (int i = 0; i < m; i++)
        {
            if (!closed && (i == 0 || i == m - 1)) continue;
            var p = Walk(i, -1); var q = dp[i]; var r = Walk(i, 1);
            double ax = q.X - p.X, ay = q.Y - p.Y, bx = r.X - q.X, by = r.Y - q.Y;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            if (la < 1e-9 || lb < 1e-9) continue;
            turn[i] = Math.Acos(Math.Clamp((ax * bx + ay * by) / (la * lb), -1, 1));
        }
        var corners = new List<int>();
        double limit = 35 * Math.PI / 180;
        for (int i = 0; i < m; i++)
        {
            if (!closed && (i == 0 || i == m - 1)) { corners.Add(i); continue; }
            if (turn[i] <= limit) continue;
            // keep only the sharpest within the window
            bool sharpest = true;
            foreach (int dir in new[] { -1, 1 })
            {
                double acc = 0; int j = i;
                for (int steps = 0; steps < m && sharpest; steps++)
                {
                    int k = closed ? ((j + dir) % m + m) % m : j + dir;
                    if (k < 0 || k >= m || k == i) break;
                    acc += Math.Sqrt((dp[k].X - dp[j].X) * (dp[k].X - dp[j].X) + (dp[k].Y - dp[j].Y) * (dp[k].Y - dp[j].Y));
                    if (acc > win) break;
                    if (turn[k] > turn[i] || (turn[k] == turn[i] && k < i)) sharpest = false;
                    j = k;
                }
            }
            if (sharpest) corners.Add(i);
        }
        // The second pass starts on the corner it was rotated to; keep it even if the new simplification ranks a neighbour higher.
        if (closed && rotated && corners.Count > 0 && corners[0] != 0) corners.Insert(0, 0);
        var cubics = new List<(VPt a, VPt b, VPt c, VPt d)>();
        void FitRun(int fromDp, int toDp)
        {
            int a = keep[fromDp], b = keep[toDp];
            // Fit the midpoints of the run's pieces (exact ends kept): a mask's stair steps average out.
            var raw = loop.Skip(a).Take(b - a + 1).ToList();
            var run = new List<PointD> { new(raw[0].X, raw[0].Y) };
            for (int q = 0; q + 1 < raw.Count; q++) run.Add(new PointD((raw[q].X + raw[q + 1].X) / 2, (raw[q].Y + raw[q + 1].Y) / 2));
            run.Add(new PointD(raw[^1].X, raw[^1].Y));
            bool straight = toDp - fromDp == 1;
            if (straight || run.Count <= 2)
            {
                var s0 = loop[a]; var s1 = loop[b];
                cubics.Add((s0, s0, s1, s1));
                return;
            }
            // A smooth closed loop: the same tangent at both ends, so the seam point is smooth too.
            var tan = closed && corners.Count == 0 && run.Count > 2 ? (run[1] - run[^2]).Normalized() : default;
            foreach (var (p0, p1, p2, p3) in PathFit.FitCubics(run, tan, tan, tolerance))
                cubics.Add((new VPt(p0.X, p0.Y), new VPt(p1.X, p1.Y), new VPt(p2.X, p2.Y), new VPt(p3.X, p3.Y)));
        }
        if (closed && corners.Count == 0)
        {
            // A smooth loop: fit it as one run from point 0 round to point 0.
            FitRun(0, n - 1);
        }
        else if (closed && corners[0] != 0 && !rotated)
        {
            // Start the loop on a corner (so no curve is cut in two at an arbitrary point), then fit again.
            int at = keep[corners[0]];
            return ToSubPath(pts.Skip(at).Concat(pts.Take(at)).ToList(), true, tolerance, corner, true);
        }
        else if (closed)
        {
            var ends = corners.Append(n - 1).ToList();
            for (int i = 0; i + 1 < ends.Count; i++) FitRun(ends[i], ends[i + 1]);
        }
        else
        {
            for (int i = 0; i + 1 < corners.Count; i++) FitRun(corners[i], corners[i + 1]);
        }
        if (cubics.Count == 0) return null;
        var nodes = new List<PathNode>();
        for (int i = 0; i < cubics.Count; i++)
        {
            var c = cubics[i];
            var prevIn = i > 0 ? cubics[i - 1].c : (closed ? cubics[^1].c : c.a);
            nodes.Add(new PathNode(c.a, prevIn, c.b));
        }
        if (!closed) { var last = cubics[^1]; nodes.Add(new PathNode(last.d, last.c, last.d)); }
        return new SubPath(nodes.ToImmutableArray(), closed);
    }

    /// <summary>Douglas-Peucker on a polyline: indices of the points kept (first and last always).</summary>
    private static List<int> Dp(List<VPt> p, double tol)
    {
        var keep = new bool[p.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, p.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double best = -1; int at = -1;
            double dx = p[b].X - p[a].X, dy = p[b].Y - p[a].Y, len = Math.Sqrt(dx * dx + dy * dy);
            for (int i = a + 1; i < b; i++)
            {
                double d = len < 1e-12 ? Math.Sqrt((p[i].X - p[a].X) * (p[i].X - p[a].X) + (p[i].Y - p[a].Y) * (p[i].Y - p[a].Y))
                                       : Math.Abs((p[i].X - p[a].X) * dy - (p[i].Y - p[a].Y) * dx) / len;
                if (d > best) { best = d; at = i; }
            }
            if (at >= 0 && best > tol) { keep[at] = true; stack.Push((a, at)); stack.Push((at, b)); }
        }
        return Enumerable.Range(0, p.Count).Where(i => keep[i]).ToList();
    }

    /// <summary>Exact squared Euclidean distance to the nearest sample that is <paramref name="toSet"/> (Felzenszwalb-Huttenlocher).</summary>
    private static float[] DistanceSquared(bool[] bits, int w, int h, bool toSet)
    {
        const double Inf = 1e20;
        var d = new float[w * h];
        for (int i = 0; i < d.Length; i++) d[i] = bits[i] == toSet ? 0 : (float)Inf;
        var f = new double[Math.Max(w, h)];
        var o = new double[Math.Max(w, h)];
        var v = new int[Math.Max(w, h)];
        var z = new double[Math.Max(w, h) + 1];
        void Pass(int n)
        {
            int k = 0; v[0] = 0; z[0] = -Inf; z[1] = Inf;
            for (int q = 1; q < n; q++)
            {
                double s;
                while (true)
                {
                    s = (f[q] + (double)q * q - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                    if (s <= z[k]) { k--; if (k < 0) { k = 0; break; } } else break;
                }
                k++; v[k] = q; z[k] = s; z[k + 1] = Inf;
            }
            k = 0;
            for (int q = 0; q < n; q++) { while (z[k + 1] < q) k++; o[q] = (double)(q - v[k]) * (q - v[k]) + f[v[k]]; }
        }
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++) f[y] = d[y * w + x];
            Pass(h);
            for (int y = 0; y < h; y++) d[y * w + x] = (float)o[y];
        }
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++) f[x] = d[y * w + x];
            Pass(w);
            for (int x = 0; x < w; x++) d[y * w + x] = (float)o[x];
        }
        return d;
    }
}
