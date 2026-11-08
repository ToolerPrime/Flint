/* Layer masks: menu, edit-mask mode and the operations (add, delete, apply, enable/disable).
 *
 * A mask is a canvas-sized grey bitmap on the cel (Cel.Mask). Structure changes (add, delete, apply,
 * enable) are StructureEdits. Painting the mask goes through the same PaintRecorder as pixels:
 * while "edit mask" is on, ActiveRaster() hands the tools the mask instead of the layer's pixels,
 * and the ink is the foreground's grey (the eraser and Clear paint black, which hides).
 */
namespace Flint;

public sealed partial class MainForm
{
    private bool _editMask;

    private enum MaskInit { RevealAll, HideAll, FromSelection }

    private Cel? ActiveCelOrNull => ActiveLayer.CelAt(_cur);

    /// <summary>True while the paint tools write the mask (needs a mask on the active layer's cel).</summary>
    private bool EditingMask => _editMask && ReferenceEquals(_editMaskDoc, _doc) && _editMaskLayer == _activeLayer && _editMaskFrame == _cur && ActiveCelOrNull?.Mask != null;

    // The layer, frame and document whose mask is being edited; changing any of them ends mask editing.
    private Document? _editMaskDoc;
    private int _editMaskLayer, _editMaskFrame;

    private void StartEditMask() { _editMask = true; _editMaskDoc = _doc; _editMaskLayer = _activeLayer; _editMaskFrame = _cur; }

    /// <summary>Called on every refresh: if the active layer or frame is no longer the one whose mask was being edited, go back to pixels and say so.</summary>
    private void CheckMaskEditTarget()
    {
        if (!_editMask || EditingMask) return;
        _editMask = false;
        Say("mask editing is off: you changed layer or frame. Click the mask box to edit a mask again.");
    }

    private void BuildMaskMenu(ToolStripMenuItem layerMenu)
    {
        layerMenu.DropDownItems.Add(new ToolStripSeparator());
        var m = new ToolStripMenuItem("Layer m&ask");
        m.DropDownItems.Add("Add mask: &reveal all", null, (_, _) => AddMask(MaskInit.RevealAll));
        m.DropDownItems.Add("Add mask: &hide all", null, (_, _) => AddMask(MaskInit.HideAll));
        m.DropDownItems.Add("Add mask from &selection", null, (_, _) => AddMask(MaskInit.FromSelection));
        m.DropDownItems.Add(new ToolStripSeparator());
        m.DropDownItems.Add("&Edit mask (paint it)", null, (_, _) => ToggleEditMask());
        m.DropDownItems.Add("&Disable / enable mask", null, (_, _) => ToggleMaskEnabled());
        m.DropDownItems.Add("&Apply mask (bake into pixels)", null, (_, _) => ApplyMask());
        m.DropDownItems.Add("De&lete mask", null, (_, _) => DeleteMask());
        layerMenu.DropDownItems.Add(m);
    }

    /// <summary>Grey of a colour as an opaque BGRA pixel (Rec. 601 luma).</summary>
    private static uint GreyInk(Color c)
    {
        uint g = (uint)Math.Clamp((int)Math.Round(0.299 * c.R + 0.587 * c.G + 0.114 * c.B), 0, 255);
        return 0xFF000000 | g << 16 | g << 8 | g;
    }

    private void AddMask(MaskInit init)
    {
        if (ActiveLayer.Locked) { Say($"layer '{ActiveLayer.Name}' is locked"); return; }
        if (ActiveCelOrNull?.Mask != null) { Say("this layer already has a mask here"); return; }
        var before = Before();
        var px = new byte[DocW * DocH * 4];
        for (int y = 0; y < DocH; y++)
            for (int x = 0; x < DocW; x++)
            {
                bool show = init == MaskInit.RevealAll || (init == MaskInit.FromSelection && _selection != null && _selection.Contains(x, y));
                int o = (y * DocW + x) * 4;
                px[o] = px[o + 1] = px[o + 2] = (byte)(show ? 255 : 0);
                px[o + 3] = 255;
            }
        ActiveLayer.EnsureCel(_cur).Mask = new RasterObject(ImageOps.FromPixels(DocW, DocH, px)) { Name = "Mask" };
        StartEditMask();
        PushStructure("add mask", before);
        Say("mask added. Painting now changes the mask: white shows, black hides. Click the mask box on the layer to go back to the pixels.");
    }

    private void DeleteMask()
    {
        if (ActiveCelOrNull?.Mask == null) { Say("this layer has no mask here"); return; }
        var before = Before();
        ActiveLayer.CelAt(_cur)!.Mask = null;
        _editMask = false;
        PushStructure("delete mask", before);
        Say("mask deleted");
    }

    private void ToggleMaskEnabled()
    {
        if (ActiveCelOrNull?.Mask == null) { Say("this layer has no mask here"); return; }
        var before = Before();
        ActiveLayer.MaskEnabled = !ActiveLayer.MaskEnabled;
        PushStructure(ActiveLayer.MaskEnabled ? "enable mask" : "disable mask", before);
        Say(ActiveLayer.MaskEnabled ? "mask enabled" : "mask disabled (still there, not applied)");
    }

    private void ToggleEditMask()
    {
        if (ActiveCelOrNull?.Mask == null) { Say("this layer has no mask here: Layer > Layer mask > Add"); return; }
        FinishFloat();
        if (EditingMask) _editMask = false; else StartEditMask();
        RefreshAll();
        Say(_editMask ? $"editing the mask of '{ActiveLayer.Name}': tools paint grey (white shows, black hides)" : "editing pixels again");
    }

    /// <summary>Bake the mask into the layer's alpha and remove it. One undo step.</summary>
    private void ApplyMask()
    {
        var mask = ActiveCelOrNull?.Mask;
        if (mask == null) { Say("this layer has no mask here"); return; }
        if (ActiveLayer.Locked) { Say($"layer '{ActiveLayer.Name}' is locked"); return; }
        FinishFloat();
        var before = Before();
        var cel = ActiveLayer.CelAt(_cur)!;
        if (ActiveLayer.MaskEnabled)
        {
            BakeCel(cel);
            for (int i = 0; i < cel.Objects.Count; i++)
            {
                if (cel.Objects[i] is not RasterObject r || r.Width != DocW || r.Height != DocH) continue;
                var px = ImageOps.GetPixels(r.Pixels);
                Document.ApplyMask(px, ImageOps.GetPixels(mask.Pixels));
                cel.Objects[i] = r.With(ImageOps.FromPixels(DocW, DocH, px));
            }
        }
        cel.Mask = null;
        _editMask = false;
        PushStructure("apply mask", before);
        Say(ActiveLayer.MaskEnabled ? "mask applied to the pixels" : "mask was disabled, so it was just removed");
    }

    /// <summary>Strokes preview with GDI+, which knows nothing about masks or blend modes; use the exact compositor instead.</summary>
    private bool NeedsExactComposite()
    {
        if (EditingMask) return true;
        foreach (var l in _doc.Layers)
        {
            if (l.Blend != BlendMode.Normal) return true;
            var cel = l.CelAt(_cur);
            if (cel == null) continue;
            if (cel.Mask != null && l.MaskEnabled) return true;
            foreach (var o in cel.Objects) if (o.Blend != BlendMode.Normal) return true;
        }
        return false;
    }

    #region test hooks

    internal void TestAddMask(int kind) => AddMask((MaskInit)kind);
    internal void TestDeleteMask() => DeleteMask();
    internal void TestApplyMask() => ApplyMask();
    internal void TestToggleMask() => ToggleMaskEnabled();
    internal void TestToggleEditMask() => ToggleEditMask();
    internal bool TestEditingMask => EditingMask;
    internal void TestClickMaskBox(int layer) => _layers.RaiseMaskClicked(layer);
    internal void TestScale(double factor, bool smooth) { _scaleBy.Value = (decimal)factor; _imageQuality.SelectedIndex = smooth ? (int)Resample.Smooth : (int)Resample.Nearest; _smooth.Checked = smooth; ScaleAll(); }
    internal void TestTrim() => Trim();
    internal void TestMergeDown() => MergeDown();
    internal void TestFlatten() => Flatten();
    internal void TestDuplicateLayer() => DuplicateLayer();
    internal void TestSetFrame(int f) { _cur = f; _selStart = _selEnd = f; RefreshAll(); }
    internal void TestSetMaskLinked(bool on) => ActiveLayer.MaskLinked = on;

    #endregion
}
