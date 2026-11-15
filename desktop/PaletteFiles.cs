/* Load and save colour palettes: Adobe Colour Table (.act), GIMP palette
 * (.gpl) and JASC / Paint Shop Pro palette (.pal). Colours are packed
 * 0xRRGGBB, at most 256. Written for Flint from the public descriptions of
 * these formats. */
using System.Globalization;
using System.Text;

namespace Flint;

public static class PaletteFiles
{
    public const string Filter = "Palettes (*.act;*.gpl;*.pal)|*.act;*.gpl;*.pal|Adobe Colour Table (*.act)|*.act|GIMP palette (*.gpl)|*.gpl|JASC palette (*.pal)|*.pal";

    public static int[] Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var list = new List<int>();
        string text = Encoding.UTF8.GetString(bytes);
        if (text.StartsWith("GIMP Palette"))
        {
            foreach (var line in text.Split('\n').Skip(1))
            {
                var t = line.Trim();
                if (t.Length == 0 || t[0] == '#' || t.StartsWith("Name:") || t.StartsWith("Columns:")) continue;
                var parts = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && TryRgb(parts, out int c)) list.Add(c);
            }
        }
        else if (text.StartsWith("JASC-PAL"))
        {
            var lines = text.Split('\n').Select(l => l.Trim()).ToArray();
            for (int i = 3; i < lines.Length; i++)
            {
                var parts = lines[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && TryRgb(parts, out int c)) list.Add(c);
            }
        }
        else if (bytes.Length == 768 || bytes.Length == 772)
        {
            // ACT: 256 RGB triples, then optionally a big-endian count and transparent index.
            int n = 256;
            if (bytes.Length == 772) { n = bytes[768] << 8 | bytes[769]; if (n <= 0 || n > 256) n = 256; }
            for (int i = 0; i < n; i++) list.Add(bytes[i * 3] << 16 | bytes[i * 3 + 1] << 8 | bytes[i * 3 + 2]);
        }
        else throw new InvalidDataException("not a palette file Flint can read (.act, .gpl or JASC .pal)");
        if (list.Count == 0) throw new InvalidDataException("the palette file has no colours");
        return list.Take(256).ToArray();
    }

    private static bool TryRgb(string[] p, out int c)
    {
        c = 0;
        if (!byte.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte r) ||
            !byte.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g) ||
            !byte.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b)) return false;
        c = r << 16 | g << 8 | b;
        return true;
    }

    public static void Save(string path, IReadOnlyList<int> colours)
    {
        var cols = colours.Take(256).ToList();
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".gpl")
        {
            var sb = new StringBuilder("GIMP Palette\nName: Flint\nColumns: 16\n#\n");
            foreach (int c in cols) sb.Append($"{c >> 16 & 255,3} {c >> 8 & 255,3} {c & 255,3}\tIndex\n");
            File.WriteAllText(path, sb.ToString());
        }
        else if (ext == ".pal")
        {
            var sb = new StringBuilder($"JASC-PAL\r\n0100\r\n{cols.Count}\r\n");
            foreach (int c in cols) sb.Append($"{c >> 16 & 255} {c >> 8 & 255} {c & 255}\r\n");
            File.WriteAllText(path, sb.ToString());
        }
        else
        {
            var b = new byte[772];
            for (int i = 0; i < cols.Count; i++) { b[i * 3] = (byte)(cols[i] >> 16); b[i * 3 + 1] = (byte)(cols[i] >> 8); b[i * 3 + 2] = (byte)cols[i]; }
            b[768] = (byte)(cols.Count >> 8); b[769] = (byte)cols.Count;
            b[770] = 0xff; b[771] = 0xff;                       // no transparent index
            File.WriteAllBytes(path, b);
        }
    }
}
