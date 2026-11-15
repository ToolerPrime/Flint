/* GIF / PNG-8 colour reduction options: palette types, dithering, colour
 * table edits (lock, edit, transparent), remove unused colours.
 *
 * The fixed palettes (web 216, uniform), the web-adaptive snap, the ordered
 * (Bayer 8x8) and noise dithers and their strength rule are translated from
 * PhotoCraft, crates/algo/src/quantize.rs
 * (https://github.com/storytold/photocraft). Copyright (c) 2026 ArtCraft Team
 * and the PhotoCraft contributors. MIT licence (MIT or Apache-2.0 at the
 * user's option); see CREDITS_LICENSES.md.
 *
 * The adaptive palette is still Flint's port of Craig Kolb's variance
 * quantizer (Quantizer.cs), so the default output is unchanged. Floyd-Steinberg
 * is the existing serpentine ImageOps.Dither. */
namespace Flint;

public enum PaletteKind { Adaptive, Exact, Web216, WebAdaptive, Uniform, Greyscale, BlackWhite, Custom }
public enum DitherKind { None, FloydSteinberg, Ordered, Noise }

/// <summary>Everything that decides a GIF's colours and how it is compressed.</summary>
public sealed class GifOptions
{
    public PaletteKind Palette { get; set; } = PaletteKind.Adaptive;
    public int Colours { get; set; } = 256;
    public DitherKind Dither { get; set; } = DitherKind.None;
    /// <summary>Dither strength, percent (0-100).</summary>
    public int DitherAmount { get; set; } = 100;
    /// <summary>Lossy LZW tolerance, 0 = off, up to 200.</summary>
    public int Lossy { get; set; }
    public bool RemoveUnused { get; set; } = true;
    /// <summary>Crop each frame to what changed and reuse unchanged pixels.</summary>
    public bool Optimize { get; set; } = true;
    /// <summary>Packed 0xRRGGBB colours for PaletteKind.Custom.</summary>
    public int[]? Custom { get; set; }
    /// <summary>Colours always kept in the palette (they count toward Colours).</summary>
    public List<int> Locked { get; set; } = new();
    /// <summary>Palette colour -> the colour written instead (pixels keep their entry).</summary>
    public Dictionary<int, int> Edits { get; set; } = new();
    /// <summary>Palette colours whose pixels become transparent.</summary>
    public HashSet<int> Transparent { get; set; } = new();

    public GifOptions Clone()
    {
        var c = (GifOptions)MemberwiseClone();
        c.Custom = Custom?.ToArray();
        c.Locked = new(Locked); c.Edits = new(Edits); c.Transparent = new(Transparent);
        return c;
    }

    public static string Describe(PaletteKind k) => k switch
    {
        PaletteKind.Adaptive => "Adaptive",
        PaletteKind.Exact => "Exact",
        PaletteKind.Web216 => "Web 216",
        PaletteKind.WebAdaptive => "Web Adaptive",
        PaletteKind.Uniform => "Uniform",
        PaletteKind.Greyscale => "Greyscale",
        PaletteKind.BlackWhite => "Black and white",
        _ => "Custom (loaded)",
    };

    public static string Describe(DitherKind k) => k switch
    {
        DitherKind.None => "No dither",
        DitherKind.FloydSteinberg => "Diffusion (Floyd-Steinberg)",
        DitherKind.Ordered => "Pattern (ordered)",
        _ => "Noise",
    };
}

public static class GifPalette
{
    public static int Pack(byte r, byte g, byte b) => r << 16 | g << 8 | b;

    /// <summary>The 216-colour web-safe cube (0, 51, ..., 255 per channel).</summary>
    public static List<int> Web216()
    {
        var v = new List<int>(216);
        for (int r = 0; r < 6; r++) for (int g = 0; g < 6; g++) for (int b = 0; b < 6; b++)
            v.Add(Pack((byte)(r * 51), (byte)(g * 51), (byte)(b * 51)));
        return v;
    }

    /// <summary>Up to n colours on an equal-step RGB grid.</summary>
    public static List<int> Uniform(int n)
    {
        int k = Math.Max(2, (int)Math.Floor(Math.Cbrt(n) + 1e-9));
        byte Lv(int i) => (byte)Math.Round(i / (double)(k - 1) * 255);
        var v = new List<int>(k * k * k);
        for (int r = 0; r < k; r++) for (int g = 0; g < k; g++) for (int b = 0; b < k; b++) v.Add(Pack(Lv(r), Lv(g), Lv(b)));
        return v;
    }

    public static List<int> Greyscale(int n)
    {
        n = Math.Clamp(n, 2, 256);
        return Enumerable.Range(0, n).Select(i => { byte g = (byte)Math.Round(i * 255.0 / (n - 1)); return Pack(g, g, g); }).ToList();
    }

    /// <summary>8x8 Bayer threshold in -0.5..0.5.</summary>
    public static float Bayer8(int x, int y)
    {
        ReadOnlySpan<byte> m = new byte[]
        {
            0, 32, 8, 40, 2, 34, 10, 42, 48, 16, 56, 24, 50, 18, 58, 26, 12, 44, 4, 36, 14, 46, 6, 38, 60, 28, 52, 20, 62, 30, 54, 22, 3, 35, 11, 43, 1, 33, 9, 41,
            51, 19, 59, 27, 49, 17, 57, 25, 15, 47, 7, 39, 13, 45, 5, 37, 63, 31, 55, 23, 61, 29, 53, 21,
        };
        return (m[(y & 7) * 8 + (x & 7)] + 0.5f) / 64f - 0.5f;
    }

    private static float Hash01(int x, int y, uint seed)
    {
        uint h = unchecked((uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ seed * 0xcb1ab31fu);
        h ^= h >> 13;
        h = unchecked(h * 0x5bd1e995u);
        h ^= h >> 15;
        return (h & 0xffff) / 65535f;
    }

    private static bool HasClear(byte[] px) { for (int i = 3; i < px.Length; i += 4) if (px[i] < 128) return true; return false; }

    /// <summary>
    /// The opaque colours to offer, before edits. Sized palettes (adaptive,
    /// uniform, greyscale) take <paramref name="room"/>; fixed ones ignore the
    /// colour count but never pass <paramref name="hard"/> (256 less the transparent slot).
    /// </summary>
    private static List<int> BuildColours(IList<byte[]> frames, GifOptions o, int room, int hard)
    {
        bool sized = o.Palette is PaletteKind.Adaptive or PaletteKind.WebAdaptive or PaletteKind.Uniform or PaletteKind.Greyscale;
        if (!sized) room = hard;
        // Uniform's smallest grid is 2x2x2; below 8 colours it keeps all 8 rather than lose white.
        if (o.Palette == PaletteKind.Uniform) room = Math.Min(hard, Math.Max(room, 8));
        // Edited and transparent-marked colours stay put too, or the edit would be lost on the next rebuild.
        var locked = o.Locked.Concat(o.Edits.Keys).Concat(o.Transparent).Distinct().Take(room).ToList();
        int left = room - locked.Count;
        List<int> computed;
        switch (o.Palette)
        {
            case PaletteKind.Exact:
            {
                var set = new HashSet<int>();
                foreach (var px in frames)
                    for (int i = 0; i < px.Length; i += 4)
                        if (px[i + 3] >= 128 && set.Add(Pack(px[i + 2], px[i + 1], px[i])) && set.Count > 256) break;
                set.ExceptWith(locked);
                if (set.Count > left)
                    throw new InvalidOperationException($"the animation has {(set.Count > 256 ? "more than 256" : set.Count.ToString())} colours; Exact fits {left}. Choose Adaptive or more colours.");
                computed = set.OrderBy(c => c).ToList();
                break;
            }
            case PaletteKind.Web216: computed = Web216(); break;
            case PaletteKind.Uniform: computed = Uniform(Math.Max(8, left)); break;
            case PaletteKind.Greyscale: computed = Greyscale(Math.Max(2, left)); break;
            case PaletteKind.BlackWhite: computed = new List<int> { 0, 0xffffff }; break;
            case PaletteKind.Custom:
                computed = (o.Custom ?? throw new InvalidOperationException("load a palette first (Custom)")).ToList();
                break;
            default:
            {
                computed = new List<int>();
                if (left > 0)
                {
                    var hist = Quantizer.BuildHistogram(frames, out double total);
                    var q = Quantizer.Quantize(hist, total, left);
                    for (int i = 0; i < q.Count; i++) computed.Add(Pack(q.R[i], q.G[i], q.B[i]));
                }
                if (o.Palette == PaletteKind.WebAdaptive)
                {
                    // Favour web-safe colours: snap entries within ~10 levels of one.
                    for (int i = 0; i < computed.Count; i++)
                    {
                        int c = computed[i], snap = 0;
                        bool near = true;
                        for (int s = 16; s >= 0; s -= 8)
                        {
                            int v = c >> s & 255, sv = (int)Math.Round(v / 51.0) * 51;
                            if (Math.Abs(v - sv) > 10) near = false;
                            snap |= sv << s;
                        }
                        if (near) computed[i] = snap;
                    }
                }
                break;
            }
        }
        var pal = new List<int>(locked);
        var seen = new HashSet<int>(locked);
        foreach (int c in computed) if (seen.Add(c)) pal.Add(c);
        return pal.Take(room).ToList();
    }

    /// <summary>
    /// One shared palette over all frames, then each frame mapped to it with
    /// the chosen dither. Slot 0 is transparent when any pixel is clear or a
    /// colour is marked transparent.
    /// </summary>
    public static IndexedFrames Index(IList<byte[]> frames, int w, int h, GifOptions o, CancellationToken ct = default)
    {
        bool anyClear = frames.Any(HasClear);
        bool needT = anyClear || o.Transparent.Count > 0;
        int offset = needT ? 1 : 0;
        int room = Math.Clamp(o.Colours, 2, 256) - offset;
        var colours = BuildColours(frames, o, room, 256 - offset);
        if (colours.Count == 0) throw new InvalidOperationException("no opaque pixels to build a palette from");

        var match = new Palette
        {
            R = colours.Select(c => (byte)(c >> 16)).ToArray(),
            G = colours.Select(c => (byte)(c >> 8)).ToArray(),
            B = colours.Select(c => (byte)c).ToArray(),
        };
        var ix = new Quantizer.Indexer(match);
        // raw entry k -> final index (transparent-marked entries go to slot 0)
        var map = colours.Select((c, k) => o.Transparent.Contains(c) ? 0 : k + offset).ToArray();

        var res = new IndexedFrames { TransparentIndex = needT ? 0 : -1 };
        float amount = Math.Clamp(o.DitherAmount, 0, 100) / 100f;
        float spread = MathF.Min(1f / MathF.Max(1f, MathF.Cbrt(match.Count)), 0.5f) * amount * 255f;
        foreach (var px in frames)
        {
            ct.ThrowIfCancellationRequested();
            byte[] idx;
            if (o.Dither == DitherKind.FloydSteinberg && amount > 0)
            {
                // With a transparent slot, clear pixels come back as 0 and entry k as k + 1;
                // without one (up to 256 entries) nothing is clear and k comes back as k.
                idx = ImageOps.Dither(px, w, h, match, ix, offset, amount);
                for (int q = 0; q < idx.Length; q++) idx[q] = needT ? (idx[q] == 0 ? (byte)0 : (byte)map[idx[q] - 1]) : (byte)map[idx[q]];
            }
            else
            {
                idx = new byte[w * h];
                bool noise = o.Dither == DitherKind.Noise, ordered = o.Dither == DitherKind.Ordered;
                for (int q = 0; q < idx.Length; q++)
                {
                    int p = q * 4;
                    if (px[p + 3] < 128) { idx[q] = 0; continue; }
                    byte r = px[p + 2], g = px[p + 1], b = px[p];
                    if ((ordered || noise) && spread > 0)
                    {
                        int x = q % w, y = q / w;
                        float t = (ordered ? Bayer8(x, y) : Hash01(x, y, 7) - 0.5f) * spread;
                        r = (byte)Math.Clamp(r + t, 0, 255); g = (byte)Math.Clamp(g + t, 0, 255); b = (byte)Math.Clamp(b + t, 0, 255);
                    }
                    idx[q] = (byte)map[ix.Nearest(r, g, b)];
                }
            }
            res.Indices.Add(idx);
        }

        // Written slots: the source colour of each (-1 = transparent), compacted if asked.
        var entries = new List<int>();
        if (needT) entries.Add(-1);
        entries.AddRange(colours);
        if (o.RemoveUnused)
        {
            var used = new bool[entries.Count];
            foreach (var f in res.Indices) foreach (byte b in f) used[b] = true;
            if (needT) used[0] = true;
            // Locked and transparent-marked colours stay listed, so the colour table can still show and undo them.
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] >= 0 && (o.Locked.Contains(entries[i]) || o.Transparent.Contains(entries[i]))) used[i] = true;
            var remap = new byte[entries.Count];
            var kept = new List<int>();
            for (int i = 0; i < entries.Count; i++) if (used[i]) { remap[i] = (byte)kept.Count; kept.Add(entries[i]); }
            if (kept.Count < entries.Count)
            {
                foreach (var f in res.Indices) for (int q = 0; q < f.Length; q++) f[q] = remap[f[q]];
                entries = kept;
            }
        }
        // Edits change an entry's colour, not which pixels use it.
        var pal = entries.Select(c => c < 0 ? 0 : o.Edits.TryGetValue(c, out int e) ? e : c).ToList();
        res.Palette = new Palette
        {
            R = pal.Select(c => (byte)(c >> 16)).ToArray(),
            G = pal.Select(c => (byte)(c >> 8)).ToArray(),
            B = pal.Select(c => (byte)c).ToArray(),
        };
        res.Source = entries;
        return res;
    }
}
