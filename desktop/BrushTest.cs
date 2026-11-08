namespace Flint;

public static class BrushTest
{
    /// <summary>Paints one wavy stroke per preset (black on white) into a PNG, for looking at by eye.</summary>
    public static void Sheet(string path)
    {
        int w = 520, rowH = 70, h = rowH * BrushSettings.Presets.Length;
        var px = new byte[w * h * 4]; Array.Fill(px, (byte)255);
        for (int i = 0; i < BrushSettings.Presets.Length; i++)
        {
            var b = BrushSettings.Presets[i]; b = b with { Size = Math.Min(b.Size, 50) };
            var s = new BrushStroke(b, (byte[])px.Clone(), w, h, Surface.Pack(Color.Black), null);
            double y0 = i * rowH + rowH / 2.0;
            s.Begin(20, y0);
            for (int k = 1; k <= 120; k++) s.LineTo(20 + k * 4, y0 + 18 * Math.Sin(k / 9.0));
            s.RenderInto(px);
        }
        using var bmp = ImageOps.FromPixels(w, h, px);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    public static int Run()
    {
        int fails = 0;
        void Check(bool ok, string what) { Console.WriteLine($"{(ok ? "  ok   " : "  FAIL ")}{what}"); if (!ok) fails++; }
        const int W = 64, H = 64;
        uint black = Surface.Pack(Color.Black);
        byte[] White() { var px = new byte[W * H * 4]; Array.Fill(px, (byte)255); return px; }
        byte Grey(byte[] px, int x, int y) => px[(y * W + x) * 4];

        Console.WriteLine("Brush tips");
        var (hc, hs) = BrushTip.TipShape(10, 1); var (sc, ss) = BrushTip.TipShape(10, 0);
        Check(Math.Abs(hc - 8.8f) < 1e-4 && sc == 0 && ss > hs, "hard tip has a solid core and ~1 px edge; soft tip has no core");
        bool falls = true; float last = 1;
        for (float d = 0; d < 20; d += 0.5f) { float v = BrushTip.Falloff(d, sc, ss); if (v > last + 1e-6) falls = false; last = v; }
        Check(falls && BrushTip.Falloff(0, sc, ss) == 1, "soft falloff starts full and only goes down");

        var hard = new BrushStroke(new BrushSettings(Size: 20, Hardness: 1), White(), W, H, black, null);
        hard.Begin(32, 32); var px = White(); hard.RenderInto(px);
        Check(Grey(px, 32, 32) == 0 && Grey(px, 32, 40) == 0 && Grey(px, 32, 45) == 255, "hard dab: solid inside, nothing outside");
        var soft = new BrushStroke(new BrushSettings(Size: 20, Hardness: 0), White(), W, H, black, null);
        soft.Begin(32, 32); px = White(); soft.RenderInto(px);
        Check(Grey(px, 32, 32) < 10 && Grey(px, 32, 38) > Grey(px, 32, 32) && Grey(px, 32, 38) < 255, "soft dab: dark middle fading outwards");

        Console.WriteLine("Opacity and flow");
        var half = new BrushStroke(new BrushSettings(Size: 10, Hardness: 1, Opacity: 0.5f), White(), W, H, black, null);
        half.Begin(10, 32); half.LineTo(50, 32); half.LineTo(10, 32); px = White(); half.RenderInto(px);
        Check(Math.Abs(Grey(px, 30, 32) - 128) <= 1, $"crossing your own stroke stays at the opacity (grey {Grey(px, 30, 32)}, want 128)");
        var one = new BrushStroke(new BrushSettings(Size: 10, Hardness: 1, Flow: 0.25f), White(), W, H, black, null);
        one.Begin(32, 32);
        Check(Math.Abs(one.CoverageAt(32, 32) - 0.25f) < 1e-5, "one dab at 25% flow covers 25%");
        one.LineTo(32.5, 32); one.LineTo(32, 32);
        Check(one.CoverageAt(32, 32) > 0.25f && one.CoverageAt(32, 32) < 1, "low flow builds up gradually with more dabs");

        var spaced = new BrushStroke(new BrushSettings(Size: 10, Spacing: 0.25f), new byte[256 * 4 * 4], 256, 4, black, null);
        spaced.Begin(0, 2); spaced.LineTo(100, 2);
        Check(spaced.DabCount == 41, $"spacing 25% of 10 px puts a dab every 2.5 px (got {spaced.DabCount}, want 41)");

        Console.WriteLine("Shapes, selection, pencil mode");
        var cal = new BrushStroke(BrushSettings.Presets.First(p => p.Name == "Calligraphy Flat") with { Size = 30 }, White(), W, H, black, null);
        cal.Begin(32, 32);
        Check(cal.CoverageAt(37, 27) > 0.9f && cal.CoverageAt(37, 37) < 0.1f, "a flat 45 degree tip lies up-right, as in Photoshop");
        var sel = Selection.FromRect(W, H, new Rectangle(0, 0, 32, 64));
        var clipped = new BrushStroke(new BrushSettings(Size: 20, Hardness: 1), White(), W, H, black, sel);
        clipped.Begin(32, 32); px = White(); clipped.RenderInto(px);
        Check(Grey(px, 28, 32) == 0 && Grey(px, 36, 32) == 255, "painting stays inside the selection");
        var pencil = new BrushStroke(BrushSettings.Presets.First(p => p.Name == "Hard Pencil") with { Size = 5 }, White(), W, H, black, null);
        pencil.Begin(20.3, 20.7); pencil.LineTo(40.2, 33.9); px = White(); pencil.RenderInto(px);
        bool binary = true; for (int i = 0; i < px.Length; i += 4) if (px[i] != 0 && px[i] != 255) binary = false;
        Check(binary, "Hard Pencil gives only hard pixels (no in-between greys)");
        var colour = new BrushStroke(new BrushSettings(Size: 9, Hardness: 1), new byte[W * H * 4], W, H, Surface.Pack(Color.FromArgb(255, 200, 100, 50)), null);
        colour.Begin(32.5, 32.5); px = new byte[W * H * 4]; colour.RenderInto(px);
        int c = (32 * W + 32) * 4;
        Check(px[c] == 50 && px[c + 1] == 100 && px[c + 2] == 200 && px[c + 3] == 255, "paints the exact colour on a transparent layer");
        bool nanSafe = true;
        try
        {
            var bad = new BrushStroke(new BrushSettings(Size: float.NaN, Hardness: float.NaN, Opacity: float.PositiveInfinity, Flow: -1, Spacing: 0), White(), W, H, black, null);
            bad.Begin(double.NaN, 5); bad.LineTo(1e300, 5); bad.Begin(-500, -500); bad.LineTo(500, 500); bad.RenderInto(White());
        }
        catch (Exception ex) { nanSafe = false; Console.WriteLine("         " + ex.Message); }
        Check(nanSafe, "nonsense settings or positions do not crash");

        Console.WriteLine("Brush tool in the editor");
        using (var form = new MainForm { SuppressPrompts = true, StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) })
        {
            form.Show(); Application.DoEvents(); form.TestNewCanvas(64, 64);
            form.TestSetBrush(BrushSettings.Presets[0] with { Size = 6 });
            int steps = form.TestDrag(Tool.Brush, Keys.None, new Point(5, 5), new Point(30, 20), new Point(50, 50));
            using (var f = form.TestDocument.RenderFrame(0))
            {
                var pix = ImageOps.GetPixels(f);
                Check(pix[(20 * 64 + 30) * 4 + 3] == 255 && pix[(60 * 64 + 2) * 4 + 3] == 0, "a brush drag paints along the path and nowhere else");
            }
            Check(steps == 1, "one brush stroke is one undo step");
            form.TestUndo();
            using (var f = form.TestDocument.RenderFrame(0)) Check(ImageOps.GetPixels(f).All(b => b == 0), "undo removes the stroke completely");
            form.TestSetBrush(BrushSettings.Presets[2]);
            Check(form.TestBrush.Name == "Hard Round Fine" && form.TestBrush.Size == 5, "choosing a preset loads its settings");
        }
        Console.WriteLine(fails == 0 ? "all brush checks passed" : $"{fails} brush checks failed");
        return fails == 0 ? 0 : 1;
    }
}
