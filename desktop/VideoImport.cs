/* Import a video (MP4, WebM, MOV, MKV, AVI) as frames through ffmpeg, at a
 * chosen frame rate and time range. VP9 WebM with alpha keeps its
 * transparency. Written for Flint. */
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Flint;

public static class VideoImport
{
    public static readonly string[] Extensions = { ".mp4", ".webm", ".mov", ".mkv", ".avi", ".m4v" };
    public static bool IsVideo(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public sealed class Info
    {
        public int Width, Height;
        public double Seconds, Fps;
        public bool Alpha;
        public string Codec = "";
    }

    /// <summary>Size, length, frame rate and codec, read with ffprobe.</summary>
    public static Info Probe(string path)
    {
        var ff = WebmExport.Find() ?? throw new InvalidOperationException("Video import needs ffmpeg (winget install Gyan.FFmpeg).");
        string probe = Path.Combine(Path.GetDirectoryName(ff)!, "ffprobe.exe");
        if (!File.Exists(probe)) throw new InvalidOperationException("ffprobe.exe was not found beside ffmpeg.exe.");
        var psi = new ProcessStartInfo(probe,
            $"-v error -select_streams v:0 -show_entries stream=width,height,codec_name,pix_fmt,avg_frame_rate,r_frame_rate:stream_tags=alpha_mode,rotate:stream_side_data=rotation:format=duration -of json \"{path}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        p.StandardError.ReadToEndAsync();
        var read = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(30_000)) { try { p.Kill(true); } catch { } throw new InvalidOperationException("ffprobe did not answer within 30 s"); }
        string json = read.Result;
        var root = JsonNode.Parse(json);
        var s = root?["streams"]?.AsArray().FirstOrDefault() ?? throw new InvalidDataException("no video picture in this file");
        double Rate(string? r)
        {
            if (r == null) return 0;
            var parts = r.Split('/');
            return parts.Length == 2 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out double a) && double.TryParse(parts[1], CultureInfo.InvariantCulture, out double b) && b > 0 ? a / b : 0;
        }
        double fps = Rate(s["avg_frame_rate"]?.GetValue<string>());
        if (fps <= 0 || fps > 240) fps = Rate(s["r_frame_rate"]?.GetValue<string>());
        double.TryParse(root?["format"]?["duration"]?.GetValue<string>(), CultureInfo.InvariantCulture, out double secs);
        string pix = s["pix_fmt"]?.GetValue<string>() ?? "";
        // ffmpeg turns a phone video upright, so a quarter turn swaps the size frames come out at.
        double rot = 0;
        if (s["side_data_list"] is JsonArray sd)
            foreach (var d in sd) if (d?["rotation"] is { } rv) rot = rv.GetValue<double>();
        if (rot == 0 && double.TryParse(s["tags"]?["rotate"]?.GetValue<string>(), CultureInfo.InvariantCulture, out double tr)) rot = tr;
        bool quarter = Math.Abs(Math.Round(rot / 90)) % 2 == 1;
        int sw = s["width"]?.GetValue<int>() ?? 0, sh = s["height"]?.GetValue<int>() ?? 0;
        return new Info
        {
            Width = quarter ? sh : sw, Height = quarter ? sw : sh,
            Codec = s["codec_name"]?.GetValue<string>() ?? "", Fps = fps <= 0 || fps > 240 ? 30 : fps, Seconds = secs,
            Alpha = s["tags"]?["alpha_mode"]?.GetValue<string>() == "1" ||
                    pix.StartsWith("yuva") || pix.StartsWith("rgba") || pix.StartsWith("bgra") || pix.StartsWith("argb") || pix.StartsWith("abgr") || pix.StartsWith("ya") || pix.StartsWith("gbrap") || pix == "pal8",
        };
    }

    /// <summary>Most frames one import may hold (about 2 GB of pixels).</summary>
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Frames from <paramref name="startSec"/> for <paramref name="lengthSec"/>
    /// (0 = to the end) at <paramref name="fps"/>. Each frame lasts 1000/fps ms.
    /// </summary>
    public static ImportedFile Load(string path, double fps, double startSec, double lengthSec,
                                    CancellationToken ct = default, IProgress<(double, string)>? progress = null, Info? known = null)
    {
        var info = known ?? Probe(path);
        int w = info.Width, h = info.Height;
        if (w <= 0 || h <= 0) throw new InvalidDataException("the video has no picture size");
        fps = Math.Clamp(fps, 0.1, 120);
        double len = lengthSec > 0 ? lengthSec : Math.Max(0, info.Seconds - startSec);
        long expected = (long)Math.Ceiling(len * fps) * w * h * 4;
        if (expected > MaxBytes)
            throw new InvalidOperationException($"that would be about {expected / 1_000_000_000.0:0.0} GB of frames. Choose a lower frame rate or a shorter range.");

        var ff = WebmExport.Find()!;
        // libvpx-vp9 as decoder is what brings VP9 alpha through; ffmpeg's own VP9 decoder drops it.
        string dec = info.Alpha && info.Codec == "vp9" ? "-c:v libvpx-vp9 " : info.Alpha && info.Codec == "vp8" ? "-c:v libvpx " : "";
        string range = (startSec > 0 ? $"-ss {startSec.ToString("0.###", CultureInfo.InvariantCulture)} " : "") +
                       (lengthSec > 0 ? $"-t {lengthSec.ToString("0.###", CultureInfo.InvariantCulture)} " : "");
        var psi = new ProcessStartInfo(ff,
            $"-v error -nostdin {range}{dec}-i \"{path}\" -an -vf fps={fps.ToString("0.###", CultureInfo.InvariantCulture)} -f rawvideo -pix_fmt bgra -")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        var file = new ImportedFile { Path = path, Format = info.Codec.ToUpperInvariant() + " video" };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("could not start ffmpeg");
        var err = proc.StandardError.ReadToEndAsync();
        var stdout = proc.StandardOutput.BaseStream;
        // Cancel must work while ffmpeg is still seeking and has sent nothing yet.
        using var stop = ct.Register(() => { try { proc.Kill(true); } catch { } });
        int frameBytes = w * h * 4, delay = Math.Max(1, (int)Math.Round(1000 / fps));
        int total = Math.Max(1, (int)Math.Ceiling(len * fps));
        try
        {
            var buf = new byte[frameBytes];
            while (true)
            {
                int got = 0, k;
                while (got < frameBytes && (k = stdout.Read(buf, got, frameBytes - got)) > 0)
                {
                    got += k;
                    if (ct.IsCancellationRequested) { try { proc.Kill(true); } catch { } ct.ThrowIfCancellationRequested(); }
                }
                if (got < frameBytes) break;
                if ((long)(file.Frames.Count + 1) * frameBytes > MaxBytes) throw new InvalidOperationException("too many frames; choose a lower frame rate or a shorter range");
                if (!info.Alpha) for (int i = 3; i < frameBytes; i += 4) buf[i] = 255;
                file.Frames.Add((ImageOps.FromPixels(w, h, buf), delay));
                progress?.Report((Math.Min(1, file.Frames.Count / (double)total), $"frame {file.Frames.Count} of about {total}"));
            }
            ct.ThrowIfCancellationRequested();
            proc.WaitForExit(30_000);
            if (proc.HasExited && proc.ExitCode != 0)
                throw new InvalidDataException("the video could not be read to the end: " + string.Join(" | ", err.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(2)).Trim());
            if (file.Frames.Count == 0)
                throw new InvalidDataException("no frames came out: " + string.Join(" | ", err.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(2)).Trim());
            return file;
        }
        catch
        {
            file.Dispose();
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            throw;
        }
    }
}

/// <summary>Frame rate and time range before a video import.</summary>
internal static class VideoImportDialog
{
    /// <summary>(fps, start, length) or null when cancelled. Headless runs take the video's own rate, whole length.</summary>
    public static (double fps, double start, double length)? Ask(IWin32Window owner, string path, VideoImport.Info info)
    {
        double fps0 = Math.Min(Math.Round(info.Fps, 3), 60);
        if (Program.Headless) return (fps0, 0, 0);
        using var f = new Form
        {
            Text = "Import video: " + Path.GetFileName(path), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(380, 200), ShowInTaskbar = false,
        };
        f.Controls.Add(new Label { Text = $"{info.Width}×{info.Height}, {info.Seconds:0.##} s at {info.Fps:0.##} frames a second", Left = 14, Top = 12, AutoSize = true });
        NumericUpDown Num(string label, int top, decimal value, decimal max, int places, string name)
        {
            f.Controls.Add(new Label { Text = label, Left = 14, Top = top + 3, AutoSize = true });
            var n = new NumericUpDown { Left = 200, Top = top, Width = 90, DecimalPlaces = places, Minimum = 0, Maximum = max, Value = Math.Min(max, value), AccessibleName = name };
            f.Controls.Add(n);
            return n;
        }
        var fps = Num("Frames a second", 44, (decimal)fps0, 120, 2, "Frames a second");
        fps.Minimum = 0.1m;
        var start = Num("Start at (seconds)", 76, 0, 100_000, 2, "Start time in seconds");
        var len = Num("Length (seconds, 0 = to end)", 108, 0, 100_000, 2, "Length in seconds");
        var note = new Label { Left = 14, Top = 140, AutoSize = true, ForeColor = SystemColors.GrayText };
        void Update()
        {
            double l = (double)len.Value > 0 ? (double)len.Value : Math.Max(0, info.Seconds - (double)start.Value);
            long n = (long)Math.Ceiling(l * (double)fps.Value);
            note.Text = $"about {n} frames, {n * info.Width * info.Height * 4 / 1_000_000.0:0} MB";
        }
        fps.ValueChanged += (_, _) => Update(); start.ValueChanged += (_, _) => Update(); len.ValueChanged += (_, _) => Update();
        Update();
        f.Controls.Add(note);
        var ok = new Button { Text = "Import", DialogResult = DialogResult.OK, Left = 190, Top = 164, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 280, Top = 164, Width = 80 };
        f.Controls.AddRange(new Control[] { ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? ((double)fps.Value, (double)start.Value, (double)len.Value) : null;
    }
}
