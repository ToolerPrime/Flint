namespace Flint;

public static class ColourPickerTest
{
    public static int Run()
    {
        int fails = 0;
        void Check(bool ok, string what) { Console.WriteLine($"{(ok ? "  ok   " : "  FAIL ")}{what}"); if (!ok) fails++; }
        static bool Near(double[] a, double[] b, double tol = 1e-9) => a.Zip(b).All(p => Math.Abs(p.First - p.Second) <= tol);

        Console.WriteLine("Colour maths");
        bool round = true;
        for (int r = 0; r < 256; r += 15) for (int g = 0; g < 256; g += 15) for (int b = 0; b < 256; b += 15)
        {
            var rgb = new[] { r / 255.0, g / 255.0, b / 255.0 }; var h = ColourMath.RgbToHsb(rgb[0], rgb[1], rgb[2]);
            if (!Near(ColourMath.HsbToRgb(h[0], h[1], h[2]), rgb)) round = false;
        }
        Check(round, "RGB to HSB and back gives the same colour (4,913 colours)");
        Check(Near(ColourMath.HsbToRgb(120, 1, 1), new[] { 0.0, 1, 0 }) && Near(ColourMath.RgbToHsb(1, 0, 0), new[] { 0.0, 1, 1 }), "pure green and red convert as expected");

        var rnd = new Random(7); bool inverse = true;
        foreach (char mode in "hsvrgb")
            for (int i = 0; i < 200; i++)
            {
                var rgb = new[] { rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble() };
                var hsb = ColourMath.RgbToHsb(rgb[0], rgb[1], rgb[2]);
                var (x, y, z) = ColourMath.Locate(mode, hsb, rgb);
                if (!Near(ColourMath.FieldColour(mode, z, x, y), rgb, 1e-6)) { inverse = false; Console.WriteLine($"         mode {mode} missed {ColourMath.Hex(rgb)}"); break; }
            }
        Check(inverse, "every mode finds a colour's place in the field and gets the same colour back");
        Check(Near(ColourMath.SliderColour('h', 0.5, new[] { 10.0, 0.2, 0.3 }, new[] { 0.0, 0, 0 }), new[] { 0.0, 1, 1 }), "hue slider shows full-strength hues");

        Check(ColourMath.ParseHex("#FF8000") is { } p && ColourMath.Hex(p) == "ff8000" && ColourMath.ParseHex("ff8000") != null, "hex with or without # parses");
        Check(ColourMath.ParseHex("+f8000") == null && ColourMath.ParseHex("12345") == null && ColourMath.ParseHex("ggg000") == null, "bad hex is rejected");
        Check(ColourMath.NotWebSafe(new[] { 0.2, 0.4, 1.0 }) == null && ColourMath.NotWebSafe(new[] { 0.21, 0.4, 1.0 }) is { } w && ColourMath.Hex(w) == "3366ff", "web-colour check and nearest web colour");

        Console.WriteLine("Colour picker dialog");
        using (var dlg = new ColourPickerDialog(Color.FromArgb(128, 255, 0, 0)))
        {
            dlg.CreateControl();
            Check(dlg.Colour == Color.FromArgb(128, 255, 0, 0), "opens on the starting colour and keeps its transparency");
            dlg.SetHsb(240, 1, 1); Check(dlg.Colour.ToArgb() == Color.FromArgb(128, 0, 0, 255).ToArgb(), "setting HSB updates the colour");
            dlg.SetRgb(new[] { 0.5, 0.5, 0.5 }); Check(dlg.TestHsb[0] == 240, "a grey keeps the hue you had");
            dlg.SetRgb(new[] { 0.0, 0, 0 }); Check(dlg.TestHsb[0] == 240 && dlg.TestHsb[1] == 0, "black keeps the hue");
            foreach (char m in "hsvrgb") dlg.SetMode(m);
            dlg.SetRgb(new[] { 1.0, 0.5, 0 }); Check(dlg.Colour.R == 255 && dlg.Colour.G == 128 && dlg.Colour.B == 0, "every channel mode can be chosen and colours still set");
            using var bmp = new Bitmap(dlg.Width, dlg.Height); dlg.DrawToBitmap(bmp, new Rectangle(Point.Empty, dlg.Size));
            Check(true, "the dialog draws without error");
        }
        Console.WriteLine(fails == 0 ? "all colour picker checks passed" : $"{fails} colour picker checks failed");
        return fails == 0 ? 0 : 1;
    }
}
