/* Transforms, selections, filters, library and lossless objects. Flint.exe --edittest <dir> */
namespace Flint;

public static class EditTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    /// <summary>A small sprite with distinct colours and partial alpha, so any resampling shows.</summary>
    private static byte[] Sprite(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if ((x + y) % 7 == 0) continue;   // some transparent holes
                int o = (y * w + x) * 4;
                px[o] = (byte)(x * 17); px[o + 1] = (byte)(y * 29); px[o + 2] = (byte)((x * y) & 255);
                px[o + 3] = (byte)(x % 3 == 0 ? 128 : 255);
            }
        return px;
    }

    private static HashSet<uint> Colours(byte[] px)
    {
        var s = new HashSet<uint>();
        for (int i = 0; i < px.Length; i += 4) if (px[i + 3] > 0) s.Add(BitConverter.ToUInt32(px, i));
        return s;
    }

    private static uint At(byte[] px, int w, int x, int y) => BitConverter.ToUInt32(px, (y * w + x) * 4);

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        const int W = 9, H = 6;
        var src = Sprite(W, H);

        Console.WriteLine("Transforms");
        {
            var r = Transform.Render(src, W, H, Affine.Identity, Resample.Nearest);
            Check(r.px.SequenceEqual(src) && r.x == 0 && r.y == 0, "identity leaves every byte alone");

            r = Transform.Render(src, W, H, Affine.Translate(5, -3), Resample.Smooth);
            Check(r.px.SequenceEqual(src) && r.x == 5 && r.y == -3, "a whole-pixel move is exact even in smooth mode");

            var q = Transform.Rotate90(src, W, H, 1);
            bool exact = q.w == H && q.h == W;
            for (int y = 0; y < H && exact; y++)
                for (int x = 0; x < W && exact; x++)
                    exact = At(q.px, q.w, H - 1 - y, x) == At(src, W, x, y);
            Check(exact, "rotate 90° clockwise moves every pixel exactly");

            var back = Transform.Rotate90(q.px, q.w, q.h, 3);
            Check(back.px.SequenceEqual(src), "four quarter turns return the original bytes");

            var flip = Transform.Render(src, W, H, Affine.Scale(-1, 1), Resample.Nearest);
            Check(flip.w == W && At(flip.px, W, 0, 1) == At(src, W, W - 1, 1), "a mirror matrix flips exactly");

            var big = Transform.Render(src, W, H, Affine.Scale(3, 3), Resample.Nearest);
            Check(big.w == W * 3 && At(big.px, big.w, 7, 4) == At(src, W, 2, 1), "3× nearest scale copies each pixel into a 3×3 block");

            var rs = Transform.Render(src, W, H, Affine.Rotate(30).About(W / 2.0, H / 2.0), Resample.RotSprite);
            Check(rs.used == Resample.RotSprite && Colours(rs.px).IsSubsetOf(Colours(src)),
                  "RotSprite at 30° uses only the sprite's own colours");
            var nn = Transform.Render(src, W, H, Affine.Rotate(30).About(W / 2.0, H / 2.0), Resample.Nearest);
            Check(Colours(nn.px).IsSubsetOf(Colours(src)), "nearest rotation also adds no new colours");

            var flat = new byte[16 * 16 * 4];
            for (int i = 0; i < flat.Length; i += 4) { flat[i] = 10; flat[i + 1] = 200; flat[i + 2] = 90; flat[i + 3] = 255; }
            var sm = Transform.Render(flat, 16, 16, Affine.Rotate(17).About(8, 8), Resample.Smooth);
            bool keepsColour = true;
            for (int i = 0; i < sm.px.Length; i += 4)
                if (sm.px[i + 3] > 0 && (sm.px[i] != 10 || sm.px[i + 1] != 200 || sm.px[i + 2] != 90)) keepsColour = false;
            Check(keepsColour, "smooth rotation keeps a flat colour exact at soft edges (no dark fringe)");

            var s2 = Transform.Scale2x(new byte[] { 1, 2, 3, 255 }, 1, 1);
            Check(s2.Length == 16 && Colours(s2).Count == 1, "Scale2x of one pixel is four identical pixels");

            var m = Affine.Rotate(30).Then(Affine.Rotate(-30));
            Check(m.IsIdentity, $"rotating +30° then −30° composes to exactly the identity ({m})");
        }

        Console.WriteLine("Lossless object transforms");
        {
            var bmp = ImageOps.FromPixels(W, H, src);
            var obj = new RasterObject(bmp) { X = 4, Y = 2 };
            var t = obj;
            for (int i = 0; i < 5; i++) t = t.Retransform(Affine.Rotate(17).About(8, 5), Resample.RotSprite);
            for (int i = 0; i < 5; i++) t = t.Retransform(Affine.Rotate(-17).About(8, 5), Resample.RotSprite);
            Check(ImageOps.GetPixels(t.Pixels).SequenceEqual(src) && t.X == 4 && t.Y == 2,
                  "ten rotations there and back return the exact original pixels and position");
            Check(ReferenceEquals(t.Original, bmp), "the object keeps its original bitmap throughout");

            var moved = obj.Moved(3, 1);
            Check(ReferenceEquals(moved.Pixels, obj.Pixels) && moved.X == 7 && moved.Y == 3, "a move shares pixels and changes only position");

            var big = obj.Retransform(Affine.Scale(4, 4), Resample.Nearest);
            var small = big.Retransform(Affine.Scale(0.25, 0.25), Resample.Nearest);
            Check(ImageOps.GetPixels(small.Pixels).SequenceEqual(src), "scaling up 4× then down 4× is exact, because it re-renders from the original");

            var plain = t.With(ImageOps.Clone(t.Pixels));
            Check(!plain.IsTransformed, "replacing pixels drops the original (they no longer match it)");
        }

        Console.WriteLine("Selections");
        {
            var r = Selection.FromRect(10, 10, new Rectangle(2, 3, 4, 2));
            Check(r.Count == 8 && r.Bounds == new Rectangle(2, 3, 4, 2), "rectangle selection");
            var e = Selection.FromEllipse(9, 9, new Rectangle(0, 0, 9, 9));
            Check(e.Contains(4, 4) && !e.Contains(0, 0) && e.Contains(4, 0) && e.Count < 81, "ellipse keeps the middle, drops the corners");
            var l = Selection.FromPolygon(10, 10, new[] { new Point(0, 0), new Point(9, 0), new Point(0, 9) });
            Check(l.Contains(1, 1) && !l.Contains(8, 8), "lasso triangle selects one half");

            var px = new byte[6 * 1 * 4];
            for (int i = 0; i < 6; i++) { px[i * 4 + 3] = 255; px[i * 4 + 2] = (byte)(i == 2 || i == 5 ? 0 : 200); }
            var wand = Selection.Wand(px, 6, 1, 0, 0, 0, true);
            Check(wand.Count == 2, $"contiguous wand stops at a different colour ({wand.Count})");
            var global = Selection.Wand(px, 6, 1, 0, 0, 0, false);
            Check(global.Count == 4, $"non-contiguous wand finds every match ({global.Count})");

            var add = Selection.FromRect(10, 10, new Rectangle(6, 3, 2, 2)).Combine(r, SelectMode.Add);
            Check(add.Count == 12, "add combines two selections");
            var sub = Selection.FromRect(10, 10, new Rectangle(2, 3, 2, 2)).Combine(r, SelectMode.Subtract);
            Check(sub.Count == 4 && !sub.Contains(2, 3), "subtract removes part");
            var inter = Selection.FromRect(10, 10, new Rectangle(4, 0, 10, 10)).Combine(r, SelectMode.Intersect);
            Check(inter.Count == 4, "intersect keeps the overlap");
            Check(r.Invert().Count == 92, "invert");
            Check(r.Expand(1).Bounds == new Rectangle(1, 2, 6, 4) && r.Expand(1).Expand(-1).Count == 8, "grow then shrink by one pixel");
            Check(r.Edges().Count == 4, $"a rectangle's outline is four runs ({r.Edges().Count})");
        }

        Console.WriteLine("Filters");
        {
            Check(Adjust.Invert(Adjust.Invert(src)).SequenceEqual(src), "invert twice is the original");
            var alphaKept = Adjust.Invert(src);
            bool same = true;
            for (int i = 3; i < src.Length; i += 4) if (alphaKept[i] != src[i]) same = false;
            Check(same, "colour adjustments leave alpha alone");
            Check(Adjust.Levels(src, 0, 255, 1, 0, 255).SequenceEqual(src), "default levels change nothing");
            Check(Adjust.BrightnessContrast(src, 0, 0).SequenceEqual(src), "zero brightness and contrast change nothing");
            Check(Adjust.HueSatLight(src, 0, 0, 0).SequenceEqual(src), "zero hue/saturation/lightness changes nothing");
            var grey = Adjust.HueSatLight(src, 0, -100, 0);
            bool allGrey = true;
            for (int i = 0; i < grey.Length; i += 4) if (grey[i + 3] > 0 && !(grey[i] == grey[i + 1] && grey[i + 1] == grey[i + 2])) allGrey = false;
            Check(allGrey, "desaturate gives pure greys");
            var post = Adjust.Posterize(src, 2);
            Check(Colours(post).All(c => ((c & 0xFF) is 0 or 255) && (((c >> 8) & 0xFF) is 0 or 255)), "posterize to 2 levels leaves only 0 and 255");

            var dot = new byte[5 * 5 * 4];
            dot[(2 * 5 + 2) * 4 + 3] = 255; dot[(2 * 5 + 2) * 4 + 2] = 255;
            var ring = Adjust.Outline(dot, 5, 5, 1, true, 0xFF000000);
            int ringCount = 0;
            for (int i = 0; i < 25; i++) if (ring[i * 4 + 3] == 255 && ring[i * 4 + 2] == 0) ringCount++;
            Check(ringCount == 8, $"1px outline with corners draws 8 pixels round a dot ({ringCount})");
            var plus = Adjust.Outline(dot, 5, 5, 1, false, 0xFF000000);
            int plusCount = 0;
            for (int i = 0; i < 25; i++) if (plus[i * 4 + 3] == 255 && plus[i * 4 + 2] == 0) plusCount++;
            Check(plusCount == 4, "without corners it draws 4");

            var flat = new byte[20 * 20 * 4];
            for (int i = 0; i < flat.Length; i += 4) { flat[i] = 40; flat[i + 1] = 80; flat[i + 2] = 120; flat[i + 3] = 255; }
            var bl = Adjust.Blur(flat, 20, 20, 3);
            Check(At(bl, 20, 10, 10) == At(flat, 20, 10, 10), "blur leaves the middle of a flat area unchanged");
            Check(bl[3] < 255, "blur softens the edge into transparency");

            var shadow = Adjust.DropShadow(dot, 5, 5, 1, 1, 1, 0, 0xFF000000);
            Check(shadow[(3 * 5 + 3) * 4 + 3] == 255 && shadow[(2 * 5 + 2) * 4 + 2] == 255, "drop shadow sits under and behind the shape");

            var mask = Selection.FromRect(W, H, new Rectangle(0, 0, 2, H));
            var masked = Adjust.Masked(src, Adjust.Invert(src), W, H, mask.Contains);
            Check(At(masked, W, 5, 1) == At(src, W, 5, 1) && At(masked, W, 1, 1) != At(src, W, 1, 1), "filters apply only inside the selection");
        }

        Console.WriteLine("Library and transformed objects in the file");
        {
            var d = new Document(32, 32);
            d.Library.Add(new LibraryItem("hero", ImageOps.FromPixels(W, H, src), @"C:\x\hero.png"));
            var obj = new RasterObject(ImageOps.FromPixels(W, H, src)) { Name = "hero" }.Retransform(Affine.Rotate(30).About(4, 3), Resample.RotSprite);
            d.Layers[0].EnsureCel(0).Objects.Add(obj);
            string path = Path.Combine(dir, "edit.flint");
            FlintFormat.Save(d, path);
            var back = FlintFormat.Load(path).Document!;
            Check(back.Library.Count == 1 && back.Library[0].Name == "hero" && back.Library[0].Source == @"C:\x\hero.png"
                  && ImageOps.GetPixels(back.Library[0].Pixels).SequenceEqual(src), "library images round-trip exactly");
            var o2 = (RasterObject)back.Layers[0].CelAt(0)!.Objects[^1];
            Check(o2.IsTransformed && o2.Matrix == obj.Matrix && o2.Mode == Resample.RotSprite
                  && ImageOps.GetPixels(o2.Original!).SequenceEqual(src), "a rotated object keeps its original and matrix in the file");
            var undone = o2.Retransform(Affine.Rotate(-30).About(4, 3), Resample.RotSprite);
            Check(ImageOps.GetPixels(undone.Pixels).SequenceEqual(src), "and can be rotated back to exact after reopening");

            var snap = d.Snapshot();
            Check(ReferenceEquals(snap.Library, d.Library), "undo snapshots share the library, so undo never removes library images");
        }

        Console.WriteLine(_fails == 0 ? "all edit checks passed" : $"{_fails} edit check(s) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
