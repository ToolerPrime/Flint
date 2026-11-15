/* Editable bezier paths (Pen and Subselection tools, Combine/Alter path).
 *
 * A path object is a RasterObject whose Vector art carries the editable
 * source in VectorArt.Path: subpaths of nodes with absolute bezier handles,
 * a fill colour, a stroke colour and a centred stroke width. The art's
 * shapes (fill, then stroke pieces on top) are built from it and rendered by
 * Tracer.Render, so pixels are exact and transforms re-render from the
 * source like any vector object. All types are immutable. Written for Flint. */
using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace Flint;

/// <summary>A node: its point and its two handles (a handle equal to the point is retracted).</summary>
public readonly record struct PathNode(VPt P, VPt In, VPt Out)
{
    public static PathNode Corner(double x, double y) => new(new VPt(x, y), new VPt(x, y), new VPt(x, y));
    public PathNode Moved(double dx, double dy) =>
        new(new VPt(P.X + dx, P.Y + dy), new VPt(In.X + dx, In.Y + dy), new VPt(Out.X + dx, Out.Y + dy));
    public bool HasIn => In != P;
    public bool HasOut => Out != P;
}

public sealed record SubPath(ImmutableArray<PathNode> Nodes, bool Closed);

/// <summary>Colours are BGRA packed like Surface.Pack; alpha 0 means none.</summary>
public sealed record PathSource(ImmutableArray<SubPath> Subpaths, uint Fill, uint Stroke, double StrokeWidth)
{
    public bool HasFill => Fill >> 24 != 0;
    public bool HasStroke => Stroke >> 24 != 0 && StrokeWidth > 0;
    public int NodeCount => Subpaths.Sum(s => s.Nodes.Length);

    public void Validate()
    {
        if (Subpaths.IsDefaultOrEmpty) throw new ArgumentException("a path needs at least one part");
        if (NodeCount > 100_000) throw new ArgumentException("the path has too many points");
        if (!(StrokeWidth >= 0 && StrokeWidth <= 512)) throw new ArgumentOutOfRangeException(nameof(StrokeWidth));
        foreach (var s in Subpaths)
        {
            if (s.Nodes.IsDefaultOrEmpty) throw new ArgumentException("a path part has no points");
            foreach (var n in s.Nodes)
                foreach (var p in new[] { n.P, n.In, n.Out })
                    if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > 1e6 || Math.Abs(p.Y) > 1e6)
                        throw new ArgumentException("a path point is out of range");
        }
    }

    public PathSource Shifted(double dx, double dy) =>
        this with { Subpaths = Subpaths.Select(s => s with { Nodes = s.Nodes.Select(n => n.Moved(dx, dy)).ToImmutableArray() }).ToImmutableArray() };

    public PathSource Mapped(Affine m)
    {
        VPt M(VPt p) { var (x, y) = m.Apply(p.X, p.Y); return new VPt(x, y); }
        return this with { Subpaths = Subpaths.Select(s => s with { Nodes = s.Nodes.Select(n => new PathNode(M(n.P), M(n.In), M(n.Out))).ToImmutableArray() }).ToImmutableArray() };
    }

    public JsonObject ToJson() => new()
    {
        ["fill"] = Fill, ["stroke"] = Stroke, ["width"] = StrokeWidth,
        ["subpaths"] = new JsonArray(Subpaths.Select(s => (JsonNode)new JsonObject
        {
            ["closed"] = s.Closed,
            ["n"] = new JsonArray(s.Nodes.SelectMany(n => new[] { n.P.X, n.P.Y, n.In.X, n.In.Y, n.Out.X, n.Out.Y }).Select(v => (JsonNode)v).ToArray()),
        }).ToArray()),
    };

    /// <summary>Null when missing or damaged (the object then stays a plain vector picture).</summary>
    public static PathSource? FromJson(JsonObject? j)
    {
        if (j == null) return null;
        try
        {
            var subs = ImmutableArray.CreateBuilder<SubPath>();
            foreach (var sn in j["subpaths"]!.AsArray())
            {
                var v = sn!["n"]!.AsArray().Select(x => x!.GetValue<double>()).ToArray();
                if (v.Length == 0 || v.Length % 6 != 0) return null;
                var nodes = Enumerable.Range(0, v.Length / 6).Select(i => new PathNode(new VPt(v[i * 6], v[i * 6 + 1]), new VPt(v[i * 6 + 2], v[i * 6 + 3]), new VPt(v[i * 6 + 4], v[i * 6 + 5]))).ToImmutableArray();
                subs.Add(new SubPath(nodes, sn["closed"]?.GetValue<bool>() ?? false));
            }
            var p = new PathSource(subs.ToImmutable(), j["fill"]?.GetValue<uint>() ?? 0, j["stroke"]?.GetValue<uint>() ?? 0, j["width"]?.GetValue<double>() ?? 0);
            p.Validate();
            return p;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or NullReferenceException) { return null; }
    }

    /// <summary>
    /// A rectangle or ellipse primitive as a path in its own pixel space, drawn like the primitive: filled,
    /// or an outline as an outer and an inner shape (square corners kept; a stroke wider than half fills it).
    /// </summary>
    public static PathSource FromPrimitive(PrimitiveSpec s)
    {
        ImmutableArray<PathNode> Shape(double l, double t, double r, double b)
        {
            if (!s.Ellipse)
                return ImmutableArray.Create(PathNode.Corner(l, t), PathNode.Corner(r, t), PathNode.Corner(r, b), PathNode.Corner(l, b));
            double cx = (l + r) / 2, cy = (t + b) / 2, rx = (r - l) / 2, ry = (b - t) / 2, k = 0.5522847498;
            return ImmutableArray.Create(
                new PathNode(new VPt(cx, t), new VPt(cx - rx * k, t), new VPt(cx + rx * k, t)),
                new PathNode(new VPt(r, cy), new VPt(r, cy - ry * k), new VPt(r, cy + ry * k)),
                new PathNode(new VPt(cx, b), new VPt(cx + rx * k, b), new VPt(cx - rx * k, b)),
                new PathNode(new VPt(l, cy), new VPt(l, cy + ry * k), new VPt(l, cy - ry * k)));
        }
        var subs = ImmutableArray.CreateBuilder<SubPath>();
        subs.Add(new SubPath(Shape(0, 0, s.Width, s.Height), true));
        int w = s.Stroke;
        if (!s.Filled && 2 * w < s.Width && 2 * w < s.Height) subs.Add(new SubPath(Shape(w, w, s.Width - w, s.Height - w), true));   // the hole (even-odd)
        return new PathSource(subs.ToImmutable(), s.Colour, 0, 0);
    }
}

public static class PathSources
{
    /// <summary>Curve flattening tolerance for paths, in art pixels.</summary>
    public const double FlattenTolerance = 0.05;

    /// <summary>One subpath as a polyline (closed ones without the repeated first point).</summary>
    public static List<VPt> Flatten(SubPath s, double tol = FlattenTolerance)
    {
        var poly = new List<VPt> { s.Nodes[0].P };
        int n = s.Nodes.Length, segs = s.Closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            var a = s.Nodes[i]; var b = s.Nodes[(i + 1) % n];
            if (!a.HasOut && !b.HasIn) poly.Add(b.P);
            else FireworksPng.Flatten(a.P, a.Out, b.In, b.P, poly, 0, tol);
        }
        if (s.Closed && poly.Count > 1 && poly[^1] == poly[0]) poly.RemoveAt(poly.Count - 1);
        return poly;
    }

    /// <summary>The fill and stroke shapes of a path, in its own space (fill first, stroke on top).</summary>
    public static List<VShape> Shapes(PathSource src)
    {
        var shapes = new List<VShape>();
        var polys = src.Subpaths.Select(s => (pts: Flatten(s), s.Closed)).ToList();
        if (src.HasFill)
        {
            var fill = new VShape { Colour = src.Fill };
            foreach (var (pts, _) in polys) if (pts.Count >= 3) fill.Contours.Add(pts);
            if (fill.Contours.Count > 0) shapes.Add(fill);
        }
        if (src.HasStroke)
        {
            foreach (var (pts, closed) in polys) if (pts.Count >= 2) FireworksPng.Stroke(pts, closed, src.StrokeWidth, src.Stroke, shapes);
            // Thin strokes: the shared stroker leaves corners open below 2 px; close them with small round dots.
            if (src.StrokeWidth < 2)
                foreach (var s in src.Subpaths)
                    foreach (var n in s.Nodes)
                    {
                        double r = src.StrokeWidth / 2;
                        shapes.Add(new VShape { Colour = src.Stroke, Contours = { Enumerable.Range(0, 12).Select(k => new VPt(n.P.X + r * Math.Cos(Math.PI * k / 6), n.P.Y + r * Math.Sin(Math.PI * k / 6))).ToList() } });
                    }
        }
        shapes.RemoveAll(s => s.Contours.Count == 0);
        return shapes;
    }

    /// <summary>
    /// A new path object. The art keeps its own origin at the shapes' whole-pixel top-left (the
    /// source is shifted with it, exactly); <paramref name="m"/> places the path's own space on the canvas.
    /// </summary>
    public static RasterObject Create(PathSource src, Affine m, Resample mode, string name = "Path")
    {
        src.Validate();
        var shapes = Shapes(src);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var s in shapes) foreach (var c in s.Contours) foreach (var p in c)
        { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
        if (minX == double.MaxValue) throw new ArgumentException("there is nothing to draw: a filled path needs three points or a curve, an outline needs a stroke");
        int x0 = (int)Math.Floor(minX), y0 = (int)Math.Floor(minY);
        int w = Math.Max(1, (int)Math.Ceiling(maxX) - x0), h = Math.Max(1, (int)Math.Ceiling(maxY) - y0);
        if ((long)w * h > 16_777_216) throw new ArgumentException("the path is larger than Flint can draw");
        foreach (var s in shapes)
            for (int k = 0; k < s.Contours.Count; k++) s.Contours[k] = s.Contours[k].Select(p => new VPt(p.X - x0, p.Y - y0)).ToList();
        var art = new VectorArt { W = w, H = h, Shapes = shapes, KeepOrder = true, Path = src.Shifted(-x0, -y0) };
        var matrix = Affine.Translate(x0, y0).Then(m);
        var r = Tracer.Render(art, matrix, mode == Resample.Smooth);
        return new RasterObject(ImageOps.FromPixels(r.w, r.h, r.px)) { Name = name, X = r.x, Y = r.y, Vector = art, Matrix = matrix, Mode = mode };
    }

    /// <summary>The same object with new path geometry or style: keeps name, look, placement, effects and groups.</summary>
    public static RasterObject Replace(RasterObject old, PathSource src)
    {
        var o = Create(src, old.Matrix, old.Mode, old.Name);
        if (old.Effects is { Count: > 0 }) o = o.WithEffectList(old.Effects);
        if (old.GroupPath.Count > 0) o = o.WithGroups(old.GroupPath);
        o.Visible = old.Visible; o.Opacity = old.Opacity; o.Blend = old.Blend;
        return o;
    }

    /// <summary>The editable path of an object: its own, or a rectangle/ellipse primitive converted (Fireworks does this).</summary>
    public static PathSource? Of(RasterObject o) =>
        o.Vector?.Path ?? (o.Primitive is { } p ? PathSource.FromPrimitive(p) : null);

    /// <summary>A primitive as a real path object at the same place (its width/height settings then no longer apply).</summary>
    public static RasterObject? AsPathObject(RasterObject o)
    {
        if (o.Vector?.Path != null) return o;
        if (o.Primitive is not { } p) return null;
        var m = o.Original != null || o.Vector != null ? o.Matrix : Affine.Translate(o.X, o.Y);
        var r = Create(PathSource.FromPrimitive(p), m, o.Mode, o.Name);
        r.Visible = o.Visible; r.Opacity = o.Opacity; r.Blend = o.Blend;
        if (o.Effects is { Count: > 0 }) r = r.WithEffectList(o.Effects);
        if (o.GroupPath.Count > 0) r = r.WithGroups(o.GroupPath);
        return r;
    }

    // ---------- hit testing and node edits (all pure) ----------

    public enum Part { Node, In, Out }

    /// <summary>The nearest node or handle within <paramref name="radius"/> canvas pixels.</summary>
    /// <param name="showHandles">Which nodes' handles can be grabbed (the ones drawn); null = all.</param>
    public static (int sub, int node, Part part)? NearestNode(PathSource src, Affine m, double x, double y, double radius, Func<int, int, bool>? showHandles = null)
    {
        (int, int, Part)? best = null;
        double bd = radius * radius;
        for (int si = 0; si < src.Subpaths.Length; si++)
            for (int ni = 0; ni < src.Subpaths[si].Nodes.Length; ni++)
            {
                var n = src.Subpaths[si].Nodes[ni];
                void Try(VPt p, Part part)
                {
                    var (px, py) = m.Apply(p.X, p.Y);
                    double d = (px - x) * (px - x) + (py - y) * (py - y);
                    if (d <= bd) { bd = d; best = (si, ni, part); }
                }
                Try(n.P, Part.Node);
                if (showHandles?.Invoke(si, ni) ?? true) { if (n.HasIn) Try(n.In, Part.In); if (n.HasOut) Try(n.Out, Part.Out); }
            }
        return best;
    }

    /// <summary>The nearest point on a segment within <paramref name="radius"/>: (subpath, segment start node, t).</summary>
    public static (int sub, int seg, double t)? NearestSegment(PathSource src, Affine m, double x, double y, double radius)
    {
        (int, int, double)? best = null;
        double bd = radius * radius;
        for (int si = 0; si < src.Subpaths.Length; si++)
        {
            var s = src.Subpaths[si];
            int n = s.Nodes.Length, segs = s.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = s.Nodes[i]; var b = s.Nodes[(i + 1) % n];
                // Short pieces along the curve, and the click projected onto each, so long edges hit everywhere.
                int steps = 64;
                var prev = m.Apply(a.P.X, a.P.Y);
                for (int k = 1; k <= steps; k++)
                {
                    var q = Bez(a.P, a.Out, b.In, b.P, k / (double)steps);
                    var cur = m.Apply(q.X, q.Y);
                    double vx = cur.x - prev.x, vy = cur.y - prev.y, vv = vx * vx + vy * vy;
                    double u = vv < 1e-18 ? 0 : Math.Clamp(((x - prev.x) * vx + (y - prev.y) * vy) / vv, 0, 1);
                    double px = prev.x + u * vx, py = prev.y + u * vy;
                    double d = (px - x) * (px - x) + (py - y) * (py - y);
                    if (d < bd) { bd = d; best = (si, i, (k - 1 + u) / steps); }
                    prev = cur;
                }
            }
        }
        return best;
    }

    public static VPt Bez(VPt p0, VPt p1, VPt p2, VPt p3, double t)
    {
        double u = 1 - t;
        return new VPt(u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
                       u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y);
    }

    /// <summary>Insert a node at t on a segment without changing the shape (de Casteljau).</summary>
    public static PathSource SplitAt(PathSource src, int sub, int seg, double t)
    {
        var s = src.Subpaths[sub];
        int n = s.Nodes.Length, j = (seg + 1) % n;
        var a = s.Nodes[seg]; var b = s.Nodes[j];
        VPt L(VPt p, VPt q) => new(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t);
        var p01 = L(a.P, a.Out); var p12 = L(a.Out, b.In); var p23 = L(b.In, b.P);
        var p012 = L(p01, p12); var p123 = L(p12, p23); var mid = L(p012, p123);
        var nodes = s.Nodes.SetItem(seg, a with { Out = p01 }).SetItem(j, b with { In = p23 }).Insert(seg + 1, new PathNode(mid, p012, p123));
        return src with { Subpaths = src.Subpaths.SetItem(sub, s with { Nodes = nodes }) };
    }

    /// <summary>Move whole nodes (with their handles) by an art-space amount.</summary>
    public static PathSource MoveNodes(PathSource src, IReadOnlyCollection<(int sub, int node)> sel, double dx, double dy)
    {
        var subs = src.Subpaths.ToBuilder();
        foreach (var (si, ni) in sel)
            if (si < subs.Count && ni < subs[si].Nodes.Length)
                subs[si] = subs[si] with { Nodes = subs[si].Nodes.SetItem(ni, subs[si].Nodes[ni].Moved(dx, dy)) };
        return src with { Subpaths = subs.ToImmutable() };
    }

    /// <summary>Set one handle; a smooth node (handles in line) keeps its other handle in line at its own length unless <paramref name="breakPair"/>.</summary>
    public static PathSource SetHandle(PathSource src, int sub, int node, Part part, VPt at, bool breakPair)
    {
        var s = src.Subpaths[sub];
        var n = s.Nodes[node];
        var other = part == Part.In ? n.Out : n.In;
        bool smooth = !breakPair && n.HasIn && n.HasOut && Smooth(n);
        var next = part == Part.In ? n with { In = at } : n with { Out = at };
        if (smooth)
        {
            double len = Math.Sqrt((other.X - n.P.X) * (other.X - n.P.X) + (other.Y - n.P.Y) * (other.Y - n.P.Y));
            double dx = n.P.X - at.X, dy = n.P.Y - at.Y, dl = Math.Sqrt(dx * dx + dy * dy);
            if (dl > 1e-9)
            {
                var o = new VPt(n.P.X + dx / dl * len, n.P.Y + dy / dl * len);
                next = part == Part.In ? next with { Out = o } : next with { In = o };
            }
        }
        return src with { Subpaths = src.Subpaths.SetItem(sub, s with { Nodes = s.Nodes.SetItem(node, next) }) };
    }

    /// <summary>Handles on one line through the point (within about 1 degree).</summary>
    public static bool Smooth(PathNode n)
    {
        double ax = n.In.X - n.P.X, ay = n.In.Y - n.P.Y, bx = n.Out.X - n.P.X, by = n.Out.Y - n.P.Y;
        double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
        return la > 1e-9 && lb > 1e-9 && (ax * bx + ay * by) / (la * lb) < -0.9998;
    }

    /// <summary>Delete nodes. Parts left too short are dropped; null when nothing is left.</summary>
    public static PathSource? DeleteNodes(PathSource src, IReadOnlyCollection<(int sub, int node)> sel)
    {
        var subs = new List<SubPath>();
        for (int si = 0; si < src.Subpaths.Length; si++)
        {
            var s = src.Subpaths[si];
            var keep = s.Nodes.Where((_, ni) => !sel.Contains((si, ni))).ToImmutableArray();
            if (keep.Length >= (s.Closed ? 3 : 2)) subs.Add(s with { Nodes = keep });
        }
        return subs.Count == 0 ? null : src with { Subpaths = subs.ToImmutableArray() };
    }
}
