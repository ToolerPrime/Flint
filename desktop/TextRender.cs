/* Exact text rendering shared by the Text tool and the Fireworks importer.
 *
 * The text is drawn white on black into a throwaway bitmap and only the coverage is kept; the stored
 * pixels are then the chosen colour with that coverage as alpha, so no GDI+ drawing ever touches
 * stored pixels and a hard-edged render contains only the exact colour.
 */
using System.Drawing.Text;

namespace Flint;

internal static class TextRender
{
    /// <summary>The text is drawn this far from the top-left of the canvas returned by <see cref="Pixels"/>.</summary>
    public const int Pad = 4;

    /// <summary>
    /// <paramref name="typographic"/> = Fireworks style: no side padding in the measure and ungridded antialiasing, which matched the
    /// labels in Fireworks' own flattened pictures best (global.png) among the GDI+ hints.
    /// </summary>
    /// <remarks>Straight-alpha BGRA of <paramref name="text"/> in <paramref name="colour"/>, canvas w×h, text origin at (Pad, Pad).</remarks>
    public static byte[] Pixels(string text, Font font, Color colour, bool smooth, out int w, out int h, bool typographic = false)
    {
        using var fmt = typographic ? new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap } : new StringFormat();
        SizeF size;
        using (var probe = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(probe)) size = g.MeasureString(text, font, int.MaxValue, fmt);
        w = (int)Math.Ceiling(size.Width) + 2 * Pad; h = (int)Math.Ceiling(size.Height) + 2 * Pad;
        using var canvas = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Black);
            g.TextRenderingHint = smooth ? (typographic ? TextRenderingHint.AntiAlias : TextRenderingHint.AntiAliasGridFit) : TextRenderingHint.SingleBitPerPixelGridFit;
            using var white = new SolidBrush(Color.White);
            g.DrawString(text, font, white, Pad, Pad, fmt);
        }
        var grey = ImageOps.GetPixels(ImageOps.ReadExact(canvas));
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int cov = grey[i * 4 + 1];
            if (cov == 0) continue;
            px[i * 4] = colour.B; px[i * 4 + 1] = colour.G; px[i * 4 + 2] = colour.R;
            px[i * 4 + 3] = (byte)(cov * colour.A / 255);
        }
        return px;
    }

    /// <summary>Width of a single line as the typographic (no side padding) measure.</summary>
    public static SizeF Measure(string text, Font font)
    {
        using var fmt = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap };
        using var probe = new Bitmap(1, 1);
        using var g = Graphics.FromImage(probe);
        return g.MeasureString(text, font, int.MaxValue, fmt);
    }
}
