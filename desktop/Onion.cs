/* Onion skin: display-only ghosts of neighbouring frames. */
using System.Drawing.Imaging;

namespace Flint;

public sealed class OnionSettings
{
    public bool Enabled;
    public int Before = 1;
    public int After = 1;
    /// <summary>Opacity of the nearest ghost, 0..1. Further ghosts fade out.</summary>
    public float Opacity = 0.35f;
    /// <summary>Red for earlier frames, blue for later ones.</summary>
    public bool Tint = true;
    /// <summary>Wrap at the ends, matching looped playback.</summary>
    public bool Wrap;
}

public static class Onion
{
    /// <summary>
    /// Frames to ghost, nearest first, with their opacity. Frames are never
    /// repeated and the current frame is never included, so a short animation
    /// with Wrap on cannot ghost itself.
    /// </summary>
    public static List<(int frame, float opacity, bool before)> Plan(
        OnionSettings s, int current, int frameCount)
    {
        var plan = new List<(int, float, bool)>();
        if (!s.Enabled || frameCount < 2) return plan;
        var seen = new HashSet<int> { current };

        void Walk(int count, int dir, bool before)
        {
            for (int d = 1; d <= count; d++)
            {
                int f = current + dir * d;
                if (s.Wrap) f = ((f % frameCount) + frameCount) % frameCount;
                else if (f < 0 || f >= frameCount) break;
                if (!seen.Add(f)) continue;
                // Linear fade: nearest at full ghost opacity, furthest near zero.
                float a = s.Opacity * (count - d + 1) / count;
                plan.Add((f, a, before));
            }
        }

        Walk(s.Before, -1, true);
        Walk(s.After, +1, false);
        return plan;
    }

    public static void Draw(Graphics g, Document doc, OnionSettings s, int current, Rectangle dest)
    {
        var plan = Plan(s, current, doc.FrameCount);
        if (plan.Count == 0) return;

        // Shared layers already appear in the current composite. Skip them in
        // ghosts, and show ghosts only through the current frame's moving art.
        var currentPixels = doc.RenderPixels(current, omitShared: true);
        foreach (var (frame, alpha, before) in plan.OrderBy(item => item.opacity))
        {
            var pixels = doc.RenderPixels(frame, omitShared: true);
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = (byte)((pixels[i] * (255 - currentPixels[i]) + 127) / 255);
            using var ghost = ImageOps.FromPixels(doc.Width, doc.Height, pixels);
            var m = new ColorMatrix { Matrix33 = alpha };
            if (s.Tint)
            {
                // Push toward red (past) or blue (future) while keeping shape.
                m.Matrix00 = before ? 1f : 0.4f;
                m.Matrix11 = 0.4f;
                m.Matrix22 = before ? 0.4f : 1f;
                m.Matrix40 = before ? 0.35f : 0f;
                m.Matrix42 = before ? 0f : 0.35f;
            }
            using var attrs = new ImageAttributes();
            attrs.SetColorMatrix(m);
            g.DrawImage(ghost, dest, 0, 0, ghost.Width, ghost.Height, GraphicsUnit.Pixel, attrs);
        }
    }
}
