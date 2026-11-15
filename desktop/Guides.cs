/* Ruler guides: the data, the file format, the Fireworks import and the
 * snapping maths. No window code, so it is testable on its own. */
using System.Text.Json.Nodes;

namespace Flint;

/// <summary>A guide on a pixel edge: Vertical guides have an x, horizontal ones a y.</summary>
public readonly record struct Guide(bool Vertical, int Pos);

/// <summary>The document grid (View > Grid): lines every SpacingX/SpacingY pixels from OffsetX/OffsetY. Saved per document.</summary>
public sealed record GridSpec(bool Show = false, bool Snap = false, int SpacingX = 16, int SpacingY = 16, int OffsetX = 0, int OffsetY = 0, int Argb = 0x5A80C8FF)
{
    public static readonly GridSpec Default = new();
    public Color Colour => Color.FromArgb(Argb);

    /// <summary>The grid line nearest <paramref name="pos"/> on one axis (edges are whole pixels).</summary>
    public double Nearest(double pos, bool vertical)
    {
        int step = Math.Max(1, vertical ? SpacingX : SpacingY), off = vertical ? OffsetX : OffsetY;
        return off + Math.Round((pos - off) / step, MidpointRounding.AwayFromZero) * step;
    }

    public JsonObject ToJson() => new()
    {
        ["show"] = Show, ["snap"] = Snap, ["spacingX"] = SpacingX, ["spacingY"] = SpacingY,
        ["offsetX"] = OffsetX, ["offsetY"] = OffsetY, ["colour"] = $"#{(uint)Argb:X8}",
    };

    public static GridSpec FromJson(JsonNode? n)
    {
        if (n is not JsonObject) return Default;
        try { return Parse(n); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException) { return Default; }   // a bad grid must not stop the file opening
    }

    private static GridSpec Parse(JsonNode n)
    {
        int Clamp(string k, int def, int lo, int hi) => Math.Clamp(n[k]?.GetValue<int>() ?? def, lo, hi);
        string c = n["colour"]?.GetValue<string>() ?? "";
        int argb = c.Length == 9 && c[0] == '#' && uint.TryParse(c[1..], System.Globalization.NumberStyles.HexNumber, null, out uint u) ? (int)u : Default.Argb;
        return new GridSpec(n["show"]?.GetValue<bool>() ?? false, n["snap"]?.GetValue<bool>() ?? false,
            Clamp("spacingX", 16, 1, 4096), Clamp("spacingY", 16, 1, 4096), Clamp("offsetX", 0, -4096, 4096), Clamp("offsetY", 0, -4096, 4096), argb);
    }
}

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
    public static double? SnapAxis(double pos, bool vertical, IEnumerable<Guide>? guides, int canvasSize, int zoom, GridSpec? grid = null)
    {
        // Guides and canvas edges win when close; otherwise a snapping grid always pulls to its nearest line.
        if (guides != null && SnapGuides(pos, vertical, guides, canvasSize, zoom) is { } g) return g;
        return grid is { Snap: true } ? grid.Nearest(pos, vertical) : null;
    }

    private static double? SnapGuides(double pos, bool vertical, IEnumerable<Guide> guides, int canvasSize, int zoom)
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
