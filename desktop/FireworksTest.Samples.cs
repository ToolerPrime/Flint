/* Real Fireworks-made sample files (blend modes, gradient kinds, frame delays). Part of --fwtest.
 * They are local test data and are never copied into the repository; the test skips when they are absent. */
namespace Flint;

public static partial class FireworksTest
{
    private const string SampleDir = @"C:\Users\Techn\AppData\Local\Temp\claude\ea0d8db51509f597\6db06cae-c45c-4294-96cd-7fcbf57e0816\scratchpad\fwsamples";

    /// <summary>Percent of pixels within tol of Fireworks' flattened picture, in a rectangle.</summary>
    private static double RegionScore(string file, Rectangle r, int tol, out FwImport imp)
    {
        var (_, _, i) = Score(file, tol);   // opens and composites
        imp = i;
        var d = i.Doc;
        var got = d.RenderPixels(0, 0);
        uint bg = i.CanvasColour;
        int ba = (int)(bg >> 24), br = (int)((bg >> 16) & 255), bgg = (int)((bg >> 8) & 255), bb = (int)(bg & 255);
        var want = ImageOps.GetPixels(((RasterObject)d.Layers[0].CelAt(0)!.Objects[0]).Pixels);
        int ok = 0, n = 0;
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
            {
                int o = (y * d.Width + x) * 4, a = got[o + 3];
                int gb = got[o], gg = got[o + 1], gr = got[o + 2];
                if (a < 255 && ba > 0)   // canvas colour behind, as Score does
                {
                    int oa = a * 255 + ba * (255 - a);
                    if (a > 0) { gb = (gb * a * 255 + bb * ba * (255 - a) + oa / 2) / oa; gg = (gg * a * 255 + bgg * ba * (255 - a) + oa / 2) / oa; gr = (gr * a * 255 + br * ba * (255 - a) + oa / 2) / oa; }
                    else { gb = bb; gg = bgg; gr = br; }
                    a = (oa + 127) / 255;
                }
                n++;
                if (Math.Abs(gb - want[o]) <= tol && Math.Abs(gg - want[o + 1]) <= tol && Math.Abs(gr - want[o + 2]) <= tol && Math.Abs(a - want[o + 3]) <= tol) ok++;
            }
        return 100.0 * ok / n;
    }

    private static void RealSamples()
    {
        if (!Directory.Exists(SampleDir)) { Console.WriteLine("  skip  no Fireworks-made sample files"); return; }
        string blends = Path.Combine(SampleDir, "blends.png");
        // each square is 60x60 around its centre; (file, centre x, centre y, name, verified)
        var squares = new (string file, int cx, int cy, string name, bool verified)[]
        {
            ("gradients2.png", 38, 38, "Rectangular", true), ("gradients2.png", 108, 38, "Rectangular (contour failed)", true), ("gradients2.png", 178, 38, "Conical", true),
            ("gradients2.png", 248, 38, "Satin", false), ("gradients2.png", 318, 38, "Pinch", true), ("gradients2.png", 388, 38, "Folds", false),
            ("gradients2.png", 458, 38, "Bars", true), ("gradients2.png", 528, 38, "Ripple", true), ("gradients2.png", 598, 38, "Waves", false),
            ("gradients2.png", 38, 114, "Rectangular row 2", true), ("gradients2.png", 178, 114, "Conical row 2", true), ("gradients2.png", 318, 114, "Pinch row 2", true),
            ("gradients2.png", 458, 114, "Bars row 2", true), ("gradients2.png", 528, 114, "Ripple row 2", true),
            ("gradients.png", 40, 130, "Bars", true), ("gradients.png", 176, 130, "Waves", false), ("gradients.png", 244, 130, "Satin", false), ("gradients.png", 312, 130, "Folds", false),
            ("gradients.png", 40, 40, "Linear", true), ("gradients.png", 108, 40, "Circular", true), ("gradients.png", 176, 40, "Elliptical", true),
        };
        foreach (var sq in squares)
        {
            string f = Path.Combine(SampleDir, sq.file);
            if (!File.Exists(f)) continue;
            double sc = RegionScore(f, new Rectangle(sq.cx - 27, sq.cy - 27, 54, 54), 8, out _);
            Console.WriteLine($"  score {sc,6:F1}%  {sq.file} {sq.name}{(sq.verified ? "" : "  (approximated)")}");
            if (sq.verified) Check(sc >= 99, $"{sq.file} {sq.name} matches Fireworks' own picture ({sc:F1}%)");
        }
        string anim = Path.Combine(SampleDir, "animation.png");
        if (File.Exists(anim))
        {
            var ai = FireworksPng.Open(anim);
            var dl = ai.Doc.Frames.Select(f => f.Delay).ToArray();
            Check(dl.SequenceEqual(new[] { 250, 500, 1000 }), $"animation.png opens as 3 frames with delays 250/500/1000 ms (got {string.Join("/", dl)})");
        }
        if (File.Exists(blends))
        {
            string[] names = { "normal", "multiply", "screen", "darken", "lighten", "difference", "hue", "saturation", "color", "luminosity", "invert", "tint", "erase" };
            for (int i = 0; i < 13; i++)
            {
                double s = RegionScore(blends, new Rectangle(i * 43 + 2, 2, 40, 76), 4, out var imp);
                Console.WriteLine($"  score {s,6:F1}%  blends.png square {i} {names[i]}");
                // Hue, Saturation, Color and Luminosity follow the W3C spec; Fireworks' versions are HSV-based and differ in the overlap
                double need = i is 6 or 7 or 9 ? 90 : i == 8 ? 65 : 99;
                Check(s >= need, $"blends.png {names[i]} scores {s:F1}% (needs {need}%)");
            }
        }
    }
}
