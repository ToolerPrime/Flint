/* Sprite-sheet slicing and texture-atlas export.
 *
 * Slicing finds sprites either on a fixed grid or automatically, as islands
 * of opaque pixels separated by transparency. Each found sprite becomes a
 * frame; frames are aligned on a shared anchor (bottom-centre by default, so
 * a character's feet stay planted) rather than each being centred alone.
 *
 * The atlas packs trimmed frames, stores identical frames once, and writes
 * JSON in Aseprite's "hash" layout, which Godot, Phaser, Unity importers and
 * most engines already read. Pivots, hitboxes and tags ride along.
 */
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flint;

public enum SliceAnchor { BottomCentre, Centre, TopLeft }

public static class Sprites
{
    // ---------------- slicing ----------------

    /// <summary>Cells of a fixed grid, optionally with margin and spacing between cells.</summary>
    public static List<Rectangle> Grid(int w, int h, int cellW, int cellH, int margin = 0, int spacing = 0)
    {
        var cells = new List<Rectangle>();
        if (cellW <= 0 || cellH <= 0) return cells;
        for (int y = margin; y + cellH <= h - margin + spacing && y + cellH <= h; y += cellH + spacing)
            for (int x = margin; x + cellW <= w - margin + spacing && x + cellW <= w; x += cellW + spacing)
                cells.Add(new Rectangle(x, y, cellW, cellH));
        return cells;
    }

    /// <summary>
    /// Find sprites as groups of opaque pixels. Pixels within
    /// <paramref name="joinGap"/> of each other count as one sprite, so a
    /// character with a detached hand or sword stays whole. Returned in
    /// reading order: rows top to bottom, then left to right.
    /// </summary>
    /// <summary>
    /// The top-left colour when the image has no transparent pixel at all,
    /// else null. Auto slicing treats that colour as empty.
    /// </summary>
    public static uint? SolidBackground(byte[] bgra, int w, int h, int alphaThreshold = 1)
    {
        if (w * h == 0) return null;
        for (int i = 3; i < w * h * 4; i += 4)
            if (bgra[i] < alphaThreshold) return null;
        return BitConverter.ToUInt32(bgra, 0);
    }

    public static List<Rectangle> Auto(byte[] bgra, int w, int h, int alphaThreshold = 1, int joinGap = 2, int minArea = 4)
    {
        var seen = new bool[w * h];
        var boxes = new List<Rectangle>();
        var stack = new Stack<int>();
        // A sheet with no transparency at all usually has a flat backdrop
        // (magenta, white…): treat the top-left colour as empty instead.
        uint? bg = SolidBackground(bgra, w, h, alphaThreshold);
        bool Empty(int i) => bg is { } k ? BitConverter.ToUInt32(bgra, i * 4) == k : bgra[i * 4 + 3] < alphaThreshold;
        // joinGap counts empty pixels allowed between parts, so reach one further.
        int reach = joinGap + 1;

        for (int start = 0; start < w * h; start++)
        {
            if (seen[start] || Empty(start)) continue;
            int minX = w, minY = h, maxX = -1, maxY = -1, count = 0;
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                int px = p % w, py = p / w;
                count++;
                if (px < minX) minX = px;
                if (px > maxX) maxX = px;
                if (py < minY) minY = py;
                if (py > maxY) maxY = py;
                // Search a neighbourhood the size of the join gap.
                for (int dy = -reach; dy <= reach; dy++)
                {
                    int ny = py + dy;
                    if (ny < 0 || ny >= h) continue;
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int nx = px + dx;
                        if (nx < 0 || nx >= w) continue;
                        int q = ny * w + nx;
                        if (seen[q] || Empty(q)) continue;
                        seen[q] = true;
                        stack.Push(q);
                    }
                }
            }
            if (count >= minArea) boxes.Add(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1));
        }

        // Reading order. Boxes whose vertical spans overlap belong to one row.
        boxes.Sort((a, b) => a.Y.CompareTo(b.Y));
        var rows = new List<List<Rectangle>>();
        foreach (var b in boxes)
        {
            var row = rows.FirstOrDefault(r => r.Any(o => b.Top < o.Bottom && o.Top < b.Bottom));
            if (row == null) rows.Add(new List<Rectangle> { b });
            else row.Add(b);
        }
        return rows.SelectMany(r => r.OrderBy(b => b.X)).ToList();
    }

    /// <summary>
    /// Cut the rectangles into same-size frames, all aligned on one anchor.
    /// The frame size is the largest sprite, so nothing is cropped.
    /// </summary>
    public static (int w, int h, List<byte[]> frames) Cut(byte[] bgra, int w, int h, List<Rectangle> rects, SliceAnchor anchor)
    {
        int fw = rects.Count == 0 ? 0 : rects.Max(r => r.Width);
        int fh = rects.Count == 0 ? 0 : rects.Max(r => r.Height);
        var frames = new List<byte[]>();
        foreach (var r in rects)
        {
            int ox = anchor == SliceAnchor.TopLeft ? 0 : (fw - r.Width) / 2;
            int oy = anchor switch
            {
                SliceAnchor.BottomCentre => fh - r.Height,
                SliceAnchor.Centre => (fh - r.Height) / 2,
                _ => 0,
            };
            var f = new byte[fw * fh * 4];
            for (int y = 0; y < r.Height; y++)
                Buffer.BlockCopy(bgra, ((r.Y + y) * w + r.X) * 4, f, ((oy + y) * fw + ox) * 4, r.Width * 4);
            frames.Add(f);
        }
        return (fw, fh, frames);
    }

    // ---------------- atlas ----------------

    public sealed class AtlasResult
    {
        public int Width, Height;
        public byte[] Pixels = Array.Empty<byte>();
        public string Json = "";
        public int UniqueFrames;
    }

    /// <summary>
    /// Pack frames into one image: each frame trimmed to its opaque content,
    /// identical frames stored once, placed by a simple shelf packer.
    /// </summary>
    public static AtlasResult Atlas(Document doc, int from, int to, string imageName, int padding = 1)
    {
        int fw = doc.Width, fh = doc.Height;
        var entries = new List<(int frame, Rectangle trim, string hash, byte[] px)>();
        for (int f = from; f <= to; f++)
        {
            var px = doc.RenderPixels(f);
            var trim = ContentRect(px, fw, fh);
            var cut = trim.IsEmpty ? Array.Empty<byte>() : CropBytes(px, fw, trim);
            string hash = trim.IsEmpty ? "empty" : Convert.ToHexString(MD5.HashData(cut)) + trim.Width + "x" + trim.Height;
            entries.Add((f, trim, hash, cut));
        }

        // One slot per distinct image.
        var slots = new Dictionary<string, (Rectangle trim, byte[] px)>();
        foreach (var e in entries)
            if (!e.trim.IsEmpty && !slots.ContainsKey(e.hash)) slots[e.hash] = (e.trim, e.px);

        // Shelf packing, tallest first, into a width near the square root of the area.
        var order = slots.OrderByDescending(s => s.Value.trim.Height).ThenByDescending(s => s.Value.trim.Width).ToList();
        long area = order.Sum(s => (long)(s.Value.trim.Width + padding) * (s.Value.trim.Height + padding));
        int widest = order.Count == 0 ? 1 : order.Max(s => s.Value.trim.Width) + padding;
        int sheetW = Math.Max(widest, (int)Math.Ceiling(Math.Sqrt(area) * 1.1));
        var place = new Dictionary<string, Point>();
        int x = 0, y = 0, shelf = 0;
        foreach (var (hash, (trim, _)) in order)
        {
            if (x + trim.Width > sheetW) { x = 0; y += shelf + padding; shelf = 0; }
            place[hash] = new Point(x, y);
            x += trim.Width + padding;
            shelf = Math.Max(shelf, trim.Height);
        }
        int sheetH = Math.Max(1, y + shelf);
        sheetW = Math.Max(1, sheetW);

        var sheet = new byte[sheetW * sheetH * 4];
        foreach (var (hash, (trim, px)) in slots)
        {
            var p = place[hash];
            for (int r = 0; r < trim.Height; r++)
                Buffer.BlockCopy(px, r * trim.Width * 4, sheet, ((p.Y + r) * sheetW + p.X) * 4, trim.Width * 4);
        }

        // Aseprite "hash" JSON.
        var frames = new JsonObject();
        foreach (var e in entries)
        {
            var spec = doc.Frames[e.frame];
            string name = spec.Name.Length > 0 ? $"{spec.Name} {e.frame}" : $"frame {e.frame}";
            var p = e.trim.IsEmpty ? Point.Empty : place[e.hash];
            var node = new JsonObject
            {
                ["frame"] = Rect(p.X, p.Y, e.trim.Width, e.trim.Height),
                ["rotated"] = false,
                ["trimmed"] = e.trim.Width != fw || e.trim.Height != fh,
                ["spriteSourceSize"] = Rect(e.trim.X, e.trim.Y, e.trim.Width, e.trim.Height),
                ["sourceSize"] = new JsonObject { ["w"] = fw, ["h"] = fh },
                ["duration"] = spec.Delay,
            };
            if (spec.Pivot is { } pv)
                node["pivot"] = new JsonObject
                {
                    ["x"] = Math.Round(pv.X / (double)fw, 4), ["y"] = Math.Round(pv.Y / (double)fh, 4),
                    ["px"] = pv.X, ["py"] = pv.Y,
                };
            if (spec.Hitboxes.Count > 0)
                node["hitboxes"] = new JsonArray(spec.Hitboxes.Select(h => (JsonNode)new JsonObject
                {
                    ["name"] = h.Name, ["x"] = h.Rect.X, ["y"] = h.Rect.Y, ["w"] = h.Rect.Width, ["h"] = h.Rect.Height,
                }).ToArray());
            frames[name] = node;
        }

        var tags = new JsonArray();
        foreach (var t in doc.Tags.Where(t => t.End >= from && t.Start <= to))
            tags.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["from"] = Math.Max(t.Start, from) - from,
                ["to"] = Math.Min(t.End, to) - from,
                ["direction"] = "forward",
                ["color"] = $"#{t.Colour.R:x2}{t.Colour.G:x2}{t.Colour.B:x2}ff",
            });

        var root = new JsonObject
        {
            ["frames"] = frames,
            ["meta"] = new JsonObject
            {
                ["app"] = "Flint",
                ["version"] = "1",
                ["image"] = imageName,
                ["format"] = "RGBA8888",
                ["size"] = new JsonObject { ["w"] = sheetW, ["h"] = sheetH },
                ["scale"] = "1",
                ["frameTags"] = tags,
            },
        };
        return new AtlasResult
        {
            Width = sheetW, Height = sheetH, Pixels = sheet, UniqueFrames = slots.Count,
            Json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
        };
    }

    private static JsonObject Rect(int x, int y, int w, int h) => new() { ["x"] = x, ["y"] = y, ["w"] = w, ["h"] = h };

    public static Rectangle ContentRect(byte[] px, int w, int h)
    {
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (px[(y * w + x) * 4 + 3] == 0) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        return maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static byte[] CropBytes(byte[] px, int w, Rectangle r)
    {
        var o = new byte[r.Width * r.Height * 4];
        for (int y = 0; y < r.Height; y++)
            Buffer.BlockCopy(px, ((r.Y + y) * w + r.X) * 4, o, y * r.Width * 4, r.Width * 4);
        return o;
    }

    /// <summary>Audio timing for engines that play sound separately from a sprite sheet.</summary>
    public static string AudioCues(Document doc, int from, int to, string wavName)
    {
        var d = doc.Frames.Select(f => Math.Max(1, f.Delay)).ToArray();
        double start = Playback.FrameStart(d, from), end = Playback.FrameStart(d, to + 1);
        var clips = new JsonArray();
        foreach (var t in doc.Audio.Tracks)
            foreach (var c in t.Clips)
            {
                if (c.EndMs <= start || c.StartMs >= end) continue;
                int frame = Playback.FrameAt(d, (int)Math.Max(c.StartMs, start));
                clips.Add(new JsonObject
                {
                    ["track"] = t.Name, ["clip"] = c.Name,
                    ["startMs"] = Math.Round(c.StartMs - start, 3),
                    ["lengthMs"] = Math.Round(c.LengthMs, 3),
                    ["startFrame"] = frame - from,
                    ["muted"] = t.Muted,
                });
            }
        var root = new JsonObject
        {
            ["audio"] = wavName,
            ["note"] = "Times are from the first exported frame. The WAV is the full mix of this range.",
            ["durationMs"] = end - start,
            ["clips"] = clips,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
