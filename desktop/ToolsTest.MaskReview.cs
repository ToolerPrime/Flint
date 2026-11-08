/* Merge down with masks, blends and shared layers; apply mask with blends; smooth scale; mask editing target. Part of --toolstest. */
namespace Flint;

public static partial class ToolsTest
{
    private static void Fill(MainForm form, Color c)
    {
        form.TestSetColours(c, Color.White);
        form.TestDrag(Tool.Fill, Keys.None, new Point(0, 0));
    }

    private static void SetMask(MainForm form, Func<int, int, int> grey)
    {
        var m = form.TestActiveCel!.Mask!;
        var px = new byte[8 * 4 * 4];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 8; x++) { int o = (y * 8 + x) * 4; px[o] = px[o + 1] = px[o + 2] = (byte)grey(x, y); px[o + 3] = 255; }
        ImageOps.SetPixels(m.Pixels, px);
    }

    private static void MaskReviewChecks(MainForm form)
    {
        Console.WriteLine("Merge down, apply mask, scale and mask editing");
        var red = Color.FromArgb(255, 220, 40, 40);

        // 1. the lower layer's mask is baked in before merging
        foreach (bool hideAll in new[] { true, false })
        {
            form.TestNewCanvas(8, 4);
            Fill(form, Color.Lime);
            form.TestAddMask(0);
            SetMask(form, (x, y) => hideAll ? 0 : x * 36);
            form.TestAddLayer();
            form.TestSetActiveLayer(1);
            form.TestSetColours(red, Color.White);
            form.TestStroke(Tool.Pencil, red, 3, new Point(3, 2));
            var want = form.TestDocument.RenderPixels(0);
            form.TestMergeDown();
            var d = form.TestDocument;
            Check(d.LayerCount == 1 && d.Layers[0].CelAt(0)!.Mask == null && d.RenderPixels(0).SequenceEqual(want), $"merge down into a layer with a {(hideAll ? "hide-all" : "partial")} mask leaves the picture unchanged, and no mask remains");
            form.TestUndo();
            Check(form.TestDocument.LayerCount == 2 && form.TestDocument.Layers[0].CelAt(0)!.Mask != null && form.TestDocument.RenderPixels(0).SequenceEqual(want), "undo brings both layers and the lower mask back");
        }

        // 2. blends survive a merge
        form.TestNewCanvas(8, 4);
        Fill(form, Color.FromArgb(255, 60, 200, 90));
        form.TestAddLayer();
        form.TestSetActiveLayer(1);
        Fill(form, Color.FromArgb(255, 200, 120, 220));
        form.TestDocument.Layers[1].Blend = BlendMode.Multiply;
        var wantL = form.TestDocument.RenderPixels(0);
        form.TestMergeDown();
        Check(form.TestDocument.RenderPixels(0).SequenceEqual(wantL) && form.TestDocument.LayerCount == 1, "merging a Multiply layer keeps the composite identical");
        form.TestUndo();
        form.TestDocument.Layers[1].Blend = BlendMode.Normal;
        ((RasterObject)form.TestDocument.Layers[1].CelAt(0)!.Objects[0]).Blend = BlendMode.Screen;
        var wantO = form.TestDocument.RenderPixels(0);
        form.TestSetActiveLayer(1);
        form.TestMergeDown();
        Check(form.TestDocument.RenderPixels(0).SequenceEqual(wantO) && !wantO.SequenceEqual(wantL), "merging a layer whose object is Screen keeps the composite identical");

        // 3. a shared lower layer is merged into once (3 frames)
        form.TestNewCanvas(8, 4);
        form.TestAddFrame(); form.TestAddFrame();
        form.TestDocument.Layers[0].Shared = true;
        Fill(form, Color.Lime);
        form.TestAddLayer();
        form.TestSetActiveLayer(1);
        form.TestDocument.Layers[1].Shared = true;
        Fill(form, red);
        form.TestDocument.Layers[1].Opacity = 128;
        var wants = Enumerable.Range(0, 3).Select(f => form.TestDocument.RenderPixels(f)).ToList();
        form.TestMergeDown();
        Check(form.TestDocument.LayerCount == 1 && Enumerable.Range(0, 3).All(f => form.TestDocument.RenderPixels(f).SequenceEqual(wants[f])), "merging a half-transparent shared layer into a shared one is done once: all 3 frames look the same");
        form.TestUndo();
        // upper per frame over a shared lower is refused
        form.TestDocument.Layers[1].Shared = false;
        form.TestDocument.Layers[1].Cels.Clear();
        form.TestSetActiveLayer(1);
        form.TestSetFrame(1);
        form.TestSetColours(red, Color.White);
        form.TestStroke(Tool.Pencil, red, 2, new Point(2, 2));
        int stepsNow = form.TestUndoDone;
        form.TestSetActiveLayer(1);
        form.TestMergeDown();
        Check(form.TestDocument.LayerCount == 2 && form.TestUndoDone == stepsNow, "a per-frame layer over a shared one is refused (no change, no undo step)");

        // 4. apply mask keeps object blends
        form.TestNewCanvas(8, 4);
        Fill(form, red);
        var cel = form.TestActiveCel!;
        var grey = new byte[8 * 4 * 4];
        for (int i = 0; i < grey.Length; i += 4) { grey[i] = grey[i + 1] = grey[i + 2] = 150; grey[i + 3] = 255; }
        cel.Objects.Add(new RasterObject(ImageOps.FromPixels(8, 4, grey)) { Name = "grey", Blend = BlendMode.Multiply });
        form.TestAddMask(0);
        SetMask(form, (x, y) => x < 4 ? 255 : 0);
        var wantM = form.TestDocument.RenderPixels(0);
        form.TestApplyMask();
        Check(form.TestActiveCel!.Mask == null && form.TestDocument.RenderPixels(0).SequenceEqual(wantM) && wantM[2] != 220, "apply mask on a cel with a Multiply object keeps the look");

        // 5. smooth scale never fades a mask
        form.TestNewCanvas(8, 4);
        Fill(form, red);
        form.TestAddMask(0);
        form.TestScale(2.5, true);
        var sm = MaskPx(form);
        Check(sm.Length == 20 * 10 * 4 && sm.Select((v, i) => (v, i)).All(t => t.v == 255), "a reveal-all mask is still fully revealing after a smooth scale");

        // 7. mask editing ends when the layer or frame changes
        form.TestNewCanvas(8, 4);
        form.TestAddFrame();
        Fill(form, red);
        form.TestAddMask(0);
        form.TestAddLayer();
        form.TestSetActiveLayer(0);
        Check(!form.TestEditingMask, "switching layer ends mask editing");
        form.TestToggleEditMask();
        Check(form.TestEditingMask, "and it can be turned on again");
        form.TestSetFrame(1);
        form.TestSetFrame(0);
        Check(!form.TestEditingMask, "changing frame ends mask editing, and coming back does not turn it on");
    }
}
