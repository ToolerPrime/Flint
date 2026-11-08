/* Image > Trace bitmap, and Export layer as SVG.
 *
 * Turns the active layer's pixels into one vector object (see Tracer) that
 * the free-transform tool then scales and rotates without going blocky.
 * Painting on it merges it back into pixels like any placed image.
 */
namespace Flint;

public sealed class TraceDialog : ThemedForm
{
    private readonly NumericUpDown _colours, _minArea, _smoothing, _simplify;
    private readonly CheckBox _smooth, _preview;
    private readonly Label _info;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 250 };

    /// <summary>Raised after a pause; null options means "preview is off, just restore".</summary>
    public event Action<TraceOptions?, bool>? PreviewRequested;
    public bool SmoothEdges => _smooth.Checked;
    public TraceOptions Options => new()
    {
        Colours = (int)_colours.Value, MinArea = (int)_minArea.Value,
        Smoothing = (double)_smoothing.Value, Simplify = (double)_simplify.Value,
    };
    public string Info { set => _info.Text = value; }

    public TraceDialog(int defaultColours = 0, string note = "")
    {
        Text = "Trace bitmap";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        int y = 14;
        NumericUpDown Row(string label, decimal min, decimal max, decimal val, int dec)
        {
            Controls.Add(new Label { Text = label, Left = 14, Top = y + 3, Width = 130 });
            var n = new NumericUpDown
            {
                Left = 150, Top = y, Width = 80, Minimum = min, Maximum = max, Value = val,
                DecimalPlaces = dec, Increment = dec > 0 ? 0.1m : 1, TextAlign = HorizontalAlignment.Right,
            };
            n.ValueChanged += (_, _) => Kick();
            Controls.Add(n);
            y += 30;
            return n;
        }
        _colours = Row("Colours (0 = exact)", 0, 256, defaultColours, 0);
        _minArea = Row("Minimum area (px)", 1, 10000, 1, 0);
        _smoothing = Row("Smoothing (0-100)", 0, 100, 0, 0);
        _simplify = Row("Simplify (0-10 px)", 0, 10, 0, 1);
        _smooth = new MintCheckBox { Text = "Smooth edges", Left = 14, Top = y + 2, Width = 220, Checked = true };
        _smooth.CheckedChanged += (_, _) => Kick();
        Controls.Add(_smooth);
        y += 26;
        _preview = new MintCheckBox { Text = "Preview on the canvas", Left = 14, Top = y + 2, Width = 220, Checked = true };
        _preview.CheckedChanged += (_, _) => Kick();
        Controls.Add(_preview);
        y += 28;
        _info = new Label { Left = 14, Top = y, Width = 230, Height = 18, ForeColor = SystemColors.GrayText };
        Controls.Add(_info);
        y += 26;
        if (note.Length > 0)
        {
            Controls.Add(new Label { Text = note, Left = 14, Top = y, Width = 230, Height = 48, ForeColor = SystemColors.GrayText });
            y += 54;
        }
        var ok = new Button { Text = "Apply", Left = 76, Top = y, Width = 76, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 158, Top = y, Width = 76, DialogResult = DialogResult.Cancel };
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
        ClientSize = new Size(258, y + 40);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            PreviewRequested?.Invoke(_preview.Checked ? Options : null, SmoothEdges);
        };
        Shown += (_, _) => Kick();
        FormClosed += (_, _) => _debounce.Dispose();
    }

    private void Kick() { _debounce.Stop(); _debounce.Start(); }
}

public sealed partial class MainForm
{
    /// <summary>One run of the trace dialog: the snapshot to go back to, the input, and the last result.</summary>
    private sealed class TraceSession
    {
        public Document Before = null!;
        public byte[] Px = null!;
        public Rectangle Rect;
        public (int, int, double, double, bool)? Key;
        public VectorArt? Art;
        public (byte[] px, int x, int y, int w, int h) R;
        public CancellationTokenSource? Cts;
        public bool Done;
        public Affine M => Affine.Translate(Rect.X, Rect.Y);
    }

    private static (int, int, double, double, bool) TraceKey(TraceOptions o, bool smooth) => (o.Colours, o.MinArea, o.Smoothing, o.Simplify, smooth);

    private void BuildTraceMenu(ToolStripMenuItem image)
    {
        image.DropDownItems.Add(new ToolStripSeparator());
        image.DropDownItems.Add("&Trace bitmap…", null, (_, _) => RunTraceDialog());
        image.DropDownItems.Add("Export layer as S&VG…", null, (_, _) => ExportLayerSvg());
    }

    private TraceSession? TraceBegin()
    {
        FinishFloat();
        if (ActiveLayer.Locked) { Say($"layer '{ActiveLayer.Name}' is locked"); return null; }
        if (!TraceInput(out var px, out var rect)) { Say("nothing to trace"); return null; }
        return new TraceSession { Before = Before(), Px = px, Rect = rect };
    }

    private void RunTraceDialog()
    {
        var s = TraceBegin();
        if (s == null) return;
        var distinct = new HashSet<uint>();
        for (int i = 0; i < s.Px.Length; i += 4) if (s.Px[i + 3] != 0) distinct.Add(BitConverter.ToUInt32(s.Px, i));
        bool many = distinct.Count > 1024;
        using var dlg = new TraceDialog(many ? 16 : 0,
            many ? $"This picture has {distinct.Count} colours, so Colours starts at 16 to keep tracing quick. Set it to 0 for an exact trace." : "");
        dlg.PreviewRequested += (o, smooth) => TracePreview(s, o, smooth, dlg);
        bool ok = dlg.ShowDialog(this) == DialogResult.OK;
        TraceEnd(s, ok, dlg.Options, dlg.SmoothEdges);
    }

    /// <summary>Trace and render without touching the document, so it can run on any thread.</summary>
    private static (VectorArt art, (byte[] px, int x, int y, int w, int h) r) TraceRun(TraceSession s, TraceOptions o, bool smooth, CancellationToken ct)
    {
        var art = Tracer.Trace(s.Px, s.Rect.Width, s.Rect.Height, o, ct);
        return (art, Tracer.Render(art, s.M, smooth, ct));
    }

    private void TraceCompute(TraceSession s, TraceOptions o, bool smooth)
    {
        var key = TraceKey(o, smooth);
        if (s.Art != null && s.Key == key) return;
        var (art, r) = TraceRun(s, o, smooth, default);
        s.Art = art; s.R = r; s.Key = key;
    }

    /// <summary>Put the session's current result on the canvas, on top of the untouched snapshot.</summary>
    private void TraceShow(TraceSession s, bool smooth)
    {
        RestoreDoc(s.Before);
        InstallTrace(s.Art!, s.R, s.M, smooth);
        InvalidateComposite();
    }

    /// <summary>Preview from the dialog: trace on a background task, show the result on the UI thread, drop stale requests.</summary>
    private void TracePreview(TraceSession s, TraceOptions? o, bool smooth, TraceDialog dlg)
    {
        s.Cts?.Cancel();
        if (o == null)
        {
            RestoreDoc(s.Before);
            InvalidateComposite();
            dlg.Info = "";
            return;
        }
        if (s.Art != null && s.Key == TraceKey(o, smooth))
        {
            TraceShow(s, smooth);
            dlg.Info = $"{s.Art.Shapes.Count} shapes, {s.Art.PointCount} points";
            return;
        }
        var cts = s.Cts = new CancellationTokenSource();
        var ct = cts.Token;
        dlg.Info = "tracing…";
        Task.Run(() => TraceRun(s, o, smooth, ct), ct).ContinueWith(t =>
        {
            if (ct.IsCancellationRequested || s.Done || dlg.IsDisposed) return;
            if (t.IsFaulted) { dlg.Info = t.Exception!.GetBaseException().Message; return; }
            if (t.IsCanceled) return;
            s.Art = t.Result.art; s.R = t.Result.r; s.Key = TraceKey(o, smooth);
            TraceShow(s, smooth);
            dlg.Info = $"{s.Art.Shapes.Count} shapes, {s.Art.PointCount} points";
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Close a session: Cancel puts the document back; Apply takes one undo step and drops the selection.</summary>
    private VectorArt? TraceEnd(TraceSession s, bool apply, TraceOptions o, bool smooth)
    {
        s.Done = true;
        s.Cts?.Cancel();
        RestoreDoc(s.Before);
        if (!apply) { InvalidateComposite(); RefreshAll(); return null; }
        try { TraceCompute(s, o, smooth); }
        catch (InvalidOperationException ex) { InvalidateComposite(); RefreshAll(); Say(ex.Message); return null; }
        var art = s.Art!;
        InstallTrace(art, s.R, s.M, smooth);
        _selection = null;
        PushStructure("trace bitmap", s.Before);
        Say($"traced into {art.Shapes.Count} shapes, {art.PointCount} points. Ctrl+T to scale or rotate it.");
        return art;
    }

    /// <summary>The active layer's pixels, inside the selection, trimmed to content.</summary>
    private bool TraceInput(out byte[] px, out Rectangle rect)
    {
        var full = LayerPixels();
        if (_selection is { IsEmpty: false } sel)
            for (int y = 0; y < DocH; y++)
                for (int x = 0; x < DocW; x++)
                    if (!sel.Contains(x, y)) Array.Clear(full, (y * DocW + x) * 4, 4);
        rect = Sprites.ContentRect(full, DocW, DocH);
        px = new byte[rect.Width * rect.Height * 4];
        for (int y = 0; y < rect.Height; y++)
            Buffer.BlockCopy(full, ((rect.Y + y) * DocW + rect.X) * 4, px, y * rect.Width * 4, rect.Width * 4);
        return !rect.IsEmpty;
    }

    /// <summary>Replace the traced pixels with one vector object. Does not touch undo or the selection.</summary>
    private void InstallTrace(VectorArt art, (byte[] px, int x, int y, int w, int h) r, Affine m, bool smooth)
    {
        var obj = new RasterObject(ImageOps.FromPixels(r.w, r.h, r.px))
        {
            Name = "Traced bitmap", X = r.x, Y = r.y, Vector = art, Matrix = m,
            Mode = smooth ? Resample.Smooth : Resample.Nearest,
        };
        var cel = ActiveLayer.EnsureCel(_cur);
        if (_selection is not { IsEmpty: false } sel)
        {
            // Hidden objects stay, as BakeCel keeps them.
            cel.Objects.RemoveAll(o => o is RasterObject { Visible: true });
            cel.Objects.Add(obj);
            return;
        }
        BakeCel(cel);
        var baseObj = cel.EnsureRaster(DocW, DocH);
        int bi = cel.Objects.IndexOf(baseObj);
        var bp = ImageOps.GetPixels(baseObj.Pixels);
        for (int y = sel.Bounds.Top; y < sel.Bounds.Bottom; y++)
            for (int x = sel.Bounds.Left; x < sel.Bounds.Right; x++)
                if (sel.Contains(x, y)) Array.Clear(bp, (y * DocW + x) * 4, 4);
        cel.Objects[bi] = baseObj.With(ImageOps.FromPixels(DocW, DocH, bp));
        cel.Objects.Insert(bi + 1, obj);
    }

    private void ExportLayerSvg()
    {
        FinishFloat();
        var o = ActiveLayer.CelAt(_cur)?.Objects.OfType<RasterObject>().LastOrDefault(r => r.Vector != null);
        if (o == null) { Say("this layer has no traced vector art: use Image > Trace bitmap first"); return; }
        using var dlg = new SaveFileDialog { Filter = "SVG image|*.svg", FileName = ActiveLayer.Name + ".svg" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dlg.FileName, Tracer.ToSvg(o.Vector!, o.Matrix, DocW, DocH, o.Opacity));
        Say($"saved {Path.GetFileName(dlg.FileName)}");
    }

    internal int TestUndoDone => _undo.DoneCount;
    private TraceSession? _testTrace;
    internal int TestTrace(TraceOptions o, bool smooth)
    {
        var s = TraceBegin();
        return s == null ? 0 : TraceEnd(s, true, o, smooth)?.Shapes.Count ?? 0;
    }
    internal bool TestTraceBegin() => (_testTrace = TraceBegin()) != null;
    internal int TestTracePreview(TraceOptions o, bool smooth)
    {
        TraceCompute(_testTrace!, o, smooth);
        TraceShow(_testTrace!, smooth);
        return _testTrace!.Art!.Shapes.Count;
    }
    internal int TestTraceEnd(bool apply, TraceOptions o, bool smooth) => TraceEnd(_testTrace!, apply, o, smooth)?.Shapes.Count ?? 0;
}
