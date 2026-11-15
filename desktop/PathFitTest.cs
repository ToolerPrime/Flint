/* PathFit: cubic fitting of point runs.
 * Flint.exe --pathfittest */
namespace Flint;

public static class PathFitTest
{
    private static int _fails;
    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private static PointD Cub(PointD a, PointD b, PointD c, PointD d, double t)
    {
        double s = 1 - t;
        return a * (s * s * s) + b * (3 * s * s * t) + c * (3 * s * t * t) + d * (t * t * t);
    }

    private static PointD[] Dense(List<(PointD p0, PointD p1, PointD p2, PointD p3)> cs, int n = 2000)
    {
        var l = new List<PointD>();
        foreach (var c in cs) for (int i = 0; i <= n; i++) l.Add(Cub(c.p0, c.p1, c.p2, c.p3, i / (double)n));
        return l.ToArray();
    }

    private static double DistSeg(PointD a, PointD b, PointD p)
    {
        var ab = b - a; double l2 = ab.Dot(ab);
        double t = l2 > 0 ? Math.Clamp((p - a).Dot(ab) / l2, 0, 1) : 0;
        return p.DistanceTo(a + ab * t);
    }

    /// <summary>Largest distance from a point to the dense polyline of the fit.</summary>
    private static double Farthest(IReadOnlyList<PointD> pts, List<(PointD p0, PointD p1, PointD p2, PointD p3)> cs)
    {
        var d = Dense(cs);
        double worst = 0;
        foreach (var p in pts)
        {
            double best = double.MaxValue;
            for (int i = 0; i + 1 < d.Length; i++) best = Math.Min(best, DistSeg(d[i], d[i + 1], p));
            worst = Math.Max(worst, best);
        }
        return worst;
    }

    private static bool Joined(List<(PointD p0, PointD p1, PointD p2, PointD p3)> cs)
    {
        for (int i = 0; i + 1 < cs.Count; i++) if (cs[i].p3 != cs[i + 1].p0) return false;
        return true;
    }

    private static bool Finite(List<(PointD p0, PointD p1, PointD p2, PointD p3)> cs)
        => cs.All(c => c.p0.IsFinite && c.p1.IsFinite && c.p2.IsFinite && c.p3.IsFinite);

    public static int Run()
    {
        Console.WriteLine("Refit a known cubic");
        {
            PointD a = new(0, 0), b = new(30, 60), c = new(70, 60), d = new(100, 0);
            var pts = Enumerable.Range(0, 41).Select(i => Cub(a, b, c, d, i / 40.0)).ToList();
            var fit = PathFit.FitCubics(pts, (b - a).Normalized(), (d - c).Normalized(), 0.05);
            Check(fit.Count == 1, $"one cubic ({fit.Count})");
            Check(fit.Count == 1 && fit[0].p1.DistanceTo(b) < 1.0, "first handle is near the original");
            Check(Farthest(pts, fit) <= 0.05 + 1e-6, "every sample within tolerance of the fit");
            var auto = PathFit.FitCubicsAuto(pts, 0.5);
            Check(Farthest(pts, auto) <= 0.5 + 1e-6, $"estimated tangents also stay within tolerance ({auto.Count} cubics)");
            Check(fit[0].p0 == pts[0] && fit[^1].p3 == pts[^1], "endpoints exact");
        }

        Console.WriteLine("Circle");
        {
            var pts = Enumerable.Range(0, 64).Select(i => { double t = i / 64.0 * Math.Tau; return new PointD(50 * Math.Cos(t), 50 * Math.Sin(t)); }).ToList();
            var fit = PathFit.FitCubicsAuto(pts, 0.25);
            Check(fit.Count <= 8, $"64 samples fit with <= 8 cubics ({fit.Count})");
            Check(Farthest(pts, fit) <= 0.25 + 1e-6, "within 0.25 px");
            Check(Joined(fit), "cubics join end to start");
            Check(fit[0].p0 == pts[0] && fit[^1].p3 == pts[^1], "endpoints exact");
            var closed = new List<PointD>(pts) { pts[0] };
            var tf = PathFit.FitCubics(closed, new PointD(0, 1), new PointD(0, 1), 0.25);
            Check(tf.Count <= 8 && Farthest(closed, tf) <= 0.25 + 1e-6, $"closed loop with true tangents ({tf.Count} cubics)");
            var th = PathFit.FitCubicsThrough(closed, new PointD(0, 1), new PointD(0, 1), 0.25);
            Check(th.Count <= 8 && Farthest(closed, th) <= 0.25 + 1e-6, $"through-fit of the loop ({th.Count} cubics)");
        }

        Console.WriteLine("Straight and degenerate");
        {
            var line = Enumerable.Range(0, 20).Select(i => new PointD(i * 3, i * 2)).ToList();
            var fit = PathFit.FitCubicsAuto(line, 0.1);
            bool onLine = fit.Count == 1 && new[] { fit[0].p1, fit[0].p2 }.All(h => Math.Abs(h.X * 2 - h.Y * 3) < 1e-9);
            Check(fit.Count == 1 && onLine, "collinear points: one segment, handles on the line");
            Check(fit.Count == 1 && fit[0].p0 == line[0] && fit[0].p3 == line[^1], "endpoints exact");
            var skew = PathFit.FitCubics(line, new PointD(0, 1), new PointD(1, 0), 0.1);
            Check(skew.Count == 1 && fit.Count == 1 && skew[0].p1 == fit[0].p1, "collinear with odd tangents still a straight segment");

            Check(PathFit.FitCubicsAuto(new List<PointD>(), 1).Count == 0, "0 points: nothing");
            Check(PathFit.FitCubicsAuto(new[] { new PointD(1, 1) }, 1).Count == 0, "1 point: nothing");
            var two = PathFit.FitCubicsAuto(new[] { new PointD(0, 0), new PointD(9, 0) }, 1);
            Check(two.Count == 1 && two[0].p0 == new PointD(0, 0) && two[0].p3 == new PointD(9, 0) && Finite(two), "2 points: one segment");
            var dup = PathFit.FitCubicsAuto(new[] { new PointD(2, 2), new PointD(2, 2), new PointD(2, 2) }, 1);
            Check(dup.Count == 0 && Finite(dup), "all points equal: nothing, no throw");
            var dups = PathFit.FitCubicsAuto(new[] { new PointD(0, 0), new PointD(0, 0), new PointD(5, 5), new PointD(5, 5), new PointD(10, 0) }, 0.5);
            Check(dups.Count >= 1 && Finite(dups) && dups[0].p0 == new PointD(0, 0) && dups[^1].p3 == new PointD(10, 0), "duplicate points are merged");
            var nan = PathFit.FitCubicsAuto(new[] { new PointD(0, 0), new PointD(double.NaN, 1), new PointD(4, 0), new PointD(8, 3) }, double.NaN);
            Check(Finite(nan), "NaN input and NaN tolerance do not poison the output");
            var back = PathFit.FitCubicsAuto(new[] { new PointD(0, 0), new PointD(10, 0), new PointD(0, 0.5) }, 0.2);
            Check(back.Count >= 1 && Finite(back), "a polyline that doubles back does not throw");
            Check(PathFit.FitCubicsThrough(new[] { new PointD(0, 0) }, default, default, 1).Count == 0, "through-fit of one point: nothing");
        }

        Console.WriteLine("Ported from fit.rs");
        {
            // zig-zag cannot be one cubic; pieces stay joined
            var pts = Enumerable.Range(0, 41).Select(i => new PointD(i, Math.Sin(i * 0.5) * 10)).ToList();
            var fit = PathFit.FitCubics(pts, new PointD(1, 0), new PointD(1, 0), 0.1);
            Check(fit.Count > 1 && Joined(fit), $"waves need several cubics, G0 joined ({fit.Count})");
            Check(Farthest(pts, fit) <= 0.1 + 1e-6, "waves within tolerance");

            // a smooth outline keeps its bow between coarse samples
            Func<double, PointD> node = t => new PointD(100 * t, 4 * t * (1 - t));
            var coarse = Enumerable.Range(0, 9).Select(i => node(i / 8.0)).ToList();
            var t0 = new PointD(1, 0.04).Normalized(); var t1 = new PointD(1, -0.04).Normalized();
            var thr = PathFit.FitCubicsThrough(coarse, t0, t1, 0.001);
            double worst = 0;
            foreach (var q in Dense(thr, 100)) worst = Math.Max(worst, Math.Abs(q.Y - 4 * (q.X / 100) * (1 - q.X / 100)));
            Check(worst < 0.01, $"through-fit follows the curve between points (off by {worst:0.0000})");

            // wide points: the curve stays near the polyline
            var wide = new[] { new PointD(0, 0), new PointD(100, 20), new PointD(200, 0), new PointD(300, 20), new PointD(400, 0) };
            var wf = PathFit.FitCubics(wide, new PointD(0.6, 0.8), new PointD(0.6, -0.8), 1.0);
            double far = 0;
            foreach (var q in Dense(wf, 500))
            {
                double best = double.MaxValue;
                for (int i = 0; i + 1 < wide.Length; i++) best = Math.Min(best, DistSeg(wide[i], wide[i + 1], q));
                far = Math.Max(far, best);
            }
            Check(far <= 1.0 + 1e-6, $"curve stays within tolerance of the chords ({far:0.000})");
        }

        Console.WriteLine(_fails == 0 ? "PATHFIT TEST: ALL PASSED" : $"PATHFIT TEST: {_fails} FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
