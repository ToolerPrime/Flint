/* Combine Path (Union, Intersect, Punch, Crop) and Alter Path (Simplify,
 * Expand Stroke, Inset Path).
 *
 * Booleans and offsets run on the paths' flattened outlines through Clipper2
 * (Angus Johnson, Boost Software License 1.0; see CREDITS_LICENSES.md),
 * which works exactly in fixed-point coordinates. The resulting polygons
 * become smooth bezier paths again: Douglas-Peucker finds real corners and
 * PathFit (translated from VectorCraft) fits curves between them. Straight
 * edges come back exact. Written for Flint. */
using System.Collections.Immutable;
using Clipper2Lib;
using CPath = Clipper2Lib.PathD;
using CPaths = Clipper2Lib.PathsD;

namespace Flint;

public enum CombineOp { Union, Intersect, Punch, Crop }

public static class PathOps
{
    /// <summary>A path's fill outlines in canvas space (open parts count as closed).</summary>
    public static List<List<VPt>> FillPolygons(PathSource src, Affine artToCanvas) =>
        src.Mapped(artToCanvas).Subpaths.Select(s => PathSources.Flatten(s, OpsFlatten)).Where(p => p.Count >= 3).ToList();

    private const int Precision = 4;                  // Clipper works to 1/10000 px
    // Curves are cut into straight pieces this close to the curve before combining (chords cut corners, so keep it fine).
    private const double OpsFlatten = 0.005;

    private static CPaths ToClipper(IEnumerable<List<VPt>> polys)
    {
        var o = new CPaths();
        foreach (var poly in polys)
        {
            var c = new CPath(poly.Count);
            foreach (var p in poly) c.Add(new Clipper2Lib.PointD(p.X, p.Y));
            o.Add(c);
        }
        return o;
    }

    /// <summary>One input's outlines resolved even-odd (as Flint draws them) into clean shapes.</summary>
    private static CPaths Resolve(List<List<VPt>> polys) => Clipper.Union(ToClipper(polys), new CPaths(), FillRule.EvenOdd, Precision);

    /// <summary>
    /// Combine fill outlines (canvas space). Union/Intersect fold all inputs; Punch and Crop use the
    /// last input as the cutter on each of the others. Returns one result per output (Punch/Crop: per lower input).
    /// </summary>
    public static List<PathSource?> Combine(CombineOp op, IReadOnlyList<List<List<VPt>>> inputs, uint fill)
    {
        var shapes = inputs.Select(Resolve).ToList();
        if (op is CombineOp.Union or CombineOp.Intersect)
        {
            var acc = shapes[0];
            foreach (var next in shapes.Skip(1))
                acc = op == CombineOp.Union ? Clipper.Union(acc, next, FillRule.NonZero, Precision) : Clipper.Intersect(acc, next, FillRule.NonZero, Precision);
            return new List<PathSource?> { ToPath(acc, fill) };
        }
        var cutter = shapes[^1];
        return shapes.Take(shapes.Count - 1).Select(lower => ToPath(op == CombineOp.Punch
            ? Clipper.Difference(lower, cutter, FillRule.NonZero, Precision)
            : Clipper.Intersect(lower, cutter, FillRule.NonZero, Precision), fill)).ToList();
    }

    /// <summary>Grow (positive) or shrink (negative) fill outlines by <paramref name="by"/> canvas pixels; round corners when growing, sharp when shrinking.</summary>
    public static PathSource? Offset(List<List<VPt>> polys, double by, uint fill) =>
        ToPath(Clipper.InflatePaths(Resolve(polys), by, by > 0 ? JoinType.Round : JoinType.Miter, EndType.Polygon, 4, Precision, OpsFlatten), fill);

    /// <summary>A stroke's outline as a filled path (canvas space): round joins and ends.</summary>
    public static PathSource? ExpandStroke(PathSource src, Affine artToCanvas, double width, uint fill)
    {
        var mapped = src.Mapped(artToCanvas);
        var all = new CPaths();
        foreach (var s in mapped.Subpaths)
        {
            var pts = PathSources.Flatten(s, OpsFlatten);
            if (pts.Count < 2) continue;
            all.AddRange(Clipper.InflatePaths(ToClipper(new[] { pts }), width / 2, JoinType.Round, s.Closed ? EndType.Joined : EndType.Round, 4, Precision, OpsFlatten));
        }
        return ToPath(Clipper.Union(all, new CPaths(), FillRule.NonZero, Precision), fill);   // one union at the end, not one per part
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

    private static PathSource? ToPath(CPaths result, uint fill)
    {
        var subs = new List<SubPath>();
        foreach (var poly in result)
        {
            if (poly.Count < 3 || Math.Abs(Clipper.Area(poly)) < 0.01) continue;   // slivers from nearly matching edges
            var pts = poly.Select(p => new VPt(p.x, p.y)).ToList();
            if (ToSubPath(pts, true, 0.05, 0.02) is { } sp) subs.Add(sp);
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
        // corner tolerance either side, so many short flattened pieces of one curve never count; the sharpest turn in a cluster wins.
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
            var run = loop.Skip(a).Take(b - a + 1).Select(v => new PointD(v.X, v.Y)).ToList();
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
}
