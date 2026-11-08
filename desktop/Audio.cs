/* Audio tracks, clips and the mixer.
 *
 * Every source is converted to one internal format at import: 48 kHz,
 * stereo, 32-bit float. The mixer then never resamples, so clip positions are
 * exact sample offsets and the mix is deterministic — which is what makes
 * sync testable. 48 kHz because Opus (WebM audio) requires it.
 *
 * Timing is in milliseconds on the same clock as frame delays, so a clip at
 * 350 ms starts exactly when the frame beginning at 350 ms appears.
 */
namespace Flint;

public static class AudioFormat
{
    public const int Rate = 48000;
    public const int Channels = 2;

    public static long MsToFrames(double ms) => (long)Math.Round(ms * Rate / 1000.0);
    public static double FramesToMs(long frames) => frames * 1000.0 / Rate;
}

/// <summary>Decoded audio. Immutable once created, so clips can share it.</summary>
public sealed class AudioSource
{
    public int Id;
    public string Name = "Audio";
    /// <summary>Interleaved stereo float, at <see cref="AudioFormat.Rate"/>.</summary>
    public float[] Samples = Array.Empty<float>();
    /// <summary>Where it came from, for the user; never re-read automatically.</summary>
    public string? OriginalPath;

    public long FrameCount => Samples.Length / AudioFormat.Channels;
    public double DurationMs => AudioFormat.FramesToMs(FrameCount);
    public long Bytes => Samples.LongLength * sizeof(float);
}

public enum TrackKind { Dialogue, Effects, Music }

public sealed class AudioClip
{
    public int SourceId;
    public string Name = "Clip";
    /// <summary>Position on the timeline.</summary>
    public double StartMs;
    /// <summary>How far into the source playback begins (left trim).</summary>
    public double InMs;
    /// <summary>How much of the source plays.</summary>
    public double LengthMs;
    /// <summary>Linear gain; 1 = unchanged.</summary>
    public float Gain = 1f;
    public double FadeInMs, FadeOutMs;
    public bool Loop;

    public double EndMs => StartMs + LengthMs;

    public AudioClip Clone() => (AudioClip)MemberwiseClone();
}

public sealed class AudioTrack
{
    public string Name = "Audio";
    public TrackKind Kind = TrackKind.Dialogue;
    public bool Muted, Solo;
    public float Volume = 1f;
    public readonly List<AudioClip> Clips = new();

    public AudioTrack Clone()
    {
        var t = new AudioTrack { Name = Name, Kind = Kind, Muted = Muted, Solo = Solo, Volume = Volume };
        foreach (var c in Clips) t.Clips.Add(c.Clone());
        return t;
    }
}

public sealed class AudioProject
{
    public readonly List<AudioTrack> Tracks = new();
    public readonly Dictionary<int, AudioSource> Sources = new();
    public int NextSourceId = 1;

    public bool IsEmpty => Tracks.All(t => t.Clips.Count == 0);

    public AudioSource AddSource(float[] samples, string name, string? path = null)
    {
        var s = new AudioSource { Id = NextSourceId++, Name = name, Samples = samples, OriginalPath = path };
        Sources[s.Id] = s;
        return s;
    }

    public AudioClip AddClip(AudioTrack track, AudioSource src, double startMs)
    {
        var c = new AudioClip
        {
            SourceId = src.Id, Name = src.Name, StartMs = startMs, LengthMs = src.DurationMs,
        };
        track.Clips.Add(c);
        return c;
    }

    /// <summary>Clips share sources, so a clone shares them too — sources never change.</summary>
    public AudioProject Clone()
    {
        var p = new AudioProject { NextSourceId = NextSourceId };
        foreach (var t in Tracks) p.Tracks.Add(t.Clone());
        foreach (var (k, v) in Sources) p.Sources[k] = v;
        return p;
    }

    public double EndMs => Tracks.SelectMany(t => t.Clips).Select(c => c.EndMs).DefaultIfEmpty(0).Max();

    public long Bytes => Sources.Values.Sum(s => s.Bytes);

    /// <summary>Drop sources no clip uses, e.g. after a recording is deleted.</summary>
    public void PruneSources()
    {
        var used = Tracks.SelectMany(t => t.Clips).Select(c => c.SourceId).ToHashSet();
        foreach (var id in Sources.Keys.Where(k => !used.Contains(k)).ToList()) Sources.Remove(id);
    }

    /// <summary>Split a clip at a timeline position; both halves keep the source.</summary>
    public AudioClip? Split(AudioTrack track, AudioClip clip, double atMs)
    {
        if (atMs <= clip.StartMs || atMs >= clip.EndMs) return null;
        double cut = atMs - clip.StartMs;
        var right = clip.Clone();
        right.StartMs = atMs;
        right.InMs = clip.InMs + cut;
        right.LengthMs = clip.LengthMs - cut;
        right.FadeInMs = 0;
        clip.LengthMs = cut;
        clip.FadeOutMs = 0;
        track.Clips.Insert(track.Clips.IndexOf(clip) + 1, right);
        return right;
    }
}

public static class AudioMixer
{
    /// <summary>
    /// Mix every audible clip into interleaved stereo for [startMs, startMs+lengthMs).
    /// Pure: same project and range always give the same samples.
    /// </summary>
    public static float[] Mix(AudioProject p, double startMs, double lengthMs) =>
        MixFrames(p, AudioFormat.MsToFrames(startMs), AudioFormat.MsToFrames(lengthMs));

    /// <summary>
    /// Same as <see cref="Mix"/> but addressed in whole sample frames. A player
    /// asking for consecutive blocks must use this: rounding each block's
    /// start through milliseconds would drop or repeat samples and click.
    /// </summary>
    public static float[] MixFrames(AudioProject p, long winStart, long outFrames)
    {
        var mix = new float[Math.Max(0, outFrames) * AudioFormat.Channels];
        if (outFrames <= 0) return mix;

        bool anySolo = p.Tracks.Any(t => t.Solo);

        foreach (var track in p.Tracks)
        {
            if (track.Muted || (anySolo && !track.Solo) || track.Volume <= 0) continue;
            foreach (var clip in track.Clips)
            {
                if (!p.Sources.TryGetValue(clip.SourceId, out var src) || src.FrameCount == 0) continue;
                MixClip(mix, winStart, outFrames, clip, src, track.Volume);
            }
        }

        // Hard clip only as a last resort; it means the mix is too hot.
        for (int i = 0; i < mix.Length; i++) mix[i] = Math.Clamp(mix[i], -1f, 1f);
        return mix;
    }

    private static void MixClip(float[] mix, long winStart, long outFrames,
                                AudioClip clip, AudioSource src, float trackVolume)
    {
        long clipStart = AudioFormat.MsToFrames(clip.StartMs);
        long clipLen = AudioFormat.MsToFrames(clip.LengthMs);
        long inFrame = AudioFormat.MsToFrames(clip.InMs);
        long fadeIn = AudioFormat.MsToFrames(clip.FadeInMs);
        long fadeOut = AudioFormat.MsToFrames(clip.FadeOutMs);
        float gain = clip.Gain * trackVolume;

        // Overlap of the clip with the output window, in timeline frames.
        long from = Math.Max(clipStart, winStart);
        long to = Math.Min(clipStart + clipLen, winStart + outFrames);
        if (to <= from) return;

        long srcFrames = src.FrameCount;
        for (long t = from; t < to; t++)
        {
            long intoClip = t - clipStart;
            long s = inFrame + intoClip;
            if (clip.Loop) s %= srcFrames;
            else if (s >= srcFrames) break;
            if (s < 0) continue;

            float env = gain;
            if (fadeIn > 0 && intoClip < fadeIn) env *= intoClip / (float)fadeIn;
            long fromEnd = clipLen - intoClip;
            if (fadeOut > 0 && fromEnd < fadeOut) env *= fromEnd / (float)fadeOut;

            long o = (t - winStart) * 2;
            mix[o] += src.Samples[s * 2] * env;
            mix[o + 1] += src.Samples[s * 2 + 1] * env;
        }
    }

    /// <summary>Peak per bucket, for drawing a waveform without touching every sample.</summary>
    public static float[] Peaks(AudioSource src, double inMs, double lengthMs, int buckets)
    {
        var peaks = new float[Math.Max(0, buckets)];
        if (buckets <= 0 || src.FrameCount == 0) return peaks;
        long a = AudioFormat.MsToFrames(inMs);
        long n = AudioFormat.MsToFrames(lengthMs);
        for (int b = 0; b < buckets; b++)
        {
            long s0 = a + n * b / buckets, s1 = a + n * (b + 1) / buckets;
            float peak = 0;
            // Stride so a long clip on a narrow lane stays cheap.
            long stride = Math.Max(1, (s1 - s0) / 64);
            for (long s = s0; s < s1 && s < src.FrameCount; s += stride)
            {
                float v = Math.Max(Math.Abs(src.Samples[s * 2]), Math.Abs(src.Samples[s * 2 + 1]));
                if (v > peak) peak = v;
            }
            peaks[b] = peak;
        }
        return peaks;
    }
}
