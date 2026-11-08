using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Flint;

public static class EditableObjectsTest
{
    public static int Run()
    {
        int fails = 0;
        void Check(bool ok, string message) { Console.WriteLine($"{(ok ? "  ok   " : "  FAIL ")}{message}"); if (!ok) fails++; }
        var spec = new PrimitiveSpec(false, 20, 12, 0xFF0000FF, false, 2);
        var shape = EditableSources.Shape(spec, Affine.Translate(7, 9));
        Check(shape.Primitive == spec && shape.Vector != null && shape.X == 7 && shape.Y == 9, "shape retains editable parameters and vector source");
        Check(shape.Pixels.GetPixel(0, 0).A == 255 && shape.Pixels.GetPixel(5, 5).A == 0, "outline has an opaque edge and transparent interior");
        var tiny = EditableSources.Shape(new PrimitiveSpec(true, 2, 2, 0xFFFFFFFF, false, 4), Affine.Identity);
        Check(tiny.Vector!.Shapes[0].Contours.Count == 1, "a stroke consuming the shape uses valid solid geometry");
        var moved = shape.Moved(3, 4);
        Check(moved.Primitive == spec && moved.X == 10 && moved.Y == 13 && shape.X == 7, "moves retain shape source without mutating undo data");
        var changed = EditableSources.Replace(shape, new PrimitiveSpec(true, 30, 18, 0xFF00FF00, true, 1), null);
        Check(changed.Primitive!.Ellipse && changed.Matrix == shape.Matrix && shape.Primitive == spec, "editing geometry preserves transform and old parameters");
        Check(((RasterObject)shape.CloneObject()).Primitive == spec && shape.WithEffectList(null).Primitive == spec, "cloning and effect changes retain shape metadata");
        Check(shape.With(ImageOps.Clone(shape.Pixels)).Primitive == null, "raster edits explicitly discard editable shape source");
        var words = new TextSource("Hello", "Arial", 20, false, false, 0xFFCC5533, false);
        var text = EditableSources.Text(words, Affine.Translate(40, 20));
        var edited = EditableSources.Replace(text, null, words with { Text = "World" });
        Check(edited.TextSource!.Text == "World" && text.TextSource!.Text == "Hello" && edited.Matrix == text.Matrix, "text edits retain wording and transform without changing the old object");
        Check(text.Retransform(Affine.Rotate(20), Resample.Smooth).TextSource == words, "rotating text preserves its editable wording");
        var doc = new Document(100, 80);
        doc.Layers[0].EnsureCel(0).Objects.Add(shape);
        doc.Layers[0].EnsureCel(0).Objects.Add(text);
        string path = Path.Combine(Path.GetTempPath(), $"flint-editable-{Guid.NewGuid():N}.flint");
        try
        {
            FlintFormat.Save(doc, path);
            var result = FlintFormat.Load(path);
            var objects = result.Document!.Layers[0].CelAt(0)!.Objects.Cast<RasterObject>().ToArray();
            Check(FlintFormat.CurrentVersion >= 9 && objects[0].Primitive == spec && objects[1].TextSource == words, "format 9 round-trips editable geometry and text");
            Check(ImageOps.GetPixels(objects[0].Pixels).SequenceEqual(ImageOps.GetPixels(shape.Pixels)), "cached shape pixels survive exactly");
            result.Document.Dispose();
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var entry = zip.GetEntry("project.json")!;
                string json; using (var reader = new StreamReader(entry.Open())) json = reader.ReadToEnd();
                var root = JsonNode.Parse(json)!; root["format"] = 8;
                foreach (var obj in root["layers"]![0]!["cels"]![0]!["objects"]!.AsArray()) { obj!.AsObject().Remove("primitive"); obj.AsObject().Remove("textSource"); }
                entry.Delete(); using var writer = new StreamWriter(zip.CreateEntry("project.json").Open()); writer.Write(root.ToJsonString());
            }
            var old = FlintFormat.Load(path);
            Check(old.Ok && old.Document!.Layers[0].CelAt(0)!.Objects.Count == 2, "version 8 objects load without new source fields");
            old.Document?.Dispose();
        }
        finally { File.Delete(path); doc.Dispose(); }
        using var form = new MainForm { SuppressPrompts = true, StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) };
        form.Show(); Application.DoEvents(); form.TestNewCanvas(64, 64); form.TestEditableShapes = true;
        form.TestStroke(Tool.Rectangle, Color.Red, 1, new Point(2, 2), new Point(21, 13));
        form.TestStroke(Tool.Ellipse, Color.Blue, 1, new Point(30, 8), new Point(45, 23));
        var cel = form.TestDocument.Layers[0].CelAt(0)!;
        Check(cel.Objects.Count == 2 && cel.Objects.Cast<RasterObject>().All(r => r.Primitive != null), "canvas shape tools create separate retained objects");
        form.TestMouse(Tool.ObjectSelect, form.TestScreen(2, 5), form.TestScreen(7, 8));
        Check(form.TestSelectedObject?.X == 7 && form.TestSelectedObject.Y == 5 && cel.Objects[1].X == 30, "object drag moves only the picked shape");
        form.TestUndo();
        Check(form.TestDocument.Layers[0].CelAt(0)!.Objects[0].X == 2, "undo restores individual object movement");
        form.TestRedo();
        form.TestMouse(Tool.Move, form.TestScreen(10, 7), form.TestScreen(11, 8));
        Check(form.TestDocument.Layers[0].CelAt(0)!.Objects[0].X == 8 && form.TestDocument.Layers[0].CelAt(0)!.Objects[1].X == 31, "Move (V) still moves the whole layer even when an object was selected");
        form.TestUndo();
        var selected = form.TestSelectedObject!;
        form.TestEditObject(selected.Primitive! with { Width = 24, Filled = true, Colour = 0xFF00FF00 }, null, "Edited rectangle", 8, 9);
        Check(form.TestSelectedObject?.Primitive?.Width == 24 && form.TestSelectedObject.X == 8, "property edit changes geometry and position");
        form.TestUndo();
        Check(form.TestSelectedObject?.Primitive?.Width == 20, "undo restores previous geometry source");
        form.TestSelectObject(0); form.TestCopyAppearance(); form.TestSelectObject(1); form.TestPasteAppearance();
        Check(form.TestSelectedObject?.Primitive?.Colour == 0xFFFF0000 && form.TestSelectedObject.Primitive.Ellipse, "appearance paste changes colour without replacing ellipse geometry");
        form.TestCloneObject();
        Check(form.TestDocument.Layers[0].CelAt(0)!.Objects.Count == 3 && form.TestSelectedObject?.Primitive != null, "clone keeps editable source");
        form.TestAlignObject(CanvasAlign.Right);
        Check(form.TestSelectedObject!.X + form.TestSelectedObject.Width == 64, "selected object aligns to canvas right edge");
        form.TestRasterizeObject();
        Check(form.TestSelectedObject?.Primitive == null && form.TestSelectedObject?.Vector == null, "explicit rasterize removes shape source");
        form.TestUndo();
        Check(form.TestSelectedObject?.Primitive != null, "undo rasterize restores editable geometry");
        form.TestDeleteObject(); form.TestUndo();
        Check(form.TestDocument.Layers[0].CelAt(0)!.Objects.Count == 3, "deleting one object is undoable");
        form.TestAddEditable(EditableSources.Text(words, Affine.Translate(2, 35)));
        form.TestEditObject(null, words with { Text = "Changed" }, "Editable text", 3, 36);
        Check(form.TestSelectedObject?.TextSource?.Text == "Changed" && form.TestSelectedObject.X == 3, "text source can be edited through the real property action");
        form.TestUndo();
        Check(form.TestSelectedObject?.TextSource?.Text == "Hello", "text property undo restores words");
        form.TestSelectObject(0);
        int count = form.TestDocument.Layers[0].CelAt(0)!.Objects.Count;
        form.TestDocument.Layers[0].Locked = true; form.TestCloneObject();
        Check(form.TestDocument.Layers[0].CelAt(0)!.Objects.Count == count, "object actions respect layer lock");
        form.TestDocument.Layers[0].Locked = false;
        int undoBeforePaint = form.TestUndoCount;
        form.TestStroke(Tool.Pencil, Color.Black, 1, new Point(0, 0));
        Check(form.TestDocument.Layers[0].CelAt(0)!.Objects.Cast<RasterObject>().All(r => r.Primitive == null && r.TextSource == null), "painting performs the existing explicit raster merge");
        while (form.TestUndoCount > undoBeforePaint) form.TestUndo();
        Check(form.TestDocument.Layers[0].CelAt(0)!.Objects.Cast<RasterObject>().Any(r => r.TextSource != null), "undo paint restores retained sources");
        Check(CanvasAlignment.Offset(new Rectangle(-8, 3, 9, 7), 20, 16, CanvasAlign.HorizontalCentre) == new Point(14, 0), "alignment handles negative coordinates and midpoint ties");
        form.TestNewCanvas(64, 64); form.TestPixelSnap = false;
        form.TestAddEditable(EditableSources.Shape(spec with { Filled = true }, Affine.Translate(4, 4)));
        form.TestMouse(Tool.ObjectSelect, form.TestScreen(8, 8), form.TestScreen(8.25, 8.25));
        Check(form.TestSelectedObject!.Matrix.Tx == 4.25 && form.TestSelectedObject.Matrix.Ty == 4.25, "snap off preserves fractional positions in the actual object matrix");
        form.Close();
        Console.WriteLine(fails == 0 ? "all editable object checks passed" : $"{fails} editable object checks failed");
        return fails == 0 ? 0 : 1;
    }
}
