/* Tween easing and curved motion paths.
 *
 * The cubic-bezier time solve (Newton steps with a bisection fallback) and the
 * arc-length table used to move at an even speed along a curved path are
 * translated from EffectCraft, crates/keyframe/src/lib.rs (solve_u, bez,
 * SpatialSeg) (https://github.com/storytold/effectcraft). Copyright (c) 2026
 * ArtCraft Team and the EffectCraft contributors. MIT licence (MIT or
 * Apache-2.0 at the user's option); see CREDITS_LICENSES.md.
 *
 * The named curves (quad, cubic, back, bounce, elastic) are the standard
 * published easing formulas, written for Flint. */
namespace Flint;

public enum EaseKind
{
    Linear, InQuad, OutQuad, InOutQuad, InCubic, OutCubic, InOutCubic,
    InBack, OutBack, InOutBack, InBounce, OutBounce, InOutBounce,
    InElastic, OutElastic, InOutElastic, Hold, Custom,
}

/// <summary>An easing curve: progress 0..1 in, eased progress out (back and elastic overshoot).</summary>
public sealed record Ease(EaseKind Kind, double X1 = 0.42, double Y1 = 0, double X2 = 0.58, double Y2 = 1)
{
    public static readonly Ease Linear = new(EaseKind.Linear);

    public static string Describe(EaseKind k) => k switch
    {
        EaseKind.Linear => "Linear",
        EaseKind.InQuad => "Ease in (quad)", EaseKind.OutQuad => "Ease out (quad)", EaseKind.InOutQuad => "Ease in-out (quad)",
        EaseKind.InCubic => "Ease in (cubic)", EaseKind.OutCubic => "Ease out (cubic)", EaseKind.InOutCubic => "Ease in-out (cubic)",
        EaseKind.InBack => "Back in", EaseKind.OutBack => "Back out", EaseKind.InOutBack => "Back in-out",
        EaseKind.InBounce => "Bounce in", EaseKind.OutBounce => "Bounce out", EaseKind.InOutBounce => "Bounce in-out",
        EaseKind.InElastic => "Elastic in", EaseKind.OutElastic => "Elastic out", EaseKind.InOutElastic => "Elastic in-out",
        EaseKind.Hold => "Hold (jump at the end)",
        _ => "Custom curve",
    };

    public double Apply(double t)
    {
        t = Math.Clamp(t, 0, 1);
        const double c1 = 1.70158, c2 = c1 * 1.525, c3 = c1 + 1, c4 = 2 * Math.PI / 3, c5 = 2 * Math.PI / 4.5;
        return Kind switch
        {
            EaseKind.Linear => t,
            EaseKind.InQuad => t * t,
            EaseKind.OutQuad => 1 - (1 - t) * (1 - t),
            EaseKind.InOutQuad => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2,
            EaseKind.InCubic => t * t * t,
            EaseKind.OutCubic => 1 - Math.Pow(1 - t, 3),
            EaseKind.InOutCubic => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2,
            EaseKind.InBack => c3 * t * t * t - c1 * t * t,
            EaseKind.OutBack => 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2),
            EaseKind.InOutBack => t < 0.5
                ? Math.Pow(2 * t, 2) * ((c2 + 1) * 2 * t - c2) / 2
                : (Math.Pow(2 * t - 2, 2) * ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2,
            EaseKind.InBounce => 1 - BounceOut(1 - t),
            EaseKind.OutBounce => BounceOut(t),
            EaseKind.InOutBounce => t < 0.5 ? (1 - BounceOut(1 - 2 * t)) / 2 : (1 + BounceOut(2 * t - 1)) / 2,
            EaseKind.InElastic => t is 0 or 1 ? t : -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * c4),
            EaseKind.OutElastic => t is 0 or 1 ? t : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1,
            EaseKind.InOutElastic => t is 0 or 1 ? t : t < 0.5
                ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * c5)) / 2
                : Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * c5) / 2 + 1,
            EaseKind.Hold => t < 1 ? 0 : 1,
            _ => CubicBezier(X1, Y1, X2, Y2, t),
        };
    }

    private static double BounceOut(double t)
    {
        const double n = 7.5625, d = 2.75;
        if (t < 1 / d) return n * t * t;
        if (t < 2 / d) { t -= 1.5 / d; return n * t * t + 0.75; }
        if (t < 2.5 / d) { t -= 2.25 / d; return n * t * t + 0.9375; }
        t -= 2.625 / d; return n * t * t + 0.984375;
    }

    // ---- translated from EffectCraft keyframe: bez, bez_d, solve_u ----

    internal static double Bez(double p0, double p1, double p2, double p3, double u)
    {
        double v = 1 - u;
        return v * v * v * p0 + 3 * v * v * u * p1 + 3 * v * u * u * p2 + u * u * u * p3;
    }

    private static double BezD(double p0, double p1, double p2, double p3, double u)
    {
        double v = 1 - u;
        return 3 * v * v * (p1 - p0) + 6 * v * u * (p2 - p1) + 3 * u * u * (p3 - p2);
    }

    /// <summary>The curve parameter u where the bezier's x equals <paramref name="x"/> (x control points must keep x increasing).</summary>
    private static double SolveU(double x0, double x1, double x2, double x3, double x)
    {
        double u = (x - x0) / (x3 - x0);
        for (int i = 0; i < 8; i++)
        {
            double f = Bez(x0, x1, x2, x3, u) - x, d = BezD(x0, x1, x2, x3, u);
            if (Math.Abs(d) < 1e-12) break;
            if (Math.Abs(f / d) < 1e-12) return u;
            double nu = u - f / d;
            if (nu < 0 || nu > 1) break;
            u = nu;
        }
        double lo = 0, hi = 1;
        for (int i = 0; i < 60; i++)
        {
            double mid = (lo + hi) * 0.5;
            if (Bez(x0, x1, x2, x3, mid) < x) lo = mid; else hi = mid;
        }
        return (lo + hi) * 0.5;
    }

    /// <summary>CSS-style cubic-bezier(x1, y1, x2, y2) at time t.</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double t)
    {
        x1 = Math.Clamp(x1, 0, 1); x2 = Math.Clamp(x2, 0, 1);
        return Bez(0, y1, y2, 1, SolveU(0, x1, x2, 1, t));
    }
}

/// <summary>
/// A curved motion path from (0,0) to (dx,dy) bending towards a control
/// point, walked at an even speed (arc-length table, after EffectCraft's SpatialSeg).
/// </summary>
public sealed class MotionPath
{
    private const int N = 96;
    private readonly double[] _px = new double[4], _py = new double[4];
    private readonly double[] _table = new double[N + 1];

    /// <param name="cx">Control point (quadratic), relative to the start.</param>
    public MotionPath(double dx, double dy, double cx, double cy)
    {
        // The quadratic curve as an exact cubic: inner points 2/3 of the way to the control.
        _px[1] = cx * 2 / 3; _py[1] = cy * 2 / 3;
        _px[2] = dx + (cx - dx) * 2 / 3; _py[2] = dy + (cy - dy) * 2 / 3;
        _px[3] = dx; _py[3] = dy;
        double acc = 0, lx = 0, ly = 0;
        for (int i = 1; i <= N; i++)
        {
            var (x, y) = At(i / (double)N);
            acc += Math.Sqrt((x - lx) * (x - lx) + (y - ly) * (y - ly));
            _table[i] = acc;
            (lx, ly) = (x, y);
        }
    }

    public double Length => _table[N];

    private (double x, double y) At(double u) => (Ease.Bez(_px[0], _px[1], _px[2], _px[3], u), Ease.Bez(_py[0], _py[1], _py[2], _py[3], u));

    /// <summary>The point a fraction <paramref name="s"/> of the way along by distance (s may overshoot for back/elastic eases).</summary>
    public (double x, double y) Point(double s)
    {
        if (Length < 1e-9) return At(Math.Clamp(s, 0, 1));
        if (s <= 0 || s >= 1)
        {
            // Overshoot continues along the end tangent.
            var (ex, ey) = At(s <= 0 ? 0 : 1);
            var (nx, ny) = At(s <= 0 ? 1.0 / N : 1 - 1.0 / N);
            double seg = Math.Max(1e-9, Math.Sqrt((ex - nx) * (ex - nx) + (ey - ny) * (ey - ny)));
            double over = (s <= 0 ? -s : s - 1) * Length;
            return (ex + (ex - nx) / seg * over, ey + (ey - ny) / seg * over);
        }
        double target = s * Length;
        int i = Array.BinarySearch(_table, target);
        if (i < 0) i = ~i;
        i = Math.Clamp(i, 1, N);
        double a = _table[i - 1], b = _table[i];
        double u = (i - 1 + (b > a ? (target - a) / (b - a) : 0)) / N;
        return At(u);
    }
}

/// <summary>
/// What a tween does to one object across the frames after its start frame:
/// move (straight or along a curve), scale to a size, rotate, fade, with an
/// ease. Step k of N uses t = k/N; the start frame is t = 0. Moves are rounded
/// to whole pixels so pixel art stays sharp. Never changed once stored in a span.
/// </summary>
public sealed class TweenSpec
{
    public int NewFrames;
    public double Dx, Dy;
    public double ScaleFrom = 100, ScaleTo = 100;      // percent
    public double? OpacityFrom, OpacityTo;             // percent; null = keep the object's own
    public double Rotate;                              // degrees, + clockwise
    public Ease Ease = Ease.Linear;
    /// <summary>Curved path: the control point, relative to the start. Null = straight.</summary>
    public double? CurveX, CurveY;

    public bool Moves => Dx != 0 || Dy != 0;
    public bool Changes => Moves || ScaleFrom != 100 || ScaleTo != 100 || Rotate != 0 || OpacityFrom.HasValue || OpacityTo.HasValue;
    public TweenSpec Clone() => (TweenSpec)MemberwiseClone();
}

/// <summary>An editable tween: object <c>ObjectIndex</c> of the layer's start cel, over the next <c>Frames</c> frames.</summary>
/// <remarks>
/// BaseOpacity: the object's own opacity before a fade was written into it, so
/// turning the fade off again restores it. StartCount: how many objects the
/// start cel held, so Edit refuses rather than tween the wrong object after
/// objects there were added or deleted (-1 = unknown).
/// </remarks>
public sealed record TweenSpan(int Start, int Frames, int ObjectIndex, TweenSpec Spec, byte BaseOpacity = 255, int StartCount = -1)
{
    public int End => Start + Frames;
    /// <summary>The generated frames (not the start frame, where a new tween can begin).</summary>
    public bool Covers(int frame) => frame > Start && frame <= End;
}
