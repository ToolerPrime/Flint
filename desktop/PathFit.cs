/* Translated from VectorCraft crates/pathops/src/fit.rs (https://github.com/storytold/vectorcraft).
 * Copyright (c) 2026 ArtCraft Team and the VectorCraft contributors. MIT OR Apache-2.0; used under the MIT licence (see CREDITS_LICENSES.md).
 *
 * Least-squares cubic Bezier fitting of point runs: fixed end tangents, Newton
 * reparameterisation, split near the worst point, then join neighbouring pieces back
 * together where one cubic still fits. Consecutive output cubics are G1 continuous. */
namespace Flint;

public readonly record struct PointD(double X, double Y)
{
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);
    public static PointD operator -(PointD a) => new(-a.X, -a.Y);
    public static PointD operator *(PointD a, double k) => new(a.X * k, a.Y * k);
    public static PointD operator /(PointD a, double k) => new(a.X / k, a.Y / k);
    public double Dot(PointD o) => X * o.X + Y * o.Y;
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double DistanceTo(PointD o) => (this - o).Length;
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
    /// <summary>Unit vector, or the zero vector when (nearly) zero.</summary>
    public PointD Normalized() { var l = Length; return l > 1e-12 && double.IsFinite(l) ? this / l : default; }
}

public static class PathFit
{
    private const int MaxReparam = 6;
    private const int MaxJoins = 32;
    private const double MaxBulgeSamples = 1024.0;
    private const int CoarsePoints = 16;
    private const int MaxSplits = 40;

    private readonly record struct Cu(PointD P0, PointD P1, PointD P2, PointD P3)
    {
        public PointD Eval(double t)
        {
            double s = 1 - t;
            return P0 * (s * s * s) + P1 * (3 * s * s * t) + P2 * (3 * s * t * t) + P3 * (t * t * t);
        }
        public PointD D1(double t)
        {
            double s = 1 - t;
            return (P1 - P0) * (3 * s * s) + (P2 - P1) * (6 * s * t) + (P3 - P2) * (3 * t * t);
        }
        public PointD D2(double t) => (P2 - P1 * 2 + P0) * (6 * (1 - t)) + (P3 - P2 * 2 + P1) * (6 * t);
        public (PointD, PointD, PointD, PointD) Tuple() => (P0, P1, P2, P3);
    }

    private sealed class Piece
    {
        public Cu Curve; public int End; public PointD T1;
    }

    /// <summary>
    /// Fit a polyline with as few cubics as stay within <paramref name="tolerance"/> (max distance), first and last
    /// points kept exactly. <paramref name="tanStart"/> leaves the first point, <paramref name="tanEnd"/> arrives at
    /// the last (forward direction); a zero vector means "estimate from the points". The pieces stay close to the
    /// chords between the points (use <see cref="FitCubicsThrough"/> for samples of a smooth curve).
    /// </summary>
    public static List<(PointD p0, PointD p1, PointD p2, PointD p3)> FitCubics(IReadOnlyList<PointD> pts, PointD tanStart, PointD tanEnd, double tolerance)
    {
        var p = Dedup(pts);
        var outList = new List<(PointD, PointD, PointD, PointD)>();
        if (p.Count < 2) return outList;
        var a = p.ToArray();
        (var t0, var t1) = Tangents(a, tanStart, tanEnd);
        double tol = Math.Max(Clean(tolerance), 1e-9);
        if (Straight(a, tol * 1e-3, out var line)) return new() { line };
        var pieces = new List<Piece>();
        FitRec(a, 0, a.Length - 1, t0, t1, tol, pieces);
        foreach (var c in JoinPieces(a, pieces, t0, tol)) outList.Add(c.Tuple());
        return outList;
    }

    /// <summary>FitCubics with both tangents estimated from the points.</summary>
    public static List<(PointD p0, PointD p1, PointD p2, PointD p3)> FitCubicsAuto(IReadOnlyList<PointD> pts, double tolerance)
        => FitCubics(pts, default, default, tolerance);

    /// <summary>
    /// Like <see cref="FitCubics"/> but for dense samples of a smooth curve: a piece may leave the chords between the
    /// points, so it bows with them. The error is measured at the points only.
    /// </summary>
    public static List<(PointD p0, PointD p1, PointD p2, PointD p3)> FitCubicsThrough(IReadOnlyList<PointD> pts, PointD tanStart, PointD tanEnd, double tolerance)
    {
        var a = Dedup(pts).ToArray();
        var result = new List<(PointD, PointD, PointD, PointD)>();
        if (a.Length < 2) return result;
        (var t0, var t1) = Tangents(a, tanStart, tanEnd);
        if (Straight(a, 1e-9, out var line)) { result.Add(line); return result; }
        var cs = new List<Cu>();
        FitThroughRec(a, t0, t1, Math.Max(Clean(tolerance), 1e-9), cs, 0);
        foreach (var c in cs) result.Add(c.Tuple());
        return result;
    }

    // ---- helpers ----

    private static double Clean(double v) => double.IsFinite(v) ? v : 1e-9;

    private static List<PointD> Dedup(IReadOnlyList<PointD> pts)
    {
        var v = new List<PointD>(pts.Count);
        foreach (var p in pts)
        {
            if (!p.IsFinite) continue;
            if (v.Count == 0 || v[^1].DistanceTo(p) > 1e-12) v.Add(p);
        }
        return v;
    }

    private static (PointD, PointD) Tangents(PointD[] a, PointD t0, PointD t1)
    {
        var s = t0.IsFinite ? t0.Normalized() : default;
        var e = t1.IsFinite ? t1.Normalized() : default;
        if (s == default) s = (a[1] - a[0]).Normalized();
        if (e == default) e = (a[^1] - a[^2]).Normalized();
        return (s, e);
    }

    /// <summary>All points on the chord line and running forward along it: one segment, handles a third of the chord apart.</summary>
    private static bool Straight(PointD[] a, double eps, out (PointD, PointD, PointD, PointD) seg)
    {
        seg = default;
        var chord = a[^1] - a[0];
        double len = chord.Length;
        if (len < 1e-12) return false;
        var dir = chord / len;
        double prev = 0;
        for (int i = 1; i < a.Length - 1; i++)
        {
            var v = a[i] - a[0];
            double along = v.Dot(dir), off = Math.Abs(v.X * dir.Y - v.Y * dir.X);
            if (off > Math.Max(eps, len * 1e-9) || along < prev - 1e-12 || along > len + 1e-12) return false;
            prev = along;
        }
        seg = (a[0], a[0] + dir * (len / 3), a[^1] - dir * (len / 3), a[^1]);
        return true;
    }

    private static double[] ChordParams(PointD[] pts)
    {
        var u = new double[pts.Length];
        double acc = 0;
        for (int i = 1; i < pts.Length; i++) { acc += pts[i - 1].DistanceTo(pts[i]); u[i] = acc; }
        if (acc > 0) for (int i = 0; i < u.Length; i++) u[i] /= acc;
        return u;
    }

    private static double[] Bern(double t)
    {
        double s = 1 - t;
        return new[] { s * s * s, 3 * s * s * t, 3 * s * t * t, t * t * t };
    }

    private static Cu Generate(PointD[] pts, double[] u, PointD t0, PointD t1)
    {
        var p0 = pts[0]; var p3 = pts[^1];
        var dirIn = -t1;
        double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;
        for (int i = 0; i < pts.Length; i++)
        {
            var b = Bern(u[i]);
            var a0 = t0 * b[1];
            var a1 = dirIn * b[2];
            c00 += a0.Dot(a0); c01 += a0.Dot(a1); c11 += a1.Dot(a1);
            var tmp = pts[i] - (p0 * (b[0] + b[1]) + p3 * (b[2] + b[3]));
            x0 += a0.Dot(tmp); x1 += a1.Dot(tmp);
        }
        double det = c00 * c11 - c01 * c01;
        double seg = p0.DistanceTo(p3);
        double eps = 1e-6 * seg;
        double al, ar;
        if (Math.Abs(det) > 1e-12) { al = (x0 * c11 - x1 * c01) / det; ar = (c00 * x1 - c01 * x0) / det; }
        else { al = seg / 3; ar = seg / 3; }
        if (!double.IsFinite(al) || !double.IsFinite(ar) || al < eps || ar < eps) { al = seg / 3; ar = seg / 3; }
        double cap = seg * 4 + 1e-9;
        al = Math.Min(al, cap); ar = Math.Min(ar, cap);
        return new Cu(p0, p0 + t0 * al, p3 + dirIn * ar, p3);
    }

    private static (double, int) MaxError(PointD[] pts, Cu c, double[] u)
    {
        double e = 0; int idx = pts.Length / 2;
        for (int i = 0; i < pts.Length; i++)
        {
            double d = c.Eval(u[i]).DistanceTo(pts[i]);
            if (d > e) { e = d; idx = i; }
        }
        return (e, idx);
    }

    private static double Newton(Cu c, PointD p, double t)
    {
        var q = c.Eval(t) - p;
        var q1 = c.D1(t);
        var q2 = c.D2(t);
        double num = q.Dot(q1);
        double den = q1.Dot(q1) + q.Dot(q2);
        if (Math.Abs(den) < 1e-12) return t;
        double nt = t - num / den;
        return double.IsFinite(nt) ? Math.Clamp(nt, 0, 1) : t;
    }

    private static (Cu, double, int, double[]) FitSingleWithin(PointD[] pts, double[] u, PointD t0, PointD t1, double giveUp)
    {
        var best = Generate(pts, u, t0, t1);
        (var bestErr, var bestSplit) = MaxError(pts, best, u);
        if (bestErr > giveUp) return (best, bestErr, bestSplit, u);
        var bestU = (double[])u.Clone();
        u = (double[])u.Clone();
        for (int r = 0; r < MaxReparam; r++)
        {
            for (int i = 0; i < u.Length; i++) u[i] = Newton(best, pts[i], u[i]);
            var c = Generate(pts, u, t0, t1);
            (var e, var s) = MaxError(pts, c, u);
            if (e < bestErr) { best = c; bestErr = e; bestSplit = s; Array.Copy(u, bestU, u.Length); }
            else break;
        }
        return (best, bestErr, bestSplit, bestU);
    }

    private static (Cu, double, int) FitSingleTol(PointD[] pts, PointD t0, PointD t1, double tol)
    {
        var u = ChordParams(pts);
        var best = Generate(pts, u, t0, t1);
        (var bestErr, var bestSplit) = MaxError(pts, best, u);
        for (int r = 0; r < MaxReparam; r++)
        {
            if (bestErr <= tol) break;
            for (int i = 0; i < u.Length; i++) u[i] = Newton(best, pts[i], u[i]);
            var c = Generate(pts, u, t0, t1);
            (var e, var s) = MaxError(pts, c, u);
            if (e < bestErr) { best = c; bestErr = e; bestSplit = s; }
            else break;
        }
        return (best, bestErr, bestSplit);
    }

    private static void FitThroughRec(PointD[] pts, PointD t0, PointD t1, double tol, List<Cu> outList, int depth)
    {
        int n = pts.Length;
        if (n == 2)
        {
            double d = pts[0].DistanceTo(pts[1]) / 3;
            outList.Add(new Cu(pts[0], pts[0] + t0 * d, pts[1] - t1 * d, pts[1]));
            return;
        }
        (var curve, var err, var split) = FitSingleTol(pts, t0, t1, tol);
        if (err <= tol || depth >= MaxSplits) { outList.Add(curve); return; }
        split = Math.Clamp(split, 1, n - 2);
        var tc = (pts[split + 1] - pts[split - 1]).Normalized();
        if (tc == default) tc = t0;
        FitThroughRec(pts[..(split + 1)], t0, tc, tol, outList, depth + 1);
        FitThroughRec(pts[split..], tc, t1, tol, outList, depth + 1);
    }

    private static List<Cu> JoinPieces(PointD[] pts, List<Piece> pieces, PointD t0, double tol)
    {
        var outList = new List<Cu>();
        int start = 0, next = 0;
        var leaving = t0;
        while (next < pieces.Count)
        {
            var first = pieces[next];
            var curve = first.Curve; int end = first.End; var arriving = first.T1;
            next++;
            int last = Math.Min(pieces.Count, next + MaxJoins);
            for (int k = next; k < last; k++)
            {
                var piece = pieces[k];
                var (ok, joined, _) = FitPiece(pts[start..(piece.End + 1)], leaving, piece.T1, tol);
                if (!ok) break;
                curve = joined; end = piece.End; arriving = piece.T1;
                next++;
            }
            outList.Add(curve);
            start = end; leaving = arriving;
        }
        return outList;
    }

    private static void FitRec(PointD[] all, int start, int end, PointD t0, PointD t1, double tol, List<Piece> outList)
    {
        var pts = all[start..(end + 1)];
        int n = pts.Length;
        if (n == 2)
        {
            double d = Math.Min(pts[0].DistanceTo(pts[1]) / 3, tol);
            outList.Add(new Piece { Curve = new Cu(pts[0], pts[0] + t0 * d, pts[1] - t1 * d, pts[1]), End = end, T1 = t1 });
            return;
        }
        var (ok, curve, worst) = FitPiece(pts, t0, t1, tol);
        if (ok) { outList.Add(new Piece { Curve = curve, End = end, T1 = t1 }); return; }
        int margin = Math.Max(n * 2 / 5, 1);
        int split = Math.Clamp(worst, margin, n - 1 - margin);
        var tc = CenterTangent(pts, split, tol);
        if (tc == default) tc = t0;
        FitRec(all, start, start + split, t0, tc, tol, outList);
        FitRec(all, start + split, end, tc, t1, tol, outList);
    }

    private static (bool ok, Cu curve, int worst) FitPiece(PointD[] pts, PointD t0, PointD t1, double tol)
    {
        double giveUp = tol * 8 + pts[0].DistanceTo(pts[^1]) * 0.05;
        if (pts.Length >= CoarsePoints * 2)
        {
            int stride = pts.Length / CoarsePoints;
            var some = new List<PointD>();
            for (int i = 0; i < pts.Length; i += stride) some.Add(pts[i]);
            some.Add(pts[^1]);
            var sa = some.ToArray();
            var (_, err0, worst0, _) = FitSingleWithin(sa, ChordParams(sa), t0, t1, giveUp);
            if (err0 > tol) return (false, default, Math.Min(worst0 * stride, pts.Length - 1));
        }
        var (c, err, worst, u) = FitSingleWithin(pts, ChordParams(pts), t0, t1, giveUp);
        if (err > tol) return (false, default, worst);
        int b = Bulge(pts, c, u, tol);
        return b < 0 ? (true, c, 0) : (false, default, b);
    }

    private static PointD CenterTangent(PointD[] pts, int split, double tol)
    {
        var p = pts[split];
        var before = pts[0];
        for (int i = split - 1; i >= 0; i--) if (pts[i].DistanceTo(p) > tol) { before = pts[i]; break; }
        var after = pts[^1];
        for (int i = split + 1; i < pts.Length; i++) if (pts[i].DistanceTo(p) > tol) { after = pts[i]; break; }
        return (after - before).Normalized();
    }

    private static double LargestSecondDifference(Cu c)
        => Math.Max((c.P2 - c.P1 - (c.P1 - c.P0)).Length, (c.P3 - c.P2 - (c.P2 - c.P1)).Length);

    private static double DistToSegment(PointD a, PointD b, PointD p)
    {
        var ab = b - a;
        double l2 = ab.Dot(ab);
        double t = l2 > 0 ? Math.Clamp((p - a).Dot(ab) / l2, 0, 1) : 0;
        return p.DistanceTo(a + ab * t);
    }

    /// <summary>Index of the first pair of points between which the curve strays over 99% of tol from their chord, or -1.</summary>
    private static int Bulge(PointD[] pts, Cu c, double[] u, double tol)
    {
        double step = Math.Sqrt(0.01 * tol * 8 / (6 * LargestSecondDifference(c)));
        double limit = tol * 0.99;
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            double t0 = u[i], t1 = u[i + 1];
            double samples = Math.Max(Math.Ceiling(Math.Abs(t1 - t0) / step), 1.0);
            if (double.IsNaN(samples) || samples > MaxBulgeSamples) return i;
            int ns = (int)samples;
            for (int k = 1; k <= ns; k++)
            {
                double at = t0 + (t1 - t0) * k / (ns + 1);
                if (DistToSegment(pts[i], pts[i + 1], c.Eval(at)) > limit) return i;
            }
        }
        return -1;
    }
}
