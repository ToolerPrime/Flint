/* The Effects tab: the live effects of the selected object (the topmost object of the active layer on this frame).
 *
 * Every change builds a new RasterObject through RasterObject.WithEffectList and swaps it into the cel, so snapshots that share the
 * old object stay intact; each change is one structure undo step. A plain bitmap gets its current pixels as its Original the first
 * time an effect is added, which is what keeps effects live. Painting still merges objects into plain pixels (EnsurePaintable).
 */
namespace Flint;

internal sealed record FxNum(string Key, string Label, double Min, double Max, double Def, int Dec = 0);
internal sealed record FxCol(string Key, string Label, uint Def);
internal sealed record FxDef(string Name, EffectKind Kind, (string key, double value)[] Fixed, FxNum[] Nums, FxCol[] Cols)
{
    public ObjectEffect Make(uint? colour = null)
    {
        var e = new ObjectEffect(Kind);
        foreach (var (k, v) in Fixed) e.P[k] = v;
        foreach (var n in Nums) e.P[n.Key] = n.Def;
        foreach (var c in Cols) e.P[c.Key] = colour ?? c.Def;
        return e;
    }
}

public sealed partial class MainForm
{
    private CheckedListBox _fxList = null!;
    private bool _fxBusy;

    private static readonly FxDef[] FxDefs =
    {
        new("Drop shadow", EffectKind.DropShadow, Array.Empty<(string, double)>(),
            new[] { new FxNum("dist", "Distance", 0, 200, 6), new FxNum("angle", "Angle °", 0, 360, 315), new FxNum("blur", "Softness", 0, 100, 4) },
            new[] { new FxCol("colour", "Colour", 0xA6000000) }),
        new("Inner shadow", EffectKind.InnerShadow, Array.Empty<(string, double)>(),
            new[] { new FxNum("dist", "Distance", 0, 200, 4), new FxNum("angle", "Angle °", 0, 360, 315), new FxNum("blur", "Softness", 0, 100, 4) },
            new[] { new FxCol("colour", "Colour", 0xA6000000) }),
        new("Glow", EffectKind.Glow, Array.Empty<(string, double)>(),
            new[] { new FxNum("width", "Width", 0, 100, 6), new FxNum("soft", "Softness", 0, 100, 4), new FxNum("opacity", "Strength %", 0, 100, 100) },
            new[] { new FxCol("colour", "Colour", 0xFFFFE066) }),
        new("Inner bevel", EffectKind.Bevel, new[] { ("type", 0.0) },
            new[] { new FxNum("width", "Width", 1, 100, 6), new FxNum("contrast", "Contrast %", 0, 100, 75), new FxNum("light", "Light angle °", 0, 360, 135) },
            new[] { new FxCol("hilite", "Highlight", 0xFFFFFFFF), new FxCol("shadow", "Shadow", 0xFF000000) }),
        new("Gaussian blur", EffectKind.GaussianBlur, Array.Empty<(string, double)>(),
            new[] { new FxNum("radius", "Radius", 0, 100, 3, 1) }, Array.Empty<FxCol>()),
        new("Motion blur", EffectKind.MotionBlur, Array.Empty<(string, double)>(),
            new[] { new FxNum("angle", "Angle °", 0, 360, 0), new FxNum("dist", "Distance", 0, 200, 10) }, Array.Empty<FxCol>()),
        new("Hue/saturation", EffectKind.HueSat, Array.Empty<(string, double)>(),
            new[] { new FxNum("hue", "Hue", -180, 180, 0), new FxNum("sat", "Saturation", -100, 100, 0), new FxNum("light", "Lightness", -100, 100, 0), new FxNum("colorize", "Colorize (0/1)", 0, 1, 0) },
            Array.Empty<FxCol>()),
        new("Colour fill", EffectKind.ColorFill, Array.Empty<(string, double)>(),
            new[] { new FxNum("opacity", "Opacity %", 0, 100, 100) }, new[] { new FxCol("colour", "Colour", 0xFFFF6A00) }),
    };

    private static FxDef DefOf(ObjectEffect e) =>
        FxDefs.FirstOrDefault(d => d.Kind == e.Kind && (d.Kind != EffectKind.Bevel || e.Get("type") == 0))
        ?? new FxDef(e.Kind.ToString(), e.Kind, Array.Empty<(string, double)>(), Array.Empty<FxNum>(), Array.Empty<FxCol>());

    private TabPage BuildEffectsTab()
    {
        var page = new TabPage("Effects");
        _fxList = new CheckedListBox { Dock = DockStyle.Fill, IntegralHeight = false, CheckOnClick = true, BorderStyle = BorderStyle.None };
        _fxList.ItemCheck += (_, e) =>
        {
            if (_fxBusy) return;
            int i = e.Index;
            bool on = e.NewValue == CheckState.Checked;
            // the list is rebuilt by the refresh that the change triggers, so apply after this event finishes
            BeginInvoke(() => FxChange(on ? "enable effect" : "disable effect", l => { l[i] = l[i].With("off", on ? 0 : 1); return true; }));
        };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(2) };
        var menu = new ContextMenuStrip();
        foreach (var d in FxDefs) { var def = d; menu.Items.Add(def.Name, null, (_, _) => AddEffect(def)); }
        var add = new Button { Text = "Add ▸", Width = 56, Height = 26 };
        add.Click += (_, _) => menu.Show(add, new Point(0, add.Height));
        bar.Controls.Add(add);
        bar.Controls.Add(new Button { Text = "Edit…", Width = 56, Height = 26 }.With(b => b.Click += (_, _) => EditEffect(_fxList.SelectedIndex)));
        bar.Controls.Add(new Button { Text = "Remove", Width = 62, Height = 26 }.With(b => b.Click += (_, _) => RemoveEffect(_fxList.SelectedIndex)));
        bar.Controls.Add(new Button { Text = "▲", Width = 30, Height = 26 }.With(b => b.Click += (_, _) => MoveEffect(_fxList.SelectedIndex, -1)));
        bar.Controls.Add(new Button { Text = "▼", Width = 30, Height = 26 }.With(b => b.Click += (_, _) => MoveEffect(_fxList.SelectedIndex, 1)));
        _fxList.DoubleClick += (_, _) => EditEffect(_fxList.SelectedIndex);
        page.Controls.Add(_fxList);
        page.Controls.Add(bar);
        return page;
    }

    /// <summary>The object the panel works on: the topmost raster object of the active layer and frame (or what a transform in progress holds).</summary>
    private RasterObject? FxShown()
    {
        if (_xf != null && _xf.Targets.Count > 0 && _xf.Layer == _activeLayer) return _xf.Targets[^1].orig;
        if (SelectedObject is { } selected) return selected;
        var cel = ActiveLayer.CelAt(_cur);
        if (cel == null) return null;
        for (int i = cel.Objects.Count - 1; i >= 0; i--) if (cel.Objects[i] is RasterObject r) return r;
        return null;
    }

    private void RefreshEffects()
    {
        if (_fxList == null) return;
        _fxBusy = true;
        try
        {
            int sel = _fxList.SelectedIndex;
            _fxList.Items.Clear();
            var o = FxShown();
            if (o?.Effects != null)
                foreach (var e in o.Effects) _fxList.Items.Add(DefOf(e).Name, !e.Disabled);
            if (_fxList.Items.Count > 0) _fxList.SelectedIndex = Math.Clamp(sel, 0, _fxList.Items.Count - 1);
        }
        finally { _fxBusy = false; }
    }

    /// <summary>Change the target's effect list as one undo step. <paramref name="change"/> edits a copy and returns false to do nothing.</summary>
    private bool FxChange(string label, Func<List<ObjectEffect>, bool> change)
    {
        FinishFloat();
        if (ActiveLayer.Locked) { Say($"layer '{ActiveLayer.Name}' is locked"); return false; }
        var cel = ActiveLayer.CelAt(_cur);
        int idx = cel == null ? -1 : SelectedObject != null ? _objectIndex : cel.Objects.FindLastIndex(o => o is RasterObject);
        if (cel == null || idx < 0) { Say("nothing to add effects to: this layer has no object on this frame"); return false; }
        var obj = (RasterObject)cel.Objects[idx];
        var list = new List<ObjectEffect>(obj.Effects ?? new List<ObjectEffect>());
        if (!change(list)) return false;
        var before = Before();
        cel.Objects[idx] = obj.WithEffectList(list);
        PushStructure(label, before);
        return true;
    }

    private void AddEffect(FxDef d)
    {
        if (FxChange("add " + d.Name.ToLowerInvariant(), l => { l.Add(d.Make(d.Kind == EffectKind.ColorFill ? Surface.Pack(_fg) : null)); return true; }))
        {
            if (_fxList.Items.Count > 0) _fxList.SelectedIndex = _fxList.Items.Count - 1;
            Say($"{d.Name} added. Edit… changes its settings; the box turns it off.");
        }
    }

    private void RemoveEffect(int i)
    {
        if (i < 0) { Say("select an effect first"); return; }
        FxChange("remove effect", l => { if (i >= l.Count) return false; l.RemoveAt(i); return true; });
    }

    private void MoveEffect(int i, int dir)
    {
        if (i < 0) { Say("select an effect first"); return; }
        int j = i + dir;
        if (FxChange("move effect", l => { if (i >= l.Count || j < 0 || j >= l.Count) return false; (l[i], l[j]) = (l[j], l[i]); return true; })) _fxList.SelectedIndex = j;
    }

    /// <summary>The parameter dialog with a live preview on the canvas; Cancel puts the object back.</summary>
    private void EditEffect(int i)
    {
        if (i < 0) { Say("select an effect first"); return; }
        FinishFloat();
        if (ActiveLayer.Locked) { Say($"layer '{ActiveLayer.Name}' is locked"); return; }
        var cel = ActiveLayer.CelAt(_cur);
        int idx = cel == null ? -1 : SelectedObject != null ? _objectIndex : cel.Objects.FindLastIndex(o => o is RasterObject);
        if (cel == null || idx < 0 || ((RasterObject)cel.Objects[idx]).Effects is not { } fx || i >= fx.Count) return;
        var original = (RasterObject)cel.Objects[idx];
        var before = Before();
        using var dlg = new EffectDialog(DefOf(fx[i]), fx[i]);
        dlg.Preview += ne =>
        {
            var l = new List<ObjectEffect>(fx) { [i] = ne };
            cel.Objects[idx] = original.WithEffectList(l);
            InvalidateComposite();
        };
        bool ok = dlg.ShowDialog(this) == DialogResult.OK;
        cel.Objects[idx] = original;
        if (!ok) { InvalidateComposite(); return; }
        cel.Objects[idx] = original.WithEffectList(new List<ObjectEffect>(fx) { [i] = dlg.Result });
        PushStructure("edit effect", before);
    }

    #region test hooks

    internal void TestFxAdd(string name) => AddEffect(FxDefs.First(d => d.Name == name));
    internal void TestFxSet(int i, string key, double value) => FxChange("edit effect", l => { l[i] = l[i].With(key, value); return true; });
    internal void TestFxRemove(int i) => RemoveEffect(i);
    internal void TestFxMove(int i, int dir) => MoveEffect(i, dir);
    internal void TestFxToggle(int i) => FxChange("toggle effect", l => { l[i] = l[i].With("off", l[i].Disabled ? 0 : 1); return true; });
    internal void TestFxSelect(int i) => _fxList.SelectedIndex = i;
    internal List<(string name, bool on)> TestFxRows() => _fxList.Items.Cast<string>().Select((n, i) => (n, _fxList.GetItemChecked(i))).ToList();
    internal RasterObject? TestFxObject => FxShown();

    #endregion
}

internal static class ControlExtensions
{
    public static T With<T>(this T c, Action<T> a) { a(c); return c; }
}

/// <summary>Settings of one effect, with a preview event (debounced) while they change.</summary>
public sealed class EffectDialog : ThemedForm
{
    private readonly FxDef _def;
    private readonly ObjectEffect _base;
    private readonly Dictionary<string, NumericUpDown> _nums = new();
    private readonly Dictionary<string, (Button swatch, NumericUpDown alpha)> _cols = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 150 };

    internal event Action<ObjectEffect>? Preview;

    internal EffectDialog(FxDef def, ObjectEffect current)
    {
        _def = def; _base = current;
        Text = def.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        int y = 14;
        foreach (var p in def.Nums)
        {
            Controls.Add(new Label { Text = p.Label, Left = 14, Top = y + 3, Width = 120 });
            var n = new NumericUpDown
            {
                Left = 140, Top = y, Width = 90, Minimum = (decimal)p.Min, Maximum = (decimal)p.Max, DecimalPlaces = p.Dec,
                Increment = p.Dec > 0 ? 0.1m : 1, Value = (decimal)Math.Clamp(current.Get(p.Key, p.Def), p.Min, p.Max), TextAlign = HorizontalAlignment.Right,
            };
            n.ValueChanged += (_, _) => Kick();
            _nums[p.Key] = n;
            Controls.Add(n);
            y += 30;
        }
        foreach (var c in def.Cols)
        {
            uint v = current.Colour(c.Key, c.Def);
            Controls.Add(new Label { Text = c.Label, Left = 14, Top = y + 3, Width = 60 });
            var sw = new Button { Left = 76, Top = y, Width = 40, Height = 24, BackColor = Color.FromArgb(255, (int)(v >> 16) & 255, (int)(v >> 8) & 255, (int)v & 255), FlatStyle = FlatStyle.Flat };
            var al = new NumericUpDown { Left = 140, Top = y, Width = 90, Minimum = 0, Maximum = 100, Value = (int)Math.Round((v >> 24) * 100 / 255.0), TextAlign = HorizontalAlignment.Right };
            sw.Click += (_, _) =>
            {
                if (ColourPickerDialog.Pick(this, sw.BackColor, "Effect colour") is { } c) { sw.BackColor = c; Kick(); }
            };
            al.ValueChanged += (_, _) => Kick();
            _cols[c.Key] = (sw, al);
            Controls.Add(sw); Controls.Add(al);
            Controls.Add(new Label { Text = "% opaque", Left = 236, Top = y + 3, Width = 60 });
            y += 30;
        }
        y += 8;
        var ok = new Button { Text = "OK", Left = 86, Top = y, Width = 76, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 168, Top = y, Width = 76, DialogResult = DialogResult.Cancel };
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
        ClientSize = new Size(310, y + 40);
        _debounce.Tick += (_, _) => { _debounce.Stop(); Preview?.Invoke(Result); };
        Shown += (_, _) => Kick();
        FormClosed += (_, _) => _debounce.Dispose();
    }

    private void Kick() { _debounce.Stop(); _debounce.Start(); }

    internal ObjectEffect Result
    {
        get
        {
            var e = new ObjectEffect(_base.Kind);
            foreach (var (k, v) in _base.P) e.P[k] = v;
            foreach (var (k, n) in _nums) e.P[k] = (double)n.Value;
            foreach (var (k, (sw, al)) in _cols)
                e.P[k] = (uint)(((int)Math.Round((double)al.Value * 255 / 100) << 24) | (sw.BackColor.R << 16) | (sw.BackColor.G << 8) | sw.BackColor.B);
            return e;
        }
    }
}
