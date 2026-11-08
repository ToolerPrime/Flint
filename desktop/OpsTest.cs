/* Assert-based checks for the pixel operations, driving the same calls the
 * form makes. Run: Flint.exe --opstest <sheet.png> <out.gif>
 * The GIF it writes is then checked with Pillow by test/verify_ops.py. */
using System.Drawing.Imaging;

namespace Flint;

public static class OpsTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    public static int Run(string sheetPath, string outGif)
    {
        Console.WriteLine("ImageOps checks on " + Path.GetFileName(sheetPath));

        using var loaded = Image.FromFile(sheetPath);
        var sheet = ImageOps.NewBitmap(loaded.Width, loaded.Height);
        using (var g = Graphics.FromImage(sheet)) g.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
        Check(sheet.Width == 288 && sheet.Height == 48, $"sheet is {sheet.Width}x{sheet.Height}");

        // Slice into 6 cells, as the Slice button does.
        const int cols = 6;
        int cw = sheet.Width / cols, ch = sheet.Height;
        var frames = new List<Bitmap>();
        for (int c = 0; c < cols; c++)
            frames.Add(ImageOps.Crop(sheet, new Rectangle(c * cw, 0, cw, ch)));
        Check(frames.Count == 6, "sliced into 6 frames");
        Check(frames.All(f => f.Width == 48 && f.Height == 48), "each cell is 48x48");

        // Every frame starts fully opaque, so trimming must be a no-op now.
        var preBox = ImageOps.ContentBounds(frames);
        Check(preBox == new Rectangle(0, 0, 48, 48), $"before keying, content fills the frame ({preBox})");

        // Key out the magenta background across every frame.
        foreach (var f in frames) ImageOps.ColorKey(f, Color.Magenta, 20);

        var corner = frames[0].GetPixel(0, 0);
        Check(corner.A == 0, "corner is transparent after keying");

        int opaque = 0;
        var buf = ImageOps.GetPixels(frames[0]);
        for (int i = 3; i < buf.Length; i += 4) if (buf[i] > 0) opaque++;
        Check(opaque > 200 && opaque < 48 * 48, $"frame 0 keeps {opaque} opaque pixels of {48 * 48}");

        // No stray magenta should survive.
        bool magentaLeft = false;
        foreach (var f in frames)
        {
            var px = ImageOps.GetPixels(f);
            for (int i = 0; i < px.Length; i += 4)
            {
                if (px[i + 3] == 0) continue;
                if (px[i + 2] > 200 && px[i + 1] < 60 && px[i] > 200) { magentaLeft = true; break; }
            }
        }
        Check(!magentaLeft, "no magenta pixels survive the key");

        // Trim should now bite, and must use the union across all frames so the
        // sprite stays registered (it bobs up and down between frames).
        var box = ImageOps.ContentBounds(frames);
        Check(box.Height < 48 || box.Width < 48, $"trim box shrank to {box.Width}x{box.Height}");
        Check(box.Width >= 24, "trim box still contains the sprite");
        var trimmed = frames.Select(f => ImageOps.Crop(f, box)).ToList();
        Check(trimmed.All(f => f.Width == box.Width && f.Height == box.Height), "all frames trimmed alike");

        // The union box must leave at least one frame touching the top edge and
        // one touching the bottom, or the crop was not tight.
        bool touchesTop = false, touchesBottom = false;
        foreach (var f in trimmed)
        {
            var px = ImageOps.GetPixels(f);
            for (int x = 0; x < f.Width; x++)
            {
                if (px[x * 4 + 3] != 0) touchesTop = true;
                if (px[((f.Height - 1) * f.Width + x) * 4 + 3] != 0) touchesBottom = true;
            }
        }
        Check(touchesTop && touchesBottom, "trim is tight on both top and bottom across the set");

        // Flip must be reversible.
        using (var once = ImageOps.Flip(trimmed[0], true))
        using (var twice = ImageOps.Flip(once, true))
        {
            var a = ImageOps.GetPixels(trimmed[0]);
            var b = ImageOps.GetPixels(twice);
            Check(a.AsSpan().SequenceEqual(b), "flipping twice restores the original");
        }

        // Nearest-neighbour scaling must not invent colours.
        using (var big = ImageOps.Scale(trimmed[0], trimmed[0].Width * 3, trimmed[0].Height * 3, false))
        {
            var src = new HashSet<uint>();
            var sp = ImageOps.GetPixels(trimmed[0]);
            for (int i = 0; i < sp.Length; i += 4)
                src.Add(BitConverter.ToUInt32(sp, i));
            var bp = ImageOps.GetPixels(big);
            bool allKnown = true;
            for (int i = 0; i < bp.Length; i += 4)
                if (!src.Contains(BitConverter.ToUInt32(bp, i))) { allKnown = false; break; }
            Check(allKnown, "3x nearest-neighbour introduces no new colours");
            Check(big.Width == trimmed[0].Width * 3, "scaled width is exact");
        }

        // Reframe to a bigger canvas, centred, then confirm the padding is clear.
        using (var padded = ImageOps.Reframe(trimmed[0], box.Width + 20, box.Height + 20, 10, 10))
        {
            Check(padded.GetPixel(0, 0).A == 0, "padding added by Reframe is transparent");
            Check(padded.Width == box.Width + 20, "Reframe width is exact");
        }

        // Full export path, as the GIF button runs it.
        var all = trimmed.Select(ImageOps.GetPixels).ToList();
        var hist = Quantizer.BuildHistogram(all, out double total);
        var colors = Quantizer.Quantize(hist, total, 63);
        var ix = new Quantizer.Indexer(colors);
        var palette = new Palette
        {
            R = new byte[] { 0 }.Concat(colors.R).ToArray(),
            G = new byte[] { 0 }.Concat(colors.G).ToArray(),
            B = new byte[] { 0 }.Concat(colors.B).ToArray(),
        };
        var gifFrames = new List<GifFrame>();
        foreach (var px in all)
        {
            var idx = new byte[box.Width * box.Height];
            for (int p = 0; p < idx.Length; p++)
            {
                int o = p * 4;
                idx[p] = px[o + 3] < 128 ? (byte)0 : (byte)(ix.Nearest(px[o + 2], px[o + 1], px[o]) + 1);
            }
            gifFrames.Add(new GifFrame { Indices = idx, DelayMs = 90 });
        }
        byte[] gif = GifEncoder.Encode(box.Width, box.Height, gifFrames, palette, 0, true);
        File.WriteAllBytes(outGif, gif);
        Check(gif.Length > 100, $"wrote {Path.GetFileName(outGif)}, {gif.Length} bytes, {palette.Count} colours");

        foreach (var f in frames) f.Dispose();
        foreach (var f in trimmed) f.Dispose();
        sheet.Dispose();

        Console.WriteLine(_fails == 0 ? "all ImageOps checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
