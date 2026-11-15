/* Named export presets (format, quality, GIF palette/dither/lossy settings)
 * kept in %AppData%\Flint\export-presets.json, and Optimize to Size. */
using System.Text.Json;

namespace Flint;

internal static class ExportPresets
{
    /// <summary>Overridable so tests do not touch the user's own presets.</summary>
    public static string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Flint", "export-presets.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static SortedDictionary<string, OptSettings> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<SortedDictionary<string, OptSettings>>(File.ReadAllText(FilePath), Json)
                       ?? new();
        }
        catch (Exception) { /* a broken presets file must not stop export */ }
        return new();
    }

    public static void Save(string name, OptSettings s)
    {
        var all = Load();
        all[name] = s.Clone();
        Write(all);
    }

    public static void Delete(string name)
    {
        var all = Load();
        if (all.Remove(name)) Write(all);
    }

    private static void Write(SortedDictionary<string, OptSettings> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(all, Json));
    }
}

internal static class OptimizeToSize
{
    /// <summary>
    /// GIF or PNG-8: the most colours (on a tie, the least lossy) whose file
    /// fits in <paramref name="targetBytes"/>. Returns null when even 2 colours
    /// at full lossy do not fit. Binary search over colours at each lossy step;
    /// size grows with colours almost always, so the answer is near-best.
    /// </summary>
    public static OptSettings? Find(OptSettings start, OptSource src, long targetBytes, CancellationToken ct)
    {
        start = start.Clone();
        if (start.Gif.Palette is not (PaletteKind.Adaptive or PaletteKind.WebAdaptive or PaletteKind.Uniform or PaletteKind.Greyscale))
            start.Gif.Palette = PaletteKind.Adaptive;   // fixed palettes ignore the colour count
        var lossySteps = start.Format == OptFormat.Gif ? new[] { 0, 20, 40, 70, 100, 150, 200 } : new[] { 0 };
        OptSettings? found = null;
        foreach (int lossy in lossySteps)
        {
            int lo = 2, hi = 256, best = -1;
            while (lo <= hi)
            {
                ct.ThrowIfCancellationRequested();
                int mid = (lo + hi) / 2;
                var s = start.Clone();
                s.Gif.Colours = mid; s.Gif.Lossy = lossy;
                if (OptimizeEncoder.Encode(s, src, ct).Bytes <= targetBytes) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            if (best > (found?.Colours ?? 0))
            {
                found = start.Clone();
                found.Gif.Colours = best; found.Gif.Lossy = lossy;
                if (best == 256) break;          // cannot do better
            }
        }
        return found;
    }
}
