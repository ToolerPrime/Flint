/* Ruler guides: the data, the file format, the Fireworks import and the
 * snapping maths. No window code, so it is testable on its own. */
using System.Text.Json.Nodes;

namespace Flint;

/// <summary>A guide on a pixel edge: Vertical guides have an x, horizontal ones a y.</summary>
public readonly record struct Guide(bool Vertical, int Pos);

public static class GuideSet
{
    public const int SnapPixels = 6;   // screen pixels

    public static JsonArray ToJson(IEnumerable<Guide> guides) =>
        new(guides.Select(g => (JsonNode)new JsonObject { ["vertical"] = g.Vertical, ["pos"] = g.Pos }).ToArray());

    public static IReadOnlyList<Guide> FromJson(JsonArray? a) =>
        a == null ? Array.Empty<Guide>()
            : a.Where(n => n != null).Select(n => new Guide(n!["vertical"]?.GetValue<bool>() ?? false, n["pos"]?.GetValue<int>() ?? 0)).ToList();

    /// <summary>
    /// Fireworks: GDS > GDV (vertical, x) and GDH (horizontal, y), each a list of GDC floats.
    /// Checked on UIQ.png, which has two vertical guides (15 and 193) and no horizontal ones.
    /// </summary>
    public static IReadOnlyList<Guide> FromFireworks(FwNode root)
    {
        var list = new List<Guide>();
        var gds = root.Get("GDS");
        if (gds == null) return list;
        foreach (var (tag, vertical) in new[] { ("GDV", true), ("GDH", false) })
            foreach (var c in gds.Get(tag)?.All("GDC") ?? Enumerable.Empty<FwNode>())
                if (double.TryParse(c.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
                    list.Add(new Guide(vertical, (int)Math.Round(v)));
        return list;
    }

    /// <summary>
    /// Snap a position along one axis, in image pixels (fractions allowed, edges are integers),
    /// to the nearest of: the guides on this axis, the canvas edges and the canvas centre.
    /// Returns the snapped position, or null when nothing is within SnapPixels on screen.
    /// </summary>
    public static double? SnapAxis(double pos, bool vertical, IEnumerable<Guide> guides, int canvasSize, int zoom)
    {
        double best = double.MaxValue, hit = 0;
        void Try(double c)
        {
            double d = Math.Abs(c - pos) * zoom;
            if (d <= SnapPixels && d < best) { best = d; hit = c; }
        }
        foreach (var g in guides) if (g.Vertical == vertical) Try(g.Pos);
        Try(0); Try(canvasSize); Try(canvasSize / 2.0);
        return best == double.MaxValue ? null : hit;
    }

    /// <summary>
    /// Pixel index for a snapped edge. The start of a drag sits just right of / below the line;
    /// an end point is inclusive, so it lands on the pixel before the line when dragging right/down.
    /// </summary>
    public static int EdgeToPixel(double edge, int? startPixel) =>
        edge != Math.Floor(edge) ? (int)Math.Floor(edge)
        : startPixel is { } s && edge > s ? (int)edge - 1 : (int)edge;
}
