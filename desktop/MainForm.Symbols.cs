/* Symbols in the editor: Convert to Symbol (F8), place from the library,
 * edit a symbol on its own (Done to go back), instance options, Break
 * Apart (Ctrl+B) and Tween Instances. See Symbols.cs for the model. */
namespace Flint;

public sealed partial class MainForm
{
    private sealed record SymbolEditState(Document RootDoc, UndoStack RootUndo, string Id, int Cur, int Layer, int SelStart, int SelEnd, bool Dirty);
    private SymbolEditState? _symbolEdit;
    private ToolStrip? _symbolBar;
    private ToolStripLabel? _symbolCrumb;

    /// <summary>The project document, also while a symbol is being edited.</summary>
    private Document RootDocument => _symbolEdit?.RootDoc ?? _doc;
    internal bool EditingSymbol => _symbolEdit != null;

    private void AddSymbolMenus(ToolStripMenuItem edit)
    {
        var m = new ToolStripMenuItem("S&ymbol");
        m.DropDownItems.Add(new ToolStripMenuItem("&Convert to symbol…", null, (_, _) => ConvertToSymbol()) { ShortcutKeys = Keys.F8 });
        m.DropDownItems.Add("&Edit symbol", null, (_, _) => EditSelectedSymbol());
        m.DropDownItems.Add("&Instance options…", null, (_, _) => RunInstanceOptions());
        m.DropDownItems.Add(new ToolStripMenuItem("&Break apart", null, (_, _) => BreakApart()) { ShortcutKeys = Keys.Control | Keys.B });
        m.DropDownItems.Add(new ToolStripMenuItem("&Tween instances…", null, (_, _) => RunTweenInstances()) { ShortcutKeys = Keys.Control | Keys.Alt | Keys.Shift | Keys.T });
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(m);
    }

    private RasterObject? SelectedInstance =>
        SelectedObjectIndices.Length == 1 && ActiveLayer.CelAt(_cur) is { } c && c.Objects[SelectedObjectIndices[0]] is RasterObject { Symbol: not null } r ? r : null;

    // ---------- convert ----------

    internal bool ConvertToSymbol(string? name = null)
    {
        if (EditingSymbol) { Say("Finish editing this symbol first (Done): symbols inside symbols are not supported yet."); return false; }
        if (!CanEditObject()) return false;
        var idx = SelectedObjectIndices.OrderBy(i => i).ToArray();
        var cel = ActiveLayer.CelAt(_cur);
        if (cel == null || idx.Length == 0) { Say("Select one or more objects to convert to a symbol."); return false; }
        var objs = idx.Select(i => (RasterObject)cel.Objects[i]).ToArray();
        if (objs.Any(o => o.Symbol != null)) { Say("A symbol cannot hold another symbol yet. Break the instance apart first."); return false; }
        if (objs.Any(o => o.Blend != BlendMode.Normal)) { Say("Convert objects with Normal blending, so they keep their look."); return false; }
        var bounds = objs.Select(o => new Rectangle(o.X, o.Y, o.Width, o.Height)).Aggregate(Rectangle.Union);
        try { ResizePlan.Validate(bounds.Width, bounds.Height); }
        catch (Exception ex) { Say(ex.Message); return false; }
        var content = new Document(bounds.Width, bounds.Height);
        var into = content.Layers[0].EnsureCel(0);
        foreach (var o in objs) into.Objects.Add(o.Retransform(Affine.Translate(-bounds.X, -bounds.Y), o.Mode));
        name ??= AskSymbolName($"Symbol {RootDocument.Symbols.Count + 1}");
        if (name == null) return false;
        var master = new SymbolMaster(Guid.NewGuid().ToString("N"), name, content);
        var before = Before();
        foreach (int i in idx.Reverse()) cel.Objects.RemoveAt(i);
        _doc.Symbols = _doc.Symbols.Append(master).ToArray();
        cel.Objects.Insert(idx[0], SymbolOps.Create(master, Affine.Translate(bounds.X, bounds.Y), name));
        SelectObjects(new[] { idx[0] });
        PushStructure("convert to symbol", before);
        Say($"'{name}' is a symbol now; it is in the library. Double-click it with Object (Q) to edit it.");
        return true;
    }

    private string? AskSymbolName(string def)
    {
        if (SuppressPrompts || Program.Headless) return def;
        using var dlg = new RenameDialog(def) { Text = "Symbol name" };
        return dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.Value) ? dlg.Value : null;
    }

    // ---------- place ----------

    internal bool PlaceSymbol(string id, Point? imageCentre = null)
    {
        if (EditingSymbol) { Say("Finish editing this symbol first (Done)."); return false; }
        if (SymbolOps.Find(_doc, id) is not { } m) return false;
        if (ActiveLayer.Locked) { Say("The layer is locked."); return false; }
        var c = imageCentre ?? VisibleCentre();
        var before = Before();
        var cel = ActiveLayer.EnsureCel(_cur);
        cel.Objects.Add(SymbolOps.Create(m, Affine.Translate(c.X - m.Content.Width / 2, c.Y - m.Content.Height / 2), m.Name));
        SelectObjects(new[] { cel.Objects.Count - 1 });
        PushStructure("place symbol", before);
        Say($"placed '{m.Name}'. To let it play over frames, use Frame > Copy to frames.");
        return true;
    }

    // ---------- edit a symbol on its own ----------

    private void EditSelectedSymbol()
    {
        if (SelectedInstance is { Symbol: { } s }) EnterSymbolEdit(s.Id);
        else Say("Select a symbol instance with Object (Q), or double-click it in the library.");
    }

    internal bool EnterSymbolEdit(string id)
    {
        if (EditingSymbol) { Say("Already editing a symbol."); return false; }
        if (SymbolOps.Find(_doc, id) is not { } m) return false;
        FinishFloat();
        StopPlay();
        if (_xf != null) CommitTransform();
        _symbolEdit = new SymbolEditState(_doc, _undo, id, _cur, _activeLayer, _selStart, _selEnd, _dirty);
        var root = _doc;
        _doc = m.Content.Clone();                     // the stored master stays untouched (undo snapshots share it)
        _doc.Library = root.Library;
        SetUndoStack(new UndoStack());
        _cur = _selStart = _selEnd = 0;
        _activeLayer = _doc.Layers.Count - 1;
        _selection = null;
        ShowSymbolBar(m.Name);
        FitZoom();
        InvalidateComposite();
        RefreshAll();
        UpdateTitle();
        Say($"editing symbol '{m.Name}'. Press Done to go back.");
        return true;
    }

    /// <summary>Go back to the document; any change becomes one undo step there and every instance updates.</summary>
    internal void LeaveSymbolEdit()
    {
        if (_symbolEdit is not { } st) return;
        FinishFloat();
        StopPlay();
        if (_xf != null) CommitTransform();
        var edited = _doc;
        bool changed = _undo.DoneCount > 0;
        edited.Library = new();
        _doc = st.RootDoc;
        SetUndoStack(st.RootUndo);
        _symbolEdit = null;
        _cur = Math.Clamp(st.Cur, 0, _doc.FrameCount - 1); _selStart = st.SelStart; _selEnd = st.SelEnd;
        _activeLayer = Math.Clamp(st.Layer, 0, _doc.Layers.Count - 1);
        _selection = null;
        HideSymbolBar();
        if (changed && SymbolOps.Find(_doc, st.Id) is { } old)
        {
            var before = Before();
            _doc.Symbols = _doc.Symbols.Select(s => s.Id == st.Id ? s with { Content = edited } : s).ToArray();
            int n = SymbolOps.Refresh(_doc, st.Id);
            PushStructure($"edit symbol '{old.Name}'", before);
            Say($"symbol '{old.Name}' updated ({n} instance{(n == 1 ? "" : "s")})");
        }
        else Say("back to the document");
        FitZoom();
        InvalidateComposite();
        RefreshAll();
        UpdateTitle();
    }

    private void SetUndoStack(UndoStack u)
    {
        _undo.Pushed -= OnUndoPushed;
        _undo = u;
        _undo.Pushed += OnUndoPushed;
        RefreshHistory();
    }

    private void OnUndoPushed() { if (!_historyBusy) RefreshHistory(); }

    private void ShowSymbolBar(string name)
    {
        if (_symbolBar == null)
        {
            _symbolBar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Visible = false };
            _symbolBar.Items.Add(new ToolStripButton("‹ Document", null, (_, _) => LeaveSymbolEdit()) { ToolTipText = "Go back to the document" });
            _symbolCrumb = new ToolStripLabel();
            _symbolBar.Items.Add(_symbolCrumb);
            _symbolBar.Items.Add(new ToolStripButton("Done", null, (_, _) => LeaveSymbolEdit()) { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Finish editing the symbol" });
            _canvas.Parent?.Controls.Add(_symbolBar);
        }
        _symbolCrumb!.Text = $"›  Symbol: {name}   (every copy updates when you press Done)";
        _symbolBar.Visible = true;
    }

    private void HideSymbolBar() { if (_symbolBar != null) _symbolBar.Visible = false; }

    // ---------- instance options ----------

    /// <summary>Set play mode and first frame on the selected instance and its run across the following frames.</summary>
    internal bool SetInstanceOptions(SymbolPlay play, int first)
    {
        if (SelectedInstance is not { Symbol: { } s } || ActiveLayer.Locked) { Say("Select one symbol instance on an unlocked layer."); return false; }
        if (SymbolOps.Find(_doc, s.Id) is not { } m) return false;
        int idx = SelectedObjectIndices[0];
        var run = InstanceRun(ActiveLayer, _cur, idx, s).ToList();
        var before = Before();
        foreach (var (f, i) in run)
        {
            var cel = ActiveLayer.CelAt(f)!;                 // a shared layer keeps its cel under frame 0
            var o = (RasterObject)cel.Objects[i];
            cel.Objects[i] = SymbolOps.Make(m, o.Symbol! with { Play = play, First = first }, o);
        }
        PushStructure("instance options", before);
        Say($"{(play == SymbolPlay.Loop ? "loops" : play == SymbolPlay.Once ? "plays once" : "shows one frame")} from frame {first + 1} on {run.Count} frame{(run.Count == 1 ? "" : "s")}");
        return true;
    }

    /// <summary>This instance and the matching copies right after it (Copy to frames / tweens), frame by frame.</summary>
    private static IEnumerable<(int frame, int index)> InstanceRun(Layer layer, int frame, int index, SymbolRef s)
    {
        yield return (frame, index);
        if (layer.Shared) yield break;
        // Copies can sit at any position in later cels (Copy to frames adds on top); the one with the next offset continues the run.
        for (int g = frame + 1; layer.Cels.TryGetValue(g, out var c); g++)
        {
            var want = s with { Offset = s.Offset + (g - frame) };
            int at = c.Objects.FindIndex(o => o is RasterObject { Symbol: { } t } && t == want);
            if (at < 0) yield break;
            yield return (g, at);
        }
    }

    private void RunInstanceOptions()
    {
        if (SelectedInstance is not { Symbol: { } s } || SymbolOps.Find(_doc, s.Id) is not { } m) { Say("Select a symbol instance first."); return; }
        using var f = new ThemedForm
        {
            Text = "Instance options", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(300, 170),
        };
        var loop = new RadioButton { Text = "Loop", Left = 16, Top = 14, AutoSize = true, Checked = s.Play == SymbolPlay.Loop };
        var once = new RadioButton { Text = "Play once", Left = 16, Top = 40, AutoSize = true, Checked = s.Play == SymbolPlay.Once };
        var single = new RadioButton { Text = "Single frame", Left = 16, Top = 66, AutoSize = true, Checked = s.Play == SymbolPlay.Single };
        var first = new NumericUpDown { Left = 170, Top = 96, Width = 70, Minimum = 1, Maximum = Math.Max(1, m.Content.FrameCount), Value = Math.Clamp(s.First + 1, 1, Math.Max(1, m.Content.FrameCount)), AccessibleName = "First frame" };
        var ok = new Button { Text = "OK", Left = 110, Top = 130, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 200, Top = 130, Width = 80, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { loop, once, single, new Label { Text = $"First frame (of {m.Content.FrameCount})", Left = 16, Top = 99, AutoSize = true }, first, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        SetInstanceOptions(loop.Checked ? SymbolPlay.Loop : once.Checked ? SymbolPlay.Once : SymbolPlay.Single, (int)first.Value - 1);
    }

    // ---------- break apart ----------

    /// <summary>The instance becomes the objects of the symbol frame it shows (or one flat picture when that keeps its look exactly).</summary>
    internal bool BreakApart()
    {
        if (SelectedInstance is not { Symbol: { } s } inst || ActiveLayer.Locked) { Say("Select one symbol instance on an unlocked layer to break it apart."); return false; }
        var m = SymbolOps.Find(_doc, s.Id);
        int idx = SelectedObjectIndices[0];
        var parts = new List<DocObject>();
        // Parts only when that composites exactly like the instance: a whole-pixel placement, full opacity,
        // plain layers and objects, and no two parts overlapping (overlaps of soft pixels round differently).
        bool exact = m != null && inst.Visible && inst.Opacity == 255 && inst.Blend == BlendMode.Normal && inst.Effects is not { Count: > 0 } &&
                     inst.Matrix.IsIntegerTranslation;
        if (exact)
        {
            var c = m!.Content;
            int frame = s.FrameIn(c.FrameCount);
            foreach (var l in c.Layers)
            {
                if (!l.Visible || l.CelAt(frame) is not { } cel) continue;
                if (l.Blend != BlendMode.Normal || l.Opacity != 255 || (l.MaskEnabled && cel.Mask != null) ||
                    cel.Objects.Any(o => o is not RasterObject || o.Blend != BlendMode.Normal)) { exact = false; break; }
                foreach (var o in cel.Objects.Cast<RasterObject>())
                    if (o.Visible) parts.Add(o.Retransform(inst.Matrix, inst.Mode));
            }
            var boxes = parts.Select(p => new Rectangle(p.X, p.Y, p.Width, p.Height)).ToList();
            for (int i = 0; exact && i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                    if (boxes[i].IntersectsWith(boxes[j])) { exact = false; break; }
        }
        if (!exact || parts.Count == 0)
        {
            parts.Clear();
            parts.Add(((RasterObject)inst.CloneObject()).WithSymbol(null));   // own pixels: the frame bitmap is a shared cache
        }
        var before = Before();
        var target = ActiveLayer.CelAt(_cur)!;
        target.Objects.RemoveAt(idx);
        target.Objects.InsertRange(idx, parts);
        SelectObjects(Enumerable.Range(idx, parts.Count));
        PushStructure("break apart", before);
        Say(parts.Count == 1 ? "the instance is a plain picture now" : $"broken apart into {parts.Count} objects");
        return true;
    }

    // ---------- tween instances ----------

    /// <summary>The tween that turns instance <paramref name="a"/> into <paramref name="b"/> (move, scale, rotate, fade), or null with a reason.</summary>
    internal static TweenSpec? SpecBetween(RasterObject a, RasterObject b, int frames, out string why)
    {
        why = "";
        Affine inv;
        try { inv = a.Matrix.Inverse(); } catch (InvalidOperationException) { why = "The first instance is squashed flat and cannot be measured."; return null; }
        var r = inv.Then(b.Matrix);
        double det = r.A * r.D - r.B * r.C, sc = Math.Sqrt(Math.Abs(det));
        if (det <= 0 || Math.Abs(r.A - r.D) > 1e-3 * sc || Math.Abs(r.B + r.C) > 1e-3 * sc)
        { why = "The two instances differ by a skew or a flip; Tween instances handles move, scale, rotation and opacity."; return null; }
        double cx = a.X + a.Width / 2.0, cy = a.Y + a.Height / 2.0;
        // Where a's centre lands under b's placement (TweenStep turns and scales about that centre).
        var lc = inv.Apply(cx, cy);
        var mapped = b.Matrix.Apply(lc.x, lc.y);
        var spec = new TweenSpec
        {
            NewFrames = frames, Dx = mapped.x - cx, Dy = mapped.y - cy,
            ScaleTo = 100 * sc, Rotate = Math.Atan2(r.B, r.A) * 180 / Math.PI,
        };
        if (a.Opacity != b.Opacity) { spec.OpacityFrom = a.Opacity * 100 / 255.0; spec.OpacityTo = b.Opacity * 100 / 255.0; }
        if (!spec.Changes) { why = "The two instances are the same; move, turn, scale or fade the second one."; return null; }
        return spec;
    }

    /// <summary>Two selected instances of one symbol: the lower is the start, the upper the end, with <paramref name="frames"/> new frames between.</summary>
    internal bool TweenInstances(int frames)
    {
        if (EditingSymbol) { Say("Finish editing this symbol first (Done)."); return false; }
        var idx = SelectedObjectIndices.OrderBy(i => i).ToArray();
        var cel = ActiveLayer.CelAt(_cur);
        if (idx.Length != 2 || cel == null || cel.Objects[idx[0]] is not RasterObject { Symbol: { } sa } a || cel.Objects[idx[1]] is not RasterObject { Symbol: { } sb } b)
        { Say("Select exactly two instances of the same symbol."); return false; }
        if (sa.Id != sb.Id) { Say("The two instances must be of the same symbol."); return false; }
        if (ActiveLayer.Shared || ActiveLayer.Locked) { Say("Tween instances needs an unlocked layer with separate frames."); return false; }
        if (SpecBetween(a, b, frames, out string why) is not { } spec) { Say(why); return false; }
        var before = Before();
        cel.Objects.RemoveAt(idx[1]);
        SelectObjects(new[] { idx[0] });
        if (CreateTween(spec, "tween instances", before)) return true;
        cel.Objects.Insert(idx[1], b);
        SelectObjects(idx);
        return false;
    }

    private void RunTweenInstances()
    {
        using var f = new ThemedForm
        {
            Text = "Tween instances", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(300, 100),
        };
        var n = new NumericUpDown { Left = 170, Top = 14, Width = 70, Minimum = 1, Maximum = MaxTweenFrames, Value = 6, AccessibleName = "New frames" };
        var ok = new Button { Text = "Tween", Left = 110, Top = 56, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 200, Top = 56, Width = 80, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { new Label { Text = "New frames between them", Left = 16, Top = 17, AutoSize = true }, n, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(this) == DialogResult.OK) TweenInstances((int)n.Value);
    }

    // ---------- library ----------

    internal void RenameSymbol(string id, string name)
    {
        if (EditingSymbol || string.IsNullOrWhiteSpace(name) || SymbolOps.Find(_doc, id) is not { } m || m.Name == name) return;
        var before = Before();
        _doc.Symbols = _doc.Symbols.Select(s => s.Id == id ? s with { Name = name.Trim() } : s).ToArray();
        PushStructure("rename symbol", before);
    }

    internal bool DeleteSymbol(string id)
    {
        if (EditingSymbol || SymbolOps.Find(_doc, id) is not { } m) return false;
        int n = SymbolOps.CountInstances(_doc, id);
        if (n > 0) { Say($"'{m.Name}' is used {n} time{(n == 1 ? "" : "s")}; break those instances apart first."); return false; }
        var before = Before();
        _doc.Symbols = _doc.Symbols.Where(s => s.Id != id).ToArray();
        PushStructure("delete symbol", before);
        return true;
    }
}
