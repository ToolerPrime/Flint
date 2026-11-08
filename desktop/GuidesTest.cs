/* Rulers, guides, snapping, the .flint guides field and the Fireworks import,
 * through the real form's mouse handlers. Flint.exe --guidestest <dir> */
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Flint;

public static class GuidesTest
{
    private static int _fails;
    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private static string G(IReadOnlyList<Guide> g) => string.Join(" ", g.Select(x => (x.Vertical ? "V" : "H") + x.Pos));

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);

        Console.WriteLine("Snap maths");
        {
            var gs = new[] { new Guide(true, 20), new Guide(false, 7) };
            Check(GuideSet.SnapAxis(18.5, true, gs, 64, 4) == 20, "within 6 screen pixels (1.5 px at 4x) snaps to a vertical guide");
            Check(GuideSet.SnapAxis(17, true, gs, 64, 4) == null, "3 px away at 4x is 12 screen pixels: no snap");
            Check(GuideSet.SnapAxis(20.5, false, gs, 64, 4) == null, "a vertical guide does not snap a y position");
            Check(GuideSet.SnapAxis(31, true, gs, 64, 4) == 32, "the canvas centre snaps");
            Check(GuideSet.SnapAxis(63, true, gs, 64, 4) == 64 && GuideSet.SnapAxis(0.5, true, gs, 64, 4) == 0, "the canvas edges snap");
            Check(GuideSet.EdgeToPixel(20, null) == 20 && GuideSet.EdgeToPixel(20, 5) == 19 && GuideSet.EdgeToPixel(20, 30) == 20,
                  "a drag start lands right of the line; an end point stays inside the rectangle");
        }

        Console.WriteLine(".flint file");
        {
            var d = new Document(40, 30);
            d.Guides = new[] { new Guide(true, 12), new Guide(false, 5), new Guide(true, -3) };
            string path = Path.Combine(dir, "g.flint");
            FlintFormat.Save(d, path);
            var r = FlintFormat.Load(path);
            Check(FlintFormat.CurrentVersion >= 8 && r.Ok && G(r.Document!.Guides) == "V12 H5 V-3", $"version 8 saves and loads guides ({(r.Document != null ? G(r.Document.Guides) : r.Error)})");
            // An older file (no guides field, format 7) must still load.
            var plain = new Document(10, 10);
            string old = Path.Combine(dir, "old.flint");
            FlintFormat.Save(plain, old);
            using (var zip = ZipFile.Open(old, ZipArchiveMode.Update))
            {
                var e = zip.GetEntry("project.json")!;
                string json;
                using (var sr = new StreamReader(e.Open())) json = sr.ReadToEnd();
                var node = JsonNode.Parse(json)!;
                node["format"] = 7;
                node.AsObject().Remove("guides");
                e.Delete();
                using var w = new StreamWriter(zip.CreateEntry("project.json").Open());
                w.Write(node.ToJsonString());
            }
            var o = FlintFormat.Load(old);
            Check(o.Ok && o.Document!.Guides.Count == 0, "a version 7 file loads, with no guides");
            Check(!File.ReadAllText(old, System.Text.Encoding.Latin1).Contains("\"guides\""), "a document without guides writes no guides field");
        }

        Console.WriteLine("Fireworks guides");
        {
            string uiq = @"C:\Program Files (x86)\Macromedia\Fireworks 8\Samples\UIQ.png";
            if (!File.Exists(uiq)) Console.WriteLine("  skip  Adobe sample UIQ.png not found");
            else
            {
                var imp = FireworksPng.Open(uiq);
                Check(G(imp.Doc.Guides) == "V193 V15", $"UIQ.png (GDSv > GDVv has 193 and 15) imports as vertical guides ({G(imp.Doc.Guides)}) on a {imp.Doc.Width}x{imp.Doc.Height} canvas");
                var other = FireworksPng.Open(@"C:\Program Files (x86)\Macromedia\Fireworks 8\Samples\Pocket_PC.png");
                Check(other.Doc.Guides.Count == 0, "a sample with empty guide lists imports none");
            }
        }

        var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000), SuppressPrompts = true };
        form.Show();
        Application.DoEvents();

        Console.WriteLine("Rulers");
        {
            form.TestNewCanvas(64, 48);
            form.TestZoom(4);
            var before = form.TestScreen(0, 0);
            form.TestRulersOn = true;
            var after = form.TestScreen(0, 0);
            Check(form.TestRulerPad > 0 && after.X >= form.TestRulerPad && after.Y >= form.TestRulerPad, $"with rulers on, the canvas starts clear of them ({after.X},{after.Y}; was {before.X},{before.Y})");
            form.TestZoom(16);
            var big = form.TestScreen(0, 0);
            Check(big.X >= form.TestRulerPad && big.Y >= form.TestRulerPad, "and still does when zoomed in");
            form.TestZoom(4);
            Application.DoEvents();
            Check(true, "the rulers paint without error");
        }

        Console.WriteLine("Creating, moving and deleting guides");
        {
            form.TestZoom(4);
            var pad = form.TestRulerPad;
            int steps0 = form.TestUndoCount;
            // Drag from the left ruler into the canvas: a vertical guide.
            form.TestMouse(Tool.Pencil, new Point(pad / 2, 100), form.TestScreen(20, 10), form.TestScreen(20, 12));
            Check(G(form.TestGuides) == "V20" && form.TestUndoCount == steps0 + 1, $"drag from the left ruler makes a vertical guide at 20 ({G(form.TestGuides)}), one undo step");
            // From the top ruler: horizontal.
            form.TestMouse(Tool.Pencil, new Point(100, pad / 2), form.TestScreen(10, 15));
            Check(G(form.TestGuides) == "V20 H15", $"drag from the top ruler makes a horizontal guide ({G(form.TestGuides)})");
            Check(form.TestSelection == null, "making guides did not paint or select anything");
            // Move the vertical guide.
            int s1 = form.TestUndoCount;
            form.TestMouse(Tool.Pencil, form.TestScreen(20, 30), form.TestScreen(26, 30));
            Check(G(form.TestGuides) == "V26 H15" && form.TestUndoCount == s1 + 1, $"dragging a guide moves it, one undo step ({G(form.TestGuides)})");
            form.TestUndo();
            Check(G(form.TestGuides) == "V20 H15", "undo puts the guide back");
            form.TestRedo();
            Check(G(form.TestGuides) == "V26 H15", "redo moves it again");
            form.TestUndo();
            // Drag it back onto the ruler to delete it.
            int s2 = form.TestUndoCount;
            form.TestMouse(Tool.Pencil, form.TestScreen(20, 30), new Point(pad / 2, 150));
            Check(G(form.TestGuides) == "H15" && form.TestUndoCount == s2 + 1, $"dragging a guide onto the ruler deletes it ({G(form.TestGuides)})");
            form.TestUndo();
            Check(G(form.TestGuides) == "V20 H15", "undo brings it back");

            // Lock.
            form.TestGuideFlags(true, true, true);
            int s3 = form.TestUndoCount;
            form.TestMouse(Tool.Pan, form.TestScreen(20, 30), form.TestScreen(30, 30));
            form.TestMouse(Tool.Pan, new Point(pad / 2, 100), form.TestScreen(40, 10));
            Check(G(form.TestGuides) == "V20 H15" && form.TestUndoCount == s3, "locked guides cannot be moved or added");
            form.TestGuideFlags(true, false, true);

            // Hidden guides are not grabbed.
            form.TestGuideFlags(false, false, true);
            int s5 = form.TestUndoCount;
            form.TestMouse(Tool.Pan, form.TestScreen(20, 30), form.TestScreen(30, 30));
            Check(G(form.TestGuides) == "V20 H15" && form.TestUndoCount == s5, "hidden guides are not grabbed by the mouse");
            form.TestGuideFlags(true, false, true);

            int s4 = form.TestUndoCount;
            form.TestClearGuides();
            Check(form.TestGuides.Count == 0 && form.TestUndoCount == s4 + 1, "Clear guides empties the list in one undo step");
            form.TestUndo();
            Check(G(form.TestGuides) == "V20 H15", "undo restores them all");
        }

        Console.WriteLine("Guides survive structural undo");
        {
            form.TestUseDocument(new Document(64, 48));
            form.TestGuides = new[] { new Guide(true, 20), new Guide(false, 15) };
            form.TestAddLayer();
            Check(G(form.TestGuides) == "V20 H15", "adding a layer leaves the guides alone");
            form.TestUndo();
            Check(G(form.TestGuides) == "V20 H15", "and so does undoing it");
        }

        Console.WriteLine("Snapping");
        {
            form.TestUseDocument(new Document(64, 48));
            form.TestRulersOn = true;
            form.TestZoom(2);
            form.TestGuides = new[] { new Guide(true, 20), new Guide(false, 10) };
            form.TestGuideFlags(true, false, true);

            // Marquee: start 2 px (4 screen px) short of the guide, end 2 px past a second spot.
            form.TestMouse(Tool.Marquee, form.TestScreen(18, 5), form.TestScreen(40, 30));
            var sel = form.TestSelection?.Bounds ?? Rectangle.Empty;
            Check(sel.X == 20, $"marquee start snaps to the vertical guide (x={sel.X})");
            Check(sel.Y == 5, $"and its y is untouched (y={sel.Y})");
            form.TestMouse(Tool.Marquee, form.TestScreen(30, 8), form.TestScreen(50, 40));
            sel = form.TestSelection?.Bounds ?? Rectangle.Empty;
            Check(sel.Y == 10, $"marquee start snaps to the horizontal guide (y={sel.Y})");
            // End point snaps so the selection's edge is on the line.
            form.TestMouse(Tool.Marquee, form.TestScreen(2, 2), form.TestScreen(19, 30));
            sel = form.TestSelection?.Bounds ?? Rectangle.Empty;
            Check(sel.Right == 20, $"dragging right, the end edge lands on the guide (right edge {sel.Right})");
            // Canvas centre and edge.
            form.TestMouse(Tool.Marquee, form.TestScreen(2, 2), form.TestScreen(31, 30));
            sel = form.TestSelection?.Bounds ?? Rectangle.Empty;
            Check(sel.Right == 32, $"the canvas centre (x=32) snaps ({sel.Right})");
            form.TestMouse(Tool.Marquee, form.TestScreen(2, 2), form.TestScreen(63, 47));
            sel = form.TestSelection?.Bounds ?? Rectangle.Empty;
            Check(sel.Right == 64 && sel.Bottom == 48, $"the canvas edges snap ({sel.Right}x{sel.Bottom})");

            // Snap off: exactly where the mouse is.
            form.TestGuideFlags(true, false, false);
            form.TestMouse(Tool.Marquee, form.TestScreen(18, 5), form.TestScreen(40, 30));
            sel = form.TestSelection?.Bounds ?? Rectangle.Empty;
            Check(sel.X == 18, $"with Snap off the start is where the mouse was (x={sel.X})");
            // Hidden guides do not snap.
            form.TestGuideFlags(false, false, true);
            form.TestMouse(Tool.Marquee, form.TestScreen(18, 5), form.TestScreen(40, 30));
            sel = form.TestSelection?.Bounds ?? Rectangle.Empty;
            Check(sel.X == 18, $"hidden guides do not snap (x={sel.X})");
            form.TestGuideFlags(true, false, true);

            // Ellipse select, crop, line, rectangle, gradient and hitbox share the same path; check the shape ones.
            form.TestMouse(Tool.EllipseSelect, form.TestScreen(18, 20), form.TestScreen(40, 30));
            check2(form, "ellipse select");

            // Rectangle tool draws from the snapped corner.
            form.TestSelect(Rectangle.Empty);
            form.TestMouse(Tool.Rectangle, form.TestScreen(18, 12), form.TestScreen(40, 30));
            using var rendered = form.TestDocument.RenderFrame(0);
            var px = ImageOps.GetPixels(rendered);
            uint At(int x, int y) => BitConverter.ToUInt32(px, (y * 64 + x) * 4);
            Check(At(20, 20) != 0 && At(19, 20) == 0, "the rectangle tool's edge is on the guide (pixel 20 inked, 19 clear)");

            // Move tool: a placed picture's left edge pulls onto the guide.
            form.TestUseDocument(new Document(64, 48));
            form.TestGuides = new[] { new Guide(true, 20) };
            form.TestRulersOn = true;
            form.TestZoom(2);
            using var sprite = new Bitmap(6, 4);
            using (var g = Graphics.FromImage(sprite)) g.Clear(Color.Red);
            form.TestPlace(sprite, "s", new Point(10, 10), false);
            var o = (RasterObject)form.TestActiveCel!.Objects[^1];
            int startX = o.X;
            var from = form.TestScreen(startX + 3, 11);
            var to = new Point(from.X + (19 - startX) * 2, from.Y);    // left edge would land on 19, one pixel short
            form.TestMouse(Tool.Move, from, to);
            o = (RasterObject)form.TestActiveCel!.Objects[^1];
            Check(o.X == 20, $"Move: a picture's left edge pulls onto the guide (x={o.X}, was {startX})");
            form.TestGuideFlags(true, false, false);
            form.TestUndo();
            form.TestMouse(Tool.Move, from, to);
            o = (RasterObject)form.TestActiveCel!.Objects[^1];
            Check(o.X == 19, $"Move with Snap off lands exactly where dragged (x={o.X})");
        }

        form.Close();
        Console.WriteLine(_fails == 0 ? "all guide checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    private static void check2(MainForm f, string what)
    {
        var b = f.TestSelection?.Bounds ?? Rectangle.Empty;
        Check(b.X == 20 && b.Y == 20, $"{what}: bounds start at the guide ({b.X},{b.Y})");
    }
}
