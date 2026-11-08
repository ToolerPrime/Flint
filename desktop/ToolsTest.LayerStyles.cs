/* Photoshop-style layer effects added from PhotoCraft's list: Stroke, Satin, Gradient overlay. Part of --toolstest. */
namespace Flint;

public static partial class ToolsTest
{
    private static void LayerStyleChecks()
    {
        Console.WriteLine("Layer styles: stroke, satin, gradient overlay");
        const int S = 12;
        var red = new byte[S * S * 4];
        for (int i = 0; i < red.Length; i += 4) { red[i + 2] = 255; red[i + 3] = 255; }
        (byte b, byte g, byte r, byte a) Px(byte[] p, int w, int x, int y) { int o = (y * w + x) * 4; return (p[o], p[o + 1], p[o + 2], p[o + 3]); }

        var outside = ObjectEffects.Apply(new[] { new ObjectEffect(EffectKind.Stroke, ("size", 2.0), ("position", 0.0), ("opacity", 100.0), ("colour", 0xFF000000)) }, red, S, S, 0, 0);
        int ox = -outside.x, oy = -outside.y;
        Check(outside.w > S && Px(outside.px, outside.w, ox - 1, oy + 6) == (0, 0, 0, 255) && Px(outside.px, outside.w, ox + 6, oy + 6) == (0, 0, 255, 255),
            "outside stroke: a black ring round the shape, the shape itself untouched");
        var inside = ObjectEffects.Apply(new[] { new ObjectEffect(EffectKind.Stroke, ("size", 2.0), ("position", 1.0), ("opacity", 100.0), ("colour", 0xFF000000)) }, red, S, S, 0, 0);
        Check(inside.w == S && Px(inside.px, S, 0, 6) == (0, 0, 0, 255) && Px(inside.px, S, 6, 6) == (0, 0, 255, 255), "inside stroke: drawn inside the edge, no growth");

        var satin = ObjectEffects.Apply(new[] { new ObjectEffect(EffectKind.Satin, ("dist", 3.0), ("angle", 45.0), ("size", 2.0), ("opacity", 100.0), ("colour", 0xFF000000)) }, red, S, S, 0, 0);
        bool darker = false, alphaKept = true;
        for (int i = 0; i < satin.px.Length; i += 4) { if (satin.px[i + 2] < 250) darker = true; if (satin.px[i + 3] != 255) alphaKept = false; }
        Check(satin.w == S && darker && alphaKept, "satin shades inside the shape without changing its transparency");

        var grad = ObjectEffects.Apply(new[] { new ObjectEffect(EffectKind.GradientOverlay, ("angle", 90.0), ("style", 0.0), ("scale", 100.0), ("opacity", 100.0), ("colour1", 0xFF000000), ("colour2", 0xFFFFFFFF)) }, red, S, S, 0, 0);
        var top = Px(grad.px, S, 6, 0); var bottom = Px(grad.px, S, 6, S - 1);
        Check(top.r > 220 && top.g > 220 && bottom.r < 35 && bottom.a == 255, "gradient overlay at 90°: colour 1 at the bottom, colour 2 at the top");
        var radial = ObjectEffects.Apply(new[] { new ObjectEffect(EffectKind.GradientOverlay, ("angle", 0.0), ("style", 1.0), ("scale", 100.0), ("opacity", 100.0), ("colour1", 0xFF000000), ("colour2", 0xFFFFFFFF)) }, red, S, S, 0, 0);
        Check(Px(radial.px, S, 6, 6).r < Px(radial.px, S, 0, 0).r, "radial gradient overlay: colour 1 in the middle, colour 2 at the corners");
    }
}
