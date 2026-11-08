/* The Effects panel through the form's test hooks. Part of --toolstest. */
namespace Flint;

public static partial class ToolsTest
{
    private static void EffectsChecks(MainForm form)
    {
        Console.WriteLine("Effects panel");
        var sprite = Sprite(8, 6);
        var spritePx = ImageOps.GetPixels(sprite);
        form.TestNewCanvas(48, 48);
        form.TestPlace(sprite, "hero", new Point(24, 24), false);
        var plain = Top(form);
        int steps = form.TestUndoDone;

        form.TestFxAdd("Drop shadow");
        var o = Top(form);
        var rows = form.TestFxRows();
        Check(rows.Count == 1 && rows[0].name == "Drop shadow" && rows[0].on, "Add > Drop shadow lists the effect, switched on");
        Check(o.Effects is { Count: 1 } && o.Original != null && o.Width > plain.Width && form.TestUndoDone == steps + 1, "a plain bitmap gets an Original and grows to hold the shadow, in one undo step");
        Check(ImageOps.GetPixels(o.Original!).SequenceEqual(spritePx), "the Original is the bitmap's exact pixels");
        form.TestUndo();
        Check(Top(form) == plain && Top(form).Effects == null && form.TestFxRows().Count == 0, "undo gives back the very same plain object");
        form.TestRedo();
        Check(Top(form).Effects is { Count: 1 }, "redo brings the effect back");

        var shadowPx = ImageOps.GetPixels(Top(form).Pixels);
        form.TestFxSet(0, "dist", 12);
        o = Top(form);
        Check(o.Effects![0].P["dist"] == 12 && !ImageOps.GetPixels(o.Pixels).SequenceEqual(shadowPx) && o.Width > 8 + 12 - 3, "editing the distance re-renders the object");
        form.TestUndo();
        Check(Top(form).Effects![0].P["dist"] == 6 && ImageOps.GetPixels(Top(form).Pixels).SequenceEqual(shadowPx), "undo restores the previous setting and pixels");

        form.TestFxToggle(0);
        o = Top(form);
        Check(!form.TestFxRows()[0].on && ImageOps.GetPixels(o.Pixels).SequenceEqual(spritePx) && o.Effects!.Count == 1, "unticking an effect shows the object without it but keeps it in the list");
        string path = Path.Combine(Path.GetTempPath(), "flint_fx_panel.flint");
        form.TestSave(path);
        form.TestOpen(path);
        Check(!form.TestFxRows()[0].on, "a switched-off effect stays off after save and load");
        File.Delete(path);
        form.TestFxToggle(0);
        Check(form.TestFxRows()[0].on && Top(form).Width > 8, "ticking it again brings the shadow back");

        form.TestFxAdd("Gaussian blur");
        Check(form.TestFxRows().Select(r => r.name).SequenceEqual(new[] { "Drop shadow", "Gaussian blur" }), "effects are listed in order");
        var order1 = ImageOps.GetPixels(Top(form).Pixels);
        form.TestFxMove(1, -1);
        Check(form.TestFxRows().Select(r => r.name).SequenceEqual(new[] { "Gaussian blur", "Drop shadow" }) && !ImageOps.GetPixels(Top(form).Pixels).SequenceEqual(order1), "Move up reorders them and the order changes the picture");
        form.TestUndo();
        Check(form.TestFxRows()[0].name == "Drop shadow", "undo puts the order back");

        // the object stays live through a move
        form.TestDrag(Tool.Move, Keys.None, new Point(24, 24), new Point(28, 25));
        o = Top(form);
        Check(o.Effects is { Count: 2 } && o.Original != null, "moving the object keeps its effects live");
        form.TestUndo();

        form.TestFxRemove(1);
        form.TestFxRemove(0);
        o = Top(form);
        Check(o.Effects == null && form.TestFxRows().Count == 0 && ImageOps.GetPixels(o.Pixels).SequenceEqual(spritePx), "removing every effect gives back the original pixels");
        form.TestUndo();
        Check(Top(form).Effects is { Count: 1 }, "undo brings a removed effect back");

        // painting bakes the effect into plain pixels
        int before = form.TestUndoDone;
        form.TestStroke(Tool.Pencil, Color.Lime, 1, new Point(1, 1));
        Check(Top(form).Effects == null && !form.TestActiveCel!.Objects.Any(x => x is RasterObject { Effects: not null }) && form.TestUndoDone > before, "painting merges the effects into plain pixels (undoable)");
        form.TestUndo(); form.TestUndo();

        // a layer without objects
        form.TestAddLayer();
        int n = form.TestUndoDone;
        form.TestFxAdd("Glow");
        Check(form.TestUndoDone == n && form.TestFxRows().Count == 0, "an empty layer has nothing to add an effect to, and nothing changes");
        form.TestSetActiveLayer(1);
        Check(form.TestFxRows().Count == 1, "the list follows the active layer");

        // every kind can be added and renders
        form.TestNewCanvas(48, 48);
        form.TestPlace(sprite, "hero", new Point(24, 24), false);
        foreach (var name in new[] { "Drop shadow", "Inner shadow", "Glow", "Inner bevel", "Gaussian blur", "Motion blur", "Hue/saturation", "Colour fill" })
            form.TestFxAdd(name);
        Check(form.TestFxRows().Count == 8 && Top(form).Effects!.Count == 8, "all eight effects can be added to one object");
    }
}
