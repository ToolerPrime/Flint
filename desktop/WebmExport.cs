/* WebM (VP9) export by driving ffmpeg.
 *
 * VP9 is not something to hand-roll, and ffmpeg is the standard encoder, so
 * this shells out. ffmpeg is NOT bundled: Find() looks for it and the caller
 * reports its absence rather than failing silently.
 *
 * Per-frame delays are preserved exactly using the concat demuxer, which takes
 * an explicit duration per image. A fixed -framerate would quantise every
 * frame to one interval and lose the timing.
 */
using System.Diagnostics;
using System.Globalization;
using System.Drawing.Imaging;
using System.Text;

namespace Flint;

public static class WebmExport
{
    /// <summary>Path to an ffmpeg we can use, or null.</summary>
    public static string? Find()
    {
        var candidates = new List<string>();

        // Next to our own exe wins, so a bundled copy can be dropped in.
        string? dir = Path.GetDirectoryName(Environment.ProcessPath);
        if (dir != null) candidates.Add(Path.Combine(dir, "ffmpeg.exe"));

        // The process PATH, plus the User PATH read fresh. winget edits the
        // User PATH, and an already-running process keeps the copy it started
        // with, so without the second read a just-installed ffmpeg is invisible
        // until the machine is logged out and back in.
        var paths = new List<string>();
        paths.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'));
        try
        {
            paths.AddRange((Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "")
                .Split(';'));
        }
        catch { /* no registry access; the process PATH still applies */ }

        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            try { candidates.Add(Path.Combine(p.Trim(), "ffmpeg.exe")); } catch { }
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe"));
        candidates.Add(@"C:\ffmpeg\bin\ffmpeg.exe");
        candidates.Add(@"C:\Program Files\ffmpeg\bin\ffmpeg.exe");

        foreach (var c in candidates)
        {
            try { if (File.Exists(c)) return c; } catch { }
        }

        // Last resort: winget unpacks into a versioned folder under Packages,
        // so the exact path moves with every ffmpeg release.
        try
        {
            string pkgs = Path.Combine(local, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(pkgs))
            {
                foreach (var dirPath in Directory.EnumerateDirectories(pkgs, "*FFmpeg*"))
                {
                    var hit = Directory
                        .EnumerateFiles(dirPath, "ffmpeg.exe", SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (hit != null) return hit;
                }
            }
        }
        catch { /* nothing more to try */ }

        return null;
    }

    private static int Gcd(int a, int b)
    {
        a = Math.Abs(a); b = Math.Abs(b);
        while (b != 0) { (a, b) = (b, a % b); }
        return a == 0 ? 1 : a;
    }

    public sealed class Result
    {
        public bool Ok;
        public string Message = "";
        public long Bytes;
    }

    public enum Kind { WebM, Mp4, WebP }

    /// <summary>Back-compatible entry point used by the existing tests.</summary>
    public static Result Encode(IList<Frame> frames, string outPath, int quality, bool loopHint,
                                float[]? audio = null, bool mp4 = false, Color? matte = null) =>
        Encode(frames, outPath, mp4 ? Kind.Mp4 : Kind.WebM, quality, loopHint, audio, matte, default);

    /// <summary>
    /// Encode frames (and for video, optionally a 48 kHz stereo mix) with ffmpeg.
    /// </summary>
    /// <param name="quality">WebM/MP4: CRF, lower is better (30 is sane).
    /// WebP: 0 means lossless, otherwise 1-100 lossy quality.</param>
    /// <param name="audio">Interleaved stereo float covering exactly the
    /// animation's duration, or null for silence. Ignored for WebP.</param>
    /// <param name="matte">MP4 has no alpha; transparency is flattened onto this.</param>
    public static Result Encode(IList<Frame> frames, string outPath, Kind kind, int quality, bool loop,
                                float[]? audio, Color? matte, CancellationToken ct,
                                IProgress<(double, string)>? progress = null)
    {
        var ffmpeg = Find();
        if (ffmpeg == null)
            return new Result
            {
                Ok = false,
                Message = "ffmpeg not found. Install it (winget install Gyan.FFmpeg) " +
                          "or drop ffmpeg.exe beside Flint.exe, then try again.",
            };

        string work = Path.Combine(Path.GetTempPath(), "flint-video-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            for (int i = 0; i < frames.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                frames[i].Bmp.Save(Path.Combine(work, $"f{i:D5}.png"), ImageFormat.Png);
                progress?.Report((0.3 * (i + 1) / frames.Count, $"preparing frame {i + 1} of {frames.Count}"));
            }

            var list = new StringBuilder();
            for (int i = 0; i < frames.Count; i++)
            {
                list.Append("file '").Append($"f{i:D5}.png").Append("'\n");
                list.Append("duration ")
                    .Append((frames[i].Delay / 1000.0).ToString("0.####", CultureInfo.InvariantCulture))
                    .Append('\n');
            }
            string listPath = Path.Combine(work, "frames.txt");
            File.WriteAllText(listPath, list.ToString());

            // Constant frame rate at the gcd of all delays, so each source frame
            // covers a whole number of output frames and the total is exact.
            // With variable frame rate the WebM muxer gave the last frame a
            // ~30 ms tail, so it flashed past on every loop.
            int step = frames[0].Delay;
            foreach (var f in frames) step = Gcd(step, f.Delay);
            step = Math.Clamp(step, 10, 1000);          // cap at 100 fps
            string rate = $"1000/{step}";

            int totalMs = frames.Sum(f => Math.Max(1, f.Delay));
            // Hard cap on output length: whatever the filters do, ffmpeg cannot
            // write past the animation's real duration.
            string cap = "-t " + (totalMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " ";

            string audioIn = "", audioMap = "", audioCodec = "-an ";
            if (kind != Kind.WebP && audio != null && audio.Length > 0)
            {
                string wav = Path.Combine(work, "mix.wav");
                using (var fs = File.Create(wav)) AudioIO.WriteFloatWav(fs, audio);
                audioIn = $"-i \"{wav}\" ";
                audioMap = "-map 1:a:0 ";
                // Opus for WebM (needs 48 kHz, our internal rate); AAC for MP4.
                audioCodec = kind == Kind.Mp4 ? "-c:a aac -b:a 192k " : "-c:a libopus -b:a 160k ";
            }

            string video;
            switch (kind)
            {
                case Kind.Mp4:
                {
                    // H.264 has no alpha: flatten onto a finite matte sized to
                    // the frames (an unbounded colour source once made ffmpeg
                    // write hours of video), and pad to even dimensions.
                    var m = matte ?? Color.Black;
                    int w = frames[0].Bmp.Width, h = frames[0].Bmp.Height;
                    video =
                        $"-filter_complex \"color=c=0x{m.R:X2}{m.G:X2}{m.B:X2}:s={w}x{h}:r={rate}[bg];" +
                        "[bg][0:v]overlay=shortest=1:format=auto," +
                        "pad=ceil(iw/2)*2:ceil(ih/2)*2,format=yuv420p[v]\" -map \"[v]\" " +
                        $"-c:v libx264 -crf {Math.Clamp(quality - 12, 0, 51)} -preset medium -movflags +faststart ";
                    break;
                }
                case Kind.WebP:
                    // Lossless keeps every pixel; -loop 0 repeats forever.
                    video = "-map 0:v:0 -c:v libwebp_anim " +
                            (quality <= 0 ? "-lossless 1 -pix_fmt bgra " : $"-lossless 0 -quality {Math.Clamp(quality, 1, 100)} -pix_fmt yuva420p ") +
                            $"-loop {(loop ? 0 : 1)} ";
                    break;
                default:
                    // yuva420p keeps the alpha channel.
                    video = $"-map 0:v:0 -c:v libvpx-vp9 -pix_fmt yuva420p -b:v 0 -crf {quality} -row-mt 1 ";
                    break;
            }

            string args =
                $"-y -nostats -loglevel error -f concat -safe 0 -i \"{listPath}\" {audioIn}" +
                video + audioMap + $"-r {rate} " + audioCodec + cap +
                (loop && kind == Kind.WebM ? "-metadata title=\"loop\" " : "") +
                $"\"{outPath}\"";

            progress?.Report((0.35, "encoding with ffmpeg…"));
            var (ok, message) = RunFfmpeg(ffmpeg, args, 60_000 + totalMs * 20, ct);
            if (!ok || !File.Exists(outPath))
                return new Result { Ok = false, Message = message };

            long size = new FileInfo(outPath).Length;
            progress?.Report((1.0, "done"));
            return new Result { Ok = true, Bytes = size, Message = $"wrote {Path.GetFileName(outPath)}, {size / 1024.0:0.0} KB" };
        }
        catch (OperationCanceledException)
        {
            try { File.Delete(outPath); } catch { }
            throw;
        }
        catch (Exception ex)
        {
            return new Result { Ok = false, Message = "video export failed: " + ex.Message };
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* temp dir */ }
        }
    }

    /// <summary>
    /// Run ffmpeg with a real timeout and cancellation. Stderr is read
    /// asynchronously: a synchronous read blocks until exit and makes any
    /// timeout meaningless.
    /// </summary>
    internal static (bool ok, string message) RunFfmpeg(string ffmpeg, string args, int timeoutMs, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpeg, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
        };
        using var proc = Process.Start(psi);
        if (proc == null) return (false, "could not start ffmpeg");
        var errTask = proc.StandardError.ReadToEndAsync();
        proc.StandardOutput.ReadToEndAsync();

        var timer = Stopwatch.StartNew();
        while (!proc.WaitForExit(100))
        {
            if (ct.IsCancellationRequested)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                ct.ThrowIfCancellationRequested();
            }
            if (timer.ElapsedMilliseconds > timeoutMs)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return (false, $"ffmpeg did not finish within {timeoutMs / 1000}s and was stopped");
            }
        }
        if (proc.ExitCode == 0) return (true, "");
        string tail = string.Join(" | ", errTask.Result
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(2).Select(l => l.Trim()));
        return (false, "ffmpeg failed: " + tail);
    }
}
