/* Animation timing, kept apart from any timer or window so it can be tested.
 *
 * Playback is driven by elapsed time rather than by counting timer ticks.
 * WinForms timers drift and skip under load; if playback advanced one frame
 * per tick, a busy UI would slow the animation and audio would drift out of
 * sync. Asking "which frame is showing at time t?" instead means a late tick
 * just skips ahead to the right frame. */
namespace Flint;

public enum LoopMode { Loop, PingPong, Once }

public sealed class Playback
{
    public LoopMode Mode = LoopMode.Loop;
    /// <summary>1 = normal speed. 0.5 is half speed, 2 is double.</summary>
    public double Speed = 1.0;

    /// <summary>Start time of each frame in ms, plus the total at the end.</summary>
    public static int[] Timeline(IReadOnlyList<int> delays, int from, int to)
    {
        var t = new int[to - from + 2];
        for (int i = from; i <= to; i++) t[i - from + 1] = t[i - from] + Math.Max(1, delays[i]);
        return t;
    }

    /// <summary>Start of a frame in ms from the beginning of the document.</summary>
    public static int FrameStart(IReadOnlyList<int> delays, int frame)
    {
        int t = 0;
        for (int i = 0; i < frame && i < delays.Count; i++) t += Math.Max(1, delays[i]);
        return t;
    }

    public static int TotalMs(IReadOnlyList<int> delays) => FrameStart(delays, delays.Count);

    /// <summary>Which frame is on screen at a document time, ignoring loop mode.</summary>
    public static int FrameAt(IReadOnlyList<int> delays, int ms)
    {
        int t = 0;
        for (int i = 0; i < delays.Count; i++)
        {
            t += Math.Max(1, delays[i]);
            if (ms < t) return i;
        }
        return delays.Count - 1;
    }

    /// <summary>
    /// Frame showing after <paramref name="elapsedMs"/> of wall-clock playback
    /// over the range [from, to]. Returns -1 once a Once-mode play has ended.
    /// </summary>
    public int FrameAfter(IReadOnlyList<int> delays, int from, int to, double elapsedMs)
    {
        if (delays.Count == 0) return -1;
        from = Math.Clamp(from, 0, delays.Count - 1);
        to = Math.Clamp(to, from, delays.Count - 1);

        var t = Timeline(delays, from, to);
        int span = t[^1];
        double local = elapsedMs * Speed;

        if (Mode == LoopMode.Once)
        {
            if (local >= span) return -1;
            return from + Index(t, local);
        }

        if (Mode == LoopMode.Loop || to == from)
            return from + Index(t, local % span);

        // Ping-pong: forward over every frame, then back over the inner frames
        // only, so the end frames are not shown twice in a row.
        int inner = t[^2] - t[1];            // time of frames from+1 .. to-1
        int cycle = span + inner;
        double c = local % cycle;
        if (c < span) return from + Index(t, c);
        double back = c - span;               // walking backwards from to-1
        double acc = 0;
        for (int i = to - 1; i > from; i--)
        {
            acc += Math.Max(1, delays[i]);
            if (back < acc) return i;
        }
        return from + 1;
    }

    private static int Index(int[] t, double local)
    {
        for (int i = 0; i < t.Length - 1; i++)
            if (local < t[i + 1]) return i;
        return t.Length - 2;
    }
}
