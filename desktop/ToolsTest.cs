/* The image-editor tools through the real form and its mouse handlers.
 * Flint.exe --toolstest */
namespace Flint;

public static partial class ToolsTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private static Bitmap Sprite(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                px[o] = (byte)(40 + x * 30); px[o + 1] = (byte)(60 + y * 40); px[o + 2] = 200;
                px[o + 3] = (byte)(x == 0 ? 128 : 255);
            }
        return ImageOps.FromPixels(w, h, px);
    }

    private static uint At(byte[] px, int w, int x, int y) => BitConverter.ToUInt32(px, (y * w + x) * 4);

    private static RasterObject Top(MainForm f) => (RasterObject)f.TestActiveCel!.Objects[^1];

    public static int Run()
    {
        var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000), SuppressPrompts = true };
        form.Show();
        Application.DoEvents();
        var red = Color.FromArgb(255, 220, 40, 40);
        var sprite = Sprite(6, 4);
        var spritePx = ImageOps.GetPixels(sprite);

        Console.WriteLine("Place, library, move");
        {
            form.TestNewCanvas(32, 32);
            form.TestPlace(sprite, "hero", new Point(10, 10), true);
            Check(form.TestLayerCount == 2 && form.TestActiveLayer == 1, "placing an image adds a layer and selects it");
            var o = Top(form);
            Check(o.X == 7 && o.Y == 8 && ImageOps.GetPixels(o.Pixels).SequenceEqual(spritePx), "it is centred on the drop point with exact pixels");
            Check(form.TestLibraryCount == 1, "and goes into the library");
            form.TestPlace(sprite, "hero", new Point(20, 20), true);
            Check(form.TestLibraryCount == 1, "placing the same picture again does not duplicate it in the library");
            form.TestUndo();
            Check(form.TestLayerCount == 2, "undo removes the second placement");

            int steps = form.TestDrag(Tool.Move, Keys.None, new Point(10, 10), new Point(13, 11), new Point(15, 12));
            o = Top(form);
            Check(o.X == 12 && o.Y == 10 && ImageOps.GetPixels(o.Pixels).SequenceEqual(spritePx), $"Move tool drags the layer by whole pixels ({o.X},{o.Y})");
            form.TestUndo();
            Check(Top(form).X == 7, "undo puts it back");
            form.TestUndo();
            Check(form.TestLayerCount == 1 && form.TestLibraryCount == 1, "undoing the placement keeps the library image");

            string file = Path.Combine(Path.GetTempPath(), "flint_drop_test.png");
            Sprite(5, 3).Save(file, System.Drawing.Imaging.ImageFormat.Png);
            bool placed = form.TryPlaceFile(file, new Point(20, 5));
            o = Top(form);
            Check(placed && form.TestLayerCount == 2 && o.X == 18 && o.Y == 4 && form.TestLibraryCount == 2,
                  $"a dropped file is placed at the drop point and kept in the library ({o.X},{o.Y})");
            File.Delete(file);
        }

        Console.WriteLine("Lossless transforms");
        {
            form.TestNewCanvas(32, 32);
            form.TestPlace(sprite, "hero", new Point(16, 16), false);
            var start = Top(form);
            form.TestSetResample(Resample.RotSprite);
            form.TestTransformParams(0, 0, 1, 1, 30);
            Check(form.TestTransforming, "free transform is live until applied");
            form.TestCommitTransform();
            var rotated = Top(form);
            Check(!ImageOps.GetPixels(rotated.Pixels).SequenceEqual(spritePx), "rotating 30° changes the pixels");
            form.TestTransformParams(0, 0, 1, 1, -30);
            form.TestCommitTransform();
            var back = Top(form);
            Check(ImageOps.GetPixels(back.Pixels).SequenceEqual(spritePx) && back.X == start.X && back.Y == start.Y,
                  "rotating back −30° restores the exact original pixels and place");

            form.TestTransformParams(0, 0, 3, 3, 0);
            form.TestCommitTransform();
            form.TestTransformParams(0, 0, 1 / 3.0, 1 / 3.0, 0);
            form.TestCommitTransform();
            Check(ImageOps.GetPixels(Top(form).Pixels).SequenceEqual(spritePx), "scale 300% then back to 100% is exact");

            form.TestTransformParams(5, 0, 1, 1, 45);
            form.TestCancelTransform();
            Check(ImageOps.GetPixels(Top(form).Pixels).SequenceEqual(spritePx) && !form.TestTransforming, "Esc cancels a transform");

            int stepsBefore = form.TestUndoDone;
            form.TestTransformParams(3, 0, 1, 1, 0);
            form.TestFocusSidePanel();
            bool took = form.TestCmdKey(Keys.Enter);
            Check(took && !form.TestTransforming && form.TestUndoDone == stepsBefore + 1,
                  "Enter applies an open transform even when a side-panel button has focus");
            form.TestUndo();
            form.TestTransformParams(2, 0, 1, 1, 0);
            form.TestFilter("Invert");
            Check(!form.TestTransforming && form.TestUndoDone == stepsBefore + 2,
                  "another command applies the open transform first (two clean steps)");
            form.TestUndo(); form.TestUndo();
            form.TestTransformParams(0, 0, 1, 1, 45);
            form.TestUndo();
            Check(!form.TestTransforming && ImageOps.GetPixels(Top(form).Pixels).SequenceEqual(spritePx), "Ctrl+Z during a transform cancels it");

            var before = Top(form);
            form.TestQuick("cw");
            var cw = Top(form);
            var expect = Transform.Rotate90(spritePx, 6, 4, 1);
            Check(cw.Width == 4 && cw.Height == 6 && ImageOps.GetPixels(cw.Pixels).SequenceEqual(expect.px), "Rotate 90° is exact");
            form.TestQuick("cw"); form.TestQuick("cw"); form.TestQuick("cw");
            Check(ImageOps.GetPixels(Top(form).Pixels).SequenceEqual(spritePx) && Top(form).X == before.X && Top(form).Y == before.Y,
                  "four quarter turns return to the exact start");
            form.TestQuick("fliph");
            Check(At(ImageOps.GetPixels(Top(form).Pixels), 6, 0, 1) == At(spritePx, 6, 5, 1), "flip horizontal mirrors exactly");

            form.TestNumeric(200, 200, 0, Resample.Nearest);
            Check(Top(form).Width == 12 && Top(form).Height == 8, "numeric transform at 200% doubles the size");
        }

        Console.WriteLine("Selections through the mouse");
        {
            form.TestNewCanvas(20, 20);
            form.TestDrag(Tool.Marquee, Keys.None, new Point(2, 2), new Point(5, 4));
            Check(form.TestSelection?.Count == 12, $"rectangle select from (2,2) to (5,4) covers 4×3 ({form.TestSelection?.Count})");
            form.TestDrag(Tool.Marquee, Keys.Shift, new Point(10, 10), new Point(11, 11));
            Check(form.TestSelection?.Count == 16, "Shift adds to the selection");
            form.TestDrag(Tool.Marquee, Keys.Alt, new Point(2, 2), new Point(3, 4));
            Check(form.TestSelection?.Count == 10, "Alt subtracts from it");
            form.TestDrag(Tool.Marquee, Keys.None, new Point(7, 7), new Point(7, 7));
            Check(form.TestSelection == null, "a click without a drag clears the selection");
            form.TestDrag(Tool.EllipseSelect, Keys.None, new Point(0, 0), new Point(8, 8));
            Check(form.TestSelection is { } es && es.Contains(4, 4) && !es.Contains(0, 0), "ellipse select");
            form.TestDrag(Tool.Lasso, Keys.None, new Point(0, 0), new Point(10, 0), new Point(0, 10));
            Check(form.TestSelection is { } ls && ls.Contains(2, 2) && !ls.Contains(9, 9), "lasso select");

            form.TestSetSelection(null);
            form.TestStroke(Tool.Rectangle, red, 1, new Point(4, 4), new Point(12, 12));
            form.TestDrag(Tool.Wand, Keys.None, new Point(8, 8));
            int inside = form.TestSelection?.Count ?? 0;
            Check(inside == 7 * 7, $"magic wand inside an outlined square takes the inside only ({inside})");
        }

        Console.WriteLine("Moving selected pixels");
        {
            form.TestNewCanvas(20, 20);
            form.TestStroke(Tool.Pencil, red, 2, new Point(4, 4));
            var orig = form.TestLayerPixels();
            form.TestSelect(new Rectangle(2, 2, 5, 5));
            form.TestDrag(Tool.Move, Keys.None, new Point(4, 4), new Point(9, 6));
            var px = form.TestLayerPixels();
            Check(At(px, 20, 3, 3) == 0 && At(px, 20, 8, 5) == At(orig, 20, 3, 3), "selected pixels move and leave transparency behind");
            form.TestUndo();
            Check(form.TestLayerPixels().SequenceEqual(orig), "undo restores the exact pixels");
        }

        Console.WriteLine("Painting on a placed image merges it");
        {
            form.TestNewCanvas(20, 20);
            form.TestPlace(sprite, "hero", new Point(10, 10), false);
            form.TestStroke(Tool.Pencil, red, 1, new Point(1, 1));
            var cel = form.TestActiveCel!;
            Check(cel.Objects.Count == 1 && !((RasterObject)cel.Objects[0]).IsTransformed, "the layer becomes one plain bitmap");
            var px = form.TestLayerPixels();
            Check(At(px, 20, 8, 9) == At(spritePx, 6, 1, 1) && At(px, 20, 1, 1) == Surface.Pack(red), "with the image and the new stroke both there");
        }

        Console.WriteLine("Clipboard");
        {
            form.TestNewCanvas(20, 20);
            form.TestStroke(Tool.Rectangle, red, 1, new Point(3, 3), new Point(6, 6));
            form.TestSelect(new Rectangle(3, 3, 4, 4));
            form.TestCopy(false);
            form.TestPaste();
            Check(form.TestLayerCount == 2, "paste makes a new layer");
            var o = Top(form);
            Check(o.X == 3 && o.Y == 3 && o.Width == 4, $"pasted where it was copied from ({o.X},{o.Y} {o.Width}×{o.Height})");
            form.TestSetActiveLayer(0);
            form.TestSelect(new Rectangle(3, 3, 4, 4));
            form.TestCut();
            Check(form.TestLayerPixels().All(b => b == 0), "cut clears the pixels");
        }

        Console.WriteLine("Crop, rotate and flip the canvas");
        {
            form.TestNewCanvas(16, 8);
            form.TestStroke(Tool.Pencil, red, 1, new Point(1, 2));
            form.TestCrop(new Rectangle(1, 1, 5, 4));
            Check(form.TestDocument.Width == 5 && form.TestDocument.Height == 4 && At(form.TestLayerPixels(), 5, 0, 1) == Surface.Pack(red),
                  "crop resizes and shifts the pixels");
            form.TestUndo();
            Check(form.TestDocument.Width == 16, "undo restores the size");

            var orig = form.TestLayerPixels();
            form.TestRotateCanvas(1);
            Check(form.TestDocument.Width == 8 && form.TestDocument.Height == 16 && At(form.TestLayerPixels(), 8, 8 - 1 - 2, 1) == Surface.Pack(red),
                  "rotating the canvas 90° swaps the size and moves pixels exactly");
            form.TestRotateCanvas(1); form.TestRotateCanvas(1); form.TestRotateCanvas(1);
            Check(form.TestLayerPixels().SequenceEqual(orig) && form.TestDocument.Width == 16, "four turns return to the original");
            form.TestFlipCanvas(true);
            Check(At(form.TestLayerPixels(), 16, 14, 2) == Surface.Pack(red), "flip canvas horizontal");
        }

        Console.WriteLine("Filters through the form");
        {
            form.TestNewCanvas(10, 10);
            form.TestStroke(Tool.Rectangle, red, 1, new Point(0, 0), new Point(9, 9));
            form.TestSelect(new Rectangle(0, 0, 5, 10));
            int n = form.TestFilter("Invert");
            var px = form.TestLayerPixels();
            Check(n == 1 && At(px, 10, 0, 5) != Surface.Pack(red) && At(px, 10, 9, 5) == Surface.Pack(red), "invert applies only inside the selection");
            form.TestUndo();
            Check(At(form.TestLayerPixels(), 10, 0, 5) == Surface.Pack(red), "and undoes exactly");

            form.TestNewCanvas(20, 20);
            form.TestPlace(sprite, "hero", new Point(10, 10), false);
            form.TestFilter("Outline", 1, 1);
            var o = Top(form);
            Check(o.Width == 8 && o.Height == 6 && o.X == 6, $"an outline on a placed image grows it instead of being cut off ({o.Width}×{o.Height})");
        }

        Console.WriteLine("Gradient, clone, text");
        {
            form.TestNewCanvas(16, 4);
            form.TestSetColours(Color.Black, Color.White);
            form.TestSetGradient(0);
            form.TestDrag(Tool.Gradient, Keys.None, new Point(0, 1), new Point(15, 1));
            var px = form.TestLayerPixels();
            Check(At(px, 16, 0, 0) == 0xFF000000 && At(px, 16, 15, 0) == 0xFFFFFFFF && (px[(8 * 4)] is > 100 and < 160),
                  "linear gradient runs foreground to background");

            form.TestNewCanvas(16, 16);
            form.TestStroke(Tool.Pencil, red, 1, new Point(2, 2));
            form.TestSetCloneSource(new Point(2, 2));
            form.TestDrag(Tool.Clone, Keys.None, new Point(10, 10));
            Check(At(form.TestLayerPixels(), 16, 10, 10) == Surface.Pack(red), "clone stamp copies from the source point");

            form.TestNewCanvas(64, 32);
            form.TestSetColours(red, Color.White);
            form.TestText("Hi", new Point(2, 2), false);
            var t = ImageOps.GetPixels(Top(form).Pixels);
            bool hard = true; int ink = 0;
            for (int i = 0; i < t.Length; i += 4)
            {
                if (t[i + 3] == 0) continue;
                ink++;
                if (BitConverter.ToUInt32(t, i) != Surface.Pack(red)) hard = false;
            }
            Check(ink > 10 && hard, $"text with smooth edges off uses only the exact colour ({ink} pixels)");
        }

        MaskChecks(form);
        GradientKindChecks(form);
        EffectsChecks(form);
        MaskOpChecks(form);
        MaskReviewChecks(form);

        form.Close();
        Console.WriteLine(_fails == 0 ? "all tools checks passed" : $"{_fails} tools check(s) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
