/* Video host formats and presets, checked with ffprobe and by decoding
 * frames back with ffmpeg: MOV (H.264, ProRes 4444 with alpha), MKV, AVIF,
 * whole-number nearest-neighbour upscale + letterbox, repeat / minimum
 * length, fixed frame rate, size cap, audio.
 * Flint.exe --videotest <dir> */
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Flint;

public static class VideoTest
{
    private static int _fails;
    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private static string _ffmpeg = "", _ffprobe = "";

    private static string Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        string o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(120_000);
        return o;
    }

    private static JsonNode Probe(string file) =>
        JsonNode.Parse(Run(_ffprobe, $"-v error -show_streams -show_format -of json \"{file}\""))!;

    private static JsonNode? Stream(JsonNode probe, string type) =>
        probe["streams"]!.AsArray().FirstOrDefault(s => s!["codec_type"]!.GetValue<string>() == type);

    private static double Duration(JsonNode probe) =>
        double.Parse(probe["format"]!["duration"]!.GetValue<string>(), CultureInfo.InvariantCulture);

    /// <summary>Frame n decoded to RGBA.</summary>
    private static byte[] Frame(string file, int n, int w, int h, string dir)
    {
        string raw = Path.Combine(dir, "frame.raw");
        if (File.Exists(raw)) File.Delete(raw);
        Run(_ffmpeg, $"-v error -y -i \"{file}\" -vf \"select=eq(n\\,{n})\" -frames:v 1 -f rawvideo -pix_fmt rgba \"{raw}\"");
        var b = File.Exists(raw) ? File.ReadAllBytes(raw) : Array.Empty<byte>();
        return b.Length == w * h * 4 ? b : Array.Empty<byte>();
    }

    private const int W = 24, H = 16;

    /// <summary>Pixel art: a 4-colour checker of 1-pixel cells on a clear background, 3 frames of 100 ms.</summary>
    private static Document Art(bool audio)
    {
        var d = new Document(W, H);
        for (int i = 1; i < 3; i++) d.InsertFrame(i);
        for (int f = 0; f < 3; f++)
        {
            d.Frames[f].Delay = 100;
            var px = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int o = (y * W + x) * 4;
                    if (x < 2) continue;                                   // clear columns on the left
                    var c = Colour(x + f, y);
                    px[o] = c.B; px[o + 1] = c.G; px[o + 2] = c.R; px[o + 3] = 255;
                }
            d.Layers[0].EnsureCel(f).Objects.Add(new RasterObject(ImageOps.FromPixels(W, H, px)));
        }
        if (audio)
        {
            var t = new AudioTrack { Name = "Music" };
            d.Audio.Tracks.Add(t);
            var tone = new float[AudioFormat.MsToFrames(300) * 2];
            for (int i = 0; i < tone.Length; i++) tone[i] = 0.3f * MathF.Sin(i / 2 * 0.05f);
            d.Audio.AddClip(t, d.Audio.AddSource(tone, "tone"), 0);
        }
        return d;
    }

    private static Color Colour(int x, int y) => ((x + y) & 3) switch
    {
        0 => Color.FromArgb(230, 40, 40), 1 => Color.FromArgb(40, 200, 60), 2 => Color.FromArgb(40, 60, 230), _ => Color.FromArgb(240, 220, 40),
    };

    private static string Export(Document d, ExportKind kind, string path, VideoOptions? v, Color? matte = null)
    {
        try
        {
            return Exporter.Run(new ExportJob
            {
                Doc = d, Kind = kind, Path = path, From = 0, To = d.FrameCount - 1, Quality = 30,
                Matte = matte ?? Color.White, Video = v, IncludeAudio = true, Loop = true,
            }, null, default);
        }
        catch (Exception ex) { return "FAILED: " + ex.Message; }
    }

    private static int Diff(byte[] px, int w, int x, int y, Color c)
    {
        int o = (y * w + x) * 4;
        return Math.Abs(px[o] - c.R) + Math.Abs(px[o + 1] - c.G) + Math.Abs(px[o + 2] - c.B);
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        Console.WriteLine("Video host formats");
        var ff = WebmExport.Find();
        if (ff == null) { Console.WriteLine("  skip  ffmpeg not installed: video checks skipped"); return 0; }
        _ffmpeg = ff;
        _ffprobe = Path.Combine(Path.GetDirectoryName(ff)!, "ffprobe.exe");
        if (!File.Exists(_ffprobe)) { Console.WriteLine("  skip  ffprobe not beside ffmpeg"); return 0; }

        var doc = Art(audio: true);
        string P(string n) => Path.Combine(dir, n);

        foreach (var (kind, file, codec, fmt, alpha, audio) in new[]
        {
            (ExportKind.Mov, "h264.mov", "h264", "mov", false, "aac"),
            (ExportKind.MovProRes, "prores.mov", "prores", "mov", true, "pcm_s16le"),
            (ExportKind.Mkv, "h264.mkv", "h264", "matroska", false, "aac"),
            (ExportKind.Avif, "anim.avif", "av1", "avis", false, ""),
        })
        {
            string msg = Export(doc, kind, P(file), new VideoOptions());
            if (!File.Exists(P(file))) { Check(false, $"{kind}: {msg}"); continue; }
            var pr = Probe(P(file));
            var v = Stream(pr, "video");
            string pix = v?["pix_fmt"]?.GetValue<string>() ?? "";
            Check(v?["codec_name"]?.GetValue<string>() == codec && (pr["format"]!["format_name"]!.GetValue<string>().Contains(fmt) || pr["format"]?["tags"]?["major_brand"]?.GetValue<string>() == fmt),
                  $"{kind}: {codec} in {pr["format"]!["format_name"]} (brand {pr["format"]?["tags"]?["major_brand"]}), {v?["width"]}x{v?["height"]}, {pix}");
            Check(!alpha || pix.Contains("yuva") || pix.Contains("4444"), $"{kind}: {(alpha ? "keeps an alpha channel" : "no alpha needed")} ({pix})");
            var a = Stream(pr, "audio");
            Check(audio == "" ? a == null : a?["codec_name"]?.GetValue<string>() == audio, $"{kind}: audio {(audio == "" ? "none (AVIF has no sound)" : audio)}");
            if (kind == ExportKind.Avif)
            {
                // An animated AVIF holds a still cover picture plus the animation track.
                var c = JsonNode.Parse(Run(_ffprobe, $"-v error -count_frames -show_entries stream=nb_read_frames -of json \"{P(file)}\""));
                int nf = c?["streams"]?.AsArray().Max(x => int.TryParse(x?["nb_read_frames"]?.GetValue<string>(), out int v) ? v : 0) ?? 0;
                Check(nf == 3, $"{kind}: animation track has {nf} frames (want 3)");
            }
            else
            {
                double dur = Duration(pr);
                Check(Math.Abs(dur - 0.3) < 0.05, $"{kind}: length {dur:0.###} s (want 0.3)");
            }
        }

        Console.WriteLine("Presets: whole-number nearest-neighbour upscale and letterbox");
        {
            var shorts = VideoPreset.All.First(p => p.Name.Contains("Shorts"));
            var bars = Color.FromArgb(20, 30, 200);
            var o = shorts.Options(new VideoOptions { Letterbox = bars });
            string path = P("shorts.mp4");
            string msg = Export(doc, shorts.Kind, path, o, Color.Black);
            var pr = File.Exists(path) ? Probe(path) : null;
            var v = pr == null ? null : Stream(pr, "video");
            Check(v?["width"]?.GetValue<int>() == 1080 && v["height"]?.GetValue<int>() == 1920, $"Shorts/TikTok/Reels: 1080x1920 ({msg})");
            Check(v?["r_frame_rate"]?.GetValue<string>() == "30/1", $"Shorts: 30 fps ({v?["r_frame_rate"]})");
            var (sw, sh, k) = o.ScaledSize(W, H);
            Check(k == 45 && sw == 1080 && sh == 720, $"24x16 art is scaled by a whole number: x{k} = {sw}x{sh}");
            var px = Frame(path, 0, 1080, 1920, dir);
            if (px.Length == 0) Check(false, "decoded a frame back");
            else
            {
                int top = (1920 - sh) / 2, worstCentre = 0, worstBar = 0, sharp = 0;
                for (int y = 0; y < H; y++)
                    for (int x = 2; x < W; x++)
                        worstCentre = Math.Max(worstCentre, Diff(px, 1080, x * k + k / 2, top + y * k + k / 2, Colour(x, y)));
                worstBar = Math.Max(Diff(px, 1080, 5, 5, bars), Diff(px, 1080, 1070, 1910, bars));
                // Sharp edges: 3 px either side of a cell border already show each cell's own colour.
                for (int x = 3; x < W - 1; x++)
                    if (Diff(px, 1080, x * k + k - 4, top + 5 * k + k / 2, Colour(x, 5)) < 60 && Diff(px, 1080, (x + 1) * k + 3, top + 5 * k + k / 2, Colour(x + 1, 5)) < 60) sharp++;
                Check(worstCentre < 60, $"every art pixel lands as a {k}x{k} block of its colour (worst centre error {worstCentre})");
                Check(sharp >= W - 5, $"cell edges stay sharp, no blur ({sharp} of {W - 4} borders)");
                Check(worstBar < 40, $"bars use the chosen colour (error {worstBar})");
                Check(Diff(px, 1080, k / 2, top + 4 * k, Color.Black) < 40, "clear pixels are flattened onto the background colour");
            }
        }
        {
            // ProRes 4444 with clear bars: alpha survives the upscale and the letterbox.
            var o = new VideoOptions { TargetW = 320, TargetH = 240, TransparentLetterbox = true };
            string path = P("bars.mov");
            Export(doc, ExportKind.MovProRes, path, o);
            var px = Frame(path, 1, 320, 240, dir);
            var (sw, sh, k) = o.ScaledSize(W, H);
            int top = (240 - sh) / 2, left = (320 - sw) / 2;
            Check(px.Length > 0 && px[3] == 0 && px[(5 * 320 + 160) * 4 + 3] == 0, "ProRes: clear bars stay clear");
            Check(px.Length > 0 && px[((top + 8 * k) * 320 + left + k / 2) * 4 + 3] < 10 && px[((top + 8 * k) * 320 + left + 10 * k + k / 2) * 4 + 3] > 245,
                  "ProRes: clear art pixels stay clear, solid ones solid");
        }
        {
            // A canvas bigger than the target shrinks to fit instead of cropping.
            var o = new VideoOptions { TargetW = 16, TargetH = 16 };
            var (sw, sh, k) = o.ScaledSize(W, H);
            Check(k == 0 && sw == 16 && sh <= 16, $"art larger than the target is shrunk to fit ({sw}x{sh})");
        }

        Console.WriteLine("Repeat and minimum length");
        {
            string path = P("rep.mp4");
            Export(doc, ExportKind.Mp4, path, new VideoOptions { Repeat = 3 });
            var pr = Probe(path);
            Check(Math.Abs(Duration(pr) - 0.9) < 0.05, $"repeat 3 times: {Duration(pr):0.###} s");
            var a = Stream(pr, "audio");
            Check(a != null && Math.Abs(double.Parse(a["duration"]!.GetValue<string>(), CultureInfo.InvariantCulture) - 0.9) < 0.06, "the sound repeats with the picture");
            Export(doc, ExportKind.Mp4, path, new VideoOptions { MinSeconds = 2 });
            Check(Math.Abs(Duration(Probe(path)) - 2.1) < 0.05, $"at least 2 s: {Duration(Probe(path)):0.###} s (7 whole plays)");
            Check(new VideoOptions { MinSeconds = 2, Repeat = 10 }.RepeatFor(300) == 10, "the larger of repeat and minimum length wins");
        }

        Console.WriteLine("Size cap (Discord)");
        {
            var noisy = new Document(160, 120);
            for (int i = 1; i < 30; i++) noisy.InsertFrame(i);
            var rng = new Random(3);
            for (int f = 0; f < 30; f++)
            {
                var px = new byte[160 * 120 * 4];
                rng.NextBytes(px);
                for (int i = 3; i < px.Length; i += 4) px[i] = 255;
                noisy.Layers[0].EnsureCel(f).Objects.Add(new RasterObject(ImageOps.FromPixels(160, 120, px)));
            }
            string path = P("capped.mp4");
            var o = new VideoOptions { MaxBytes = 150_000 };
            string msg = Export(noisy, ExportKind.Mp4, path, null);
            long free = File.Exists(path) ? new FileInfo(path).Length : 0;
            msg = Export(noisy, ExportKind.Mp4, path, o);
            long capped = File.Exists(path) ? new FileInfo(path).Length : 0;
            Check(free > o.MaxBytes && capped > 0 && capped <= o.MaxBytes, $"noise video: {free} B uncapped, {capped} B with a {o.MaxBytes} B cap ({msg})");
            string tiny = P("tiny.mp4");
            if (File.Exists(tiny)) File.Delete(tiny);
            msg = Export(noisy, ExportKind.Mp4, tiny, new VideoOptions { MaxBytes = 2_000 });
            Check(msg.StartsWith("FAILED") && !File.Exists(tiny), $"a cap that cannot be met fails and leaves no file ({msg})");
            var discord = VideoPreset.All.First(p => p.Name.StartsWith("Discord"));
            Check(discord.MaxBytes == 10_000_000 && discord.Kind == ExportKind.Mp4, "the Discord preset caps at 10 MB");
        }

        Console.WriteLine("Every preset produces a playable file");
        foreach (var p in VideoPreset.All.Where(p => p.W * p.H <= 1920 * 1920))
        {
            string path = P("preset." + p.Ext);
            string msg = Export(Art(false), p.Kind, path, p.Options());
            var v = File.Exists(path) ? Stream(Probe(path), "video") : null;
            Check(v?["width"]?.GetValue<int>() == p.W && v["height"]?.GetValue<int>() == p.H, $"{p.Name}: {v?["codec_name"]} {v?["width"]}x{v?["height"]}");
        }
        {
            var yt4k = VideoPreset.All.First(p => p.Name.Contains("4K"));
            string path = P("4k.mp4");
            Export(Art(false), yt4k.Kind, path, yt4k.Options());
            var v = File.Exists(path) ? Stream(Probe(path), "video") : null;
            Check(v?["width"]?.GetValue<int>() == 3840 && v["height"]?.GetValue<int>() == 2160, $"YouTube 4K: {v?["width"]}x{v?["height"]}");
        }

        Console.WriteLine(_fails == 0 ? "all video checks passed" : $"{_fails} video check(s) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
