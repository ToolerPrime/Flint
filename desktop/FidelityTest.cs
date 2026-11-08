/* Pixel fidelity: every alpha level through every path, checked against PIL.
 * Flint.exe --fidelitytest <dir> */
using System.Diagnostics;

namespace Flint;

public static class FidelityTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    /// <summary>256 x 16: every alpha, with colour that varies by row and column.</summary>
    private static byte[] Pattern(out int w, out int h)
    {
        w = 256; h = 16;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int a = 0; a < 256; a++)
            {
                int o = (y * w + a) * 4;
                px[o] = (byte)(y * 16 + 3);
                px[o + 1] = (byte)(255 - y * 13);
                px[o + 2] = (byte)(a ^ 0x5A);
                px[o + 3] = (byte)a;
            }
        return px;
    }

    /// <summary>
    /// Pixels that differ, ignoring colour under alpha 0: a fully transparent
    /// pixel has no visible colour and formats are free to drop it.
    /// </summary>
    private static int Diff(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return -1;
        int n = 0;
        for (int i = 0; i < a.Length; i += 4)
        {
            if (a[i + 3] == 0 && b[i + 3] == 0) continue;
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3]) n++;
        }
        return n;
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        Console.WriteLine("Every alpha level, every path");
        var px = Pattern(out int w, out int h);
        var src = ImageOps.FromPixels(w, h, px);

        Check(Diff(px, ImageOps.GetPixels(src)) == 0, "raw read/write is exact");
        Check(Diff(px, ImageOps.GetPixels(ImageOps.Clone(src))) == 0, "clone is exact");
        Check(Diff(px, ImageOps.GetPixels(ImageOps.Crop(src, new Rectangle(0, 0, w, h)))) == 0, "crop is exact");
        Check(Diff(px, ImageOps.GetPixels(ImageOps.Reframe(src, w, h, 0, 0))) == 0, "reframe is exact");
        Check(Diff(px, ImageOps.GetPixels(ImageOps.Flip(ImageOps.Flip(src, true), true))) == 0, "flip twice is exact");
        Check(Diff(px, ImageOps.GetPixels(ImageOps.Flip(ImageOps.Flip(src, false), false))) == 0, "vertical flip twice is exact");
        Check(Diff(px, ImageOps.GetPixels(ImageOps.Scale(src, w, h, false))) == 0, "1x nearest scale is exact");

        {
            var big = ImageOps.GetPixels(ImageOps.Scale(src, w * 3, h * 3, false));
            bool blocks = true;
            for (int y = 0; y < h * 3 && blocks; y++)
                for (int x = 0; x < w * 3; x++)
                {
                    int o = (y * w * 3 + x) * 4, s = ((y / 3) * w + x / 3) * 4;
                    if (big[o] != px[s] || big[o + 1] != px[s + 1] || big[o + 2] != px[s + 2] || big[o + 3] != px[s + 3])
                    { blocks = false; break; }
                }
            Check(blocks, "3x nearest scale makes exact 3x3 blocks of each pixel");
        }

        {
            var d = new Document(w, h);
            d.Layers[0].EnsureCel(0).Objects.Add(new RasterObject(ImageOps.Clone(src)));
            Check(Diff(px, d.RenderPixels(0)) == 0, "a single layer composites to exactly what was drawn");
            d.AddLayer("empty above");
            Check(Diff(px, d.RenderPixels(0)) == 0, "an empty layer above changes nothing");
            string path = Path.Combine(dir, "alpha.flint");
            FlintFormat.Save(d, path);
            var back = FlintFormat.Load(path);
            Check(back.Ok && Diff(px, back.Document!.RenderPixels(0)) == 0, "a project save and reload is exact");
            back.Document?.Dispose();
            d.Dispose();
        }

        Console.WriteLine("Compositing maths");
        {
            var dst = new byte[4];
            ImageOps.Over(dst, 1, 1, new byte[] { 10, 20, 30, 77 }, 1, 1, 0, 0, 255);
            Check(dst[0] == 10 && dst[1] == 20 && dst[2] == 30 && dst[3] == 77, "over a transparent pixel copies exactly");
            dst = new byte[] { 255, 255, 255, 255 };
            ImageOps.Over(dst, 1, 1, new byte[] { 0, 0, 0, 128 }, 1, 1, 0, 0, 255);
            Check(dst[3] == 255 && Math.Abs(dst[0] - 127) <= 1, $"50% black over white is mid grey ({dst[0]})");
            dst = new byte[] { 1, 2, 3, 200 };
            ImageOps.Over(dst, 1, 1, new byte[] { 9, 9, 9, 255 }, 1, 1, 0, 0, 255);
            Check(dst[0] == 9 && dst[3] == 255, "an opaque pixel replaces what is under it");
            dst = new byte[] { 1, 2, 3, 200 };
            ImageOps.Over(dst, 1, 1, new byte[] { 9, 9, 9, 0 }, 1, 1, 0, 0, 255);
            Check(dst[0] == 1 && dst[3] == 200, "a transparent pixel leaves what is under it");
        }

        Console.WriteLine("Against an independent PNG implementation (PIL)");
        string pilIn = Path.Combine(dir, "pil_in.png");
        string pilOut = Path.Combine(dir, "flint_out.png");
        string writer =
            "from PIL import Image\n" +
            "im = Image.new('RGBA', (256, 16))\n" +
            "im.putdata([(a ^ 0x5A, 255 - y * 13, y * 16 + 3, a) for y in range(16) for a in range(256)])\n" +
            $"im.save(r'{pilIn}')\n" +
            "print('ok')\n";
        string pyOk = Py(writer);
        Check(pyOk.Trim() == "ok", "PIL wrote a 256-level alpha PNG" + (pyOk.Trim() == "ok" ? "" : ": " + pyOk.Trim()));
        if (pyOk.Trim() == "ok")
        {
            using var img = Image.FromFile(pilIn);
            var imported = ImageOps.ReadExact(img);
            Check(Diff(px, ImageOps.GetPixels(imported)) == 0, "importing PIL's PNG gives exactly PIL's pixels");

            imported.Save(pilOut, System.Drawing.Imaging.ImageFormat.Png);
            string compare =
                "from PIL import Image\n" +
                $"a = list(Image.open(r'{pilIn}').convert('RGBA').getdata())\n" +
                $"b = list(Image.open(r'{pilOut}').convert('RGBA').getdata())\n" +
                "print(sum(1 for p, q in zip(a, b) if (p[3] or q[3]) and p != q))\n";
            string cmp = Py(compare).Trim();
            Check(cmp == "0", $"PIL reads Flint's exported PNG back identically ({cmp} pixels differ)");
        }

        Console.WriteLine(_fails == 0 ? "all fidelity checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    private static string Py(string code)
    {
        string script = Path.Combine(Path.GetTempPath(), "flint_fid_" + Guid.NewGuid().ToString("N")[..8] + ".py");
        File.WriteAllText(script, code);
        try
        {
            var psi = new ProcessStartInfo("python", $"-W ignore \"{script}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync();
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(60_000);
            return o.Length > 0 ? o : err.Result;
        }
        finally { File.Delete(script); }
    }
}
