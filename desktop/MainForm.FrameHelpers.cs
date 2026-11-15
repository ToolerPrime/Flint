/* Frame helpers, after Fireworks' Frames panel and Modify > Animation:
 * Animate Selection, Distribute to Frames, Copy to Frames, Reverse Frames
 * (all or a range), Add Frames, and per-frame "Include when exporting". */
namespace Flint;

public sealed partial class MainForm
{
    // ---------- Animate Selection ----------

    private void RunAnimateSelection()
    {
        FinishFloat();
        StopPlay();
        if (!CanTweenObject()) return;
        using var dlg = new AnimateDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK) AnimateSelection(dlg.Spec);
    }

    /// <summary>Fireworks' Animate: the selected object over <c>TotalFrames</c> frames (this one plus new ones after it).</summary>
    internal sealed class AnimateSpec
    {
        public int TotalFrames = 5;
        public double Distance = 72, AngleDeg;       // 0 = right, 90 = up
        public double ScaleTo = 100, OpacityFrom = 100, OpacityTo = 100, Rotate;   // Rotate: degrees, + clockwise
    }

    internal bool AnimateSelection(AnimateSpec a)
    {
        if (a.TotalFrames < 2) { Say("Animate needs at least 2 frames."); return false; }
        double rad = a.AngleDeg * Math.PI / 180;
        return CreateTween(new TweenSpec
        {
            NewFrames = a.TotalFrames - 1,
            Dx = a.Distance * Math.Cos(rad), Dy = -a.Distance * Math.Sin(rad),
            ScaleFrom = 100, ScaleTo = a.ScaleTo, Rotate = a.Rotate,
            // 100% to 100% means "leave opacity alone", so an object already part-clear keeps its own.
            OpacityFrom = a.OpacityFrom == 100 && a.OpacityTo == 100 ? null : a.OpacityFrom,
            OpacityTo = a.OpacityFrom == 100 && a.OpacityTo == 100 ? null : a.OpacityTo,
        }, "animate selection");
    }

    private sealed class AnimateDialog : ThemedForm
    {
        private readonly NumericUpDown _frames, _dist, _angle, _scale, _opFrom, _opTo, _rot;
        private readonly RadioButton _cw = new() { Text = "Clockwise", AutoSize = true, Checked = true };
        private readonly RadioButton _ccw = new() { Text = "Counter-clockwise", AutoSize = true };

        public AnimateSpec Spec => new()
        {
            TotalFrames = (int)_frames.Value, Distance = (double)_dist.Value, AngleDeg = (double)_angle.Value,
            ScaleTo = (double)_scale.Value, OpacityFrom = (double)_opFrom.Value, OpacityTo = (double)_opTo.Value,
            Rotate = (double)_rot.Value * (_cw.Checked ? 1 : -1),
        };

        public AnimateDialog()
        {
            Text = "Animate selection";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = ShowInTaskbar = false;
            int y = 14;
            NumericUpDown N(string label, decimal min, decimal max, decimal value, string hint)
            {
                Controls.Add(new Label { Text = label, Left = 16, Top = y + 3, Width = 170 });
                var n = new NumericUpDown { Left = 190, Top = y, Width = 100, Minimum = min, Maximum = max, Value = value, AccessibleName = label.Replace("&", ""), TextAlign = HorizontalAlignment.Right };
                Controls.Add(n);
                Controls.Add(new Label { Text = hint, Left = 296, Top = y + 3, AutoSize = true, ForeColor = Theme.Muted });
                y += 32;
                return n;
            }
            _frames = N("&Frames (total)", 2, MaxTweenFrames + 1, 5, "this one + new");
            _dist = N("&Move (px)", 0, MaxTweenOffset, 72, "");
            _angle = N("&Direction (degrees)", -360, 360, 0, "0 right, 90 up");
            _scale = N("&Scale to (%)", 1, 1000, 100, "");
            _opFrom = N("&Opacity from (%)", 0, 100, 100, "");
            _opTo = N("Opacity &to (%)", 0, 100, 100, "");
            _rot = N("&Rotate (degrees)", 0, 3600, 0, "");
            _cw.Left = 190; _cw.Top = y; _ccw.Left = 290; _ccw.Top = y;
            Controls.Add(_cw); Controls.Add(_ccw);
            y += 36;
            var ok = new Button { Text = "Animate", Left = 200, Top = y, Width = 90, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 300, Top = y, Width = 90, DialogResult = DialogResult.Cancel };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            ClientSize = new Size(410, y + 44);
        }
    }

    // ---------- Distribute to Frames, Copy to Frames ----------

    private (Cel cel, int[] idx)? SelectedObjectsHere(string what)
    {
        FinishFloat();
        var idx = SelectedObjectIndices.OrderBy(i => i).ToArray();
        var cel = ActiveLayer.CelAt(_cur);
        if (idx.Length == 0 || cel == null) { Say($"Select objects first to {what}."); return null; }
        if (ActiveLayer.Shared) { Say($"{what}: this layer is shared by every frame; choose a layer with separate frames."); return null; }
        if (ActiveLayer.Locked) { Say("The layer is locked."); return null; }
        return (cel, idx);
    }

    /// <summary>Each selected object to its own frame: the bottom one stays, the next goes to the next frame, and so on (frames are added as needed).</summary>
    internal bool DistributeToFrames()
    {
        if (SelectedObjectsHere("distribute to frames") is not { } sel) return false;
        if (sel.idx.Length < 2) { Say("Select two or more objects to distribute to frames."); return false; }
        var before = Before();
        var layer = ActiveLayer;
        var moving = sel.idx.Skip(1).Select(i => sel.cel.Objects[i]).ToList();
        foreach (int i in sel.idx.Skip(1).Reverse()) sel.cel.Objects.RemoveAt(i);
        for (int k = 0; k < moving.Count; k++)
        {
            int frame = _cur + 1 + k;
            if (frame >= _doc.FrameCount) _doc.InsertFrame(_doc.FrameCount, new FrameSpec { Delay = _doc.Frames[_cur].Delay });
            layer.EnsureCel(frame).Objects.Add(moving[k]);
        }
        SelectObjects(new[] { sel.idx[0] });
        PushStructure("distribute to frames", before);
        Say($"distributed {sel.idx.Length} objects to frames {_cur + 1}–{_cur + sel.idx.Length}");
        return true;
    }

    internal enum CopyTarget { All, Previous, Next, Range }

    /// <summary>Copies of the selected objects on top of the same layer in other frames.</summary>
    internal bool CopyToFrames(CopyTarget target, int from = 0, int to = 0)
    {
        if (SelectedObjectsHere("copy to frames") is not { } sel) return false;
        var frames = target switch
        {
            CopyTarget.All => Enumerable.Range(0, _doc.FrameCount),
            CopyTarget.Previous => new[] { _cur - 1 },
            CopyTarget.Next => new[] { _cur + 1 },
            _ => Enumerable.Range(Math.Min(from, to), Math.Abs(to - from) + 1),
        };
        var list = frames.Where(f => f >= 0 && f < _doc.FrameCount && f != _cur).ToList();
        if (list.Count == 0) { Say("There is no such frame to copy to."); return false; }
        var before = Before();
        foreach (int f in list)
        {
            var cel = ActiveLayer.EnsureCel(f);
            // A symbol instance plays on: its copy shows the frame that many frames later.
            foreach (int i in sel.idx)
                cel.Objects.Add(sel.cel.Objects[i] is RasterObject { Symbol: not null } r ? SymbolOps.Advance(_doc, r, f - _cur) : sel.cel.Objects[i].CloneObject());
        }
        PushStructure("copy to frames", before);
        Say($"copied {sel.idx.Length} object{(sel.idx.Length == 1 ? "" : "s")} to {list.Count} frame{(list.Count == 1 ? "" : "s")}");
        return true;
    }

    private void RunCopyToFrames()
    {
        if (SelectedObjectsHere("copy to frames") == null) return;
        using var f = new ThemedForm
        {
            Text = "Copy to frames", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(300, 190),
        };
        var all = new RadioButton { Text = "All frames", Left = 16, Top = 14, AutoSize = true, Checked = true };
        var prev = new RadioButton { Text = "Previous frame", Left = 16, Top = 40, AutoSize = true };
        var next = new RadioButton { Text = "Next frame", Left = 16, Top = 66, AutoSize = true };
        var range = new RadioButton { Text = "Range", Left = 16, Top = 92, AutoSize = true };
        var a = new NumericUpDown { Left = 90, Top = 90, Width = 60, Minimum = 1, Maximum = _doc.FrameCount, Value = 1, AccessibleName = "First frame" };
        var b = new NumericUpDown { Left = 170, Top = 90, Width = 60, Minimum = 1, Maximum = _doc.FrameCount, Value = _doc.FrameCount, AccessibleName = "Last frame" };
        var ok = new Button { Text = "Copy", Left = 110, Top = 140, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 200, Top = 140, Width = 80, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { all, prev, next, range, a, new Label { Text = "to", Left = 152, Top = 93, AutoSize = true }, b, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var t = all.Checked ? CopyTarget.All : prev.Checked ? CopyTarget.Previous : next.Checked ? CopyTarget.Next : CopyTarget.Range;
        CopyToFrames(t, (int)a.Value - 1, (int)b.Value - 1);
    }

    // ---------- Reverse Frames, Add Frames ----------

    /// <summary>Reverse frames <paramref name="from"/>..<paramref name="to"/> (0-based, inclusive).</summary>
    internal void ReverseFrames(int from, int to)
    {
        (from, to) = (Math.Clamp(Math.Min(from, to), 0, _doc.FrameCount - 1), Math.Clamp(Math.Max(from, to), 0, _doc.FrameCount - 1));
        if (from == to) { Say("Choose two or more frames to reverse."); return; }
        var before = Before();
        _doc.ReverseFrames(from, to);
        if (_cur >= from && _cur <= to) _cur = from + to - _cur;
        PushStructure("reverse frames", before);
        Say($"reversed frames {from + 1}–{to + 1}");
    }

    private void RunReverseFrames()
    {
        int lo = Math.Min(_selStart, _selEnd), hi = Math.Max(_selStart, _selEnd);
        using var f = new ThemedForm
        {
            Text = "Reverse frames", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(300, 130),
        };
        var all = new RadioButton { Text = "All frames", Left = 16, Top = 14, AutoSize = true, Checked = hi == lo };
        var range = new RadioButton { Text = "Range", Left = 16, Top = 44, AutoSize = true, Checked = hi > lo };
        var a = new NumericUpDown { Left = 90, Top = 42, Width = 60, Minimum = 1, Maximum = _doc.FrameCount, Value = lo + 1, AccessibleName = "First frame" };
        var b = new NumericUpDown { Left = 170, Top = 42, Width = 60, Minimum = 1, Maximum = _doc.FrameCount, Value = hi > lo ? hi + 1 : _doc.FrameCount, AccessibleName = "Last frame" };
        var ok = new Button { Text = "Reverse", Left = 110, Top = 86, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 200, Top = 86, Width = 80, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { all, range, a, new Label { Text = "to", Left = 152, Top = 45, AutoSize = true }, b, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        if (all.Checked) ReverseFrames(0, _doc.FrameCount - 1); else ReverseFrames((int)a.Value - 1, (int)b.Value - 1);
    }

    internal enum AddWhere { BeforeCurrent, AfterCurrent, AtEnd }

    /// <summary>Fireworks' Add Frames: how many, where, empty or copies of the current frame.</summary>
    internal void AddFrames(int count, AddWhere where, bool duplicate)
    {
        count = Math.Clamp(count, 1, 1000);
        if (duplicate)
        {
            long perFrame = _doc.Layers.Where(l => !l.Shared && l.Cels.ContainsKey(_cur))
                .Sum(l => l.Cels[_cur].Objects.Sum(TweenObjectBytes) + TweenObjectBytes(l.Cels[_cur].Mask));
            if (perFrame * count > TweenPixelBudget) { Say("That many copies would use too much memory. Add fewer."); return; }
        }
        var before = Before();
        int src = _cur;
        int at = where switch { AddWhere.BeforeCurrent => _cur, AddWhere.AfterCurrent => _cur + 1, _ => _doc.FrameCount };
        for (int k = 0; k < count; k++)
        {
            var spec = duplicate ? _doc.Frames[src].Clone() : new FrameSpec { Delay = _doc.Frames[src].Delay };
            _doc.InsertFrame(at + k, spec);
            if (where == AddWhere.BeforeCurrent) src++;                    // the source moved right
            if (!duplicate) continue;
            foreach (var l in _doc.Layers)
                if (!l.Shared && l.Cels.TryGetValue(src, out var c)) l.Cels[at + k] = c.Clone();
        }
        _cur = _selStart = _selEnd = at;
        PushStructure("add frames", before);
        Say($"added {count} {(duplicate ? "copied" : "empty")} frame{(count == 1 ? "" : "s")}");
    }

    private void RunAddFrames()
    {
        using var f = new ThemedForm
        {
            Text = "Add frames", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(300, 230),
        };
        var n = new NumericUpDown { Left = 120, Top = 14, Width = 70, Minimum = 1, Maximum = 1000, Value = 1, AccessibleName = "Number of frames" };
        var bef = new RadioButton { Text = "Before current frame", Left = 16, Top = 48, AutoSize = true };
        var aft = new RadioButton { Text = "After current frame", Left = 16, Top = 72, AutoSize = true, Checked = true };
        var end = new RadioButton { Text = "At the end", Left = 16, Top = 96, AutoSize = true };
        var where = new Panel { Left = 0, Top = 0, Width = 300, Height = 124 };
        where.Controls.AddRange(new Control[] { bef, aft, end });
        var empty = new RadioButton { Text = "Empty", Left = 16, Top = 6, AutoSize = true, Checked = true };
        var dup = new RadioButton { Text = "Copies of the current frame", Left = 16, Top = 30, AutoSize = true };
        var what = new Panel { Left = 0, Top = 124, Width = 300, Height = 56 };
        what.Controls.AddRange(new Control[] { empty, dup });
        var ok = new Button { Text = "Add", Left = 110, Top = 188, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 200, Top = 188, Width = 80, DialogResult = DialogResult.Cancel };
        f.Controls.Add(new Label { Text = "Number of frames", Left = 16, Top = 17, AutoSize = true });
        f.Controls.AddRange(new Control[] { n, where, what, ok, cancel });
        n.BringToFront();
        f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        AddFrames((int)n.Value, bef.Checked ? AddWhere.BeforeCurrent : aft.Checked ? AddWhere.AfterCurrent : AddWhere.AtEnd, dup.Checked);
    }

    // ---------- Include when exporting ----------

    /// <summary>Turn "Include when exporting" on or off for the selected frames (one undo step).</summary>
    internal void SetFramesExported(bool on)
    {
        int lo = Math.Min(_selStart, _selEnd), hi = Math.Max(_selStart, _selEnd);
        if (!on && Enumerable.Range(0, _doc.FrameCount).Count(i => (i < lo || i > hi) && _doc.Frames[i].Export) == 0)
        {
            Say("At least one frame must stay in the export.");
            return;
        }
        var old = Enumerable.Range(lo, hi - lo + 1).Select(i => _doc.Frames[i].Export).ToArray();
        for (int i = lo; i <= hi; i++) _doc.Frames[i].Export = on;
        _undo.Push(new PropertyEdit(on ? "include frames in export" : "leave frames out of export",
            d => { for (int i = lo; i <= hi; i++) d.Frames[i].Export = old[i - lo]; },
            d => { for (int i = lo; i <= hi; i++) d.Frames[i].Export = on; }));
        Touch();
        RebuildStrip();
        Say(on ? $"frames {lo + 1}–{hi + 1} will be exported" : $"frames {lo + 1}–{hi + 1} are left out when exporting");
    }

    private void AddFrameHelperMenus(ToolStripMenuItem frameMenu)
    {
        frameMenu.DropDownItems.Add("Add f&rames…", null, (_, _) => RunAddFrames());
        frameMenu.DropDownItems.Add("&Animate selection…", null, (_, _) => RunAnimateSelection());
        frameMenu.DropDownItems.Add("Di&stribute to frames", null, (_, _) => DistributeToFrames());
        frameMenu.DropDownItems.Add("Copy &to frames…", null, (_, _) => RunCopyToFrames());
        frameMenu.DropDownItems.Add("Re&verse frames…", null, (_, _) => RunReverseFrames());
        var inc = new ToolStripMenuItem("&Include when exporting");
        inc.Click += (_, _) => SetFramesExported(!_doc.Frames[_cur].Export);
        frameMenu.DropDownOpening += (_, _) => inc.Checked = _doc.Frames[_cur].Export;
        frameMenu.DropDownItems.Add(inc);
    }

    // ---------- test hooks ----------
    internal void TestSetCurrent(int frame) { _cur = _selStart = _selEnd = frame; }
    internal void TestSelectFrameRange(int a, int b) { _selStart = a; _selEnd = b; _cur = b; }
}
