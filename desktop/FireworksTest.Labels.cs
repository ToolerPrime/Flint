/* Button labels, blend ids and rare gradient kinds in Fireworks import. Part of --fwtest. */
namespace Flint;

public static partial class FireworksTest
{
    private static byte[] Txt(double l, double t, double r, double b, int jst, string font = "Arial", double size = 12, uint col = 0xffffffff) =>
        V("TXT", F("LFT", l), F("TOP", t), F("RIT", r), F("BOT", b),
          V("PAT", Raw($"FCLi{{{col:x}}}")), S("FON", font), F("PTS", size), B("BOL", false), B("ITL", false), I("JST", jst),
          V("TFS", S("TRN", "TEXT")));

    private static byte[] AlsLabel(int id, double tx, double ty, string label) =>
        Cat(V("ALS", I("MSN", id), Mtx(1, 1, tx, ty), I("ANN", 1), S("BUT", label))[..^1], Raw("}"));

    private static void LabelsAndBlends()
    {
        // --- button labels: drawn from BUTs in the symbol's text style, centred in its box, through the instance matrix
        var syms = V("MSL", Sym("button", 0x30, Rect(-30, -10, 30, 10, 0xff333366), Txt(-20, -6, 20, 6, 1)));
        var d = Build1(Cat(AlsLabel(0x30, 100, 50, "OK")), 200, 100, syms, null, out var imp);
        var objs = d.Layers[1].CelAt(0)!.Objects.OfType<RasterObject>().ToList();
        var label = objs.FirstOrDefault(o => o.Name.EndsWith("Label: OK"));
        Check(label != null && !imp.Warnings.Any(w => w.Contains("label")), "a button instance's BUTs text is drawn (and no 'left out' warning)");
        if (label != null)
        {
            var p = ImageOps.GetPixels(label.Pixels);
            int minX = int.MaxValue, maxX = -1, soft = 0, ink = 0;
            for (int y = 0; y < label.Height; y++)
                for (int x = 0; x < label.Width; x++)
                {
                    int a = p[(y * label.Width + x) * 4 + 3];
                    if (a == 0) continue;
                    ink++; if (a < 255) soft++;
                    minX = Math.Min(minX, label.X + x); maxX = Math.Max(maxX, label.X + x);
                    if (p[(y * label.Width + x) * 4] != 255 || p[(y * label.Width + x) * 4 + 2] != 255) { ink = -99999; }
                }
            double centre = (minX + maxX) / 2.0;
            Check(ink > 20 && Math.Abs(centre - 100) <= 2, $"the label is centred in the text box (centre {centre:F1} for a box centred at 100)");
            Check(soft > 5, $"the label is antialiased ({soft} soft pixels)");
            Check(label.Y >= 50 - 12 && label.Y + label.Height <= 50 + 12, "and sits inside the button");
        }
        var d2 = Build1(Cat(AlsLabel(0x30, 100, 50, "LONGER LABEL")), 200, 100, syms, null, out _);
        var wide = d2.Layers[1].CelAt(0)!.Objects.OfType<RasterObject>().First(o => o.Name.Contains("Label")).Width;
        Check(wide > label!.Width + 20, "a longer label is wider (the text itself changes, not a cached picture)");
        var syms2 = V("MSL", Sym("button", 0x30, Rect(-30, -10, 30, 10, 0xff333366), Txt(-20, -6, 20, 6, 2)));
        var d3 = Build1(Cat(AlsLabel(0x30, 100, 50, "OK")), 200, 100, syms2, null, out _);
        var right = d3.Layers[1].CelAt(0)!.Objects.OfType<RasterObject>().First(o => o.Name.Contains("Label"));
        Check(right.X + right.Width <= 100 + 20 + 2 && right.X + right.Width >= 100 + 20 - 6, "right-aligned text ends at the box's right edge");
        var scaled = V("ALS", I("MSN", 0x30), Mtx(2, 2, 100, 50), I("ANN", 1), S("BUT", "OK"));
        var d4 = Build1(scaled, 300, 150, syms, null, out _);
        var big = d4.Layers[1].CelAt(0)!.Objects.OfType<RasterObject>().First(o => o.Name.Contains("Label"));
        Check(big.Original != null && big.Width > label.Width, "a scaled instance scales its label through the matrix");
        var plain = Build1(Als(0x30, 1, 1, 100, 50), 200, 100, syms, null, out var imp5);
        Check(plain.Layers[1].CelAt(0)!.Objects.All(o => !o.Name.Contains("Label")), "an instance without BUTs draws no label of its own");

        // --- BLDi: only Normal is known; anything else is reported, not guessed
        byte[] WithBld(byte[] pth, int v) => Cat(pth[..^1], I("BLD", v), Raw("}"));
        var dn = Build1(WithBld(Rect(0, 0, 10, 10, 0xffff0000), 0), 20, 20, null, null, out var impN);
        Check(!impN.Warnings.Any(w => w.Contains("blend")) && ((RasterObject)dn.Layers[1].CelAt(0)!.Objects[0]).Blend == BlendMode.Normal, "BLD 0 is Normal and says nothing");
        var dc = Build1(WithBld(Rect(0, 0, 10, 10, 0xffff0000), 4), 20, 20, null, null, out var impC);
        Check(((RasterObject)dc.Layers[1].CelAt(0)!.Objects[0]).Blend == BlendMode.Normal && impC.Warnings.Any(w => w.Contains("blend mode") && w.Contains("BLD 4 x1")),
              "an unverified BLD value (4, seen in Adobe's samples) is drawn as Normal with a warning that names it");
        var expect = new (int, BlendMode)[] { (2, BlendMode.Multiply), (3, BlendMode.Difference), (5, BlendMode.Screen), (9, BlendMode.Darken), (0xa, BlendMode.Lighten), (0xb, BlendMode.Hue),
            (0xc, BlendMode.Saturation), (0xd, BlendMode.Color), (0xe, BlendMode.Luminosity), (0x11, BlendMode.Invert), (0x1d, BlendMode.Tint), (0x19, BlendMode.Erase) };
        bool allMapped = true;
        foreach (var (code, mode) in expect)
        {
            var bd = Build1(WithBld(Rect(0, 0, 10, 10, 0xffff0000), code), 20, 20, null, null, out var bi);
            var o = (RasterObject)bd.Layers[^1].CelAt(0)!.Objects[0];
            if (o.Blend != mode || bi.Warnings.Any(w => w.Contains("blend mode"))) { allMapped = false; Console.WriteLine($"    BLD {code:x} gave {o.Blend}"); }
            if (bd.Layers.Count != 3 && bi.CanvasColour >> 24 != 0) allMapped = false;   // a canvas-colour layer is added as the backdrop
        }
        Check(allMapped, "the twelve verified BLD codes map to Multiply, Difference, Screen, Darken, Lighten, Hue, Saturation, Color, Luminosity, Invert, Tint, Erase");

        // --- gradient kinds: Fireworks' real category names map to Flint's kinds; only the approximated ones warn
        foreach (var (cat, kind, warns) in new[] { ("fc_Rectangular", GradientKind.Rectangle, false), ("fc_Conical", GradientKind.Cone, false), ("fc_Pinch", GradientKind.Pinch, false),
                                                   ("fc_Bars", GradientKind.Bars, false), ("fc_Ripple", GradientKind.Ripples, false), ("fc_Waves", GradientKind.Waves, true),
                                                   ("fc_Satin", GradientKind.Satin, true), ("fc_Folds", GradientKind.Folds, true) })
        {
            var gd = Build1(WithGeo(Rect(0, 0, 100, 100, 0xffffffff, Grad(cat)), 50, 50, 100, 50, 50, 100), 120, 120, null, null, out var gi);
            var gr = ((RasterObject)gd.Layers[1].CelAt(0)!.Objects[0]).Vector!.Shapes[0].Gradient;
            Check(gr != null && gr.Kind == kind && gi.Warnings.Any(w => w.Contains("rare gradient kind approximated")) == warns, $"{cat} becomes {kind}{(warns ? " with the approximation warning" : " (verified, no warning)")}");
        }
        var cg = Build1(WithGeo(Rect(0, 0, 100, 100, 0xffffffff, Grad("fc_ContourGrad")), 50, 50, 100, 50, 50, 100), 120, 120, null, null, out var impCg);
        Check(((RasterObject)cg.Layers[1].CelAt(0)!.Objects[0]).Vector!.Shapes[0].Gradient == null && impCg.Warnings.Any(w => w.Contains("flat colour")), "fc_ContourGrad stays flat with a warning (no sample)");
        var pat = Build1(WithGeo(Rect(0, 0, 100, 100, 0xffffffff, Grad("fc_Pattern")), 50, 50, 100, 50, 0, 0), 120, 120, null, null, out var impP);
        Check(((RasterObject)pat.Layers[1].CelAt(0)!.Objects[0]).Vector!.Shapes[0].Gradient == null && impP.Warnings.Any(w => w.Contains("flat colour")), "fc_Pattern stays one flat colour with its warning");
        var lin = Build1(WithGeo(Rect(0, 0, 100, 100, 0xffffffff, Grad("fc_Linear")), 0, 0, 100, 0, 0, 0), 120, 120, null, null, out var impL);
        Check(!impL.Warnings.Any(w => w.Contains("rare")), "verified kinds do not get the warning");
    }
}
