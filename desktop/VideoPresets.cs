/* Video export options and presets for video hosts (YouTube, Shorts /
 * TikTok / Reels, X, Discord, Vimeo). Pixel art is scaled up by a whole
 * number with nearest neighbour and letterboxed to the host's size, so the
 * host's own scaler does not blur it. Written for Flint. */
namespace Flint;

public sealed class VideoOptions
{
    /// <summary>Output size; 0 = keep the canvas size (padded to even).</summary>
    public int TargetW, TargetH;
    public Color Letterbox = Color.Black;
    /// <summary>For formats with alpha (WebM, ProRes 4444): leave the bars clear.</summary>
    public bool TransparentLetterbox;
    /// <summary>Play the animation this many times in the file.</summary>
    public int Repeat = 1;
    /// <summary>Repeat until the file is at least this long (hosts dislike 2-second videos).</summary>
    public double MinSeconds;
    /// <summary>Output frame rate; 0 = the exact rate the frame delays need.</summary>
    public int Fps;
    /// <summary>Re-encode at a lower bit rate until the file is under this size; 0 = no cap.</summary>
    public long MaxBytes;

    public int RepeatFor(int totalMs)
    {
        int n = Math.Max(1, Repeat);
        if (MinSeconds > 0 && totalMs > 0) n = Math.Max(n, (int)Math.Ceiling(MinSeconds * 1000 / totalMs - 1e-9));
        return Math.Min(n, 10_000);
    }

    /// <summary>The picture's size inside the target, and the whole-number factor (0 = shrunk to fit).</summary>
    public (int w, int h, int k) ScaledSize(int w, int h)
    {
        int k = Math.Min(TargetW / Math.Max(1, w), TargetH / Math.Max(1, h));
        if (k >= 1) return (w * k, h * k, k);
        double s = Math.Min(TargetW / (double)w, TargetH / (double)h);
        return (Math.Max(1, (int)(w * s)), Math.Max(1, (int)(h * s)), 0);
    }

    public VideoOptions Clone() => (VideoOptions)MemberwiseClone();
}

public sealed record VideoPreset(string Name, ExportKind Kind, int W, int H, int Fps, long MaxBytes, string Ext)
{
    public static readonly VideoPreset[] All =
    {
        new("YouTube 1080p (MP4)", ExportKind.Mp4, 1920, 1080, 0, 0, "mp4"),
        new("YouTube 4K (MP4)", ExportKind.Mp4, 3840, 2160, 0, 0, "mp4"),
        new("YouTube Shorts / TikTok / Instagram Reels, 1080x1920 (MP4)", ExportKind.Mp4, 1080, 1920, 30, 0, "mp4"),
        new("X / Twitter, 1280x720 (MP4)", ExportKind.Mp4, 1280, 720, 30, 0, "mp4"),
        new("Discord, under 10 MB (MP4)", ExportKind.Mp4, 1280, 720, 0, 10_000_000, "mp4"),
        new("Vimeo 1080p (ProRes 4444 MOV)", ExportKind.MovProRes, 1920, 1080, 0, 0, "mov"),
    };

    /// <summary>Options for this preset; the letterbox and repeat choices come from the user.</summary>
    public VideoOptions Options(VideoOptions? user = null)
    {
        var o = user?.Clone() ?? new VideoOptions();
        o.TargetW = W; o.TargetH = H; o.Fps = Fps; o.MaxBytes = MaxBytes;
        return o;
    }
}

/// <summary>Letterbox colour, repeat count and minimum length before a video export.</summary>
internal static class VideoOptionsDialog
{
    /// <summary>Null when cancelled. Headless runs and tests take the defaults.</summary>
    public static VideoOptions? Ask(IWin32Window owner, VideoPreset? preset, bool alpha, Color background, int totalMs)
    {
        var o = new VideoOptions { Letterbox = background };
        if (Program.Headless || (owner as MainForm)?.SuppressPrompts == true) return preset?.Options(o) ?? o;
        using var f = new Form
        {
            Text = preset?.Name ?? "Video options", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(380, 230), ShowInTaskbar = false,
        };
        int y = 14;
        Label L(string t) { var l = new Label { Text = t, Left = 14, Top = y + 3, AutoSize = true }; f.Controls.Add(l); return l; }
        if (preset != null)
        {
            L($"Size {preset.W}x{preset.H}. Pixel art is scaled up by a whole number (sharp pixels), then bars fill the rest.");
            f.Controls[^1].MaximumSize = new Size(350, 0);
            y += 40;
        }
        L("Bar colour");
        var colour = new Button { Left = 150, Top = y, Width = 60, BackColor = background, AccessibleName = "Letterbox bar colour" };
        colour.Click += (_, _) => { if (ColourPickerDialog.Pick(f, colour.BackColor, "Bar colour") is { } c) colour.BackColor = c; };
        f.Controls.Add(colour);
        var clear = new CheckBox { Text = "Clear bars", Left = 222, Top = y + 2, AutoSize = true, Visible = alpha };
        f.Controls.Add(clear);
        y += 34;
        L("Play the animation");
        var rep = new NumericUpDown { Left = 150, Top = y, Width = 60, Minimum = 1, Maximum = 1000, Value = 1, AccessibleName = "Times to repeat" };
        f.Controls.Add(rep);
        f.Controls.Add(new Label { Text = "times", Left = 216, Top = y + 3, AutoSize = true });
        y += 34;
        L("At least (seconds)");
        var min = new NumericUpDown { Left = 150, Top = y, Width = 60, Minimum = 0, Maximum = 3600, Value = preset != null && totalMs < 5000 ? 10 : 0, AccessibleName = "Minimum length in seconds" };
        f.Controls.Add(min);
        f.Controls.Add(new Label { Text = $"(one play is {totalMs / 1000.0:0.##} s)", Left = 216, Top = y + 3, AutoSize = true });
        y += 40;
        var ok = new Button { Text = "Export", DialogResult = DialogResult.OK, Left = 190, Top = y, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 280, Top = y, Width = 80 };
        f.Controls.AddRange(new Control[] { ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        f.ClientSize = new Size(380, y + 40);
        if (f.ShowDialog(owner) != DialogResult.OK) return null;
        o.Letterbox = colour.BackColor; o.TransparentLetterbox = alpha && clear.Checked;
        o.Repeat = (int)rep.Value; o.MinSeconds = (double)min.Value;
        return preset?.Options(o) ?? o;
    }
}
