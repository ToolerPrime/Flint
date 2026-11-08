/* Flint's own interface icons, drawn in code (no image files, no third-party
 * or Adobe artwork). Each icon is simple line art on a transparent square,
 * drawn in one colour so the theme can tint it (mint when active, grey
 * otherwise).
 *
 * Icons are UI decoration only: GDI+ antialiasing is fine here (this is not
 * document pixel data).
 */
using System.Drawing.Drawing2D;

namespace Flint;

public static class ToolIcons
{
    /// <summary>Every icon key the interface may ask for.</summary>
    public static readonly string[] Keys =
    {
        // tools
        "move", "transform", "crop", "select-rect", "select-ellipse", "lasso", "wand",
        "eyedropper", "pan", "pencil", "eraser", "fill", "gradient", "clone", "text",
        "line", "rect", "ellipse", "pivot", "hitbox",
        // file / edit toolbar
        "new", "open", "save", "import", "export", "undo", "redo",
        // playback and frames
        "play", "stop", "loop", "prev", "next", "add-frame", "dup-frame", "delete-frame",
        "tag", "zoom-in", "zoom-out", "onion", "record", "audio",
        // layers
        "eye", "eye-off", "lock", "unlock", "mask", "add-layer", "delete-layer",
        "layer-up", "layer-down", "duplicate", "effects", "library", "history",
    };

    private static readonly Dictionary<(string, int, int), Bitmap> _cache = new();

    /// <summary>
    /// The icon for <paramref name="key"/> at <paramref name="size"/> pixels,
    /// drawn in <paramref name="color"/>. Unknown keys give a small dot, never null.
    /// Results are cached; do not dispose them.
    /// </summary>
    public static Bitmap Get(string key, int size, Color color)
    {
        var k = (key, size, color.ToArgb());
        if (_cache.TryGetValue(k, out var hit)) return hit;
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // Draw on a 24-unit design grid scaled to the requested size.
            g.ScaleTransform(size / 24f, size / 24f);
            using var pen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var brush = new SolidBrush(color);
            Draw(g, key, pen, brush);
        }
        _cache[k] = bmp;
        return bmp;
    }

    /// <summary>Draw one icon on a 24x24 grid (0..24 in both directions, keep 2 units margin).</summary>
    private static void Draw(Graphics g, string key, Pen p, Brush b)
    {
        switch (key)
        {
            // Placeholder until the real drawings are written: a rounded square
            // with the first letter-ish mark. Every key above gets its own case.
            default:
                if (!ToolIconsArt.TryDraw(g, key, p, b)) g.FillEllipse(b, 9, 9, 6, 6);
                break;
        }
    }
}
