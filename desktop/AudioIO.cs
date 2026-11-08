/* Decoding audio files to the internal format, and WAV in and out.
 *
 * Decoding order: NAudio's AudioFileReader first (WAV and MP3 directly; AAC,
 * WMA and FLAC through Windows Media Foundation), then ffmpeg for anything
 * else, such as OGG/Opus. Nothing here writes its own codec.
 */
using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Flint;

public static class AudioIO
{
    public static readonly string[] Extensions =
        { ".wav", ".mp3", ".flac", ".ogg", ".opus", ".m4a", ".aac", ".wma", ".aiff", ".aif" };

    public static bool IsAudio(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Decode to 48 kHz stereo float. Throws with a readable message on failure.</summary>
    public static float[] Decode(string path)
    {
        Exception? first = null;
        try { return DecodeWithNAudio(path); }
        catch (Exception ex) { first = ex; }

        var ffmpeg = WebmExport.Find();
        if (ffmpeg == null)
            throw new InvalidOperationException(
                $"could not decode {Path.GetExtension(path)} ({first.Message}); " +
                "installing ffmpeg adds support for more formats");
        return DecodeWithFfmpeg(ffmpeg, path);
    }

    private static float[] DecodeWithNAudio(string path)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider sp = reader;
        int ch = sp.WaveFormat.Channels;
        if (ch == 1) sp = new MonoToStereoSampleProvider(sp);
        else if (ch != 2) throw new NotSupportedException($"{ch}-channel audio");
        if (sp.WaveFormat.SampleRate != AudioFormat.Rate)
            sp = new WdlResamplingSampleProvider(sp, AudioFormat.Rate);
        return ReadAll(sp);
    }

    private static float[] DecodeWithFfmpeg(string ffmpeg, string path)
    {
        // Raw float out, so there is no container to parse on the way back.
        var psi = new ProcessStartInfo(ffmpeg,
            $"-v error -i \"{path}\" -vn -ac 2 -ar {AudioFormat.Rate} -f f32le -")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start ffmpeg");
        using var ms = new MemoryStream();
        var errTask = p.StandardError.ReadToEndAsync();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit(120_000);
        if (p.ExitCode != 0)
            throw new InvalidOperationException("ffmpeg could not decode it: " + errTask.Result.Trim());
        var bytes = ms.ToArray();
        var samples = new float[bytes.Length / 4 / 2 * 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 4);
        return samples;
    }

    private static float[] ReadAll(ISampleProvider sp)
    {
        var list = new List<float>(AudioFormat.Rate * 2 * 10);
        var buf = new float[AudioFormat.Rate * 2];
        int n;
        while ((n = sp.Read(buf, 0, buf.Length)) > 0)
            for (int i = 0; i < n; i++) list.Add(buf[i]);
        // Keep whole stereo frames only.
        if (list.Count % 2 == 1) list.RemoveAt(list.Count - 1);
        return list.ToArray();
    }

    /// <summary>32-bit float WAV: lossless for our internal format, used inside projects.</summary>
    public static void WriteFloatWav(Stream s, float[] samples)
    {
        using var w = new WaveFileWriter(new IgnoreDisposeStream(s),
            WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.Rate, AudioFormat.Channels));
        w.WriteSamples(samples, 0, samples.Length);
    }

    public static float[] ReadWav(Stream s)
    {
        using var r = new WaveFileReader(s);
        ISampleProvider sp = r.ToSampleProvider();
        if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
        if (sp.WaveFormat.SampleRate != AudioFormat.Rate)
            sp = new WdlResamplingSampleProvider(sp, AudioFormat.Rate);
        return ReadAll(sp);
    }

    /// <summary>16-bit PCM WAV, the most widely readable, for export.</summary>
    public static void WritePcm16Wav(string path, float[] samples)
    {
        using var w = new WaveFileWriter(path, new WaveFormat(AudioFormat.Rate, 16, AudioFormat.Channels));
        var buf = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * 32767f);
            buf[i * 2] = (byte)v;
            buf[i * 2 + 1] = (byte)(v >> 8);
        }
        w.Write(buf, 0, buf.Length);
    }

    /// <summary>
    /// A stream wrapper that ignores Dispose, so WaveFileWriter can finish the
    /// RIFF header without closing a zip entry stream it does not own.
    /// </summary>
    private sealed class IgnoreDisposeStream : Stream
    {
        private readonly Stream _inner;
        public IgnoreDisposeStream(Stream inner) { _inner = inner; }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] b, int o, int c) => _inner.Read(b, o, c);
        public override long Seek(long o, SeekOrigin so) => _inner.Seek(o, so);
        public override void SetLength(long v) => _inner.SetLength(v);
        public override void Write(byte[] b, int o, int c) => _inner.Write(b, o, c);
        protected override void Dispose(bool disposing) => Flush();
    }
}
