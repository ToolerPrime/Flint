namespace Flint;

/// <summary>
/// Fireworks 8 "Macromedia Standard" keyboard shortcuts (the default set) that
/// are not already a menu item's shortcut. Tool letters are in
/// <see cref="FireworksToolKey"/>; menu shortcuts are set where each menu is built.
/// </summary>
public sealed partial class MainForm
{
    private void BuildFireworksShortcuts()
    {
        void Key(Keys k, Action run) => _productivityShortcuts.Add(k, run); // Add throws on a clash, so clashes fail at startup

        Key(Keys.Control | Keys.Shift | Keys.Z, Redo);
        Key(Keys.Control | Keys.Shift | Keys.D, () => CloneSelectedObject(0, 0));   // Clone
        Key(Keys.Control | Keys.Alt | Keys.D, () => CloneSelectedObject(10, 10));   // Duplicate
        Key(Keys.Control | Keys.Alt | Keys.Shift | Keys.V, PasteAppearance);        // Paste attributes
        foreach (var (arrow, dx, dy) in new[] { (Keys.Left, -1, 0), (Keys.Right, 1, 0), (Keys.Up, 0, -1), (Keys.Down, 0, 1) })
        {
            // Alt+arrow: clone the selection and nudge the copy (Shift: 10 pixels).
            Key(Keys.Alt | arrow, () => CloneSelectedObject(dx, dy));
            Key(Keys.Control | Keys.Alt | arrow, () => CloneSelectedObject(dx, dy));
            Key(Keys.Alt | Keys.Shift | arrow, () => CloneSelectedObject(dx * 10, dy * 10));
            Key(Keys.Control | Keys.Alt | Keys.Shift | arrow, () => CloneSelectedObject(dx * 10, dy * 10));
        }

        // Modify > Align (Flint aligns the selected objects to the canvas).
        var align = new[] { CanvasAlign.Left, CanvasAlign.HorizontalCentre, CanvasAlign.Right, CanvasAlign.Top, CanvasAlign.VerticalCentre, CanvasAlign.Bottom };
        for (int i = 0; i < align.Length; i++)
        {
            var edge = align[i];
            Key(Keys.Control | Keys.Alt | (Keys.D1 + i), () => AlignSelectedObject(edge));
            Key(Keys.Control | Keys.Alt | (Keys.NumPad1 + i), () => AlignSelectedObject(edge));
        }

        // Modify > Arrange.
        Key(Keys.Control | Keys.Up, () => ArrangeObjects(1));
        Key(Keys.Control | Keys.Down, () => ArrangeObjects(-1));
        Key(Keys.Control | Keys.Shift | Keys.Up, () => ArrangeObjects(int.MaxValue));
        Key(Keys.Control | Keys.Shift | Keys.Down, () => ArrangeObjects(int.MinValue));

        // View > Magnification. Flint zooms in whole steps, so 50% shows 1:1.
        foreach (var (k, z) in new[] { (Keys.D2, 2), (Keys.D3, 3), (Keys.D4, 4), (Keys.D8, 8), (Keys.D6, 16), (Keys.D5, 1) })
        {
            Key(Keys.Control | k, () => SetZoom(z));
            Key(Keys.Control | (Keys.NumPad0 + (k - Keys.D0)), () => SetZoom(z));
        }
        Key(Keys.Control | Keys.NumPad1, () => SetZoom(1));
        Key(Keys.Control | Keys.NumPad0, () => { FitZoom(); RefreshAll(); });
        Key(Keys.Control | Keys.Alt | Keys.D0, () => { FitZoom(); RefreshAll(); });   // Fit selection
        Key(Keys.Control | Keys.Shift | Keys.Oemplus, () => Zoom(1));
        Key(Keys.Control | Keys.Add, () => Zoom(1));
        Key(Keys.Control | Keys.Subtract, () => Zoom(-1));

        // Frames and playback.
        Key(Keys.PageUp, () => StepFrame(-1));
        Key(Keys.Control | Keys.PageUp, () => StepFrame(-1));
        Key(Keys.PageDown, () => StepFrame(1));
        Key(Keys.Control | Keys.PageDown, () => StepFrame(1));
        Key(Keys.Control | Keys.Alt | Keys.P, TogglePlay);

        Key(Keys.Control | Keys.Alt | Keys.Shift | Keys.I, () => RunFilterDialog(Adjust.All.First(f => f.Name == "Invert")));
    }

    /// <summary>Fireworks tool letters. Pressing a letter again cycles through its tool group.</summary>
    private bool FireworksToolKey(Keys key)
    {
        Tool Cycle(params Tool[] group) { int i = Array.IndexOf(group, _tool); return group[(i + 1) % group.Length]; }
        Tool? next = key switch
        {
            Keys.V or Keys.D0 or Keys.NumPad0 => Cycle(Tool.ObjectSelect, Tool.Move),  // Pointer, then whole-layer Move
            Keys.A or Keys.D1 or Keys.NumPad1 => Tool.ObjectSelect,                   // Subselection (Alt-click picks a group member)
            Keys.Q => Tool.Transform,                                                  // Scale / skew / distort
            Keys.C => Tool.Crop,
            Keys.M => Cycle(Tool.Marquee, Tool.EllipseSelect),
            Keys.L => Tool.Lasso,
            Keys.W => Tool.Wand,
            Keys.B => Tool.Brush,
            Keys.Y => Tool.Pencil,
            Keys.E => Tool.Eraser,
            Keys.S => Tool.Clone,                                                      // Rubber Stamp
            Keys.N => Tool.Line,
            Keys.U => Cycle(Tool.Rectangle, Tool.Ellipse),
            Keys.T => Tool.Text,
            Keys.I => Tool.Eyedropper,
            Keys.G => Cycle(Tool.Fill, Tool.Gradient),
            Keys.H => Tool.Pan,
            _ => null,
        };
        if (next is { } t) { SetTool(t); return true; }
        if (key == Keys.D) { SetColours(Color.Black, Color.White); return true; }
        if (key == Keys.X) { SetColours(_bg, _fg); return true; }
        return false;
    }

    private void SetColours(Color fg, Color bg)
    {
        _fg = fg; _bg = bg;
        _fgSwatch.BackColor = fg; _bgSwatch.BackColor = bg;
    }

    private void SetZoom(int zoom) { _zoom = Math.Clamp(zoom, 1, 32); RefreshAll(); }

    /// <summary>Moves the selected objects up (+1) or down (-1) the stack; int.MaxValue/MinValue = to front/back.</summary>
    private void ArrangeObjects(int step)
    {
        if (!CanEditObject()) return;
        var cel = ActiveLayer.CelAt(_cur)!;
        var order = ArrangeOrder(cel.Objects.Count, SelectedObjectIndices.ToHashSet(), step);
        if (order.SequenceEqual(Enumerable.Range(0, order.Length))) { Say("already there"); return; }
        var picked = SelectedObjectIndices.ToHashSet();
        var before = Before();
        var old = cel.Objects.ToArray();
        for (int i = 0; i < order.Length; i++) cel.Objects[i] = old[order[i]];
        SelectObjects(Enumerable.Range(0, order.Length).Where(i => picked.Contains(order[i])));
        PushStructure("arrange objects", before);
    }

    /// <summary>New stacking order as old indices (index 0 is the bottom).</summary>
    internal static int[] ArrangeOrder(int count, ISet<int> picked, int step)
    {
        var order = Enumerable.Range(0, count).ToList();
        if (step == int.MaxValue) return order.Where(i => !picked.Contains(i)).Concat(order.Where(picked.Contains)).ToArray();
        if (step == int.MinValue) return order.Where(picked.Contains).Concat(order.Where(i => !picked.Contains(i))).ToArray();
        if (step > 0)
        {
            for (int i = count - 2; i >= 0; i--)
                if (picked.Contains(order[i]) && !picked.Contains(order[i + 1])) (order[i], order[i + 1]) = (order[i + 1], order[i]);
        }
        else
        {
            for (int i = 1; i < count; i++)
                if (picked.Contains(order[i]) && !picked.Contains(order[i - 1])) (order[i], order[i - 1]) = (order[i - 1], order[i]);
        }
        return order.ToArray();
    }

    internal bool TestToolKey(Keys key) => FireworksToolKey(key);
    internal Tool TestTool => _tool;
    internal int TestZoomLevel => _zoom;
    internal (Color fg, Color bg) TestColours => (_fg, _bg);
}
