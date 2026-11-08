/* Blend mode math and layer masks, part of --doctest. */
namespace Flint;

public static partial class DocTest
{
    private static byte[] One(int b, int g, int r, int a) => new[] { (byte)b, (byte)g, (byte)r, (byte)a };

    /// <summary>One source pixel blended onto one backdrop pixel; returns BGRA.</summary>
    private static byte[] Mix(BlendMode m, byte[] backdrop, byte[] src)
    {
        var d = (byte[])backdrop.Clone();
        int sa = src[3];
        if (sa > 0) Blending.Pixel(d, 0, src[2], src[1], src[0], sa, m);
        return d;
    }

    private static bool Near(byte[] got, byte[] want, int tol = 1) =>
        Enumerable.Range(0, 4).All(i => Math.Abs(got[i] - want[i]) <= tol);

    private static void PatchManifest(string path, Func<string, string> edit)
    {
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update);
        var entry = zip.GetEntry("project.json")!;
        string text;
        using (var r = new StreamReader(entry.Open())) text = r.ReadToEnd();
        entry.Delete();
        using var w = new StreamWriter(zip.CreateEntry("project.json").Open());
        w.Write(edit(text));
    }

    private static void BlendAndMaskChecks(string scratch)
    {
        Console.WriteLine("Blend modes (W3C compositing, straight alpha)");
        var red = One(0, 0, 255, 255);
        var grey = One(128, 128, 128, 255);
        var white = One(255, 255, 255, 255);
        Check(Near(Mix(BlendMode.Multiply, red, grey), One(0, 0, 128, 255)), "Multiply: 50% grey over red gives dark red");
        Check(Near(Mix(BlendMode.Screen, red, grey), One(128, 128, 255, 255)), "Screen: 50% grey over red lightens green and blue");
        Check(Mix(BlendMode.Normal, red, grey).SequenceEqual(grey), "Normal: an opaque source replaces the backdrop exactly");
        Check(Mix(BlendMode.Difference, red, grey).SequenceEqual(Mix(BlendMode.Difference, grey, red)), "Difference is symmetric");
        Check(Near(Mix(BlendMode.Darken, red, grey), One(0, 0, 128, 255)) && Near(Mix(BlendMode.Lighten, red, grey), One(128, 128, 255, 255)), "Darken takes the minimum, Lighten the maximum");
        Check(Near(Mix(BlendMode.Exclusion, grey, grey), One(128, 128, 128, 255)), "Exclusion of 50% grey with itself stays 50% grey");
        Check(Near(Mix(BlendMode.ColorDodge, grey, grey), white) && Near(Mix(BlendMode.ColorBurn, grey, grey), One(0, 0, 0, 255), 3), "Colour dodge brightens to white, colour burn darkens to black at 50%");
        Check(Near(Mix(BlendMode.HardLight, red, white), white) && Near(Mix(BlendMode.Overlay, grey, One(0, 0, 0, 255)), One(0, 0, 0, 255)), "Hard light with white is white; overlay with black over grey is black");
        Check(Near(Mix(BlendMode.SoftLight, grey, grey), grey, 2), "Soft light with 50% grey leaves the backdrop");
        Check(Near(Mix(BlendMode.Add, One(100, 100, 100, 255), One(200, 10, 10, 255)), One(255, 110, 110, 255)), "Add clamps at white");
        Check(Near(Mix(BlendMode.Subtract, One(100, 100, 100, 255), One(200, 10, 10, 255)), One(0, 90, 90, 255)), "Subtract clamps at black");
        var col = Mix(BlendMode.Color, grey, red);
        Check(col[2] == 255 && Math.Abs(col[1] - 74) <= 2 && Math.Abs(col[0] - 74) <= 2, $"Color: red hue on a grey backdrop keeps the grey's lightness (got {col[2]},{col[1]},{col[0]})");
        var lum = Mix(BlendMode.Luminosity, red, white);
        Check(lum[0] >= 250 && lum[1] >= 250 && lum[2] >= 250, "Luminosity: a white source lights the backdrop to white");
        var hue = Mix(BlendMode.Hue, grey, red);
        Check(hue[0] == hue[1] && hue[1] == hue[2], "Hue on a grey backdrop has no saturation to colour, so it stays grey");
        var sat = Mix(BlendMode.Saturation, red, grey);
        Check(sat[0] == sat[1] && sat[1] == sat[2], "Saturation from a grey source desaturates the backdrop");

        // alpha edges
        var half = One(0, 0, 255, 128);
        Check(Mix(BlendMode.Multiply, One(0, 0, 0, 0), half).SequenceEqual(half), "over a transparent backdrop the source is copied with its own alpha");
        var mw = Mix(BlendMode.Multiply, white, half);
        Check(Near(mw, One(127, 127, 255, 255), 2), $"half-transparent red Multiply over white fades in (got {mw[2]},{mw[1]},{mw[0]},{mw[3]})");
        Check(Mix(BlendMode.Multiply, red, One(1, 2, 3, 0)).SequenceEqual(red), "a fully transparent source changes nothing");
        Check(Mix(BlendMode.Screen, One(40, 40, 40, 128), white)[3] == 255, "an opaque source over a half-transparent backdrop is opaque");

        // Fireworks' extra modes
        var inv = Mix(BlendMode.Invert, white, grey);
        Check(inv[0] == 0 && inv[1] == 0 && inv[2] == 0 && inv[3] == 255, "Invert inverts the backdrop under an opaque source");
        Check(Mix(BlendMode.Invert, white, One(0, 0, 0, 0)).SequenceEqual(white), "Invert does nothing where the source is clear");
        Check(Mix(BlendMode.Invert, One(0, 0, 0, 0), grey)[3] == 0, "Invert never adds coverage over empty space");
        Check(Mix(BlendMode.Erase, red, grey)[3] == 0 && Mix(BlendMode.Erase, red, One(0, 0, 0, 128))[3] is >= 126 and <= 128, "Erase removes the backdrop's alpha under the source");
        var tint = Mix(BlendMode.Tint, One(0, 102, 255, 255), One(255, 102, 51, 255));
        Check(Near(tint, One(128, 102, 153, 255), 2) && Mix(BlendMode.Tint, white, red).SequenceEqual(white), "Tint: orange under blue gives the 50/50 mix (as in Fireworks), a white backdrop is left alone");
        bool allOk = true;
        foreach (var m in Enum.GetValues<BlendMode>())
        {
            var o = Mix(m, One(50, 120, 200, 255), One(210, 90, 30, 255));
            var z = Mix(m, One(50, 120, 200, 200), One(10, 20, 30, 0));
            if (!((m == BlendMode.Erase ? o[3] == 0 : o[3] == 255) && z[3] == 200 && z[0] == 50)) { allOk = false; Console.WriteLine("    bad mode " + m); }
        }
        Check(allOk, $"all {Enum.GetValues<BlendMode>().Length} modes are defined for every input and ignore a clear source");

        // in a document: per layer and per object
        {
            var d = new Document(4, 4);
            FillRaster(d, 0, 0, Color.Red);
            var top = d.AddLayer("Top");
            FillRaster(d, 1, 0, Color.FromArgb(255, 128, 128, 128));
            top.Blend = BlendMode.Multiply;
            var p = d.RenderPixels(0);
            Check(Near(new[] { p[0], p[1], p[2], p[3] }, One(0, 0, 128, 255)), "layer blend Multiply in a document");
            top.Blend = BlendMode.Normal;
            Check(d.RenderPixels(0)[2] == 128, "back to Normal replaces the pixels");
            var obj = (RasterObject)top.CelAt(0)!.Objects[0];
            top.CelAt(0)!.Objects[0] = obj.With(obj.Pixels);
            ((RasterObject)top.CelAt(0)!.Objects[0]).Blend = BlendMode.Screen;
            p = d.RenderPixels(0);
            Check(Near(new[] { p[0], p[1], p[2], p[3] }, One(128, 128, 255, 255)), "object blend Screen is applied (it used to be ignored)");
            ((RasterObject)top.CelAt(0)!.Objects[0]).Blend = BlendMode.Normal;
            Check(d.RenderPixels(0)[2] == 128, "an unblended object is unchanged");
            d.Dispose();
        }

        Console.WriteLine("Layer masks");
        {
            var d = new Document(4, 2);
            FillRaster(d, 0, 0, Color.Red);
            var before = d.RenderPixels(0);
            var mask = new RasterObject(ImageOps.NewBitmap(4, 2)) { Name = "Mask" };
            var mp = new byte[4 * 2 * 4];
            for (int i = 0; i < 8; i++)
            {
                int v = i % 4 == 0 ? 255 : i % 4 == 1 ? 128 : 0;   // white, grey, black, black
                mp[i * 4] = mp[i * 4 + 1] = mp[i * 4 + 2] = (byte)v; mp[i * 4 + 3] = 255;
            }
            ImageOps.SetPixels(mask.Pixels, mp);
            d.Layers[0].CelAt(0)!.Mask = mask;
            var p = d.RenderPixels(0);
            Check(p[3] == 255 && p[7] == 128 && p[11] == 0 && p[15] == 0, $"mask multiplies alpha: white shows, grey half, black hides (alpha {p[3]},{p[7]},{p[11]},{p[15]})");
            Check(p[2] == 255 && p[6] == 255, "masked pixels keep their colour");
            d.Layers[0].MaskEnabled = false;
            Check(d.RenderPixels(0).SequenceEqual(before), "a disabled mask changes nothing");
            d.Layers[0].MaskEnabled = true;
            var snap = d.Snapshot();
            Check(snap.Layers[0].CelAt(0)!.Mask == mask && snap.RasterBitmaps().Contains(mask.Pixels), "a snapshot shares the mask and counts its bitmap");
            var cl = d.Clone();
            Check(cl.Layers[0].CelAt(0)!.Mask != mask && cl.RenderPixels(0).SequenceEqual(p), "a clone has its own mask and renders the same");

            string path = Path.Combine(scratch, "mask.flint");
            d.Layers[0].MaskLinked = false;
            FlintFormat.Save(d, path);
            var back = FlintFormat.Load(path);
            Check(back.Ok && back.Document!.RenderPixels(0).SequenceEqual(p) && back.Document.Layers[0].MaskEnabled && !back.Document.Layers[0].MaskLinked,
                  "mask and its flags survive save and load");
            d.Layers[0].MaskEnabled = false;
            FlintFormat.Save(d, path);
            Check(!FlintFormat.Load(path).Document!.Layers[0].MaskEnabled, "a disabled mask stays disabled after save and load");

            FlintFormat.Save(cl, path);
            PatchManifest(path, t => System.Text.RegularExpressions.Regex.Replace(t, "\"format\":\\s*\\d+", "\"format\": 6"));
            Check(FlintFormat.Load(path).Ok, "a format 6 file still loads");

            d.Layers[0].MaskEnabled = true;
            d.Resize(6, 3, 1, 1);
            Check(d.Layers[0].CelAt(0)!.Mask!.Width == 6 && d.RenderPixels(0).Length == 6 * 3 * 4, "canvas resize reframes the mask");
            d.Width = 5;
            Check(d.RenderPixels(0).Length == 5 * 3 * 4, "a mask of the wrong size is ignored, not an error");
            d.Dispose();
        }

        // a mask file with alpha is normalised on load; masks count in the memory figure
        {
            var d = new Document(4, 2);
            FillRaster(d, 0, 0, Color.Red);
            long noMask = d.ApproximateBytes();
            d.Layers[0].CelAt(0)!.Mask = new RasterObject(ImageOps.NewBitmap(4, 2));
            Check(d.ApproximateBytes() == noMask + 4 * 2 * 4, "the memory figure includes masks");
            string path = Path.Combine(scratch, "maskalpha.flint");
            FlintFormat.Save(d, path);
            using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update))
            {
                var names = zip.Entries.Select(e => e.FullName).Where(n => n.StartsWith("images/")).OrderBy(n => n).ToList();
                string maskEntry = names[^1];   // written after the layer's own image
                var px = new byte[4 * 2 * 4];
                for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 2] = 10; px[i + 1] = 200; px[i + 3] = 128; }
                using var bmp = ImageOps.FromPixels(4, 2, px);
                zip.GetEntry(maskEntry)!.Delete();
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                using var w = zip.CreateEntry(maskEntry).Open();
                w.Write(ms.ToArray());
            }
            var back = FlintFormat.Load(path);
            var mp = ImageOps.GetPixels(back.Document!.Layers[0].CelAt(0)!.Mask!.Pixels);
            int want = 200 * 128 / 255;
            Check(mp[3] == 255 && mp[0] == want && mp[1] == want && mp[2] == want, $"a loaded mask is normalised to opaque grey (G times A: {mp[1]} of {want})");
            d.Dispose();
        }

        // unknown blend names in a file fall back to Normal with a warning
        {
            var d = new Document(2, 2);
            d.Layers[0].Blend = BlendMode.Luminosity;
            string path = Path.Combine(scratch, "blend.flint");
            FlintFormat.Save(d, path);
            Check(FlintFormat.Load(path).Document!.Layers[0].Blend == BlendMode.Luminosity, "new blend names are saved and loaded by name");
            PatchManifest(path, t => t.Replace("Luminosity", "Plasma"));
            var lr = FlintFormat.Load(path);
            Check(lr.Document!.Layers[0].Blend == BlendMode.Normal && lr.Warnings.Any(x => x.Contains("Plasma")), "an unknown blend name loads as Normal with a warning");
            d.Dispose();
        }
    }
}
