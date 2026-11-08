/* Checks for the mixer, decoding, project storage, and audio sync in exported
 * video. Flint.exe --audiotest <scratchdir>
 *
 * The sync checks do not trust our own code: they encode, then decode the
 * file with ffmpeg and find where a click actually lands in time. */
using System.Diagnostics;
using System.Globalization;

namespace Flint;

public static class AudioTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    /// <summary>Stereo tone of the given length.</summary>
    private static float[] Tone(double ms, double hz = 440, float amp = 0.5f)
    {
        long n = AudioFormat.MsToFrames(ms);
        var s = new float[n * 2];
        for (long i = 0; i < n; i++)
        {
            float v = amp * (float)Math.Sin(2 * Math.PI * hz * i / AudioFormat.Rate);
            s[i * 2] = v; s[i * 2 + 1] = v;
        }
        return s;
    }

    private static float[] Constant(double ms, float v)
    {
        var s = new float[AudioFormat.MsToFrames(ms) * 2];
        Array.Fill(s, v);
        return s;
    }

    /// <summary>First stereo frame whose magnitude exceeds the threshold.</summary>
    private static long Onset(float[] s, float threshold = 0.05f)
    {
        for (long i = 0; i < s.Length / 2; i++)
            if (Math.Abs(s[i * 2]) > threshold || Math.Abs(s[i * 2 + 1]) > threshold) return i;
        return -1;
    }

    public static int Run(string scratch)
    {
        Directory.CreateDirectory(scratch);
        Console.WriteLine("Mixer");

        {
            var p = new AudioProject();
            var t = new AudioTrack();
            p.Tracks.Add(t);
            var src = p.AddSource(Constant(200, 0.5f), "dc");
            p.AddClip(t, src, 500);
            var mix = AudioMixer.Mix(p, 0, 1000);
            long onset = Onset(mix);
            Check(onset == AudioFormat.MsToFrames(500),
                  $"clip at 500ms starts on sample {AudioFormat.MsToFrames(500)} (got {onset})");
            Check(Math.Abs(mix[AudioFormat.MsToFrames(600) * 2] - 0.5f) < 1e-6, "clip plays at its level");
            Check(mix[AudioFormat.MsToFrames(750) * 2] == 0f, "silence after the clip ends at 700ms");

            // A window that starts inside the clip sees the right offset.
            var late = AudioMixer.Mix(p, 600, 100);
            Check(Math.Abs(late[0] - 0.5f) < 1e-6, "mixing from mid-clip starts inside it");
        }

        {
            // Trim: InMs skips into the source.
            var p = new AudioProject();
            var t = new AudioTrack();
            p.Tracks.Add(t);
            var ramp = new float[AudioFormat.MsToFrames(100) * 2];
            for (int i = 0; i < ramp.Length / 2; i++) ramp[i * 2] = ramp[i * 2 + 1] = i / (float)(ramp.Length / 2);
            var src = p.AddSource(ramp, "ramp");
            var c = p.AddClip(t, src, 0);
            c.InMs = 50; c.LengthMs = 50;
            var mix = AudioMixer.Mix(p, 0, 50);
            Check(Math.Abs(mix[0] - 0.5f) < 0.001f, $"left trim of 50ms starts halfway into the source (got {mix[0]:0.000})");
            Check(Math.Abs(mix[^2] - ramp[^2]) < 0.001f, "trimmed clip ends where the source ends");
        }

        {
            var p = new AudioProject();
            var a = new AudioTrack { Name = "A" };
            var b = new AudioTrack { Name = "B" };
            p.Tracks.Add(a); p.Tracks.Add(b);
            p.AddClip(a, p.AddSource(Constant(100, 0.2f), "a"), 0);
            p.AddClip(b, p.AddSource(Constant(100, 0.3f), "b"), 0);

            Check(Math.Abs(AudioMixer.Mix(p, 0, 10)[0] - 0.5f) < 1e-6, "two tracks sum");
            a.Muted = true;
            Check(Math.Abs(AudioMixer.Mix(p, 0, 10)[0] - 0.3f) < 1e-6, "mute removes a track");
            a.Muted = false; a.Solo = true;
            Check(Math.Abs(AudioMixer.Mix(p, 0, 10)[0] - 0.2f) < 1e-6, "solo silences the others");
            a.Solo = false; b.Volume = 0.5f;
            Check(Math.Abs(AudioMixer.Mix(p, 0, 10)[0] - 0.35f) < 1e-6, "track volume scales");
            p.Tracks[0].Clips[0].Gain = 2f; b.Volume = 1f;
            Check(Math.Abs(AudioMixer.Mix(p, 0, 10)[0] - 0.7f) < 1e-6, "clip gain scales");
        }

        {
            var p = new AudioProject();
            var t = new AudioTrack();
            p.Tracks.Add(t);
            var c = p.AddClip(t, p.AddSource(Constant(1000, 0.8f), "dc"), 0);
            c.FadeInMs = 100; c.FadeOutMs = 100;
            var mix = AudioMixer.Mix(p, 0, 1000);
            Check(mix[0] == 0f, "fade-in starts silent");
            Check(Math.Abs(mix[AudioFormat.MsToFrames(50) * 2] - 0.4f) < 0.01f, "fade-in is halfway at 50ms");
            Check(Math.Abs(mix[AudioFormat.MsToFrames(500) * 2] - 0.8f) < 1e-6, "full level between fades");
            Check(Math.Abs(mix[AudioFormat.MsToFrames(950) * 2] - 0.4f) < 0.01f, "fade-out is halfway 50ms from the end");
        }

        {
            // Splitting must not change what you hear.
            var p = new AudioProject();
            var t = new AudioTrack();
            p.Tracks.Add(t);
            var c = p.AddClip(t, p.AddSource(Tone(400), "tone"), 100);
            var before = AudioMixer.Mix(p, 0, 600);
            var right = p.Split(t, c, 333);
            var after = AudioMixer.Mix(p, 0, 600);
            Check(right != null && t.Clips.Count == 2, "split makes two clips");
            Check(before.AsSpan().SequenceEqual(after), "a split clip sounds identical to the original");
        }

        {
            var p = new AudioProject();
            var t = new AudioTrack();
            p.Tracks.Add(t);
            var c = p.AddClip(t, p.AddSource(Constant(100, 0.5f), "loop"), 0);
            c.Loop = true; c.LengthMs = 350;
            var mix = AudioMixer.Mix(p, 0, 400);
            Check(Math.Abs(mix[AudioFormat.MsToFrames(250) * 2] - 0.5f) < 1e-6, "a looped clip keeps playing past its source");
            Check(mix[AudioFormat.MsToFrames(375) * 2] == 0f, "a looped clip still stops at its length");
        }

        {
            // A player reads in blocks; the blocks must join seamlessly.
            var p = new AudioProject();
            var t = new AudioTrack();
            p.Tracks.Add(t);
            p.AddClip(t, p.AddSource(Tone(900, 523), "tone"), 37.3);
            var whole = AudioMixer.MixFrames(p, 0, 48000);
            var joined = new List<float>();
            for (long at = 0; at < 48000; at += 441)   // odd block size on purpose
                joined.AddRange(AudioMixer.MixFrames(p, at, Math.Min(441, 48000 - at)));
            Check(joined.ToArray().AsSpan().SequenceEqual(whole), "block-by-block mixing equals one mix, sample for sample");
        }

        {
            var src = new AudioSource { Samples = Tone(1000) };
            var peaks = AudioMixer.Peaks(src, 0, 1000, 50);
            Check(peaks.Length == 50 && peaks.All(v => v > 0.4f && v <= 0.5001f), "waveform peaks match the tone");
        }

        {
            // A silent project costs nothing.
            var d = new Document(32, 32);
            Check(d.Audio.IsEmpty && d.Audio.Bytes == 0, "a document without audio holds no audio data");
            d.Dispose();
        }

        Console.WriteLine("Decoding");
        var ffmpeg = WebmExport.Find();

        {
            // A WAV written by Python's wave module, an independent writer.
            string wav = Path.Combine(scratch, "py16.wav");
            RunPython($@"
import wave, struct, math
w = wave.open(r'{wav}', 'wb'); w.setnchannels(1); w.setsampwidth(2); w.setframerate(44100)
w.writeframes(b''.join(struct.pack('<h', int(16000*math.sin(2*math.pi*1000*i/44100))) for i in range(44100)))
w.close()");
            var samples = AudioIO.Decode(wav);
            double ms = AudioFormat.FramesToMs(samples.Length / 2);
            Check(Math.Abs(ms - 1000) < 2, $"44.1kHz mono WAV resampled to 48kHz stereo, 1000ms (got {ms:0.0})");
            Check(Math.Abs(samples[0] - samples[1]) < 1e-6, "mono is copied to both channels");
            float peak = samples.Max(Math.Abs);
            Check(Math.Abs(peak - 16000 / 32768f) < 0.02f, $"level survives decoding (peak {peak:0.000})");
        }

        if (ffmpeg != null)
        {
            foreach (var (ext, codec) in new[] { ("mp3", "libmp3lame"), ("ogg", "libvorbis"), ("flac", "flac") })
            {
                string path = Path.Combine(scratch, "tone." + ext);
                RunTool(ffmpeg, $"-y -v error -f lavfi -i sine=frequency=440:duration=1.5 -c:a {codec} \"{path}\"");
                try
                {
                    var s = AudioIO.Decode(path);
                    double ms = AudioFormat.FramesToMs(s.Length / 2);
                    // MP3 pads with encoder delay; allow a little.
                    Check(Math.Abs(ms - 1500) < 60, $"{ext.ToUpper()} decodes to ~1500ms (got {ms:0})");
                }
                catch (Exception ex) { Check(false, $"{ext.ToUpper()} decode: {ex.Message}"); }
            }
        }
        else Console.WriteLine("  skip  MP3/OGG/FLAC decode: ffmpeg not installed");

        Console.WriteLine("Project storage");

        {
            var d = new Document(16, 16);
            var t = new AudioTrack { Name = "Dialogue", Kind = TrackKind.Dialogue, Volume = 0.8f };
            var fx = new AudioTrack { Name = "SFX", Kind = TrackKind.Effects, Muted = true };
            d.Audio.Tracks.Add(t); d.Audio.Tracks.Add(fx);
            var noise = new float[AudioFormat.MsToFrames(300) * 2];
            var rng = new Random(7);
            for (int i = 0; i < noise.Length; i++) noise[i] = (float)(rng.NextDouble() * 2 - 1);
            var src = d.Audio.AddSource(noise, "take 1", @"C:\mic\take1.wav");
            var c = d.Audio.AddClip(t, src, 125.5);
            c.InMs = 20; c.LengthMs = 250; c.Gain = 1.3f; c.FadeInMs = 10; c.FadeOutMs = 30;

            string path = Path.Combine(scratch, "audio.flint");
            FlintFormat.Save(d, path);
            var r = FlintFormat.Load(path);
            Check(r.Ok, "project with audio loads: " + (r.Error ?? "ok"));
            if (r.Ok)
            {
                var a = r.Document!.Audio;
                Check(a.Tracks.Count == 2 && a.Tracks[0].Name == "Dialogue" && a.Tracks[1].Muted,
                      "tracks, names and mute survive");
                Check(Math.Abs(a.Tracks[0].Volume - 0.8f) < 1e-6, "track volume survives");
                var lc = a.Tracks[0].Clips[0];
                Check(lc.StartMs == 125.5 && lc.InMs == 20 && lc.LengthMs == 250, "clip position and trim survive");
                Check(Math.Abs(lc.Gain - 1.3f) < 1e-6 && lc.FadeInMs == 10 && lc.FadeOutMs == 30, "gain and fades survive");
                var ls = a.Sources[lc.SourceId];
                Check(ls.Samples.AsSpan().SequenceEqual(noise), "recorded audio survives bit-for-bit");
                Check(ls.OriginalPath == @"C:\mic\take1.wav", "original file path is remembered");
                Check(AudioMixer.Mix(a, 0, 500).AsSpan().SequenceEqual(AudioMixer.Mix(d.Audio, 0, 500)),
                      "the reopened project mixes identically");
                r.Document.Dispose();
            }
            d.Dispose();
        }

        {
            // Migration: a version-1 file (no audio section) must still open.
            string path = Path.Combine(scratch, "v1.flint");
            File.Delete(path);   // left over from a previous run
            using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
            {
                var e = zip.CreateEntry("project.json");
                using var w = new StreamWriter(e.Open());
                w.Write("{\"format\":1,\"app\":\"Flint\",\"width\":8,\"height\":8," +
                        "\"frames\":[{\"delay\":90,\"name\":\"\"}],\"tags\":[]," +
                        "\"layers\":[{\"name\":\"Old\",\"visible\":true,\"locked\":false,\"shared\":false," +
                        "\"opacity\":255,\"blend\":\"Normal\",\"cels\":[]}]}");
            }
            var r = FlintFormat.Load(path);
            Check(r.Ok && r.Document!.Layers[0].Name == "Old" && r.Document.Frames[0].Delay == 90,
                  "a version-1 project opens in this version-2 build");
            Check(r.Ok && r.Document!.Audio.IsEmpty, "and comes up with no audio, as it had none");
            r.Document?.Dispose();
        }

        Console.WriteLine("Sync in exported video");

        if (ffmpeg == null)
        {
            Console.WriteLine("  skip  video sync: ffmpeg not installed");
        }
        else
        {
            // 10 frames of 100ms = 1000ms. A click at exactly 400ms, the start of frame 4.
            var frames = new List<Frame>();
            for (int i = 0; i < 10; i++)
            {
                var bmp = ImageOps.NewBitmap(32, 32);
                using (var g = Graphics.FromImage(bmp))
                    g.FillRectangle(Brushes.Red, i * 3, 4, 6, 6);
                frames.Add(new Frame(bmp, 100));
            }
            var p = new AudioProject();
            var t = new AudioTrack();
            p.Tracks.Add(t);
            p.AddClip(t, p.AddSource(Tone(80, 1000, 0.8f), "click"), 400);
            var mix = AudioMixer.Mix(p, 0, 1000);

            foreach (bool mp4 in new[] { false, true })
            {
                string ext = mp4 ? "mp4" : "webm";
                string outPath = Path.Combine(scratch, "sync." + ext);
                var res = WebmExport.Encode(frames, outPath, 30, true, mix, mp4);
                Check(res.Ok, $"{ext}: export with audio: {res.Message}");
                if (!res.Ok) continue;

                string probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
                string streams = RunTool(probe, $"-v quiet -show_entries stream=codec_type,codec_name -of csv=p=0 \"{outPath}\"");
                string want = mp4 ? "aac" : "opus";
                Check(streams.Contains(want) && streams.Contains("audio"), $"{ext}: has a {want} audio stream");
                Check(streams.Contains(mp4 ? "h264" : "vp9"), $"{ext}: has a {(mp4 ? "h264" : "vp9")} video stream");

                string vdur = RunTool(probe, $"-v quiet -select_streams v:0 -show_entries stream=duration -of csv=p=0 \"{outPath}\"").Trim();
                if (double.TryParse(vdur, NumberStyles.Float, CultureInfo.InvariantCulture, out double vd))
                    Check(Math.Abs(vd - 1.0) < 0.02, $"{ext}: video lasts 1.000s (got {vd:0.000})");
                else Console.WriteLine($"  note  {ext}: video stream duration not reported ({vdur})");

                // Decode the audio back to raw samples and find the click.
                var back = DecodeRaw(ffmpeg, outPath);
                long at = Onset(back, 0.2f);
                double ms = at < 0 ? double.NaN : AudioFormat.FramesToMs(at);
                double off = ms - 400;
                Check(at >= 0 && Math.Abs(off) <= 5,
                      $"{ext}: click lands at {ms:0.0}ms, {off:+0.0;-0.0}ms from frame 4 (tolerance 5ms)");
            }

            foreach (var f in frames) f.Bmp.Dispose();

            // Audio-only WAV export, read back by Python independently.
            string wavOut = Path.Combine(scratch, "mix16.wav");
            AudioIO.WritePcm16Wav(wavOut, mix);
            string info = RunPython($@"
import wave, struct
w = wave.open(r'{wavOut}', 'rb')
n = w.getnframes(); data = w.readframes(n)
vals = struct.unpack('<%dh' % (n*2), data)
first = next(i for i in range(n) if abs(vals[i*2]) > 0.2*32767)
print(w.getframerate(), w.getnchannels(), n, first)");
            var parts = info.Trim().Split(' ');
            Check(parts.Length == 4 && parts[0] == "48000" && parts[1] == "2",
                  $"WAV export is 48kHz stereo ({info.Trim()})");
            if (parts.Length == 4)
            {
                double firstMs = AudioFormat.FramesToMs(long.Parse(parts[3]));
                Check(Math.Abs(firstMs - 400) < 1, $"WAV export keeps the click at {firstMs:0.0}ms");
            }
        }

        Console.WriteLine(_fails == 0 ? "all audio checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    private static float[] DecodeRaw(string ffmpeg, string path)
    {
        var psi = new ProcessStartInfo(ffmpeg, $"-v error -i \"{path}\" -vn -ac 2 -ar 48000 -f f32le -")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit(60_000);
        var bytes = ms.ToArray();
        var s = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, s, 0, s.Length * 4);
        return s;
    }

    private static string RunTool(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        string o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(60_000);
        return o;
    }

    private static string RunPython(string code)
    {
        string script = Path.Combine(Path.GetTempPath(), "flint_" + Guid.NewGuid().ToString("N")[..8] + ".py");
        File.WriteAllText(script, code.Trim());
        try { return RunTool("python", $"\"{script}\""); }
        finally { File.Delete(script); }
    }
}
