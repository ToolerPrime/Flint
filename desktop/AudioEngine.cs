/* Speaker playback and microphone recording.
 *
 * Playback renders the mix on demand from the project, so edits made while
 * stopped are always what you hear — there is no stale pre-rendered buffer.
 * While playing, the position reported by the output device is the master
 * clock: the timeline asks "where is the audio?" and shows that frame, so
 * picture follows sound rather than drifting away from it.
 */
using NAudio.Wave;

namespace Flint;

public sealed class AudioEngine : IDisposable
{
    private WaveOutEvent? _out;
    private MixProvider? _provider;
    private double _startMs;

    private WaveInEvent? _in;
    private readonly List<float> _recorded = new();
    private int _recChannels;

    /// <summary>Output buffer latency we ask for, in ms.</summary>
    public int OutputLatencyMs = 100;

    public bool Playing => _out?.PlaybackState == PlaybackState.Playing;
    public bool Recording => _in != null;
    /// <summary>Peak of the most recent microphone buffer, 0..1, for a level meter.</summary>
    public float InputLevel { get; private set; }

    public static List<string> Microphones()
    {
        var list = new List<string>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            try { list.Add(WaveInEvent.GetCapabilities(i).ProductName); }
            catch { list.Add($"Input {i + 1}"); }
        }
        return list;
    }

    /// <summary>Start playing the project from a timeline position.</summary>
    public void Play(AudioProject project, double fromMs)
    {
        Stop();
        _startMs = fromMs;
        _provider = new MixProvider(project, fromMs);
        _out = new WaveOutEvent { DesiredLatency = OutputLatencyMs, NumberOfBuffers = 3 };
        _out.Init(_provider);
        _out.Play();
    }

    /// <summary>
    /// Timeline position of what is being heard right now, in ms. The device's
    /// own play position is used rather than a stopwatch, so it accounts for
    /// buffering and never runs ahead of the sound.
    /// </summary>
    public double PositionMs
    {
        get
        {
            if (_out == null) return _startMs;
            try
            {
                long bytes = _out.GetPosition();
                int blockAlign = _provider!.WaveFormat.BlockAlign;
                return _startMs + AudioFormat.FramesToMs(bytes / blockAlign);
            }
            catch { return _startMs; }
        }
    }

    public void Stop()
    {
        try { _out?.Stop(); } catch { }
        _out?.Dispose();
        _out = null;
        _provider = null;
    }

    // ---------- recording ----------

    /// <summary>Begin capturing from a microphone. Mono devices are upmixed.</summary>
    public void StartRecording(int device)
    {
        StopRecordingDiscard();
        _recorded.Clear();
        // Ask for mono 48 kHz 16-bit: what nearly every mic supports, and it
        // avoids a resample. Windows converts if the device runs differently.
        _recChannels = 1;
        _in = new WaveInEvent
        {
            DeviceNumber = device,
            WaveFormat = new WaveFormat(AudioFormat.Rate, 16, _recChannels),
            BufferMilliseconds = 30,
        };
        _in.DataAvailable += OnData;
        _in.StartRecording();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        float peak = 0;
        lock (_recorded)
        {
            for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                float v = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8)) / 32768f;
                _recorded.Add(v);
                if (Math.Abs(v) > peak) peak = Math.Abs(v);
            }
        }
        InputLevel = peak;
    }

    /// <summary>Stop and return what was recorded, as stereo float.</summary>
    public float[] StopRecording()
    {
        if (_in == null) return Array.Empty<float>();
        try { _in.StopRecording(); } catch { }
        _in.DataAvailable -= OnData;
        _in.Dispose();
        _in = null;
        InputLevel = 0;

        float[] mono;
        lock (_recorded) mono = _recorded.ToArray();
        var stereo = new float[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++) { stereo[i * 2] = mono[i]; stereo[i * 2 + 1] = mono[i]; }
        return stereo;
    }

    private void StopRecordingDiscard()
    {
        if (_in == null) return;
        StopRecording();
    }

    public void Dispose()
    {
        Stop();
        StopRecordingDiscard();
    }

    /// <summary>Feeds the device by mixing the project block by block.</summary>
    private sealed class MixProvider : ISampleProvider
    {
        private readonly AudioProject _project;
        private long _pos;   // in sample frames, never via ms

        public MixProvider(AudioProject project, double fromMs)
        {
            _project = project;
            _pos = AudioFormat.MsToFrames(fromMs);
        }

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.Rate, AudioFormat.Channels);

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            var block = AudioMixer.MixFrames(_project, _pos, frames);
            Array.Copy(block, 0, buffer, offset, block.Length);
            if (block.Length < count) Array.Clear(buffer, offset + block.Length, count - block.Length);
            _pos += frames;
            // Always return a full block: silence past the last clip keeps the
            // device clock running, which the animation follows.
            return count;
        }
    }
}
