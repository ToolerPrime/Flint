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
    public OptFormat Format;
    public int Colours = 128;
    public bool Dither;
    public int Quality = 80;
    public OptSettings Clone() => (OptSettings)MemberwiseClone();
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
}

internal sealed class OptResult
{
    public long Bytes = -1;                  // -1 for Original
    public byte[][] Preview = Array.Empty<byte[]>();
    public string Note = "";
    public byte[]? Data;
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
        var cur = src.Frames[src.Current];
        OptResult R(byte[] data, byte[][] preview, string note) =>
            new() { Bytes = data.Length, Data = data, Preview = preview, Note = note };
        string frames = multi ? $", {n} frames" : "";

        switch (s.Format)
        {
            case OptFormat.Original:
                return new OptResult { Preview = src.Frames, Note = "exact composite" };

            case OptFormat.Gif:
            {
                var f = StillExport.Index(src.Frames, w, h, s.Colours, s.Dither, ct);
                var gf = f.Indices.Select((ix, i) => new GifFrame { Indices = ix, DelayMs = src.Delays[i] }).ToList();
                var bytes = GifEncoder.Encode(w, h, gf, f.Palette, f.TransparentIndex, src.Loop);
                return R(bytes, Enumerable.Range(0, n).Select(f.ToBgra).ToArray(), $"{f.Palette.Count} colours{frames}");
            }
            case OptFormat.Png8:
            {
                var bytes = StillExport.Png8(cur, w, h, s.Colours, s.Dither, out var pic);
                return R(bytes, new[] { pic }, "current frame");
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
    private readonly CheckBox _dither = new() { Text = "dither", AutoSize = true };
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
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        top.Controls.AddRange(new Control[] { _fmt, _lblColours, _colours, _dither, _lblQuality, _quality });
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 30 };
        bottom.Controls.Add(_size);
        bottom.Controls.Add(_export);
        Controls.Add(_canvas);
        Controls.Add(bottom);
        Controls.Add(top);
        _fmt.SelectedIndexChanged += (_, _) => { SyncControls(); Reencode(); };
        _colours.ValueChanged += (_, _) => Reencode();
        _quality.ValueChanged += (_, _) => Reencode();
        _dither.CheckedChanged += (_, _) => Reencode();
        _export.Click += (_, _) => ExportRequested?.Invoke(Settings);
        _ffmpegOk = ffmpegOk;
        SyncControls();
        _canvas.Show(src.Frames[src.Current], src.W, src.H);
    }

    private readonly Func<bool> _ffmpegOk;

    public OptSettings Settings => new()
    {
        Format = (OptFormat)_fmt.SelectedIndex, Colours = (int)_colours.Value,
        Dither = _dither.Checked, Quality = (int)_quality.Value,
    };

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
        _dither.Checked = s.Dither; _quality.Value = Math.Clamp(s.Quality, 1, 100);
        _loading = false;
        SyncControls();
        Reencode();
    }

    private void SyncControls()
    {
        var s = Settings;
        _lblColours.Visible = _colours.Visible = _dither.Visible = s.UsesColours;
        _lblQuality.Visible = _quality.Visible = s.UsesQuality;
    }

    public void ShowFrame(int frame)
    {
        _frame = frame;
        var px = _result?.PreviewFor(frame) ?? _src.Frames[frame];
        _canvas.Show(px, _src.W, _src.H);
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
    private int _mode = 2, _frame;

    public OptSettings? Chosen { get; private set; }
    public Color Matte => _src.Matte;

    public OptimizeDialog(OptSource src)
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
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        bar.Controls.AddRange(new Control[] { zo, zi, fit, _play, _matte, _info, close });

        foreach (int i in Enumerable.Range(0, 4))
        {
            _panes[i] = new OptPane(src, _view, first[i], () => WebmExport.Find() != null);
            _panes[i].ExportRequested += s => { Chosen = s; DialogResult = DialogResult.OK; Close(); };
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
        using var dlg = new ColorDialog { Color = _src.Matte, FullOpen = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _src.Matte = dlg.Color;
        _matte.BackColor = dlg.Color;
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

    // ---------- test access ----------
    internal OptPane Pane(int i) => _panes[i];
    internal bool Idle => _panes.Take(PaneCount).All(p => !p.Busy);
    internal void TestMode(int mode) { _modes[mode].Checked = true; SetMode(mode); }
    internal SharedView View => _view;
    internal void TestAdvance() => Advance();
}
