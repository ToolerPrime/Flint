/* Export preview (Fireworks' Preview / 2-Up / 4-Up). Each pane encodes the
 * document for real with its own format and settings, in the background,
 * reports the real byte size, and shows the file decoded back (or, for
 * GIF/PNG-8, the palette picture the file contains). Zoom and pan are shared.
 * Plain controls on purpose: the theme work styles them later. */
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Flint;

internal enum OptFormat { Original, Gif, Png8, Png32, Apng, WebpLossless, WebpLossy, Jpeg, Bmp, Tiff }

internal sealed class OptSettings
{
    public OptFormat Format { get; set; }
    public int Quality { get; set; } = 80;
    /// <summary>Palette, dither, lossy and colour-table edits for GIF and PNG-8.</summary>
    public GifOptions Gif { get; set; } = new() { Colours = 128 };
    public int Colours { get => Gif.Colours; set => Gif.Colours = value; }
    public bool Dither
    {
        get => Gif.Dither != DitherKind.None;
        set => Gif.Dither = value ? (Gif.Dither == DitherKind.None ? DitherKind.FloydSteinberg : Gif.Dither) : DitherKind.None;
    }
    public OptSettings Clone() { var c = (OptSettings)MemberwiseClone(); c.Gif = Gif.Clone(); return c; }
    public bool UsesColours => Format is OptFormat.Gif or OptFormat.Png8;
    public bool UsesQuality => Format is OptFormat.WebpLossy or OptFormat.Jpeg;
}

internal sealed class OptSource
{
    public byte[][] Frames = Array.Empty<byte[]>();   // straight BGRA, one per frame
    public int[] Delays = Array.Empty<int>();
    public int W, H, Current;
    public Color Matte = Color.White;
    public bool Loop = true;
    /// <summary>The current frame when it is left out of animated exports; stills still use it.</summary>
    public byte[]? Still;
    public byte[] CurrentPixels => Still ?? Frames[Current];
}

internal sealed class OptResult
{
    public long Bytes = -1;                  // -1 for Original
    public byte[][] Preview = Array.Empty<byte[]>();
    public string Note = "";
    public byte[]? Data;
    /// <summary>GIF / PNG-8: the colour table, for the Colours window.</summary>
    public IndexedFrames? Indexed;
    /// <summary>The decoded picture for a frame; a single preview serves every frame.</summary>
    public byte[] PreviewFor(int frame) => Preview.Length > 1 ? Preview[Math.Min(frame, Preview.Length - 1)] : Preview[0];
}

internal static class OptimizeEncoder
{
    public static string Describe(OptFormat f) => f switch
    {
        OptFormat.Original => "Original (exact composite)",
        OptFormat.Gif => "GIF",
        OptFormat.Png8 => "PNG-8 (indexed)",
        OptFormat.Png32 => "PNG-32",
        OptFormat.Apng => "APNG (animated PNG)",
        OptFormat.WebpLossless => "WebP lossless",
        OptFormat.WebpLossy => "WebP lossy",
        OptFormat.Jpeg => "JPEG",
        OptFormat.Bmp => "BMP (32-bit)",
        _ => "TIFF (32-bit)",
    };

    /// <summary>Encode for real. Animated formats take every frame; the others the current one.</summary>
    public static OptResult Encode(OptSettings s, OptSource src, CancellationToken ct)
    {
        int w = src.W, h = src.H, n = src.Frames.Length;
        bool multi = n > 1;
        var cur = src.CurrentPixels;
        OptResult R(byte[] data, byte[][] preview, string note) =>
            new() { Bytes = data.Length, Data = data, Preview = preview, Note = note };
        string frames = multi ? $", {n} frames" : "";

        switch (s.Format)
        {
            case OptFormat.Original:
                return new OptResult { Preview = src.Frames, Note = "exact composite" };

            case OptFormat.Gif:
            {
                var f = GifPalette.Index(src.Frames, w, h, s.Gif, ct);
                var gf = f.Indices.Select((ix, i) => new GifFrame { Indices = ix, DelayMs = src.Delays[i] }).ToList();
                var (bytes, shown) = GifEncoder.EncodeEx(w, h, gf, f.Palette, f.TransparentIndex, src.Loop, s.Gif.Optimize, s.Gif.Lossy, ct);
                var pic = new IndexedFrames { Palette = f.Palette, TransparentIndex = f.TransparentIndex, Indices = shown.ToList(), Source = f.Source };
                var r = R(bytes, Enumerable.Range(0, n).Select(pic.ToBgra).ToArray(), $"{f.Palette.Count} colours{frames}");
                r.Indexed = f;
                return r;
            }
            case OptFormat.Png8:
            {
                var g = s.Gif.Clone(); g.Optimize = false; g.Lossy = 0;
                var f = GifPalette.Index(new[] { cur }, w, h, g, ct);
                var r = R(StillExport.Png8(f, w, h), new[] { f.ToBgra(0) }, $"{f.Palette.Count} colours, current frame");
                r.Indexed = f;
                return r;
            }
            case OptFormat.Png32:
            {
                using var bmp = ImageOps.FromPixels(w, h, cur);
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                return R(ms.ToArray(), new[] { cur }, "current frame, lossless");
            }
            case OptFormat.Apng:
            {
                var list = src.Frames.Select((px, i) => new Frame(ImageOps.FromPixels(w, h, px), src.Delays[i])).ToList();
                try { return R(ApngEncoder.Encode(list, src.Loop, ct), src.Frames, "lossless" + frames); }
                finally { foreach (var f in list) f.Bmp.Dispose(); }
            }
            case OptFormat.WebpLossless:
            case OptFormat.WebpLossy:
            {
                int q = s.Format == OptFormat.WebpLossless ? 0 : s.Quality;
                byte[] bytes, still;
                if (multi)
                {
                    var list = src.Frames.Select((px, i) => new Frame(ImageOps.FromPixels(w, h, px), src.Delays[i])).ToList();
                    string tmp = Path.Combine(Path.GetTempPath(), "flint-opt-" + Guid.NewGuid().ToString("N")[..8] + ".webp");
                    try
                    {
                        var r = WebpWriter.Encode(list, tmp, q, src.Loop, ct);
                        if (!r.Ok) throw new InvalidOperationException(r.Message);
                        bytes = File.ReadAllBytes(tmp);
                    }
                    finally { foreach (var f in list) f.Bmp.Dispose(); try { File.Delete(tmp); } catch { } }
                    still = q == 0 ? Array.Empty<byte>() : StillExport.WebpStill(cur, w, h, q, ct);
                }
                else still = bytes = StillExport.WebpStill(cur, w, h, q, ct);
                if (q == 0) return R(bytes, src.Frames, "lossless" + frames);
                return R(bytes, new[] { StillExport.DecodeWebp(still, ct) }, multi ? $"preview shows the current frame{frames}" : "");
            }
            case OptFormat.Jpeg:
            {
                var bytes = StillExport.Jpeg(cur, w, h, s.Quality, src.Matte);
                return R(bytes, new[] { StillExport.DecodeGdi(bytes) }, "current frame, no transparency");
            }
            case OptFormat.Bmp: return R(StillExport.Bmp(cur, w, h), new[] { cur }, "current frame, lossless");
            default: return R(StillExport.Tiff(cur, w, h), new[] { cur }, "current frame, lossless");
        }
    }
}

/// <summary>Zoom and pan shared by every pane.</summary>
internal sealed class SharedView
{
    public int Zoom = 1;
    public double CX, CY;            // image point at the middle of each pane
    public event Action? Changed;
    public void Raise() => Changed?.Invoke();
}

internal sealed class PreviewCanvas : Control
{
    private readonly SharedView _view;
    private Bitmap? _bmp;
    private Point _last;
    private bool _drag;

    public PreviewCanvas(SharedView v)
    {
        _view = v;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(60, 62, 68);
        v.Changed += Invalidate;
    }

    public void Show(byte[] bgra, int w, int h)
    {
        var old = _bmp;
        _bmp = ImageOps.FromPixels(w, h, bgra);
        old?.Dispose();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _view.Changed -= Invalidate; _bmp?.Dispose(); }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_bmp == null) return;
        int z = _view.Zoom, w = _bmp.Width * z, h = _bmp.Height * z;
        int x = (int)Math.Round(Width / 2.0 - _view.CX * z), y = (int)Math.Round(Height / 2.0 - _view.CY * z);
        using (var light = new SolidBrush(Color.FromArgb(80, 82, 90)))
        using (var dark = new SolidBrush(Color.FromArgb(66, 68, 76)))
        {
            g.FillRectangle(light, x, y, w, h);
            var clip = Rectangle.Intersect(new Rectangle(x, y, w, h), ClientRectangle);
            for (int cy = clip.Top - (clip.Top - y) % 8; cy < clip.Bottom; cy += 8)
                for (int cx = clip.Left - (clip.Left - x) % 8; cx < clip.Right; cx += 8)
                    if ((((cx - x) / 8 + (cy - y) / 8) & 1) == 1)
                        g.FillRectangle(dark, Rectangle.Intersect(new Rectangle(cx, cy, 8, 8), new Rectangle(x, y, w, h)));
        }
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_bmp, new Rectangle(x, y, w, h));
    }

    protected override void OnMouseDown(MouseEventArgs e) { _drag = true; _last = e.Location; Capture = true; base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _drag = false; Capture = false; base.OnMouseUp(e); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag)
        {
            _view.CX -= (e.X - _last.X) / (double)_view.Zoom;
            _view.CY -= (e.Y - _last.Y) / (double)_view.Zoom;
            _last = e.Location;
            _view.Raise();
        }
        base.OnMouseMove(e);
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _view.Zoom = Math.Clamp(_view.Zoom + (e.Delta > 0 ? 1 : -1), 1, 32);
        _view.Raise();
        base.OnMouseWheel(e);
    }
}

internal sealed class OptPane : Panel
{
    private readonly OptSource _src;
    private readonly ComboBox _fmt = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly NumericUpDown _colours = new() { Minimum = 2, Maximum = 256, Value = 128, Width = 52 };
    private readonly ComboBox _palette = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 118, AccessibleName = "Palette type" };
    private readonly ComboBox _dither = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, AccessibleName = "Dither" };
    private readonly NumericUpDown _ditherAmount = new() { Minimum = 0, Maximum = 100, Value = 100, Width = 48, AccessibleName = "Dither amount percent" };
    private readonly NumericUpDown _lossy = new() { Minimum = 0, Maximum = 200, Value = 0, Width = 52, AccessibleName = "Lossy GIF amount" };
    private readonly Label _lblLossy = new() { Text = "Lossy", AutoSize = true, Margin = new Padding(6, 6, 0, 0) };
    private readonly Button _table = new() { Text = "Colour table…", AutoSize = true };
    private readonly Button _toSize = new() { Text = "Optimize to size…", AutoSize = true };
    /// <summary>The GIF settings that have no control here (colour-table edits, loaded palette, flags).</summary>
    private GifOptions _gif = new();
    private readonly NumericUpDown _quality = new() { Minimum = 1, Maximum = 100, Value = 80, Width = 52 };
    private readonly Label _lblColours = new() { Text = "Colours", AutoSize = true, Margin = new Padding(6, 6, 0, 0) };
    private readonly Label _lblQuality = new() { Text = "Quality", AutoSize = true, Margin = new Padding(6, 6, 0, 0) };
    private readonly Label _size = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _export = new() { Text = "Export with these settings…", Dock = DockStyle.Right, Width = 190 };
    private readonly PreviewCanvas _canvas;
    private CancellationTokenSource? _cts;
    private OptResult? _result;
    private OptFormat _kept;
    private bool _forced, _loading;
    private int _frame;

    public event Action<OptSettings>? ExportRequested;
    public bool Busy { get; private set; }
    public OptResult? Result => _result;
    public string SizeText => _size.Text;
    public string Error { get; private set; } = "";

    public OptPane(OptSource src, SharedView view, OptFormat first, Func<bool> ffmpegOk)
    {
        _src = src;
        _canvas = new PreviewCanvas(view) { Dock = DockStyle.Fill };
        BorderStyle = BorderStyle.FixedSingle;
        foreach (OptFormat f in Enum.GetValues<OptFormat>()) _fmt.Items.Add(OptimizeEncoder.Describe(f));
        _fmt.SelectedIndex = (int)first;
        foreach (PaletteKind k in Enum.GetValues<PaletteKind>()) _palette.Items.Add(GifOptions.Describe(k));
        foreach (DitherKind k in Enum.GetValues<DitherKind>()) _dither.Items.Add(GifOptions.Describe(k));
        _palette.SelectedIndex = 0; _dither.SelectedIndex = 0;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        top.Controls.AddRange(new Control[] { _fmt, _palette, _lblColours, _colours, _dither, _ditherAmount, _lblLossy, _lossy, _table, _toSize, _lblQuality, _quality });
        var tips = Theme.MakeTip();
        tips.SetToolTip(_ditherAmount, "Dither strength, percent");
        tips.SetToolTip(_lossy, "Lossy GIF: 0 is exact. Higher values allow near colours for a smaller file");
        tips.SetToolTip(_table, "See the colours; lock, edit or make one transparent; load or save a palette");
        tips.SetToolTip(_toSize, "Pick the most colours that fit a file size you choose");
        Disposed += (_, _) => tips.Dispose();
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 30 };
        bottom.Controls.Add(_size);
        bottom.Controls.Add(_export);
        Controls.Add(_canvas);
        Controls.Add(bottom);
        Controls.Add(top);
        _fmt.SelectedIndexChanged += (_, _) => { SyncControls(); Reencode(); };
        _colours.ValueChanged += (_, _) => Reencode();
        _quality.ValueChanged += (_, _) => Reencode();
        _dither.SelectedIndexChanged += (_, _) => { SyncControls(); Reencode(); };
        _palette.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && (PaletteKind)_palette.SelectedIndex == PaletteKind.Custom && _gif.Custom == null && !LoadPaletteFile()) { _palette.SelectedIndex = 0; return; }
            Reencode();
        };
        _ditherAmount.ValueChanged += (_, _) => Reencode();
        _lossy.ValueChanged += (_, _) => Reencode();
        _table.Click += (_, _) => ShowColourTable();
        _toSize.Click += (_, _) => AskOptimizeToSize();
        _export.Click += (_, _) => ExportRequested?.Invoke(Settings);
        _ffmpegOk = ffmpegOk;
        SyncControls();
        _canvas.Show(src.Frames[src.Current], src.W, src.H);
    }

    private readonly Func<bool> _ffmpegOk;

    public OptSettings Settings
    {
        get
        {
            var g = _gif.Clone();
            g.Palette = (PaletteKind)_palette.SelectedIndex; g.Colours = (int)_colours.Value;
            g.Dither = (DitherKind)_dither.SelectedIndex; g.DitherAmount = (int)_ditherAmount.Value; g.Lossy = (int)_lossy.Value;
            return new OptSettings { Format = (OptFormat)_fmt.SelectedIndex, Quality = (int)_quality.Value, Gif = g };
        }
    }

    /// <summary>The Original tab: this pane shows the composite and its settings are locked.</summary>
    public void ForceOriginal(bool on)
    {
        if (on == _forced) return;
        _forced = on;
        _loading = true;
        if (on) { _kept = (OptFormat)_fmt.SelectedIndex; _fmt.SelectedIndex = 0; }
        else _fmt.SelectedIndex = (int)_kept;
        _loading = false;
        _fmt.Enabled = !on;
        SyncControls();
        Reencode();
    }

    public void SetSettings(OptSettings s)
    {
        _loading = true;
        _fmt.SelectedIndex = (int)s.Format; _colours.Value = Math.Clamp(s.Colours, 2, 256);
        _quality.Value = Math.Clamp(s.Quality, 1, 100);
        _gif = s.Gif.Clone();
        _palette.SelectedIndex = (int)s.Gif.Palette; _dither.SelectedIndex = (int)s.Gif.Dither;
        _ditherAmount.Value = Math.Clamp(s.Gif.DitherAmount, 0, 100); _lossy.Value = Math.Clamp(s.Gif.Lossy, 0, 200);
        _loading = false;
        SyncControls();
        Reencode();
    }

    private void SyncControls()
    {
        var s = Settings;
        _lblColours.Visible = _colours.Visible = _dither.Visible = _palette.Visible = _table.Visible = _toSize.Visible = s.UsesColours;
        _ditherAmount.Visible = s.UsesColours && s.Gif.Dither != DitherKind.None;
        _lblLossy.Visible = _lossy.Visible = s.Format == OptFormat.Gif;
        _lblQuality.Visible = _quality.Visible = s.UsesQuality;
    }

    public void ShowFrame(int frame)
    {
        _frame = frame;
        var px = _result?.PreviewFor(frame) ?? _src.Frames[frame];
        _canvas.Show(px, _src.W, _src.H);
    }

    private bool LoadPaletteFile()
    {
        using var open = new OpenFileDialog { Title = "Load palette", Filter = PaletteFiles.Filter };
        if (open.ShowDialog(this) != DialogResult.OK) return false;
        try { _gif.Custom = PaletteFiles.Load(open.FileName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load palette", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        return true;
    }

    /// <summary>The frames a pane's colours come from (PNG-8 takes the current one).</summary>
    internal IList<byte[]> PaletteFrames(OptFormat f) => f == OptFormat.Png8 ? new[] { _src.CurrentPixels } : _src.Frames;

    private void ShowColourTable()
    {
        var s = Settings;
        using var dlg = new ColourTableDialog(s.Gif, g => GifPalette.Index(PaletteFrames(s.Format), _src.W, _src.H, g));
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var n = s.Clone(); n.Gif = dlg.Options;
        SetSettings(n);
    }

    private void AskOptimizeToSize()
    {
        long now = _result?.Bytes > 0 ? _result.Bytes : 100 * 1024;
        int kb = TargetDialog.Ask(this, (int)Math.Max(1, now / 1024 * 7 / 10));
        if (kb <= 0) return;
        var start = Settings;
        OptSettings? found = null;
        var (ok, msg) = ProgressDialog.Run(FindForm(), "Optimize to size", (p, ct) =>
        {
            found = OptimizeToSize.Find(start, _src, kb * 1024L, ct);
            return found == null ? "" : "found";
        });
        if (!ok) { if (msg != "cancelled") MessageBox.Show(this, msg, "Optimize to size"); return; }
        if (found == null)
        {
            MessageBox.Show(this, $"This cannot be made as small as {kb} KB, even with 2 colours and full lossy. Try fewer frames or a smaller canvas.",
                "Optimize to size", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SetSettings(found);
    }

    public void Reencode()
    {
        if (_loading) return;
        _cts?.Cancel();                                  // a stale job must not land after a newer one
        var cts = _cts = new CancellationTokenSource();
        var token = cts.Token;
        var s = Settings;
        Busy = true;
        Error = "";
        _size.ForeColor = SystemColors.ControlText;
        _size.Text = s.Format == OptFormat.Original ? "" : "encoding…";
        _export.Enabled = false;
        Task.Run(async () =>
        {
            OptResult? res = null; string? err = null;
            try
            {
                await Task.Delay(120, token);            // typing in a number box fires many changes
                if (s.Format is OptFormat.WebpLossless or OptFormat.WebpLossy && !_ffmpegOk())
                    throw new InvalidOperationException("WebP needs ffmpeg, which was not found (winget install Gyan.FFmpeg)");
                res = OptimizeEncoder.Encode(s, _src, token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { err = ex.Message; }
            if (token.IsCancellationRequested) return;
            try
            {
                BeginInvoke(() =>
                {
                    if (token.IsCancellationRequested || IsDisposed) return;
                    Busy = false;
                    if (err != null)
                    {
                        Error = err;
                        _result = null;
                        _size.ForeColor = Color.Firebrick;
                        _size.Text = err;
                        _canvas.Show(_src.Frames[_frame], _src.W, _src.H);
                        return;
                    }
                    _result = res;
                    _size.Text = res!.Bytes < 0 ? "" : $"{res.Bytes / 1024.0:0.0} KB ({res.Bytes:N0} bytes)   {res.Note}";
                    _export.Enabled = res.Bytes >= 0;
                    ShowFrame(_frame);
                });
            }
            catch (InvalidOperationException) { /* window closed meanwhile */ }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _cts?.Cancel(); }
        base.Dispose(disposing);
    }
}

internal sealed class OptimizeDialog : Form
{
    private readonly OptSource _src;
    private readonly SharedView _view = new();
    private readonly OptPane[] _panes = new OptPane[4];
    private readonly RadioButton[] _modes;
    private readonly Panel _stage = new() { Dock = DockStyle.Fill };
    private readonly Label _info = new() { AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
    private readonly Button _play = new() { Text = "Play", AutoSize = true };
    private readonly Button _matte = new() { Text = "Matte…", AutoSize = true };
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly ComboBox _presets = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, AccessibleName = "Export presets" };
    private int _mode = 2, _frame, _active;

    public OptSettings? Chosen { get; private set; }
    public bool OtherFormatsChosen { get; private set; }
    public Color Matte => _src.Matte;

    public OptimizeDialog(OptSource src, bool showOtherFormats = false)
    {
        _src = src;
        Text = "Export preview";
        ClientSize = new Size(1040, 720);
        MinimumSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        _frame = src.Current;
        bool webp = WebmExport.Find() != null;
        var first = new[] { OptFormat.Gif, OptFormat.Png32, OptFormat.Jpeg, webp ? OptFormat.WebpLossy : OptFormat.Bmp };
        _view.Zoom = 1;
        _view.CX = src.W / 2.0; _view.CY = src.H / 2.0;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        _modes = new[] { "Original", "Preview", "2-Up", "4-Up" }.Select((t, i) => new RadioButton { Text = t, AutoSize = true, Checked = i == _mode, Appearance = Appearance.Button }).ToArray();
        foreach (var (rb, i) in _modes.Select((r, i) => (r, i)))
        {
            rb.CheckedChanged += (_, _) => { if (rb.Checked) SetMode(i); };
            bar.Controls.Add(rb);
        }
        var fit = new Button { Text = "Fit", AutoSize = true };
        fit.Click += (_, _) => Fit();
        var zi = new Button { Text = "+", AutoSize = true };
        zi.Click += (_, _) => { _view.Zoom = Math.Min(32, _view.Zoom + 1); _view.Raise(); UpdateInfo(); };
        var zo = new Button { Text = "−", AutoSize = true };
        zo.Click += (_, _) => { _view.Zoom = Math.Max(1, _view.Zoom - 1); _view.Raise(); UpdateInfo(); };
        _play.Enabled = src.Frames.Length > 1;
        _play.Click += (_, _) => TogglePlay();
        _matte.Click += (_, _) => PickMatte();
        _matte.BackColor = src.Matte;
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel,
            AccessibleName = "Close export preview" };
        CancelButton = close;
        bar.Controls.AddRange(new Control[] { zo, zi, fit, _play, _matte });
        var savePreset = new Button { Text = "Save preset…", AutoSize = true, AccessibleName = "Save the selected pane's settings as a preset" };
        var delPreset = new Button { Text = "Delete preset", AutoSize = true };
        savePreset.Click += (_, _) => SavePreset();
        delPreset.Click += (_, _) => { if (_presets.SelectedIndex > 0) { ExportPresets.Delete((string)_presets.SelectedItem!); FillPresets(); } };
        _presets.SelectedIndexChanged += (_, _) => { if (_presets.SelectedIndex > 0) ApplyPreset((string)_presets.SelectedItem!); };
        bar.Controls.AddRange(new Control[] { _presets, savePreset, delPreset });
        FillPresets();
        if (showOtherFormats)
        {
            var more = new Button { Text = "More formats…", AutoSize = true,
                AccessibleName = "Export video, sprite sheets and other formats" };
            more.Click += (_, _) => { OtherFormatsChosen = true; DialogResult = DialogResult.OK; };
            bar.Controls.Add(more);
            var tips = Theme.MakeTip();
            tips.SetToolTip(more, "Export video, sprite sheets, audio and other formats");
            Disposed += (_, _) => tips.Dispose();
        }
        bar.Controls.AddRange(new Control[] { close, _info });

        foreach (int i in Enumerable.Range(0, 4))
        {
            _panes[i] = new OptPane(src, _view, first[i], () => WebmExport.Find() != null);
            _panes[i].ExportRequested += s => { Chosen = s; DialogResult = DialogResult.OK; Close(); };
            int pane = i;
            _panes[i].Enter += (_, _) => _active = pane;
            _panes[i].MouseDown += (_, _) => _active = pane;
            _stage.Controls.Add(_panes[i]);
        }
        Controls.Add(_stage);
        Controls.Add(bar);
        _stage.Resize += (_, _) => Relayout();
        _timer.Tick += (_, _) => Advance();
        Shown += (_, _) => { Fit(); SetMode(_mode); };
        FormClosed += (_, _) => { _timer.Stop(); _timer.Dispose(); };
        foreach (var p in _panes) p.ShowFrame(_frame);
        SetMode(_mode);
        UpdateInfo();
    }

    private void SetMode(int mode)
    {
        _mode = mode;
        _panes[0].ForceOriginal(mode == 0);
        Relayout();
        for (int i = 0; i < PaneCount; i++) _panes[i].Reencode();
        UpdateInfo();
    }

    private int PaneCount => _mode switch { 0 or 1 => 1, 2 => 2, _ => 4 };

    private void Relayout()
    {
        int count = PaneCount;
        int w = _stage.ClientSize.Width, h = _stage.ClientSize.Height;
        for (int i = 0; i < 4; i++)
        {
            _panes[i].Visible = i < count;
            if (i >= count) continue;
            _panes[i].Bounds = count switch
            {
                1 => new Rectangle(0, 0, w, h),
                2 => new Rectangle(i * (w / 2), 0, i == 0 ? w / 2 : w - w / 2, h),
                _ => new Rectangle(i % 2 * (w / 2), i / 2 * (h / 2), i % 2 == 0 ? w / 2 : w - w / 2, i / 2 == 0 ? h / 2 : h - h / 2),
            };
        }
    }

    private void Fit()
    {
        var pane = _panes[0];
        var c = pane.ClientSize;
        _view.Zoom = Math.Clamp(Math.Min(Math.Max(1, c.Width - 20) / _src.W, Math.Max(1, c.Height - 110) / _src.H), 1, 32);
        _view.CX = _src.W / 2.0; _view.CY = _src.H / 2.0;
        _view.Raise();
        UpdateInfo();
    }

    private void UpdateInfo() => _info.Text = $"{_view.Zoom}x   frame {_frame + 1} of {_src.Frames.Length}";

    private void PickMatte()
    {
        if (ColourPickerDialog.Pick(this, _src.Matte, "Matte colour") is not { } c) return;
        _src.Matte = c;
        _matte.BackColor = c;
        foreach (var p in _panes) if (p.Settings.Format == OptFormat.Jpeg) p.Reencode();
    }

    private void TogglePlay()
    {
        if (_timer.Enabled) { _timer.Stop(); _play.Text = "Play"; return; }
        _play.Text = "Stop";
        _timer.Interval = Math.Max(10, _src.Delays[_frame]);
        _timer.Start();
    }

    private void Advance()
    {
        _frame = (_frame + 1) % _src.Frames.Length;
        _timer.Interval = Math.Max(10, _src.Delays[_frame]);
        foreach (var p in _panes) p.ShowFrame(_frame);
        UpdateInfo();
    }

    private void FillPresets()
    {
        _presets.Items.Clear();
        _presets.Items.Add("Presets…");
        foreach (var name in ExportPresets.Load().Keys) _presets.Items.Add(name);
        _presets.SelectedIndex = 0;
    }

    /// <summary>The pane presets go to and come from: the one last clicked, if showing.</summary>
    private OptPane ActivePane => _panes[_active < PaneCount ? _active : 0];

    private void ApplyPreset(string name)
    {
        if (ExportPresets.Load().TryGetValue(name, out var s) && _mode != 0) ActivePane.SetSettings(s);
    }

    private void SavePreset()
    {
        if (_mode == 0) { MessageBox.Show(this, "Choose Preview, 2-Up or 4-Up first: the Original pane has no settings.", "Save preset"); return; }
        using var dlg = new RenameDialog("My preset") { Text = "Save export preset" };
        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.Value)) return;
        ExportPresets.Save(dlg.Value.Trim(), ActivePane.Settings);
        FillPresets();
        _presets.SelectedItem = dlg.Value.Trim();
    }

    // ---------- test access ----------
    internal void TestApplyPreset(string name) => ApplyPreset(name);
    internal void TestSetActive(int pane) => _active = pane;
    internal OptPane Pane(int i) => _panes[i];
    internal bool Idle => _panes.Take(PaneCount).All(p => !p.Busy);
    internal void TestMode(int mode) { _modes[mode].Checked = true; SetMode(mode); }
    internal SharedView View => _view;
    internal void TestAdvance() => Advance();
}

/// <summary>Ask for a target file size in KB; 0 when cancelled.</summary>
internal static class TargetDialog
{
    public static int Ask(IWin32Window owner, int def)
    {
        using var f = new Form
        {
            Text = "Optimize to size", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(280, 90), ShowInTaskbar = false,
        };
        var n = new NumericUpDown { Minimum = 1, Maximum = 1_000_000, Value = Math.Clamp(def, 1, 1_000_000), Left = 150, Top = 14, Width = 90, AccessibleName = "Target size in KB" };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 70, Top = 52 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 152, Top = 52 };
        f.Controls.AddRange(new Control[] { new Label { Text = "Largest file size (KB)", Left = 14, Top = 18, AutoSize = true }, n, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? (int)n.Value : 0;
    }
}

/// <summary>
/// The colour table (Fireworks' Optimize panel swatches): lock a colour,
/// edit it, make it transparent, load or save the palette, remove unused.
/// Works on a copy of the settings; OK hands them back.
/// </summary>
internal sealed class ColourTableDialog : Form
{
    private readonly Func<GifOptions, IndexedFrames> _build;
    private readonly Panel _grid = new DoubleBufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(60, 62, 68) };
    private readonly Label _info = new() { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
    private readonly CheckBox _removeUnused = new() { Text = "Remove unused colours", AutoSize = true };
    private IndexedFrames? _ix;
    private int _sel = -1;
    private const int Cell = 20, PerRow = 16;

    private sealed class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() { DoubleBuffered = true; } }

    public GifOptions Options { get; private set; }

    public ColourTableDialog(GifOptions start, Func<GifOptions, IndexedFrames> build)
    {
        Options = start.Clone();
        _build = build;
        Text = "Colour table";
        ClientSize = new Size(PerRow * Cell + 200, 16 * Cell + 60);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 190, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(6) };
        void B(string t, Action a, string name) { var b = new Button { Text = t, Width = 170, AccessibleName = name }; b.Click += (_, _) => a(); side.Controls.Add(b); }
        B("Lock / unlock", ToggleLock, "Lock or unlock the selected colour");
        B("Edit colour…", EditColour, "Edit the selected colour");
        B("Transparent on / off", ToggleTransparent, "Make the selected colour transparent");
        B("Clear all edits", () => { Options.Locked.Clear(); Options.Edits.Clear(); Options.Transparent.Clear(); Rebuild(); }, "Clear locks, edits and transparency");
        B("Load palette…", LoadPalette, "Load a palette file");
        B("Save palette…", SavePalette, "Save the palette to a file");
        _removeUnused.Checked = Options.RemoveUnused;
        _removeUnused.CheckedChanged += (_, _) => { Options.RemoveUnused = _removeUnused.Checked; Rebuild(); };
        side.Controls.Add(_removeUnused);
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        side.Controls.Add(ok); side.Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(_grid); Controls.Add(_info); Controls.Add(side);
        _grid.Paint += PaintGrid;
        _grid.MouseDown += (_, e) =>
        {
            int col = e.X / Cell, i = (e.Y - _grid.AutoScrollPosition.Y) / Cell * PerRow + col;
            if (col < PerRow && _ix != null && i < _ix.Palette.Count) { _sel = i; ShowInfo(); _grid.Invalidate(); }
        };
        _grid.MouseDoubleClick += (_, _) => EditColour();
        Rebuild();
    }

    private int SelSource => _ix != null && _sel >= 0 && _sel < _ix.Source.Count ? _ix.Source[_sel] : -1;

    private void Rebuild()
    {
        int keep = SelSource;
        try { _ix = _build(Options); }
        catch (Exception ex) { _ix = null; _info.Text = ex.Message; _grid.Invalidate(); return; }
        _sel = keep < 0 ? -1 : _ix.Source.IndexOf(keep);
        _grid.AutoScrollMinSize = new Size(PerRow * Cell, (_ix.Palette.Count + PerRow - 1) / PerRow * Cell);
        ShowInfo();
        _grid.Invalidate();
    }

    private void ShowInfo()
    {
        if (_ix == null) return;
        string t = $"{_ix.Palette.Count} colours";
        if (_sel >= 0)
        {
            int src = SelSource;
            t += src < 0 ? "   selected: the transparent slot" :
                 $"   selected #{src:X6}" + (Options.Locked.Contains(src) ? ", locked" : "") +
                 (Options.Edits.TryGetValue(src, out int e) ? $", edited to #{e:X6}" : "") +
                 (Options.Transparent.Contains(src) ? ", transparent" : "");
        }
        _info.Text = t;
    }

    private void PaintGrid(object? sender, PaintEventArgs e)
    {
        if (_ix == null) return;
        var g = e.Graphics;
        g.TranslateTransform(_grid.AutoScrollPosition.X, _grid.AutoScrollPosition.Y);
        for (int i = 0; i < _ix.Palette.Count; i++)
        {
            var r = new Rectangle(i % PerRow * Cell + 1, i / PerRow * Cell + 1, Cell - 2, Cell - 2);
            int src = i < _ix.Source.Count ? _ix.Source[i] : 0;
            if (src < 0 || Options.Transparent.Contains(src))
            {
                g.FillRectangle(Brushes.White, r);
                g.FillRectangle(Brushes.LightGray, r.X, r.Y, r.Width / 2, r.Height / 2);
                g.FillRectangle(Brushes.LightGray, r.X + r.Width / 2, r.Y + r.Height / 2, r.Width - r.Width / 2, r.Height - r.Height / 2);
            }
            else
            {
                using var b = new SolidBrush(Color.FromArgb(_ix.Palette.R[i], _ix.Palette.G[i], _ix.Palette.B[i]));
                g.FillRectangle(b, r);
            }
            if (src >= 0 && Options.Locked.Contains(src)) g.FillRectangle(Brushes.Black, r.Right - 6, r.Bottom - 6, 6, 6);
            if (src >= 0 && Options.Edits.ContainsKey(src)) g.FillEllipse(Brushes.White, r.X + 1, r.Y + 1, 6, 6);
            if (i == _sel) { r.Inflate(1, 1); g.DrawRectangle(Pens.Yellow, r); }
        }
    }

    private void ToggleLock()
    {
        int src = SelSource;
        if (src < 0) return;
        if (!Options.Locked.Remove(src)) Options.Locked.Add(src);
        Rebuild();
    }

    private void EditColour()
    {
        int src = SelSource;
        if (src < 0 || _ix == null) return;
        var now = Color.FromArgb(_ix.Palette.R[_sel], _ix.Palette.G[_sel], _ix.Palette.B[_sel]);
        if (ColourPickerDialog.Pick(this, now, "Edit colour") is not { } c) return;
        SetEdit(src, c.R << 16 | c.G << 8 | c.B);
    }

    private void SetEdit(int src, int packed)
    {
        if (packed == src) Options.Edits.Remove(src); else Options.Edits[src] = packed;
        Rebuild();
    }

    private void ToggleTransparent()
    {
        int src = SelSource;
        if (src < 0) return;
        if (!Options.Transparent.Remove(src)) Options.Transparent.Add(src);
        Rebuild();
    }

    private void LoadPalette()
    {
        using var open = new OpenFileDialog { Title = "Load palette", Filter = PaletteFiles.Filter };
        if (open.ShowDialog(this) != DialogResult.OK) return;
        try { Options.Custom = PaletteFiles.Load(open.FileName); Options.Palette = PaletteKind.Custom; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load palette", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Rebuild();
    }

    private void SavePalette()
    {
        if (_ix == null) return;
        using var save = new SaveFileDialog { Title = "Save palette", Filter = "Adobe Colour Table (*.act)|*.act|GIMP palette (*.gpl)|*.gpl|JASC palette (*.pal)|*.pal", FileName = "palette" };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        var cols = Enumerable.Range(0, _ix.Palette.Count).Where(i => i != _ix.TransparentIndex)
            .Select(i => _ix.Palette.R[i] << 16 | _ix.Palette.G[i] << 8 | _ix.Palette.B[i]).ToList();
        PaletteFiles.Save(save.FileName, cols);
    }

    // ---------- test access ----------
    internal IndexedFrames? Table => _ix;
    internal void TestSelect(int i) { _sel = i; ShowInfo(); }
    internal void TestLock() => ToggleLock();
    internal void TestTransparent() => ToggleTransparent();
    internal void TestEdit(int packed) { if (SelSource >= 0) SetEdit(SelSource, packed); }
}
