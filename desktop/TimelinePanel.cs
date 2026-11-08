/* The timeline: ruler, animation tags, frames, and audio lanes on one time axis.
 *
 * Frame cells are as wide as their duration. That is a deliberate break from
 * Fireworks' fixed-width frame strip: audio is continuous time, and only a
 * time-proportional frame row lets a waveform line up honestly under the
 * frames it plays against. A short frame is narrow; zoom in to work on it.
 *
 * Owner-drawn. Thumbnails are cached per frame and only built for frames
 * actually on screen, so a long animation does not re-render everything.
 */
using System.Drawing.Drawing2D;

namespace Flint;

public sealed partial class TimelinePanel : Control
{
    // ---------- layout ----------
    public const int Gutter = 118, RulerH = 20, TagH = 16, FrameH = 70, LaneH = 50;
    private int FramesTop => RulerH + TagH;
    private int LanesTop => FramesTop + FrameH;

    // ---------- state the host sets ----------
    private Document? _doc;
    public int Current;
    public int SelStart, SelEnd;
    public double PlayheadMs;
    public int SelectedTrack = -1;
    public AudioClip? SelectedClip;

    /// <summary>Pixels per millisecond.</summary>
    public double PxPerMs { get; private set; } = 0.6;
    private int _scrollX;
    private readonly HScrollBar _hbar = new() { Dock = DockStyle.Bottom, Height = 16 };

    private readonly Dictionary<int, Bitmap> _thumbs = new();
    private readonly Dictionary<(int src, long inUs, long lenUs, bool loop, int w), float[]> _peaks = new();

    // ---------- events ----------
    public event Action<int, bool>? FrameClicked;            // frame, extend selection
    public event Action<double>? Seek;
    public event Action<int, int>? FrameMoved;               // from, to
    public event Action<int, int>? DelayDragging;            // frame, ms (live)
    public event Action<int, int, int>? DelayDragged;        // frame, old ms, new ms
    public event Action<int>? FrameDoubleClicked;
    public event Action<int, Point>? FrameMenu;
    public event Action<AnimTag>? TagClicked;
    public event Action? AudioEditStarted;                   // host snapshots for undo
    public event Action<string>? AudioEditEnded;             // host pushes undo with this label
    public event Action<int, AudioClip?, double, Point>? LaneMenu;
    public event Action<string[], int, double>? AudioDropped;

    // ---------- drag ----------
    private enum Drag { None, Seek, Reorder, Delay, ClipMove, ClipTrimL, ClipTrimR }
    private Drag _drag;
    private Point _down;
    private int _dragFrame, _dragDelayStart, _reorderTo = -1;
    private bool _moved;
    private AudioTrack? _dragTrack;
    private AudioClip? _dragClip;
    private double _clipStart0, _clipIn0, _clipLen0, _downMs;

    public TimelinePanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.Selectable, true);
        BackColor = Theme.Panel2;
        AllowDrop = true;
        Controls.Add(_hbar);
        _hbar.Scroll += (_, _) => { _scrollX = _hbar.Value; Invalidate(); };
    }

    public void Bind(Document doc)
    {
        if (!ReferenceEquals(doc, _doc)) InvalidateThumbnails();
        _doc = doc;
        UpdateScroll();
        Invalidate();
    }

    public void InvalidateThumbnails()
    {
        foreach (var b in _thumbs.Values) b.Dispose();
        _thumbs.Clear();
        Invalidate();
    }

    public void InvalidateWaveforms()
    {
        _peaks.Clear();
        Invalidate();
    }

    /// <summary>Height that fits every lane, for the host to size the panel.</summary>
    public int PreferredHeight => _framesView ? CellPad * 2 + CellH + _hbar.Height + 2 :
        LanesTop + Math.Max(1, _doc?.Audio.Tracks.Count ?? 0) * LaneH + _hbar.Height + 2;

    // ---------- coordinates ----------

    private int[] Delays => _doc!.Frames.Select(f => Math.Max(1, f.Delay)).ToArray();

    private double TotalMs => _doc == null ? 0 : Math.Max(Playback.TotalMs(Delays), _doc.Audio.EndMs);

    private int X(double ms) => Gutter + (int)Math.Round(ms * PxPerMs) - _scrollX;
    private double Ms(int x) => (x - Gutter + _scrollX) / PxPerMs;

    private Rectangle FrameRect(int i)
    {
        var d = Delays;
        int x0 = X(Playback.FrameStart(d, i));
        int x1 = X(Playback.FrameStart(d, i + 1));
        return new Rectangle(x0, FramesTop, Math.Max(1, x1 - x0), FrameH);
    }

    private int FrameAtX(int x)
    {
        if (_doc == null || x < Gutter) return -1;
        double ms = Ms(x);
        if (ms < 0 || ms >= Playback.TotalMs(Delays)) return -1;
        return Playback.FrameAt(Delays, (int)ms);
    }

    private int LaneAtY(int y)
    {
        if (_doc == null || y < LanesTop) return -1;
        int i = (y - LanesTop) / LaneH;
        return i < _doc.Audio.Tracks.Count ? i : -1;
    }

    private Rectangle ClipRect(int lane, AudioClip c)
    {
        int x0 = X(c.StartMs), x1 = X(c.EndMs);
        return new Rectangle(x0, LanesTop + lane * LaneH + 4, Math.Max(2, x1 - x0), LaneH - 8);
    }

    private void UpdateScroll()
    {
        if (_framesView) { FramesUpdateScroll(); return; }
        int content = (int)Math.Ceiling(TotalMs * PxPerMs) + 200;
        int view = Math.Max(1, ClientSize.Width - Gutter);
        _hbar.Maximum = Math.Max(0, content);
        _hbar.LargeChange = view;
        _hbar.SmallChange = 40;
        _hbar.Enabled = content > view;
        _scrollX = Math.Clamp(_scrollX, 0, Math.Max(0, content - view));
        _hbar.Value = Math.Min(_scrollX, Math.Max(0, _hbar.Maximum));
    }

    public void SetScale(double pxPerMs, int anchorX)
    {
        double anchorMs = Ms(anchorX);
        PxPerMs = Math.Clamp(pxPerMs, 0.03, 8);
        _scrollX = Math.Max(0, (int)Math.Round(anchorMs * PxPerMs) - (anchorX - Gutter));
        InvalidateWaveforms();
        UpdateScroll();
        Invalidate();
    }

    /// <summary>Scroll so a time is visible, e.g. to follow the playhead.</summary>
    public void Reveal(double ms)
    {
        if (_framesView) { FramesReveal(ms); return; }
        int x = X(ms);
        int right = ClientSize.Width - 20;
        if (x < Gutter + 10) _scrollX = Math.Max(0, (int)(ms * PxPerMs) - 20);
        else if (x > right) _scrollX = (int)(ms * PxPerMs) - (right - Gutter) + 60;
        else return;
        UpdateScroll();
        Invalidate();
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScroll(); }

    // ---------- painting ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_doc == null) return;
        if (_framesView) { PaintFramesView(g); return; }

        int bottom = ClientSize.Height - _hbar.Height;
        g.SetClip(new Rectangle(Gutter, 0, ClientSize.Width - Gutter, bottom));
        PaintRuler(g);
        PaintTags(g);
        PaintFrames(g);
        PaintLanes(g, bottom);
        PaintPlayhead(g, bottom);
        g.ResetClip();
        PaintGutter(g, bottom);
    }

    private void PaintRuler(Graphics g)
    {
        using var bg = new SolidBrush(Theme.Panel3);
        g.FillRectangle(bg, 0, 0, ClientSize.Width, RulerH);
        using var tick = new Pen(Theme.Border);
        using var f = new Font(Font.FontFamily, 6.75f);
        using var ink = new SolidBrush(Theme.Muted);

        // Pick a label spacing that leaves room for the text.
        double[] steps = { 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000 };
        double step = steps.FirstOrDefault(s => s * PxPerMs >= 48, 10000);
        double ms0 = Math.Max(0, Math.Floor(Ms(Gutter) / step) * step);
        for (double ms = ms0; X(ms) < ClientSize.Width; ms += step)
        {
            int x = X(ms);
            g.DrawLine(tick, x, RulerH - 8, x, RulerH);
            string label = ms >= 1000 ? $"{ms / 1000:0.##}s" : $"{ms:0}";
            g.DrawString(label, f, ink, x + 2, 2);
            // Minor ticks.
            for (int k = 1; k < 5; k++)
            {
                int mx = X(ms + step * k / 5);
                g.DrawLine(tick, mx, RulerH - 4, mx, RulerH);
            }
        }
    }

    private void PaintTags(Graphics g)
    {
        if (_doc!.Tags.Count == 0) return;
        using var f = new Font(Font.FontFamily, 6.75f, FontStyle.Bold);
        var d = Delays;
        foreach (var t in _doc.Tags)
        {
            int a = Math.Clamp(t.Start, 0, d.Length - 1), b = Math.Clamp(t.End, a, d.Length - 1);
            int x0 = X(Playback.FrameStart(d, a)), x1 = X(Playback.FrameStart(d, b + 1));
            var r = new Rectangle(x0 + 1, RulerH + 2, Math.Max(2, x1 - x0 - 2), TagH - 4);
            using var br = new SolidBrush(t.Colour);
            g.FillRectangle(br, r);
            g.DrawString(t.Name, f, Brushes.White, r.X + 3, r.Y - 1);
        }
    }

    private void PaintFrames(Graphics g)
    {
        var d = Delays;
        using var selFill = new SolidBrush(Color.FromArgb(55, Theme.Accent));
        using var curPen = new Pen(Theme.Accent, 2);
        using var line = new Pen(Theme.Border);
        using var f = new Font(Font.FontFamily, 6.75f);
        using var ink = new SolidBrush(Theme.Muted);
        int lo = Math.Min(SelStart, SelEnd), hi = Math.Max(SelStart, SelEnd);

        for (int i = 0; i < d.Length; i++)
        {
            var r = FrameRect(i);
            if (r.Right < Gutter || r.Left > ClientSize.Width) continue;

            using (var cell = new SolidBrush(Theme.Panel3)) g.FillRectangle(cell, r);
            if (i >= lo && i <= hi && hi > lo) g.FillRectangle(selFill, r);

            // Thumbnail, letterboxed into whatever width the duration gives.
            var thumb = Thumb(i);
            if (thumb != null)
            {
                int th = FrameH - 26;
                float s = Math.Min((r.Width - 4) / (float)thumb.Width, th / (float)thumb.Height);
                if (s > 0)
                {
                    int tw = Math.Max(1, (int)(thumb.Width * s)), tht = Math.Max(1, (int)(thumb.Height * s));
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(thumb, r.X + (r.Width - tw) / 2, r.Y + 3 + (th - tht) / 2, tw, tht);
                }
            }

            g.DrawRectangle(line, r.X, r.Y, r.Width, r.Height - 1);
            if (r.Width > 26)
            {
                string name = _doc!.Frames[i].Name;
                string label = r.Width > 70 && name.Length > 0 ? $"{i + 1} {name}" : $"{i + 1}";
                g.DrawString(label, f, ink, r.X + 2, r.Bottom - 23);
                g.DrawString($"{d[i]}ms", f, ink, r.X + 2, r.Bottom - 13);
            }
            if (i == Current) g.DrawRectangle(curPen, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 3);
        }

        if (_drag == Drag.Reorder && _moved && _reorderTo >= 0)
        {
            int x = _reorderTo >= d.Length ? FrameRect(d.Length - 1).Right : FrameRect(_reorderTo).Left;
            using var mark = new Pen(Theme.Accent, 3);
            g.DrawLine(mark, x, FramesTop, x, FramesTop + FrameH);
        }
    }

    private Bitmap? Thumb(int i)
    {
        if (_thumbs.TryGetValue(i, out var b)) return b;
        try
        {
            using var flat = _doc!.RenderFrame(i);
            // Cap the cached size; the thumbnail never draws bigger than this.
            int h = Math.Max(FrameH - 22, 64);
            float s = Math.Min(1f, Math.Min(h * 3f / flat.Width, h / (float)flat.Height));
            int w = Math.Max(1, (int)(flat.Width * s)), hh = Math.Max(1, (int)(flat.Height * s));
            b = ImageOps.Scale(flat, w, hh, false);
            _thumbs[i] = b;
            return b;
        }
        catch { return null; }
    }

    private void PaintLanes(Graphics g, int bottom)
    {
        var a = _doc!.Audio;
        using var laneBg = new SolidBrush(Theme.Panel2);
        using var laneSel = new SolidBrush(Theme.AccentSoft(Theme.Panel2, 0.1f));
        using var sep = new Pen(Theme.Border);
        using var f = new Font(Font.FontFamily, 6.75f);
        bool anySolo = a.Tracks.Any(t => t.Solo);

        for (int lane = 0; lane < a.Tracks.Count; lane++)
        {
            var t = a.Tracks[lane];
            int y = LanesTop + lane * LaneH;
            g.FillRectangle(lane == SelectedTrack ? laneSel : laneBg, Gutter, y, ClientSize.Width, LaneH);
            g.DrawLine(sep, Gutter, y + LaneH - 1, ClientSize.Width, y + LaneH - 1);
            bool audible = !t.Muted && (!anySolo || t.Solo);

            var baseCol = t.Kind switch
            {
                TrackKind.Dialogue => Theme.AccentDark,
                TrackKind.Effects => Color.FromArgb(214, 120, 52),
                _ => Color.FromArgb(120, 92, 196),
            };
            if (!audible) baseCol = Color.FromArgb(160, 164, 172);

            foreach (var c in t.Clips)
            {
                var r = ClipRect(lane, c);
                if (r.Right < Gutter || r.Left > ClientSize.Width) continue;
                using var fill = new SolidBrush(Color.FromArgb(200, baseCol));
                g.FillRectangle(fill, r);

                if (a.Sources.TryGetValue(c.SourceId, out var src))
                    PaintWave(g, src, c, r);
                else
                    g.DrawString("missing audio", f, Brushes.White, r.X + 3, r.Y + 3);

                // Fade ramps.
                using var ramp = new Pen(Color.FromArgb(220, 255, 255, 255));
                if (c.FadeInMs > 0) g.DrawLine(ramp, r.X, r.Bottom, X(c.StartMs + c.FadeInMs), r.Y);
                if (c.FadeOutMs > 0) g.DrawLine(ramp, X(c.EndMs - c.FadeOutMs), r.Y, r.Right, r.Bottom);

                using var edge = new Pen(ReferenceEquals(c, SelectedClip) ? Color.White : Color.FromArgb(90, 0, 0, 0),
                                         ReferenceEquals(c, SelectedClip) ? 2 : 1);
                g.DrawRectangle(edge, r);
                if (r.Width > 40) g.DrawString(c.Name, f, Brushes.White, r.X + 3, r.Y + 1);
            }
        }
    }

    private void PaintWave(Graphics g, AudioSource src, AudioClip c, Rectangle r)
    {
        int w = Math.Max(1, r.Width);
        // Cached per exact slice of the source and pixel width.
        var key = (src.Id, (long)(c.InMs * 1000), (long)(c.LengthMs * 1000), c.Loop, w);
        if (!_peaks.TryGetValue(key, out var peaks))
        {
            if (c.Loop)
            {
                peaks = new float[w];
                var one = AudioMixer.Peaks(src, 0, src.DurationMs, Math.Max(1, (int)(src.DurationMs * PxPerMs)));
                for (int i = 0; i < w; i++) peaks[i] = one.Length == 0 ? 0 : one[(int)((c.InMs * PxPerMs + i) % one.Length)];
            }
            else peaks = AudioMixer.Peaks(src, c.InMs, c.LengthMs, w);
            _peaks[key] = peaks;
        }
        using var pen = new Pen(Color.FromArgb(235, 255, 255, 255));
        int mid = r.Y + r.Height / 2, half = r.Height / 2 - 2;
        float gain = Math.Min(1f, c.Gain);
        for (int i = 0; i < peaks.Length; i++)
        {
            int h = (int)(peaks[i] * gain * half);
            if (h > 0) g.DrawLine(pen, r.X + i, mid - h, r.X + i, mid + h);
        }
    }

    private void PaintPlayhead(Graphics g, int bottom)
    {
        int x = X(PlayheadMs);
        using var pen = new Pen(Theme.Danger, 1);
        g.DrawLine(pen, x, 0, x, bottom);
        var tri = new[] { new Point(x - 5, 0), new Point(x + 6, 0), new Point(x, 7) };
        using var br = new SolidBrush(Theme.Danger);
        g.FillPolygon(br, tri);
    }

    private void PaintGutter(Graphics g, int bottom)
    {
        using var bg = new SolidBrush(Theme.Panel);
        g.FillRectangle(bg, 0, 0, Gutter, bottom);
        using var sep = new Pen(Theme.Border);
        g.DrawLine(sep, Gutter - 1, 0, Gutter - 1, bottom);
        using var f = new Font(Font.FontFamily, 7.5f);
        using var small = new Font(Font.FontFamily, 6.75f, FontStyle.Bold);
        using var ink = new SolidBrush(Theme.Text);

        g.DrawString($"{PlayheadMs / 1000.0:0.000}s", f, ink, 6, 3);
        if (_doc!.Tags.Count > 0) g.DrawString("Tags", f, ink, 6, RulerH + 1);
        g.DrawString("Frames", f, ink, 6, FramesTop + 4);

        var a = _doc.Audio;
        for (int lane = 0; lane < a.Tracks.Count; lane++)
        {
            var t = a.Tracks[lane];
            int y = LanesTop + lane * LaneH;
            g.DrawLine(sep, 0, y, Gutter, y);
            string name = t.Name.Length > 14 ? t.Name[..14] + "…" : t.Name;
            g.DrawString(name, f, ink, 6, y + 4);
            DrawToggle(g, MuteRect(lane), "M", t.Muted, Color.FromArgb(210, 70, 60), small);
            DrawToggle(g, SoloRect(lane), "S", t.Solo, Color.FromArgb(220, 170, 30), small);
            g.DrawString($"{t.Volume * 100:0}%", small, ink, 54, y + 27);
        }
    }

    private static Rectangle MuteRect(int lane) => new(6, LanesTopStatic + lane * LaneH + 24, 18, 16);
    private static Rectangle SoloRect(int lane) => new(28, LanesTopStatic + lane * LaneH + 24, 18, 16);
    private static int LanesTopStatic => RulerH + TagH + FrameH;

    private static void DrawToggle(Graphics g, Rectangle r, string text, bool on, Color col, Font f)
    {
        using var fill = new SolidBrush(on ? col : Theme.Panel2);
        using var pen = new Pen(on ? col : Theme.Border);
        g.FillRectangle(fill, r);
        g.DrawRectangle(pen, r);
        using var ink = new SolidBrush(on ? Color.White : Theme.Muted);
        g.DrawString(text, f, ink, r.X + 4, r.Y + 1);
    }

    // ---------- input ----------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (_doc == null) return;
        if (_framesView) { FramesDown(e); return; }
        _down = e.Location;
        _moved = false;
        _drag = Drag.None;

        // Gutter toggles.
        if (e.X < Gutter)
        {
            for (int ln = 0; ln < _doc.Audio.Tracks.Count; ln++)
            {
                var t = _doc.Audio.Tracks[ln];
                if (MuteRect(ln).Contains(e.Location)) { AudioEditStarted?.Invoke(); t.Muted = !t.Muted; AudioEditEnded?.Invoke("mute"); Invalidate(); return; }
                if (SoloRect(ln).Contains(e.Location)) { AudioEditStarted?.Invoke(); t.Solo = !t.Solo; AudioEditEnded?.Invoke("solo"); Invalidate(); return; }
            }
            int l2 = LaneAtY(e.Y);
            if (l2 >= 0) { SelectedTrack = l2; SelectedClip = null; Invalidate(); }
            return;
        }

        if (e.Y < RulerH)
        {
            _drag = Drag.Seek;
            Seek?.Invoke(Math.Max(0, Ms(e.X)));
            return;
        }

        if (e.Y < FramesTop)
        {
            int f = FrameAtX(e.X);
            var tag = _doc.Tags.FirstOrDefault(t => f >= t.Start && f <= t.End);
            if (tag != null) TagClicked?.Invoke(tag);
            return;
        }

        if (e.Y < LanesTop)
        {
            int f = FrameAtX(e.X);
            if (f < 0) return;
            if (e.Button == MouseButtons.Right) { FrameMenu?.Invoke(f, PointToScreen(e.Location)); return; }

            var r = FrameRect(f);
            if (Math.Abs(e.X - r.Right) <= 4)
            {
                _drag = Drag.Delay;
                _dragFrame = f;
                _dragDelayStart = _doc.Frames[f].Delay;
                return;
            }
            _drag = Drag.Reorder;
            _dragFrame = f;
            FrameClicked?.Invoke(f, (ModifierKeys & Keys.Shift) != 0);
            return;
        }

        int lane = LaneAtY(e.Y);
        if (lane < 0) return;
        SelectedTrack = lane;
        var track = _doc.Audio.Tracks[lane];
        _downMs = Ms(e.X);
        AudioClip? hit = null;
        for (int i = track.Clips.Count - 1; i >= 0; i--)
            if (ClipRect(lane, track.Clips[i]).Contains(e.Location)) { hit = track.Clips[i]; break; }
        SelectedClip = hit;

        if (e.Button == MouseButtons.Right)
        {
            Invalidate();
            LaneMenu?.Invoke(lane, hit, _downMs, PointToScreen(e.Location));
            return;
        }

        if (hit == null)
        {
            _drag = Drag.Seek;
            Seek?.Invoke(Math.Max(0, _downMs));
            Invalidate();
            return;
        }

        var cr = ClipRect(lane, hit);
        _dragTrack = track;
        _dragClip = hit;
        _clipStart0 = hit.StartMs; _clipIn0 = hit.InMs; _clipLen0 = hit.LengthMs;
        _drag = e.X - cr.Left <= 5 ? Drag.ClipTrimL : cr.Right - e.X <= 5 ? Drag.ClipTrimR : Drag.ClipMove;
        AudioEditStarted?.Invoke();
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_doc == null) return;
        if (_framesView) { FramesMove(e); return; }
        if (_drag == Drag.None) { UpdateCursor(e); return; }
        if (Math.Abs(e.X - _down.X) > 3 || Math.Abs(e.Y - _down.Y) > 3) _moved = true;

        switch (_drag)
        {
            case Drag.Seek:
                Seek?.Invoke(Math.Max(0, Ms(e.X)));
                break;

            case Drag.Delay:
            {
                double start = Playback.FrameStart(Delays, _dragFrame);
                int ms = (int)Math.Round(Ms(e.X) - start);
                ms = Math.Clamp((int)(Math.Round(ms / 10.0) * 10), 10, 60000);   // whole 10 ms, like GIF
                if (ms != _doc.Frames[_dragFrame].Delay)
                {
                    _doc.Frames[_dragFrame].Delay = ms;
                    DelayDragging?.Invoke(_dragFrame, ms);
                    UpdateScroll();
                    Invalidate();
                }
                break;
            }

            case Drag.Reorder:
                if (!_moved) break;
                {
                    int f = FrameAtX(e.X);
                    if (f < 0) f = Ms(e.X) <= 0 ? 0 : _doc.FrameCount;
                    else if (e.X > FrameRect(f).Left + FrameRect(f).Width / 2) f++;
                    _reorderTo = f;
                    Invalidate();
                }
                break;

            case Drag.ClipMove when _dragClip != null:
            {
                double ms = _clipStart0 + (Ms(e.X) - _downMs);
                if ((ModifierKeys & Keys.Alt) == 0) ms = SnapToFrame(ms);
                _dragClip.StartMs = Math.Max(0, ms);
                Invalidate();
                break;
            }

            case Drag.ClipTrimL when _dragClip != null:
            {
                double delta = Ms(e.X) - _downMs;
                if ((ModifierKeys & Keys.Alt) == 0) delta = SnapToFrame(_clipStart0 + delta) - _clipStart0;
                // Cannot trim before the start of the source, or past the end.
                delta = Math.Clamp(delta, -_clipIn0, _clipLen0 - 10);
                if (_clipStart0 + delta < 0) delta = -_clipStart0;
                _dragClip.StartMs = _clipStart0 + delta;
                _dragClip.InMs = _clipIn0 + delta;
                _dragClip.LengthMs = _clipLen0 - delta;
                _peaks.Clear();
                Invalidate();
                break;
            }

            case Drag.ClipTrimR when _dragClip != null:
            {
                double end = _clipStart0 + _clipLen0 + (Ms(e.X) - _downMs);
                if ((ModifierKeys & Keys.Alt) == 0) end = SnapToFrame(end);
                double len = end - _clipStart0;
                double max = double.MaxValue;
                if (!_dragClip.Loop && _doc.Audio.Sources.TryGetValue(_dragClip.SourceId, out var src))
                    max = src.DurationMs - _dragClip.InMs;
                _dragClip.LengthMs = Math.Clamp(len, 10, max);
                _peaks.Clear();
                Invalidate();
                break;
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var was = _drag;
        _drag = Drag.None;
        if (_doc == null) return;

        switch (was)
        {
            case Drag.Delay:
                if (_doc.Frames[_dragFrame].Delay != _dragDelayStart)
                    DelayDragged?.Invoke(_dragFrame, _dragDelayStart, _doc.Frames[_dragFrame].Delay);
                break;
            case Drag.Reorder:
                if (_moved && _reorderTo >= 0)
                {
                    int to = _reorderTo > _dragFrame ? _reorderTo - 1 : _reorderTo;
                    if (to != _dragFrame) FrameMoved?.Invoke(_dragFrame, to);
                }
                _reorderTo = -1;
                break;
            case Drag.ClipMove:
            case Drag.ClipTrimL:
            case Drag.ClipTrimR:
                AudioEditEnded?.Invoke(was == Drag.ClipMove ? "move clip" : "trim clip");
                break;
        }
        _dragClip = null;
        _dragTrack = null;
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (_framesView) { FramesDoubleClick(e); return; }
        if (_doc == null || e.Y < FramesTop || e.Y >= LanesTop) return;
        int f = FrameAtX(e.X);
        if (f >= 0) FrameDoubleClicked?.Invoke(f);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_framesView) { FramesWheel(e); return; }
        if ((ModifierKeys & Keys.Control) != 0)
        {
            SetScale(PxPerMs * (e.Delta > 0 ? 1.25 : 0.8), e.X);
            return;
        }
        _scrollX = Math.Max(0, _scrollX - e.Delta / 2);
        UpdateScroll();
        Invalidate();
    }

    private void UpdateCursor(MouseEventArgs e)
    {
        if (_framesView) { FramesCursor(e); return; }
        Cursor c = Cursors.Default;
        if (_doc != null && e.Y >= FramesTop && e.Y < LanesTop)
        {
            int f = FrameAtX(e.X);
            if (f >= 0 && Math.Abs(e.X - FrameRect(f).Right) <= 4) c = Cursors.SizeWE;
        }
        else if (_doc != null && LaneAtY(e.Y) is int lane and >= 0)
        {
            foreach (var clip in _doc.Audio.Tracks[lane].Clips)
            {
                var r = ClipRect(lane, clip);
                if (!r.Contains(e.Location)) continue;
                c = e.X - r.Left <= 5 || r.Right - e.X <= 5 ? Cursors.SizeWE : Cursors.SizeAll;
            }
        }
        if (Cursor != c) Cursor = c;
    }

    /// <summary>Snap to the nearest frame start within 6 px, so dialogue lands on a frame.</summary>
    private double SnapToFrame(double ms)
    {
        var d = Delays;
        double best = ms, bestPx = 6;
        for (int i = 0; i <= d.Length; i++)
        {
            double t = Playback.FrameStart(d, i);
            double px = Math.Abs(t - ms) * PxPerMs;
            if (px < bestPx) { bestPx = px; best = t; }
        }
        return best;
    }

    // ---------- drop ----------

    protected override void OnDragEnter(DragEventArgs e)
    {
        e.Effect = e.Data?.GetData(DataFormats.FileDrop) is string[] f && f.Any(AudioIO.IsAudio)
            ? DragDropEffects.Copy : DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
        var audio = files.Where(AudioIO.IsAudio).ToArray();
        if (audio.Length == 0) return;
        var p = PointToClient(new Point(e.X, e.Y));
        AudioDropped?.Invoke(audio, LaneAtY(p.Y), Math.Max(0, SnapToFrame(Ms(p.X))));
    }
}
