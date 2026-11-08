/* Sprite-sheet slicing, atlas packing and metadata. Flint.exe --spritetest <dir> */
using System.Text.Json.Nodes;

namespace Flint;

public static class SpriteTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private static void Blob(byte[] px, int w, Rectangle r, byte a = 255)
    {
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
            {
                int o = (y * w + x) * 4;
                px[o] = (byte)(x * 3); px[o + 1] = (byte)(y * 5); px[o + 2] = 200; px[o + 3] = a;
            }
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        Console.WriteLine("Grid slicing");
        {
            var g = Sprites.Grid(128, 64, 32, 32);
            Check(g.Count == 8 && g[5] == new Rectangle(32, 32, 32, 32), $"128x64 in 32px cells gives 8, row-major ({g.Count})");
            var m = Sprites.Grid(70, 36, 32, 32, margin: 2, spacing: 2);
            Check(m.Count == 2 && m[1].X == 36, $"margin and spacing are respected ({string.Join(" ", m)})");
        }

        Console.WriteLine("Automatic slicing");
        {
            const int W = 120, H = 70;
            var px = new byte[W * H * 4];
            // Row 1: three sprites of different sizes. The second has a detached
            // piece 2px away (a sword), which must stay with it.
            Blob(px, W, new Rectangle(4, 10, 12, 20));
            Blob(px, W, new Rectangle(40, 6, 10, 24));
            Blob(px, W, new Rectangle(52, 12, 4, 4));      // 2px gap from the body
            Blob(px, W, new Rectangle(80, 14, 16, 16));
            // Row 2: two sprites, the right one placed slightly higher.
            Blob(px, W, new Rectangle(10, 44, 14, 20));
            Blob(px, W, new Rectangle(60, 42, 14, 18), 128);   // half-transparent counts as content
            Blob(px, W, new Rectangle(100, 60, 1, 1));         // a stray 1px speck
            var boxes = Sprites.Auto(px, W, H, joinGap: 2, minArea: 4);
            Check(boxes.Count == 5, $"finds 5 sprites, ignoring a 1px speck (got {boxes.Count})");
            {
                // Same idea on a fully opaque sheet with a flat magenta backdrop.
                var solid = new byte[32 * 8 * 4];
                for (int i = 0; i < 32 * 8; i++) { solid[i * 4] = 255; solid[i * 4 + 2] = 255; solid[i * 4 + 3] = 255; }
                foreach (int x0 in new[] { 2, 20 })
                    for (int y = 2; y < 6; y++) for (int x = x0; x < x0 + 4; x++)
                    { int o2 = (y * 32 + x) * 4; solid[o2] = 0; solid[o2 + 1] = 200; solid[o2 + 2] = 0; }
                var sb = Sprites.Auto(solid, 32, 8);
                Check(sb.Count == 2 && sb[0] == new Rectangle(2, 2, 4, 4),
                      $"an opaque sheet with a flat backdrop slices by ignoring the backdrop ({sb.Count})");
            }
            if (boxes.Count == 5)
            {
                Check(boxes[1] == new Rectangle(40, 6, 16, 24), $"a detached piece 2px away joins its sprite ({boxes[1]})");
                Check(boxes[0].X == 4 && boxes[2].X == 80 && boxes[3].X == 10 && boxes[4].X == 60,
                      "sprites come back in reading order, row by row");

                var (fw, fh, frames) = Sprites.Cut(px, W, H, boxes, SliceAnchor.BottomCentre);
                Check(fw == 16 && fh == 24, $"frames are sized to the largest sprite ({fw}x{fh})");
                // Bottom-centre: the 12x20 sprite sits on the bottom row, centred.
                var f0 = frames[0];
                bool bottom = f0[((fh - 1) * fw + 2) * 4 + 3] == 255 && f0[((fh - 21) * fw + 2) * 4 + 3] == 0;
                Check(bottom, "bottom-centre anchoring keeps feet on the same line in every frame");
                var (_, _, top) = Sprites.Cut(px, W, H, boxes, SliceAnchor.TopLeft);
                Check(top[0][3] == 255, "top-left anchoring puts each sprite in the corner");
            }
        }

        Console.WriteLine("Atlas");
        {
            var d = new Document(48, 40);
            for (int i = 1; i < 6; i++) d.InsertFrame(i);
            // Frames 0,2,4 identical (a reused cel); 1,3,5 differ.
            for (int f = 0; f < 6; f++)
            {
                var px = new byte[48 * 40 * 4];
                if (f % 2 == 0) Blob(px, 48, new Rectangle(10, 8, 20, 30));
                else Blob(px, 48, new Rectangle(5 + f * 2, 12, 14 + f, 24), (byte)(120 + f * 20));
                d.Layers[0].EnsureCel(f).Objects.Add(new RasterObject(ImageOps.FromPixels(48, 40, px)));
                d.Frames[f].Delay = 60 + f * 10;
            }
            d.Frames[2].Pivot = new Point(24, 38);
            d.Frames[2].Hitboxes.Add(new Hitbox { Name = "hurt", Rect = new Rectangle(12, 10, 16, 26) });
            d.Tags.Add(new AnimTag { Name = "Idle", Start = 0, End = 2 });
            d.Tags.Add(new AnimTag { Name = "Walk", Start = 3, End = 5 });

            var a = Sprites.Atlas(d, 0, 5, "hero.png");
            Check(a.UniqueFrames == 4, $"identical frames are stored once: 6 frames, {a.UniqueFrames} images");
            long packed = (long)a.Width * a.Height, naive = 48L * 40 * 6;
            Check(packed < naive, $"the packed atlas ({a.Width}x{a.Height}) is smaller than a plain sheet ({naive / 40}x40)");

            var json = JsonNode.Parse(a.Json)!;
            var frames = json["frames"]!.AsObject();
            Check(frames.Count == 6, "the JSON lists all 6 frames");
            Check(json["meta"]!["frameTags"]!.AsArray().Count == 2, "both animation tags are in the JSON");
            Check(json["meta"]!["size"]!["w"]!.GetValue<int>() == a.Width, "JSON size matches the image");

            // The real test: rebuild every frame from the atlas and the JSON alone.
            int rebuilt = 0;
            int idx = 0;
            foreach (var (name, node) in frames)
            {
                var fr = node!["frame"]!;
                var ss = node["spriteSourceSize"]!;
                int x = fr["x"]!.GetValue<int>(), y = fr["y"]!.GetValue<int>();
                int w = fr["w"]!.GetValue<int>(), h = fr["h"]!.GetValue<int>();
                int sx = ss["x"]!.GetValue<int>(), sy = ss["y"]!.GetValue<int>();
                var canvas = new byte[48 * 40 * 4];
                for (int r = 0; r < h; r++)
                    Buffer.BlockCopy(a.Pixels, ((y + r) * a.Width + x) * 4, canvas, ((sy + r) * 48 + sx) * 4, w * 4);
                if (canvas.AsSpan().SequenceEqual(d.RenderPixels(idx))) rebuilt++;
                idx++;
            }
            Check(rebuilt == 6, $"every frame rebuilt from atlas + JSON matches the original exactly ({rebuilt}/6)");

            var f2 = frames.Select(kv => kv.Value).ElementAt(2)!;
            Check(f2["pivot"]?["px"]?.GetValue<int>() == 24 && f2["pivot"]?["py"]?.GetValue<int>() == 38, "the pivot is exported");
            Check(f2["hitboxes"]?.AsArray().Count == 1 && f2["hitboxes"]![0]!["name"]!.GetValue<string>() == "hurt", "the hitbox is exported");
            Check(frames.Select(kv => kv.Value!["duration"]!.GetValue<int>()).SequenceEqual(d.Frames.Select(f => f.Delay)),
                  "every frame's duration is exported");

            // Pivots and hitboxes survive the project file.
            string path = Path.Combine(dir, "sprites.flint");
            FlintFormat.Save(d, path);
            var back = FlintFormat.Load(path);
            Check(back.Ok && back.Document!.Frames[2].Pivot == new Point(24, 38)
                  && back.Document.Frames[2].Hitboxes[0].Rect == new Rectangle(12, 10, 16, 26),
                  "pivot and hitbox survive save and reload");
            back.Document?.Dispose();

            // Sound sidecar for engines that play audio separately.
            var t = new AudioTrack { Name = "SFX" };
            d.Audio.Tracks.Add(t);
            d.Audio.AddClip(t, d.Audio.AddSource(new float[AudioFormat.MsToFrames(50) * 2], "step"), 130);
            var cues = JsonNode.Parse(Sprites.AudioCues(d, 0, 5, "hero.wav"))!;
            var clip = cues["clips"]![0]!;
            // Delays 60, 70, ... so frame 2 starts at exactly 130 ms.
            Check(clip["startMs"]!.GetValue<double>() == 130 && clip["startFrame"]!.GetValue<int>() == 2,
                  $"audio cues give a clip's time and the frame it starts on (frame {clip["startFrame"]})");
            d.Dispose();
        }

        Console.WriteLine(_fails == 0 ? "all sprite checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
