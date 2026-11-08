/* Layer masks through the real form: adding, painting with the mouse tools, undo, applying. Part of --toolstest. */
namespace Flint;

public static partial class ToolsTest
{
    private static byte[] MaskPx(MainForm f) => ImageOps.GetPixels(f.TestActiveCel!.Mask!.Pixels);

    private static void MaskChecks(MainForm form)
    {
        Console.WriteLine("Layer masks");
        var red = Color.FromArgb(255, 220, 40, 40);
        form.TestNewCanvas(8, 4);
        form.TestSetColours(red, Color.White);
        form.TestDrag(Tool.Fill, Keys.None, new Point(0, 0));
        var flat0 = form.TestDocument.RenderPixels(0);
        int steps = form.TestUndoDone;

        form.TestAddMask(0);
        Check(form.TestActiveCel!.Mask != null && form.TestEditingMask && form.TestUndoDone == steps + 1, "Add mask (reveal all) makes a mask and switches to editing it");
        Check(MaskPx(form).Where((v, i) => i % 4 != 3).All(v => v == 255), "reveal all is all white");
        Check(form.TestDocument.RenderPixels(0).SequenceEqual(flat0), "a white mask changes nothing");

        // Pencil with a black foreground hides; the red pixels stay in the layer.
        form.TestStroke(Tool.Pencil, Color.Black, 1, new Point(1, 1), new Point(3, 1));
        var mp = MaskPx(form);
        Check(mp[(1 * 8 + 2) * 4] == 0 && mp[(1 * 8 + 4) * 4] == 255, "the pencil paints the mask black where dragged");
        var comp = form.TestDocument.RenderPixels(0);
        Check(comp[(1 * 8 + 2) * 4 + 3] == 0 && comp[(1 * 8 + 5) * 4 + 3] == 255, "where the mask is black the layer is hidden, elsewhere it shows");
        Check(ImageOps.GetPixels(((RasterObject)form.TestActiveCel!.Objects[0]).Pixels).SequenceEqual(ImageOps.GetPixels(((RasterObject)form.TestActiveCel!.Objects[0]).Pixels))
              && form.TestLayerPixels()[(1 * 8 + 2) * 4 + 3] == 255, "the layer's own pixels were not touched");
        form.TestUndo();
        Check(MaskPx(form)[(1 * 8 + 2) * 4] == 255 && form.TestDocument.RenderPixels(0).SequenceEqual(flat0), "undo restores the mask");
        form.TestRedo();
        Check(MaskPx(form)[(1 * 8 + 2) * 4] == 0, "redo repaints it");
        form.TestUndo();

        // A mid grey foreground gives a half-transparent result.
        form.TestStroke(Tool.Pencil, Color.FromArgb(255, 128, 128, 128), 1, new Point(6, 3));
        int a = form.TestDocument.RenderPixels(0)[(3 * 8 + 6) * 4 + 3];
        Check(a is >= 126 and <= 130, $"a 50% grey brush gives about half alpha (got {a})");
        form.TestUndo();

        // Eraser hides, Fill paints grey, Gradient ramps, Clear hides the selection.
        form.TestStroke(Tool.Eraser, Color.White, 1, new Point(0, 0));
        Check(MaskPx(form)[0] == 0, "the eraser paints black on the mask");
        form.TestUndo();
        form.TestSetColours(Color.Black, Color.White);
        form.TestDrag(Tool.Fill, Keys.None, new Point(7, 3));
        Check(MaskPx(form).Where((v, i) => i % 4 != 3).All(v => v == 0) && form.TestDocument.RenderPixels(0).Where((v, i) => i % 4 == 3).All(v => v == 0), "the fill tool fills the whole mask black (layer fully hidden)");
        form.TestUndo();
        form.TestSetGradient(0);
        form.TestDrag(Tool.Gradient, Keys.None, new Point(0, 1), new Point(7, 1));
        mp = MaskPx(form);
        Check(mp[(1 * 8 + 0) * 4] == 0 && mp[(1 * 8 + 7) * 4] == 255 && mp[(1 * 8 + 4) * 4] is > 80 and < 190, "the gradient tool paints a black to white ramp on the mask");
        form.TestUndo();
        form.TestSelect(new Rectangle(0, 0, 4, 2));
        form.TestSetColours(red, Color.White);
        form.TestKey(Keys.Delete);
        mp = MaskPx(form);
        Check(mp[0] == 0 && mp[(2 * 8 + 6) * 4] == 255, "Clear hides the selected part of the mask");
        form.TestUndo();
        form.TestSelect(Rectangle.Empty);

        // Toggle to pixel editing: tools paint pixels again.
        form.TestToggleEditMask();
        Check(!form.TestEditingMask, "toggling leaves mask editing");
        var maskBefore = MaskPx(form);
        form.TestStroke(Tool.Pencil, Color.Lime, 1, new Point(7, 0));
        Check(MaskPx(form).SequenceEqual(maskBefore) && form.TestLayerPixels()[7 * 4 + 1] == 255, "in pixel mode the pencil paints the layer, not the mask");
        form.TestUndo();

        // Disable / enable, apply, delete, all undoable.
        form.TestToggleEditMask();
        form.TestStroke(Tool.Pencil, Color.Black, 1, new Point(1, 1));
        var masked = form.TestDocument.RenderPixels(0);
        form.TestToggleMask();
        Check(form.TestDocument.RenderPixels(0).SequenceEqual(flat0), "disabling the mask shows the whole layer");
        form.TestUndo();
        Check(form.TestDocument.RenderPixels(0).SequenceEqual(masked), "undo re-enables it");
        form.TestApplyMask();
        Check(form.TestActiveCel!.Mask == null && form.TestDocument.RenderPixels(0).SequenceEqual(masked) && form.TestLayerPixels()[(1 * 8 + 1) * 4 + 3] == 0,
              "Apply mask bakes the hiding into the pixels and removes the mask without changing the picture");
        form.TestUndo();
        Check(form.TestActiveCel!.Mask != null && form.TestLayerPixels()[(1 * 8 + 1) * 4 + 3] == 255 && form.TestDocument.RenderPixels(0).SequenceEqual(masked), "undo brings the mask and the full pixels back");
        form.TestDeleteMask();
        Check(form.TestActiveCel!.Mask == null && form.TestDocument.RenderPixels(0).SequenceEqual(flat0), "Delete mask shows the whole layer again");
        form.TestUndo();
        Check(form.TestActiveCel!.Mask != null, "undo brings the mask back");

        // Hide all, and from selection.
        form.TestDeleteMask();
        form.TestAddMask(1);
        Check(MaskPx(form).Where((v, i) => i % 4 != 3).All(v => v == 0) && form.TestDocument.RenderPixels(0).Where((v, i) => i % 4 == 3).All(v => v == 0), "hide all makes the layer vanish");
        form.TestDeleteMask();
        form.TestSelect(new Rectangle(2, 1, 3, 2));
        form.TestAddMask(2);
        mp = MaskPx(form);
        Check(mp[(1 * 8 + 2) * 4] == 255 && mp[(2 * 8 + 4) * 4] == 255 && mp[0] == 0 && mp[(1 * 8 + 5) * 4] == 0, "mask from selection is white inside and black outside");
        form.TestSelect(Rectangle.Empty);

        // The mask box on the layer row toggles mask editing, and the mask survives save, load and a new session.
        form.TestClickMaskBox(0);
        Check(!form.TestEditingMask, "clicking the mask box toggles editing off");
        form.TestClickMaskBox(0);
        Check(form.TestEditingMask, "and on again");
        string path = Path.Combine(Path.GetTempPath(), "flint_mask_tools.flint");
        form.TestSave(path);
        Check(form.TestOpen(path) && form.TestActiveCel!.Mask != null && MaskPx(form).SequenceEqual(mp), "saved and reopened: the mask is the same");
        File.Delete(path);
    }
}
