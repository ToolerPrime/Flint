/* End-to-end WebM export check: slice a sheet, key out the background, encode,
 * then probe the result with ffprobe. Flint.exe --webmtest <sheet.png> <out.webm> */
using System.Diagnostics;

namespace Flint;

public static class WebmTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    public static int Run(string sheetPath, string outPath)
    {
        Console.WriteLine("WebM export checks");

        var ffmpeg = WebmExport.Find();
        Check(ffmpeg != null, "ffmpeg located" + (ffmpeg != null ? ": " + ffmpeg : ""));
        if (ffmpeg == null)
        {
            Console.WriteLine("1 CHECK FAILED");
            return 1;
        }

        using var loaded = Image.FromFile(sheetPath);
        var sheet = ImageOps.NewBitmap(loaded.Width, loaded.Height);
        using (var g = Graphics.FromImage(sheet)) g.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);

        const int cols = 6;
        int cw = sheet.Width / cols;
        var frames = new List<Frame>();
        for (int c = 0; c < cols; c++)
        {
            var bmp = ImageOps.Crop(sheet, new Rectangle(c * cw, 0, cw, sheet.Height));
            ImageOps.ColorKey(bmp, Color.Magenta, 20);
            frames.Add(new Frame(bmp, 90));     // 6 x 90ms = 540ms
        }
        sheet.Dispose();

        var result = WebmExport.Encode(frames, outPath, 30, true);
        Check(result.Ok, "encode reported success: " + result.Message);
        if (!result.Ok)
        {
            Console.WriteLine($"{_fails} CHECK(S) FAILED");
            return 1;
        }

        Check(File.Exists(outPath) && new FileInfo(outPath).Length > 500,
              $"file written, {new FileInfo(outPath).Length} bytes");

        string probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        if (!File.Exists(probe))
        {
            Console.WriteLine("  note  ffprobe not beside ffmpeg; skipping stream checks");
        }
        else
        {
            string json = RunTool(probe,
                $"-v quiet -print_format json -show_streams -show_format \"{outPath}\"");
            Check(json.Contains("\"codec_name\": \"vp9\""), "stream is VP9");
            Check(json.Contains("yuva420p") || json.Contains("alpha_mode"),
                  "alpha channel is present");

            string dur = RunTool(probe,
                $"-v quiet -show_entries format=duration -of default=nw=1:nk=1 \"{outPath}\"");
            if (double.TryParse(dur.Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double seconds))
            {
                // 6 frames at 90ms; concat timing should land near 0.54s.
                Check(Math.Abs(seconds - 0.54) < 0.12,
                      $"duration {seconds:0.000}s matches 6x90ms");
            }
            else Check(false, "could not read duration (" + dur.Trim() + ")");

            string count = RunTool(probe,
                $"-v quiet -count_frames -select_streams v:0 " +
                $"-show_entries stream=nb_read_frames -of default=nw=1:nk=1 \"{outPath}\"");
            Check(count.Trim() == "6", $"6 frames encoded (got {count.Trim()})");
        }

        // Mixed delays exercise the gcd path; uniform ones do not.
        if (File.Exists(probe))
        {
            int[] mixed = { 60, 60, 120, 60, 180, 60 };   // 540ms, gcd 60
            for (int i = 0; i < frames.Count; i++) frames[i].Delay = mixed[i];
            string mixedPath = Path.ChangeExtension(outPath, ".mixed.webm");
            var r2 = WebmExport.Encode(frames, mixedPath, 30, true);
            Check(r2.Ok, "mixed-delay encode succeeded");
            if (r2.Ok)
            {
                string d2 = RunTool(probe,
                    $"-v quiet -show_entries format=duration -of default=nw=1:nk=1 \"{mixedPath}\"");
                string c2 = RunTool(probe,
                    $"-v quiet -count_frames -select_streams v:0 " +
                    $"-show_entries stream=nb_read_frames -of default=nw=1:nk=1 \"{mixedPath}\"");
                bool okDur = double.TryParse(d2.Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double sec2)
                    && Math.Abs(sec2 - 0.54) < 0.02;
                Check(okDur, $"mixed delays total 0.540s (got {d2.Trim()})");
                // 540ms at the 60ms gcd is 9 output frames.
                Check(c2.Trim() == "9", $"mixed delays give 9 CFR frames (got {c2.Trim()})");
            }
        }

        foreach (var f in frames) f.Bmp.Dispose();
        Console.WriteLine(_fails == 0 ? "all WebM checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    private static string RunTool(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p == null) return "";
        string outText = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(60_000);
        return outText;
    }
}
