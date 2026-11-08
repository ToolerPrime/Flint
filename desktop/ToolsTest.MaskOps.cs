/* Masks follow whole-canvas operations, merge, duplicate and linked moves. Part of --toolstest. */
namespace Flint;

public static partial class ToolsTest
{
    private static void MaskOpChecks(MainForm form)
    {
        Console.WriteLine("Masks through canvas operations");
        var red = Color.FromArgb(255, 220, 40, 40);
        // an 8x4 red layer whose mask is an asymmetric pattern: grey level = 10 + 20*x + 60*y
        void Setup()
        {
            form.TestNewCanvas(8, 4);
            form.TestSetColours(red, Color.White);
            form.TestDrag(Tool.Fill, Keys.None, new Point(0, 0));
            form.TestAddMask(0);
            var m = form.TestActiveCel!.Mask!;
            var px = new byte[8 * 4 * 4];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 8; x++)
                {
                    int o = (y * 8 + x) * 4;
                    px[o] = px[o + 1] = px[o + 2] = (byte)(10 + 20 * x + 60 * y); px[o + 3] = 255;
                }
            ImageOps.SetPixels(m.Pixels, px);
        }
        int G(byte[] p, int w, int x, int y) => p[(y * w + x) * 4 + 1];

        Setup();
        var m0 = MaskPx(form);
        form.TestRotateCanvas(1);
        var mr = MaskPx(form);
        var mk = form.TestActiveCel!.Mask!;
        Check(mk.Width == 4 && mk.Height == 8 && G(mr, 4, 3 - 1, 2) == G(m0, 8, 2, 1), "rotate canvas 90 degrees: the mask turns with the pixels");
        for (int i = 0; i < 3; i++) form.TestRotateCanvas(1);
        Check(MaskPx(form).SequenceEqual(m0), "four quarter turns return the identical mask");
        form.TestRotateCanvas(2);
        Check(G(MaskPx(form), 8, 7 - 2, 3 - 1) == G(m0, 8, 2, 1), "rotate 180 degrees moves the mask the same way");
        for (int i = 0; i < 5; i++) form.TestUndo();
        Check(MaskPx(form).SequenceEqual(m0) && form.TestDocument.Width == 8, "undo restores the mask");

        form.TestFlipCanvas(true);
        Check(G(MaskPx(form), 8, 7 - 2, 1) == G(m0, 8, 2, 1), "flip horizontal: the mask is mirrored");
        form.TestFlipCanvas(true);
        Check(MaskPx(form).SequenceEqual(m0), "flipping twice returns the mask");
        form.TestFlipCanvas(false);
        Check(G(MaskPx(form), 8, 2, 3 - 1) == G(m0, 8, 2, 1), "flip vertical: the mask is mirrored");
        for (int i = 0; i < 3; i++) form.TestUndo();

        form.TestCrop(new Rectangle(2, 1, 4, 2));
        mk = form.TestActiveCel!.Mask!;
        Check(mk.Width == 4 && mk.Height == 2 && G(MaskPx(form), 4, 0, 0) == G(m0, 8, 2, 1) && G(MaskPx(form), 4, 3, 1) == G(m0, 8, 5, 2), "crop shifts the mask with the pixels");
        form.TestUndo();
        Check(MaskPx(form).SequenceEqual(m0), "undo gives the whole mask back");

        form.TestScale(2, false);
        mk = form.TestActiveCel!.Mask!;
        Check(mk.Width == 16 && mk.Height == 8 && G(MaskPx(form), 16, 5, 3) == G(m0, 8, 2, 1), "scale 2x scales the mask the same way");
        form.TestScale(0.5, false);
        Check(MaskPx(form).SequenceEqual(m0), "then 0.5x nearest returns the identical mask");
        form.TestUndo(); form.TestUndo();

        // trim: a layer whose pixels only fill part of the canvas
        form.TestNewCanvas(8, 4);
        form.TestSetColours(red, Color.White);
        form.TestStroke(Tool.Pencil, red, 1, new Point(2, 1), new Point(4, 2));
        form.TestAddMask(0);
        var big = ImageOps.GetPixels(form.TestActiveCel!.Mask!.Pixels);
        for (int y = 0; y < 4; y++) for (int x = 0; x < 8; x++) { int o = (y * 8 + x) * 4; big[o] = big[o + 1] = big[o + 2] = (byte)(10 + 20 * x + 60 * y); }
        ImageOps.SetPixels(form.TestActiveCel.Mask!.Pixels, big);
        form.TestTrim();
        mk = form.TestActiveCel!.Mask!;
        Check(mk.Width == form.TestDocument.Width && mk.Height == form.TestDocument.Height && mk.Width < 8, "trim crops the mask with the canvas");

        // duplicate, merge down and flatten
        form.TestNewCanvas(8, 4);
        form.TestSetColours(Color.Lime, Color.White);
        form.TestDrag(Tool.Fill, Keys.None, new Point(0, 0));
        form.TestAddLayer();
        form.TestSetActiveLayer(1);
        form.TestSetColours(red, Color.White);
        form.TestDrag(Tool.Fill, Keys.None, new Point(0, 0));
        form.TestAddMask(0);
        var mm = ImageOps.GetPixels(form.TestActiveCel!.Mask!.Pixels);
        for (int y = 0; y < 4; y++) for (int x = 0; x < 8; x++) { int o = (y * 8 + x) * 4; mm[o] = mm[o + 1] = mm[o + 2] = (byte)(x * 36); }
        ImageOps.SetPixels(form.TestActiveCel.Mask!.Pixels, mm);
        var dup = form.TestDocument.RenderPixels(0);
        form.TestDuplicateLayer();
        var d2 = form.TestDocument;
        Check(d2.LayerCount == 3 && d2.Layers[2].CelAt(0)!.Mask is { } dm && !ReferenceEquals(dm, d2.Layers[1].CelAt(0)!.Mask)
              && ImageOps.GetPixels(dm.Pixels).SequenceEqual(ImageOps.GetPixels(d2.Layers[1].CelAt(0)!.Mask!.Pixels)), "duplicate layer copies the mask (its own bitmap)");
        form.TestUndo();
        form.TestSetActiveLayer(1);
        form.TestMergeDown();
        d2 = form.TestDocument;
        Check(d2.LayerCount == 1 && d2.Layers[0].CelAt(0)!.Mask == null && d2.RenderPixels(0).SequenceEqual(dup), "merge down looks identical to the composite before, and the result has no mask");
        form.TestUndo();
        Check(form.TestDocument.LayerCount == 2 && form.TestDocument.Layers[1].CelAt(0)!.Mask != null && form.TestDocument.RenderPixels(0).SequenceEqual(dup), "undo brings back the masked layer");
        form.TestFlatten();
        Check(form.TestDocument.LayerCount == 1 && form.TestDocument.Layers[0].CelAt(0)!.Mask == null && form.TestDocument.RenderPixels(0).SequenceEqual(dup), "flatten looks identical too, with no mask");
        form.TestUndo();

        // linked move: the mask follows a whole-layer move by whole pixels; unlinked it stays; a selection never moves it
        form.TestNewCanvas(16, 8);
        form.TestSetColours(red, Color.White);
        form.TestStroke(Tool.Pencil, red, 3, new Point(8, 4));
        form.TestAddMask(0);
        var lm = ImageOps.GetPixels(form.TestActiveCel!.Mask!.Pixels);
        for (int y = 0; y < 8; y++) for (int x = 0; x < 16; x++) { int o = (y * 16 + x) * 4; lm[o] = lm[o + 1] = lm[o + 2] = (byte)(5 + 15 * x + 2 * y); }
        ImageOps.SetPixels(form.TestActiveCel.Mask!.Pixels, lm);
        var l0 = MaskPx(form);
        form.TestDrag(Tool.Move, Keys.None, new Point(8, 4), new Point(10, 5));
        var l1 = MaskPx(form);
        Check(G(l1, 16, 5, 3) == G(l0, 16, 3, 2) && G(l1, 16, 0, 0) == 255, "a linked mask follows a whole-layer move (shifted by the same amount, uncovered area revealed)");
        form.TestUndo();
        Check(MaskPx(form).SequenceEqual(l0), "undo puts the mask back with the pixels");
        form.TestSetMaskLinked(false);
        form.TestDrag(Tool.Move, Keys.None, new Point(8, 4), new Point(10, 5));
        Check(MaskPx(form).SequenceEqual(l0), "an unlinked mask stays where it is");
        form.TestUndo();
        form.TestSetMaskLinked(true);
        form.TestSelect(new Rectangle(6, 2, 5, 5));
        form.TestDrag(Tool.Move, Keys.None, new Point(8, 4), new Point(10, 5));
        Check(MaskPx(form).SequenceEqual(l0), "moving only a selection leaves the mask alone");
    }
}
