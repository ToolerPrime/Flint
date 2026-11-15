/* Symbols: master/instance model, frame rule, caching, undo, .flint v14.
 * Flint.exe --symboltest <dir> */
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Flint;

public static class SymbolTest
{
    private static int _fails;
    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    /// <summary>A 3-frame, 2-layer master: frame f is a solid block of a distinct colour with a soft-alpha edge, plus a mask on layer 2.</summary>
    internal static SymbolMaster Master(string id, int w = 6, int h = 4)
    {
        var d = new Document(w, h);
        d.InsertFrame(1); d.InsertFrame(2);
        var top = d.AddLayer("Top");
        for (int f = 0; f < 3; f++)
        {
            var px = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++) { px[i * 4] = (byte)(f * 90); px[i * 4 + 1] = 40; px[i * 4 + 2] = (byte)(250 - f * 80); px[i * 4 + 3] = (byte)(i % w == 0 ? 120 : 255); }
            d.Layers[0].EnsureCel(f).Objects.Add(new RasterObject(ImageOps.FromPixels(w, h, px)) { Name = "Bitmap" });
            var dot = new byte[4 * 4]; for (int i = 0; i < 4; i++) { dot[i * 4 + 1] = 255; dot[i * 4 + 3] = 255; }
            top.EnsureCel(f).Objects.Add(new RasterObject(ImageOps.FromPixels(2, 2, dot)) { X = f, Y = 1, Name = "dot" });
        }
        return new SymbolMaster(id, "Sym " + id, d);
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);

        Console.WriteLine("Frame rule");
        Check(new SymbolRef("a", SymbolPlay.Loop, 0, 4).FrameIn(3) == 1 && new SymbolRef("a", SymbolPlay.Loop, 0, -1).FrameIn(3) == 2, "loop wraps forwards and backwards");
        Check(new SymbolRef("a", SymbolPlay.Once, 1, 5).FrameIn(3) == 2 && new SymbolRef("a", SymbolPlay.Once, 0, -2).FrameIn(3) == 0, "play once stops on the last frame");
        Check(new SymbolRef("a", SymbolPlay.Single, 7, 9).FrameIn(3) == 2, "single frame holds one frame (clamped)");

        Console.WriteLine("Instances");
        var m = Master("m1");
        var at = SymbolOps.Create(m, Affine.Translate(5, 3), "inst");
        Check(at.X == 5 && at.Y == 3 && ReferenceEquals(at.Pixels, at.Original) && ReferenceEquals(at.Original, SymbolOps.FrameBitmap(m, 0)),
              "a whole-pixel instance shows the cached frame itself (copies share one bitmap)");
        Check(ImageOps.GetPixels(at.Pixels).SequenceEqual(m.Content.RenderPixels(0)), "its pixels are exactly the master's frame");
        var turned = SymbolOps.Make(m, new SymbolRef("m1"), new RasterObject(at.Pixels) { Matrix = Affine.Rotate(90).Then(Affine.Translate(10, 2)) });
        var srcPx = m.Content.RenderPixels(0);
        var want = Transform.Render(srcPx, 6, 4, Affine.Rotate(90).Then(Affine.Translate(10, 2)), Resample.Nearest, () => Transform.Up8(srcPx, 6, 4));
        Check(turned.X == want.x && turned.Y == want.y && ImageOps.GetPixels(turned.Pixels).SequenceEqual(want.px), "a turned instance is the master frame rendered through its matrix");
        Check(at.Moved(2, 1).Symbol == at.Symbol && at.Retransform(Affine.Scale(2, 2), Resample.Nearest).Symbol == at.Symbol &&
              ((RasterObject)at.CloneObject()).Symbol == at.Symbol && at.WithGroups(new[] { "g" }).Symbol == at.Symbol && at.WithEffectList(null).Symbol == at.Symbol,
              "the link survives moving, transforming, copying, grouping and effects");
        Check(at.With(ImageOps.Clone(at.Pixels)).Symbol == null, "a destructive replacement makes plain pixels");
        var adv = SymbolOps.Advance(new Document(1, 1) { Symbols = new[] { m } }, at, 2);
        Check(adv.Symbol!.Offset == 2 && ImageOps.GetPixels(adv.Pixels).SequenceEqual(m.Content.RenderPixels(2)), "advancing two frames shows master frame 3");
        Check(ReferenceEquals(SymbolOps.FrameBitmap(m, 1), SymbolOps.FrameBitmap(m, 1)) &&
              !ReferenceEquals(SymbolOps.FrameBitmap(m with { Content = m.Content.Clone() }, 1), SymbolOps.FrameBitmap(m, 1)),
              "frames are cached per master content; new content renders afresh");

        Console.WriteLine("Document, undo");
        {
            var d = new Document(40, 30) { Symbols = new[] { m } };
            d.Layers[0].EnsureCel(0).Objects.Add(SymbolOps.Create(m, Affine.Translate(1, 1), "a"));
            d.Layers[0].EnsureCel(0).Objects.Add(SymbolOps.Create(m, Affine.Translate(20, 10), "b"));
            Check(SymbolOps.CountInstances(d, "m1") == 2, "instances are counted");
            Check(d.RasterBitmaps().Contains(m.Content.Layers[0].CelAt(0)!.FindRaster()!.Pixels), "the document's bitmaps include its masters' (undo memory counts them)");
            var before = d.Snapshot();
            var edited = m.Content.Clone();
            edited.Layers[0].Cels[0] = new Cel();
            var m2 = m with { Content = edited };
            d.Symbols = new[] { m2 };
            int n = SymbolOps.Refresh(d, "m1");
            Check(n == 2 && ImageOps.GetPixels(((RasterObject)d.Layers[0].CelAt(0)!.Objects[0]).Pixels).SequenceEqual(edited.RenderPixels(0)), "after a master changes, Refresh rebuilds every instance");
            var after = d.Snapshot();
            var edit = new StructureEdit("edit symbol", before, after);
            edit.Undo(d);
            Check(ReferenceEquals(d.Symbols[0], m) && ImageOps.GetPixels(((RasterObject)d.Layers[0].CelAt(0)!.Objects[0]).Pixels).SequenceEqual(m.Content.RenderPixels(0)),
                  "undo brings back the old master and the old instance pixels");
            edit.Redo(d);
            Check(ReferenceEquals(d.Symbols[0], m2), "redo the new one");
        }

        Console.WriteLine("Saved in .flint (format 14)");
        {
            var m1 = Master("aa");
            var mb = Master("bb", 5, 5);
            var d = new Document(48, 32) { Symbols = new[] { m1, mb } };
            for (int i = 1; i < 4; i++) d.InsertFrame(i);
            for (int f = 0; f < 4; f++)
            {
                var cel = d.Layers[0].EnsureCel(f);
                cel.Objects.Add(SymbolOps.Make(m1, new SymbolRef("aa", SymbolPlay.Loop, 0, f), new RasterObject(SymbolOps.FrameBitmap(m1, 0)) { Name = "loop", Matrix = Affine.Translate(2, 2) }));
                cel.Objects.Add(SymbolOps.Make(m1, new SymbolRef("aa", SymbolPlay.Once, 1, f), new RasterObject(SymbolOps.FrameBitmap(m1, 0)) { Name = "once", Opacity = 128, Matrix = Affine.Rotate(90).Then(Affine.Translate(20, 4)) }));
                cel.Objects.Add(SymbolOps.Make(mb, new SymbolRef("bb", SymbolPlay.Single, 2, 0), new RasterObject(SymbolOps.FrameBitmap(mb, 0)) { Name = "single", Matrix = Affine.Scale(2, 2).Then(Affine.Translate(30, 10)) }));
            }
            string path = Path.Combine(dir, "symbols.flint");
            FlintFormat.Save(d, path);
            var back = FlintFormat.Load(path);
            var e = back.Document!;
            Check(back.Ok && e.Symbols.Count == 2 && e.Symbols[0].Name == "Sym aa" && e.Symbols[1].Content.FrameCount == 3 && e.Symbols[1].Content.LayerCount == 2,
                  $"masters come back with their frames and layers ({string.Join("; ", back.Warnings)})");
            Check(Enumerable.Range(0, 4).All(f => e.RenderPixels(f).SequenceEqual(d.RenderPixels(f))), "every frame composites to the same pixels (loop, once, single, turned, scaled, faded)");
            var o = (RasterObject)e.Layers[0].CelAt(3)!.Objects[1];
            Check(o.Symbol == new SymbolRef("aa", SymbolPlay.Once, 1, 3) && o.Opacity == 128, "instance settings round-trip");
            using (var z = ZipFile.OpenRead(path))
            {
                int images = z.Entries.Count(x => x.FullName.StartsWith("images/"));
                int masterImages = 2 * (3 * 2);                       // 2 masters x 3 frames x 2 layers, plain bitmaps
                Check(images == masterImages, $"instances store no pixels; only master bitmaps are saved ({images} images)");
            }
            Patch(path, j => { j["format"] = 13; j.Remove("symbols"); foreach (var l in j["layers"]!.AsArray()) foreach (var c in l!["cels"]!.AsArray()) c!["objects"] = new JsonArray(); return j; });
            var old = FlintFormat.Load(path);
            Check(old.Ok && old.Document!.Symbols.Count == 0, "a format-13 file loads with no symbols");
            FlintFormat.Save(d, path);
            Patch(path, j => { j["symbols"]!.AsArray().RemoveAt(1); return j; });
            var miss = FlintFormat.Load(path);
            Check(miss.Ok && miss.Warnings.Any(w => w.Contains("no symbol")) && miss.Document!.Layers[0].CelAt(0)!.Objects.Count == 3 && !miss.Document.Layers[0].CelAt(0)!.Objects[2].Visible,
                  "an instance whose symbol is missing is dropped with a note (an invisible placeholder keeps the others in place)");
            FlintFormat.Save(d, path);
            Patch(path, j =>
            {
                var inner = j["layers"]![0]!["cels"]![0]!["objects"]![0]!.DeepClone();
                j["symbols"]![0]!["layers"]![0]!["cels"]![0]!["objects"]!.AsArray().Add(inner);
                return j;
            });
            var nest = FlintFormat.Load(path);
            Check(nest.Ok && nest.Warnings.Any(w => w.Contains("inside a symbol")), "a symbol inside a symbol is dropped with a note (not supported)");
            FlintFormat.Save(d, path);
            Patch(path, j => { j["format"] = 15; return j; });
            Check(!FlintFormat.Load(path).Ok, "a newer format is refused");
            FlintFormat.Save(d, path);
            Patch(path, j => { j["symbols"]![0]!["width"] = 0; return j; });
            var dmg = FlintFormat.Load(path);
            Check(dmg.Ok && dmg.Document!.Symbols.Count == 1, "a damaged symbol is dropped, the file still opens");
        }

        Console.WriteLine("Convert to symbol (F8)");
        using var form = new MainForm { SuppressPrompts = true, StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) };
        form.Show();
        Application.DoEvents();
        {
            var d = new Document(40, 30);
            var row = new byte[16 * 1 * 4];
            for (int x = 0; x < 16; x++) { row[x * 4 + 2] = 200; row[x * 4 + 3] = (byte)(x * 17); }        // alpha 0..255
            var a1 = new RasterObject(ImageOps.FromPixels(16, 1, row)) { X = 3, Y = 4, Name = "ramp", Opacity = 200 };
            var a2 = Master("tmp").Content.Layers[0].CelAt(0)!.FindRaster()!.Moved(20, 10);
            d.Layers[0].EnsureCel(0).Objects.Add(a1);
            d.Layers[0].EnsureCel(0).Objects.Add(a2);
            form.TestUseDocument(d);
            var before = d.RenderPixels(0);
            form.TestSelectObjects(0, 1);
            Check(form.ConvertToSymbol("Ramp and box") && d.Symbols.Count == 1 && d.Layers[0].CelAt(0)!.Objects.Count == 1, "two objects become one instance and a master in the library");
            var inst = (RasterObject)d.Layers[0].CelAt(0)!.Objects[0];
            Check(inst.Symbol?.Id == d.Symbols[0].Id && d.Symbols[0].Content.Width == 23 && d.Symbols[0].Content.Height == 10, $"the master is the size of the selection ({d.Symbols[0].Content.Width}x{d.Symbols[0].Content.Height})");
            Check(d.RenderPixels(0).SequenceEqual(before), "the picture is exactly the same, soft alpha and opacity included");
            form.TestUndo();
            Check(d.Symbols.Count == 0 && d.Layers[0].CelAt(0)!.Objects.Count == 2, "undo puts the objects back and removes the symbol");
            form.TestRedo();
            form.TestSelectObjects(0);
            Check(!form.ConvertToSymbol("again"), "an instance cannot go inside another symbol (refused)");
            d.Layers[0].CelAt(0)!.Objects.Add(new RasterObject(ImageOps.FromPixels(16, 1, row)) { Blend = BlendMode.Multiply });
            form.TestSelectObjects(1);
            Check(!form.ConvertToSymbol("mult"), "objects with a blend mode are refused, so nothing changes look");
        }

        Console.WriteLine("Copy to frames, instance options");
        {
            var mm = Master("anim");
            var d = new Document(30, 20) { Symbols = new[] { mm } };
            for (int i = 1; i < 5; i++) d.InsertFrame(i);
            d.Layers[0].EnsureCel(0).Objects.Add(SymbolOps.Create(mm, Affine.Translate(2, 2), "walker"));
            form.TestUseDocument(d);
            form.TestSetCurrent(0);
            form.TestSelectObjects(0);
            form.CopyToFrames(MainForm.CopyTarget.All);
            int Shown(int f) => Enumerable.Range(0, 3).First(k => ImageOps.GetPixels(((RasterObject)d.Layers[0].CelAt(f)!.Objects[0]).Pixels).SequenceEqual(mm.Content.RenderPixels(k)));
            Check(string.Join(",", Enumerable.Range(0, 5).Select(Shown)) == "0,1,2,0,1", "copied to 5 frames, the symbol plays and loops: 1,2,3,1,2");
            form.TestSetCurrent(0); form.TestSelectObjects(0);
            form.SetInstanceOptions(SymbolPlay.Once, 0);
            Check(string.Join(",", Enumerable.Range(0, 5).Select(Shown)) == "0,1,2,2,2", "play once stops on the last frame, across the whole run");
            form.TestSetCurrent(0); form.TestSelectObjects(0);
            form.SetInstanceOptions(SymbolPlay.Single, 2);
            Check(string.Join(",", Enumerable.Range(0, 5).Select(Shown)) == "2,2,2,2,2", "single frame 3 holds it");
            form.TestUndo();
            Check(string.Join(",", Enumerable.Range(0, 5).Select(Shown)) == "0,1,2,2,2", "undo puts the options back");
            using (var frames = new DisposableFrames(Exporter.Render(new ExportJob { Doc = d, From = 0, To = 4 }, null, default)))
                Check(frames.List.Select((f, i) => ImageOps.GetPixels(f.Bmp).SequenceEqual(d.RenderPixels(i))).All(x => x), "exports see the instances as ordinary pictures (every frame)");
        }

        Console.WriteLine("Edit a symbol");
        {
            var mm = Master("edit");
            var other = Master("other");
            var d = new Document(40, 30) { Symbols = new[] { mm, other } };
            d.InsertFrame(1);
            var cel = d.Layers[0].EnsureCel(0);
            cel.Objects.Add(SymbolOps.Create(mm, Affine.Translate(1, 1), "plain"));
            cel.Objects.Add(SymbolOps.Make(mm, new SymbolRef("edit"), new RasterObject(SymbolOps.FrameBitmap(mm, 0)) { Matrix = Affine.Rotate(90).Then(Affine.Translate(30, 2)) }));
            cel.Objects.Add(SymbolOps.Create(other, Affine.Translate(10, 20), "other"));
            var keepOther = cel.Objects[2];
            form.TestUseDocument(d);
            var snapshotBitmaps = d.RasterBitmaps().Select(b => (b, Convert.ToHexString(System.Security.Cryptography.MD5.HashData(ImageOps.GetPixels(b))))).ToList();
            int undo = form.TestUndoCount;
            form.TestSelectObjects(0);
            Check(form.EnterSymbolEdit("edit") && form.EditingSymbol && form.TestDocument.Width == 6, "double-click opens the symbol on its own");
            form.TestMouse(Tool.Pencil, form.TestScreen(1.5, 1.5), form.TestScreen(4.5, 1.5));
            Check(snapshotBitmaps.All(t => Convert.ToHexString(System.Security.Cryptography.MD5.HashData(ImageOps.GetPixels(t.b))) == t.Item2), "painting inside the symbol never touches the document's own bitmaps");
            form.LeaveSymbolEdit();
            var newMaster = d.Symbols.First(x => x.Id == "edit");
            Check(!form.EditingSymbol && !ReferenceEquals(newMaster, mm) && form.TestUndoCount == undo + 1, "Done: the change is one undo step in the document");
            var plain = (RasterObject)d.Layers[0].CelAt(0)!.Objects[0];
            Check(ImageOps.GetPixels(plain.Pixels).SequenceEqual(newMaster.Content.RenderPixels(0)) && !ImageOps.GetPixels(plain.Pixels).SequenceEqual(mm.Content.RenderPixels(0)),
                  "every instance shows the edited symbol");
            Check(ReferenceEquals(d.Layers[0].CelAt(0)!.Objects[2], keepOther), "another symbol's instance is left alone");
            form.TestUndo();
            Check(ReferenceEquals(d.Symbols[0], mm) && ImageOps.GetPixels(((RasterObject)d.Layers[0].CelAt(0)!.Objects[0]).Pixels).SequenceEqual(mm.Content.RenderPixels(0)), "undo brings back the old symbol and its instances");
            form.TestRedo();
            int u2 = form.TestUndoCount;
            form.EnterSymbolEdit("edit");
            form.LeaveSymbolEdit();
            Check(form.TestUndoCount == u2, "opening and closing a symbol without changes adds no undo step");
            form.EnterSymbolEdit("edit");
            form.AddFrames(1, MainForm.AddWhere.AtEnd, duplicate: true);
            string path = Path.Combine(dir, "edit-save.flint");
            form.TestSaveViaEditor(path);
            Check(!form.EditingSymbol && FlintFormat.Load(path).Document!.Symbols.First(x => x.Id == "edit").Content.FrameCount == 4, "saving while editing finishes the edit first and saves it");
        }

        Console.WriteLine("Break apart");
        {
            // A master with two separate objects on two layers: breaks into those objects.
            var two = new Document(12, 4);
            two.Layers[0].EnsureCel(0).Objects.Add(new RasterObject(ImageOps.FromPixels(4, 4, Enumerable.Repeat((byte)200, 64).ToArray())) { Name = "left" });
            two.AddLayer("right").EnsureCel(0).Objects.Add(new RasterObject(ImageOps.FromPixels(4, 4, Enumerable.Repeat((byte)90, 64).ToArray())) { X = 7, Name = "right" });
            var sep = new SymbolMaster("sep", "sep", two);
            var mm = Master("brk");
            var d = new Document(40, 30) { Symbols = new[] { mm, sep } };
            d.Layers[0].EnsureCel(0).Objects.Add(SymbolOps.Create(sep, Affine.Translate(4, 5), "sep"));
            form.TestUseDocument(d);
            var before = d.RenderPixels(0);
            form.TestSelectObjects(0);
            Check(form.BreakApart() && d.Layers[0].CelAt(0)!.Objects.Count == 2 && d.Layers[0].CelAt(0)!.Objects.All(o => o is RasterObject { Symbol: null }), "a symbol of two separate objects breaks into those two objects");
            Check(d.RenderPixels(0).SequenceEqual(before), "the picture stays exactly the same");
            form.TestUndo();
            d.Layers[0].CelAt(0)!.Objects[0] = SymbolOps.Make(mm, new SymbolRef("brk", SymbolPlay.Single, 1), new RasterObject(SymbolOps.FrameBitmap(mm, 0)) { Matrix = Affine.Translate(4, 5) });
            before = d.RenderPixels(0);
            form.TestSelectObjects(0);
            form.BreakApart();
            var flatObj = (RasterObject)d.Layers[0].CelAt(0)!.Objects[0];
            Check(d.Layers[0].CelAt(0)!.Objects.Count == 1 && d.RenderPixels(0).SequenceEqual(before) && !ReferenceEquals(flatObj.Pixels, SymbolOps.FrameBitmap(mm, 1)),
                  "overlapping parts stay one exact picture, with its own pixels");
            form.TestUndo();
            // Masked and Multiply layers inside the symbol: still exact.
            var odd = Master("odd").Content.Clone();
            odd.Layers[1].Blend = BlendMode.Multiply;
            odd.Layers[0].EnsureCel(0).Mask = new RasterObject(ImageOps.FromPixels(6, 4, Enumerable.Repeat((byte)128, 96).ToArray())) { Name = "Mask" };
            var oddM = new SymbolMaster("odd2", "odd", odd);
            d.Symbols = d.Symbols.Append(oddM).ToArray();
            d.Layers[0].CelAt(0)!.Objects[0] = SymbolOps.Make(oddM, new SymbolRef("odd2"), new RasterObject(SymbolOps.FrameBitmap(oddM, 0)) { Matrix = Affine.Translate(4, 5), Opacity = 100 });
            before = d.RenderPixels(0);
            form.TestSelectObjects(0);
            form.BreakApart();
            Check(d.RenderPixels(0).SequenceEqual(before), "a faded instance of a symbol with a masked and a Multiply layer breaks apart with no change");
            form.TestUndo();
            var faded = SymbolOps.Make(mm, new SymbolRef("brk"), new RasterObject(SymbolOps.FrameBitmap(mm, 0)) { Matrix = Affine.Translate(4, 5), Opacity = 100 });
            d.Layers[0].CelAt(0)!.Objects[0] = faded;
            before = d.RenderPixels(0);
            form.TestSelectObjects(0);
            form.BreakApart();
            Check(d.Layers[0].CelAt(0)!.Objects.Count == 1 && d.RenderPixels(0).SequenceEqual(before), "a faded instance with overlapping parts becomes one exact picture");
        }

        Console.WriteLine("Tween instances");
        {
            var mm = Master("tw");
            var d = new Document(80, 60) { Symbols = new[] { mm } };
            var cel = d.Layers[0].EnsureCel(0);
            var a = SymbolOps.Create(mm, Affine.Translate(5, 5), "a");
            var bm = Affine.Scale(2, 2).Then(Affine.Rotate(90)).Then(Affine.Translate(60, 30));
            var b = SymbolOps.Make(mm, new SymbolRef("tw"), new RasterObject(a.Pixels) { Matrix = bm, Opacity = 128 });
            cel.Objects.Add(a); cel.Objects.Add(b);
            form.TestUseDocument(d);
            form.TestSelectObjects(0, 1);
            int undo = form.TestUndoCount;
            Check(form.TweenInstances(4) && d.FrameCount == 5 && d.Layers[0].CelAt(0)!.Objects.Count == 1, "4 new frames; the end instance is replaced by the tween");
            var last = (RasterObject)d.Layers[0].CelAt(4)!.Objects[0];
            Check(Math.Abs(last.X - b.X) <= 1 && Math.Abs(last.Y - b.Y) <= 1 && last.Width == b.Width && last.Height == b.Height && Math.Abs(last.Opacity - 128) <= 1,
                  $"the last frame matches the end instance ({last.X},{last.Y} {last.Width}x{last.Height} vs {b.X},{b.Y} {b.Width}x{b.Height})");
            Check(Enumerable.Range(1, 4).All(f => ((RasterObject)d.Layers[0].CelAt(f)!.Objects[0]).Symbol?.Offset == f), "the symbol keeps playing frame by frame through the tween");
            Check(d.Layers[0].Tweens.Count == 1 && form.TestUndoCount == undo + 1, "it is an editable tween, one undo step");
            form.TestUndo();
            Check(d.FrameCount == 1 && d.Layers[0].CelAt(0)!.Objects.Count == 2, "undo puts both instances back");
            var skew = SymbolOps.Make(mm, new SymbolRef("tw"), new RasterObject(a.Pixels) { Matrix = Affine.Skew(20, 0).Then(Affine.Translate(40, 20)) });
            d.Layers[0].CelAt(0)!.Objects[1] = skew;
            form.TestSelectObjects(0, 1);
            Check(!form.TweenInstances(4) && d.FrameCount == 1, "a skewed end instance is refused with a reason");
        }

        Console.WriteLine("Review cases");
        {
            // Shared layer: instance options work on any frame.
            var mm = Master("sh");
            var d = new Document(30, 20) { Symbols = new[] { mm } };
            for (int i = 1; i < 4; i++) d.InsertFrame(i);
            d.Layers[0].Shared = true;
            d.Layers[0].EnsureCel(0).Objects.Add(SymbolOps.Create(mm, Affine.Translate(1, 1), "bg"));
            form.TestUseDocument(d);
            form.TestSetCurrent(2);
            form.TestSelectObjects(0);
            Check(form.SetInstanceOptions(SymbolPlay.Single, 1), "instance options on a shared layer, away from frame 1");

            // A still drawing copied over frames plays once frames are added to the symbol.
            var still = new SymbolMaster("still", "still", Master("x").Content.Clone());
            while (still.Content.FrameCount > 1) still.Content.RemoveFrame(1);
            var d2 = new Document(30, 20) { Symbols = new[] { still } };
            for (int i = 1; i < 4; i++) d2.InsertFrame(i);
            d2.Layers[0].EnsureCel(0).Objects.Add(SymbolOps.Create(still, Affine.Translate(1, 1), "s"));
            form.TestUseDocument(d2);
            form.TestSetCurrent(0); form.TestSelectObjects(0);
            form.CopyToFrames(MainForm.CopyTarget.All);
            form.EnterSymbolEdit("still");
            form.AddFrames(2, MainForm.AddWhere.AtEnd, duplicate: true);
            form.TestMouse(Tool.Pencil, form.TestScreen(0.5, 0.5));
            form.LeaveSymbolEdit();
            var nm = d2.Symbols[0];
            int Sh(int f) => Enumerable.Range(0, 3).First(k => ImageOps.GetPixels(((RasterObject)d2.Layers[0].CelAt(f)!.Objects[^1]).Pixels).SequenceEqual(nm.Content.RenderPixels(k)));
            Check(Enumerable.Range(0, 4).All(f => ((RasterObject)d2.Layers[0].CelAt(f)!.Objects[^1]).Symbol?.Offset == f), "copies of a one-frame symbol still count their frames");
            Check(Enumerable.Range(0, 4).All(f => ImageOps.GetPixels(((RasterObject)d2.Layers[0].CelAt(f)!.Objects[^1]).Pixels).SequenceEqual(nm.Content.RenderPixels(f % 3))) && Sh(1) == 1,
                  "after adding frames to it, the copies play its frames 1,2,3,1");

            // An instance whose symbol is not in the project (pasted from elsewhere) is saved as a plain picture.
            var d3 = new Document(30, 20);
            d3.Layers[0].EnsureCel(0).Objects.Add(SymbolOps.Create(mm, Affine.Translate(2, 2), "stray"));
            string path = Path.Combine(dir, "stray.flint");
            FlintFormat.Save(d3, path);
            var l3 = FlintFormat.Load(path);
            Check(l3.Ok && l3.Document!.Layers[0].CelAt(0)!.Objects.Count == 1 && l3.Document.RenderPixels(0).SequenceEqual(d3.RenderPixels(0)), "a stray instance is kept as a plain picture when saved");

            // One damaged instance does not stop the file opening.
            var d4 = new Document(30, 20) { Symbols = new[] { mm } };
            d4.Layers[0].EnsureCel(0).Objects.Add(SymbolOps.Create(mm, Affine.Translate(2, 2), "bad"));
            d4.Layers[0].EnsureCel(0).Objects.Add(SymbolOps.Create(mm, Affine.Translate(9, 2), "good"));
            FlintFormat.Save(d4, path);
            Patch(path, j => { j["layers"]![0]!["cels"]![0]!["objects"]![0]!["matrix"] = new JsonArray(1e9, 0, 0, 1e9, 5, 5); return j; });
            var l4 = FlintFormat.Load(path);
            Check(l4.Ok && l4.Warnings.Any(w => w.Contains("damaged placement")) && l4.Document!.Layers[0].CelAt(0)!.Objects.Count == 2 && ((RasterObject)l4.Document.Layers[0].CelAt(0)!.Objects[1]).Symbol != null,
                  "a damaged instance opens as a placeholder with a note; the rest is kept in place");
        }

        Console.WriteLine("Library");
        {
            var mm = Master("lib");
            var d = new Document(40, 30) { Symbols = new[] { mm } };
            form.TestUseDocument(d);
            Check(form.PlaceSymbol("lib", new Point(20, 15)) && d.Layers[0].CelAt(0)!.Objects.Count == 1, "placing from the library adds an instance at the drop point");
            Check(!form.DeleteSymbol("lib"), "a symbol in use cannot be deleted");
            form.RenameSymbol("lib", "Walker");
            Check(d.Symbols[0].Name == "Walker", "renaming a symbol");
            form.TestUndo();
            Check(d.Symbols[0].Name == "Sym lib", "rename is an undo step");
        }

        Console.WriteLine(_fails == 0 ? "all symbol checks passed" : $"{_fails} symbol check(s) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    private static void Patch(string path, Func<JsonObject, JsonObject> f)
    {
        using var z = ZipFile.Open(path, ZipArchiveMode.Update);
        var e = z.GetEntry("project.json")!;
        JsonObject root;
        using (var r = new StreamReader(e.Open())) root = JsonNode.Parse(r.ReadToEnd())!.AsObject();
        e.Delete();
        using var w = new StreamWriter(z.CreateEntry("project.json").Open());
        w.Write(f(root).ToJsonString());
    }
}

internal sealed class DisposableFrames : IDisposable
{
    public readonly List<Frame> List;
    public DisposableFrames(List<Frame> l) { List = l; }
    public void Dispose() { foreach (var f in List) f.Bmp.Dispose(); }
}
