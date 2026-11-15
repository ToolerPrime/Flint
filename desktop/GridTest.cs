/* View > Grid: nearest-line maths, snapping for marquee, shapes and move,
 * guides winning over the grid, drawing, and the .flint round trip.
 * Flint.exe --gridtest <dir> */
namespace Flint;

public static class GridTest
{
    private static int _fails;
    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);

        Console.WriteLine("Grid maths");
        var g = new GridSpec(Snap: true, SpacingX: 10, SpacingY: 8, OffsetX: 3);
        Check(g.Nearest(14, true) == 13 && g.Nearest(19, true) == 23 && g.Nearest(-1, true) == 3, "nearest vertical line with offset 3 and spacing 10");
        Check(g.Nearest(13, false) == 16 && g.Nearest(11, false) == 8, "nearest horizontal line with spacing 8");
        Check(GuideSet.SnapAxis(17.5, true, null, 64, 2, g) == 13, "a snapping grid always pulls to its nearest line");
        Check(GuideSet.SnapAxis(17.5, true, new[] { new Guide(true, 18) }, 64, 2, g) == 18, "a guide close by wins over the grid");
        Check(GuideSet.SnapAxis(17.5, true, null, 64, 2, g with { Snap = false }) == null, "a grid that does not snap does nothing");
        var j = GridSpec.FromJson(g.ToJson());
        Check(j == g, "grid settings survive JSON");
        Check(GridSpec.FromJson(System.Text.Json.Nodes.JsonNode.Parse("{\"spacingX\":0,\"spacingY\":999999,\"colour\":\"nope\"}")) is { SpacingX: 1, SpacingY: 4096 } bad && bad.Argb == GridSpec.Default.Argb,
              "bad grid values are clamped, a bad colour falls back");

        using var form = new MainForm { SuppressPrompts = true, StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) };
        form.Show();
        Application.DoEvents();

        Console.WriteLine("Snapping tools to the grid");
        {
            form.TestUseDocument(new Document(64, 48));
            form.TestZoom(2);
            form.TestGuideFlags(true, false, false);                  // guide snapping off: only the grid
            form.TestSetGrid(new GridSpec(Show: true, Snap: true, SpacingX: 8, SpacingY: 8));
            form.TestMouse(Tool.Marquee, form.TestScreen(9.5, 6.5), form.TestScreen(22.5, 21.5));
            var sel = form.TestSelection?.Bounds ?? Rectangle.Empty;
            Check(sel == Rectangle.FromLTRB(8, 8, 24, 24), $"marquee corners land on grid lines ({sel})");
            form.TestMouse(Tool.Marquee, form.TestScreen(22.5, 21.5), form.TestScreen(9.5, 6.5));
            sel = form.TestSelection?.Bounds ?? Rectangle.Empty;
            Check(sel == Rectangle.FromLTRB(8, 8, 24, 24), $"dragged up and left, the box is the same, not a pixel bigger ({sel})");
            form.TestSelect(Rectangle.Empty);
            form.TestMouse(Tool.Rectangle, form.TestScreen(17.5, 9.5), form.TestScreen(30.5, 22.5));
            using (var r = form.TestDocument.RenderFrame(0))
            {
                var px = ImageOps.GetPixels(r);
                uint At(int x, int y) => BitConverter.ToUInt32(px, (y * 64 + x) * 4);
                Check(At(16, 16) != 0 && At(15, 16) == 0 && At(16, 8) != 0 && At(16, 7) == 0, "the rectangle tool's corner is on the grid (16, 8)");
            }
            form.TestUseDocument(new Document(64, 48));
            form.TestZoom(2);
            form.TestSetGrid(new GridSpec(Snap: true, SpacingX: 8, SpacingY: 8));
            form.TestMouse(Tool.Rectangle, form.TestScreen(30.5, 22.5), form.TestScreen(17.5, 9.5));
            using (var r2 = form.TestDocument.RenderFrame(0))
            {
                var px = ImageOps.GetPixels(r2);
                uint At(int x, int y) => BitConverter.ToUInt32(px, (y * 64 + x) * 4);
                Check(At(31, 16) != 0 && At(32, 16) == 0 && At(20, 23) != 0 && At(20, 24) == 0, "drawn up and left, the rectangle ends on the grid line, not past it");
            }

            form.TestUseDocument(new Document(64, 48));
            form.TestZoom(2);
            form.TestSetGrid(new GridSpec(Snap: true, SpacingX: 8, SpacingY: 8));
            using var sprite = new Bitmap(6, 4);
            using (var gr = Graphics.FromImage(sprite)) gr.Clear(Color.Red);
            form.TestPlace(sprite, "s", new Point(10, 10), false);
            var o = (RasterObject)form.TestActiveCel!.Objects[^1];
            int sx = o.X, sy = o.Y;
            var from = form.TestScreen(sx + 3, sy + 2);
            form.TestMouse(Tool.Move, from, new Point(from.X + 2 * 9, from.Y + 2 * 5));   // left edge would land at sx+9
            o = (RasterObject)form.TestActiveCel!.Objects[^1];
            Check(o.X % 8 == 0 || (o.X + o.Width) % 8 == 0, $"Move: an edge of the picture lands on the grid ({o.X}..{o.X + o.Width}, was {sx})");
            Check(o.Y % 8 == 0 || (o.Y + o.Height) % 8 == 0, $"Move: and vertically too ({o.Y}..{o.Y + o.Height})");
            form.TestUndo();
            form.TestSetGrid(new GridSpec(Snap: false, SpacingX: 8, SpacingY: 8));
            form.TestMouse(Tool.Move, from, new Point(from.X + 2 * 9, from.Y + 2 * 5));
            o = (RasterObject)form.TestActiveCel!.Objects[^1];
            Check(o.X == sx + 9 && o.Y == sy + 5, $"with grid snapping off, Move lands where dragged ({o.X}, {o.Y})");
        }

        Console.WriteLine("Drawing and saving");
        {
            var d = new Document(40, 30);
            form.TestUseDocument(d);
            form.TestSetGrid(new GridSpec(Show: true, SpacingX: 5, SpacingY: 7, OffsetX: 2, OffsetY: -3, Argb: unchecked((int)0x80FF0000)));
            form.TestZoom(4);
            using (var bmp = new Bitmap(600, 500))
            {
                form.TestPaintCanvas(bmp);
                var org = form.TestCanvasOrigin;
                Color P(int ix, int iy, int dx, int dy) => bmp.GetPixel(org.X + ix * 4 + dx, org.Y + iy * 4 + dy);
                // Lines at x = 2, 7, 12 ... and y = 4, 11, 18 ... (offset -3 wraps to 4).
                // GDI+ may put a 1-px line on either side of the edge (half-pixel offset), so look at both.
                Color Red(Color a, Color b) => a.R >= b.R ? a : b;
                Color on = Red(P(7, 1, 0, 2), P(7, 1, -1, 2)), off = P(9, 1, 0, 2), onY = Red(P(1, 11, 2, 0), P(1, 11, 2, -1)), offY = P(1, 13, 2, 0);
                Check(on.R > off.R + 30 && onY.R > offY.R + 30, $"grid lines are drawn on x = 2 + 5k and, with offset -3, y = 4 + 7k (line {on}, between {off})");
            }
            int undo = form.TestUndoCount;
            form.TestSetGrid(d.Grid with { Snap = true });
            Check(form.TestUndoCount == undo, "grid changes are settings, not undo steps");
            string path = Path.Combine(dir, "grid.flint");
            FlintFormat.Save(d, path);
            var back = FlintFormat.Load(path);
            Check(back.Ok && back.Document!.Grid == d.Grid, $"the grid is saved with the document ({back.Document?.Grid})");
            // A version-12 file (no grid) and files with broken grid values load with the default grid.
            foreach (var (label, patch) in new (string, Func<System.Text.Json.Nodes.JsonObject, System.Text.Json.Nodes.JsonObject>)[]
            {
                ("version 12", j2 => { j2["format"] = 12; j2.Remove("grid"); return j2; }),
                ("grid is a number", j2 => { j2["grid"] = 5; return j2; }),
                ("spacing is text", j2 => { j2["grid"] = System.Text.Json.Nodes.JsonNode.Parse("{\"spacingX\":\"x\",\"show\":0}"); return j2; }),
            })
            {
                FlintFormat.Save(d, path);
                using (var z = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update))
                {
                    var e = z.GetEntry("project.json")!;
                    System.Text.Json.Nodes.JsonObject root;
                    using (var r = new StreamReader(e.Open())) root = System.Text.Json.Nodes.JsonNode.Parse(r.ReadToEnd())!.AsObject();
                    e.Delete();
                    using var w = new StreamWriter(z.CreateEntry("project.json").Open());
                    w.Write(patch(root).ToJsonString());
                }
                var res = FlintFormat.Load(path);
                Check(res.Ok && res.Document!.Grid == GridSpec.Default, $"{label}: opens with the default grid");
            }
            // Crop keeps the grid on the content.
            var c = new Document(40, 30) { Grid = new GridSpec(Show: true, SpacingX: 10, SpacingY: 10) };
            form.TestUseDocument(c);
            CanvasEdits.Fit(c, new Rectangle(3, 4, 20, 20));
            Check(c.Grid.OffsetX == 7 && c.Grid.OffsetY == 6, $"cropping 3,4 from the top left moves the grid with the content (offset {c.Grid.OffsetX},{c.Grid.OffsetY})");
        }

        Console.WriteLine(_fails == 0 ? "all grid checks passed" : $"{_fails} grid check(s) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
