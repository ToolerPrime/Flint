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

    public enum Kind { WebM, Mp4, WebP, Mov, MovProRes, Mkv, Avif }

    /// <summary>Kinds that keep transparency (the others are flattened onto the matte).</summary>
    public static bool KeepsAlpha(Kind k) => k is Kind.WebM or Kind.WebP or Kind.MovProRes;

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
                                IProgress<(double, string)>? progress = null, VideoOptions? opt = null)
    {
        opt ??= new VideoOptions();
        int repeat = opt.RepeatFor(frames.Sum(f => Math.Max(1, f.Delay)));
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
            for (int r = 0; r < repeat; r++)
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
            string outRate = opt.Fps > 0 ? opt.Fps.ToString(CultureInfo.InvariantCulture) : rate;

            long totalMs = (long)frames.Sum(f => Math.Max(1, f.Delay)) * repeat;
            // Hard cap on output length: whatever the filters do, ffmpeg cannot
            // write past the animation's real duration.
            string cap = "-t " + (totalMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " ";

            string audioIn = "", audioMap = "", audioCodec = "-an ";
            if (kind != Kind.WebP && audio != null && audio.Length > 0)
            {
                string wav = Path.Combine(work, "mix.wav");
                using (var fs = File.Create(wav)) AudioIO.WriteFloatWav(fs, audio);
                // The sound repeats with the picture; the -t cap trims the last pass.
                audioIn = (repeat > 1 ? $"-stream_loop {repeat - 1} " : "") + $"-i \"{wav}\" ";
                audioMap = "-map 1:a:0 ";
                // Opus for WebM (needs 48 kHz, our internal rate); AAC for MP4.
                audioCodec = kind switch
                {
                    Kind.WebM => "-c:a libopus -b:a 160k ",
                    Kind.MovProRes => "-c:a pcm_s16le ",
                    Kind.Avif => "-an ",
                    _ => "-c:a aac -b:a 192k ",
                };
                if (kind == Kind.Avif) { audioIn = ""; audioMap = ""; }
            }

            string video;
            int sw = frames[0].Bmp.Width, sh = frames[0].Bmp.Height;
            if (opt.TargetW > 0 || kind is Kind.Mov or Kind.MovProRes or Kind.Mkv or Kind.Avif)
            {
                video = VideoArgs(kind, sw, sh, rate, quality, matte ?? Color.Black, opt, loop);
            }
            else switch (kind)
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

            string Args(string v) =>
                $"-y -nostats -loglevel error -f concat -safe 0 -i \"{listPath}\" {audioIn}" +
                v + audioMap + $"-r {outRate} " + audioCodec + cap +
                (loop && kind == Kind.WebM ? "-metadata title=\"loop\" " : "") +
                $"\"{outPath}\"";

            progress?.Report((0.35, "encoding with ffmpeg…"));
            int timeout = (int)Math.Min(int.MaxValue, 60_000L + totalMs * 20L + ((long)opt.TargetW * opt.TargetH > 2_000_000 ? totalMs * 60L : 0));
            var (ok, message) = RunFfmpeg(ffmpeg, Args(video), timeout, ct);
            if (!ok || !File.Exists(outPath))
                return new Result { Ok = false, Message = message };

            long size = new FileInfo(outPath).Length;
            // A size cap (Discord): re-encode at the bit rate that fits, a little lower each try.
            for (int attempt = 0; opt.MaxBytes > 0 && size > opt.MaxBytes && attempt < 4; attempt++)
            {
                double secs = totalMs / 1000.0;
                double audioKbps = audioIn.Length > 0 ? 192 : 0;
                double kbps = opt.MaxBytes * 8 / 1000.0 / secs * (0.92 - attempt * 0.12) - audioKbps;
                if (kbps < 50) { try { File.Delete(outPath); } catch { } return new Result { Ok = false, Message = $"cannot fit {opt.MaxBytes / 1_000_000.0:0.#} MB: the video is too long for that size" }; }
                progress?.Report((0.6 + attempt * 0.1, $"too big ({size / 1_000_000.0:0.0} MB); re-encoding at {kbps:0} kbit/s"));
                string capped = System.Text.RegularExpressions.Regex.Replace(video, @"-crf \d+ ", "") +
                                $"-b:v {(int)kbps}k -maxrate {(int)(kbps * 1.2)}k -bufsize {(int)(kbps * 2)}k ";
                (ok, message) = RunFfmpeg(ffmpeg, Args(capped), timeout, ct);
                if (!ok || !File.Exists(outPath)) return new Result { Ok = false, Message = message };
                size = new FileInfo(outPath).Length;
            }
            if (opt.MaxBytes > 0 && size > opt.MaxBytes)
            {
                try { File.Delete(outPath); } catch { }
                return new Result { Ok = false, Message = $"could not get under {opt.MaxBytes / 1_000_000.0:0.#} MB" };
            }
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
    /// Video filter and codec arguments for the host-friendly path: flatten
    /// onto the matte when the codec has no alpha, scale pixel art up by a
    /// whole number with nearest neighbour (so hosts do not blur it), pad to
    /// the target size with the letterbox colour, then pick the codec.
    /// </summary>
    internal static string VideoArgs(Kind kind, int w, int h, string rate, int quality, Color matte, VideoOptions opt, bool loop = true)
    {
        bool alpha = KeepsAlpha(kind);
        var f = new StringBuilder("-filter_complex \"");
        if (!alpha)
            f.Append($"color=c=0x{matte.R:X2}{matte.G:X2}{matte.B:X2}:s={w}x{h}:r={rate}[bg];[bg][0:v]overlay=shortest=1:format=auto");
        else f.Append("[0:v]null");
        if (opt.TargetW > 0 && opt.TargetH > 0)
        {
            var (sw, sh, k) = opt.ScaledSize(w, h);
            f.Append(k >= 1 ? $",scale={sw}:{sh}:flags=neighbor" : $",scale={sw}:{sh}:flags=area");
            var c = opt.Letterbox;
            string col = $"0x{c.R:X2}{c.G:X2}{c.B:X2}" + (alpha && opt.TransparentLetterbox ? "@0" : "");
            f.Append($",pad={opt.TargetW}:{opt.TargetH}:(ow-iw)/2:(oh-ih)/2:color={col}");
        }
        if (kind != Kind.MovProRes) f.Append(",pad=ceil(iw/2)*2:ceil(ih/2)*2");
        string pix = kind switch { Kind.MovProRes => "yuva444p10le", Kind.WebM or Kind.WebP => "yuva420p", _ => "yuv420p" };
        f.Append($",format={pix}[v]\" -map \"[v]\" ");
        int crf264 = Math.Clamp(quality - 12, 0, 51);
        f.Append(kind switch
        {
            Kind.MovProRes => "-c:v prores_ks -profile:v 4444 -vendor apl0 ",
            Kind.WebM => $"-c:v libvpx-vp9 -b:v 0 -crf {quality} -row-mt 1 ",
            Kind.WebP => "-c:v libwebp_anim -lossless 1 -loop 0 ",
            Kind.Avif => $"-c:v libaom-av1 -crf {Math.Clamp(quality, 0, 63)} -cpu-used 6 -row-mt 1 -loop {(loop ? 0 : 1)} ",
            _ => $"-c:v libx264 -crf {crf264} -preset medium -profile:v high " + (kind is Kind.Mp4 or Kind.Mov ? "-movflags +faststart " : ""),
        });
        return f.ToString();
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
