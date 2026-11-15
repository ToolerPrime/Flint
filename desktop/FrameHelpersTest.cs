/* Frame helpers: Animate Selection, Distribute to Frames, Copy to Frames,
 * Reverse Frames (range), Add Frames, Include when exporting (every
 * exporter, .flint, undo). Flint.exe --framehelperstest <dir> */
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Flint;

public static class FrameHelpersTest
{
    private static int _fails;
    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private const int W = 40, H = 30;

    private static RasterObject Box(int x, int y, int w, int h, Color c)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255; }
        return new RasterObject(ImageOps.FromPixels(w, h, px)) { X = x, Y = y, Name = "box" };
    }

    private static MainForm Form(Document d)
    {
        var form = new MainForm { SuppressPrompts = true, StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) };
        form.Show();
        Application.DoEvents();
        form.TestUseDocument(d);
        return form;
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);

        Console.WriteLine("Animate selection");
        {
            var d = new Document(W, H);
            d.Layers[0].EnsureCel(0).Objects.Add(Box(4, 10, 8, 4, Color.Red));
            using var form = Form(d);
            form.TestSelectObjects(0);
            int undo = form.TestUndoCount;
            bool ok = form.AnimateSelection(new MainForm.AnimateSpec { TotalFrames = 5, Distance = 12, AngleDeg = 90, ScaleTo = 200, OpacityFrom = 100, OpacityTo = 0, Rotate = 90 });
            var objs = Enumerable.Range(0, 5).Select(f => (RasterObject)d.Layers[0].CelAt(f)!.Objects[0]).ToArray();
            Check(ok && d.FrameCount == 5 && form.TestCurrentFrame == 4, $"5 frames in total: this one and 4 new ({d.FrameCount})");
            Check(objs[0].X == 4 && objs[0].Y == 10 && objs[0].Width == 8 && objs[0].Opacity == 255, "frame 1 keeps the object where it was, fully visible");
            Check(objs[4].Opacity == 0 && Math.Abs(objs[2].Opacity - 128) <= 1, $"opacity fades 100% -> 0% (middle {objs[2].Opacity * 100 / 255}%)");
            Check(objs[4].Width == 8 && objs[4].Height == 16, $"scale 200% and a quarter turn: 8x4 ends 8x16 ({objs[4].Width}x{objs[4].Height})");
            double cy0 = objs[0].Y + objs[0].Height / 2.0, cy4 = objs[4].Y + objs[4].Height / 2.0;
            double cx0 = objs[0].X + objs[0].Width / 2.0, cx4 = objs[4].X + objs[4].Width / 2.0;
            Check(Math.Abs(cy4 - (cy0 - 12)) <= 1 && Math.Abs(cx4 - cx0) <= 1, $"direction 90 moves it 12 px up, about its centre ({cx0},{cy0} -> {cx4},{cy4})");
            Check(objs[1].Width > 8 && objs[1].Width < objs[3].Width + 20, "middle frames are in between");
            form.TestUndo();
            Check(d.FrameCount == 1 && form.TestUndoCount == undo, "one undo step removes it all");
            form.TestSelectObjects(0);
            Check(!form.AnimateSelection(new MainForm.AnimateSpec { TotalFrames = 4, Distance = 0 }) && d.FrameCount == 1, "nothing to change: refused, nothing added");
        }
        {
            // Generated frames own their pixels: painting the start frame must not reach them.
            foreach (var (label, spec) in new[]
            {
                ("move only", new MainForm.AnimateSpec { TotalFrames = 4, Distance = 6 }),
                ("scale and rotate", new MainForm.AnimateSpec { TotalFrames = 4, Distance = 6, ScaleTo = 150, Rotate = 45 }),
            })
            {
                var d = new Document(W, H);
                var full = new byte[W * H * 4];
                for (int y = 8; y < 14; y++) for (int x = 6; x < 16; x++) { int o = (y * W + x) * 4; full[o + 2] = 200; full[o + 3] = 255; }
                var start = new RasterObject(ImageOps.FromPixels(W, H, full)) { Name = "Bitmap" };   // a plain, paintable full-canvas bitmap
                d.Layers[0].EnsureCel(0).Objects.Add(start);
                using var form = Form(d);
                form.TestSelectObjects(0);
                form.AnimateSelection(spec);
                var steps = Enumerable.Range(1, 3).Select(f => (RasterObject)d.Layers[0].CelAt(f)!.Objects[0]).ToArray();
                Check(steps.All(s => !ReferenceEquals(s.Pixels, start.Pixels) && !ReferenceEquals(s.Original, start.Pixels)),
                      $"{label}: no generated frame shares the start frame's bitmap");
                var before = steps.Select(s => ImageOps.GetPixels(s.Pixels)).ToArray();
                using (var g = Graphics.FromImage(start.Pixels)) g.Clear(Color.Lime);     // stand-in for a paint stroke on frame 1
                Check(steps.Select((s, i) => ImageOps.GetPixels(s.Pixels).SequenceEqual(before[i])).All(x => x), $"{label}: changing frame 1's pixels leaves the others alone");
            }
        }

        Console.WriteLine("Distribute to frames, copy to frames");
        {
            var d = new Document(W, H);
            var cel = d.Layers[0].EnsureCel(0);
            cel.Objects.Add(Box(1, 1, 4, 4, Color.Red)); cel.Objects.Add(Box(10, 1, 4, 4, Color.Lime)); cel.Objects.Add(Box(20, 1, 4, 4, Color.Blue));
            d.InsertFrame(1);
            using var form = Form(d);
            form.TestSelectObjects(0, 1, 2);
            Check(form.DistributeToFrames() && d.FrameCount == 3, $"three objects over three frames (one frame added): {d.FrameCount}");
            Check(Enumerable.Range(0, 3).All(f => d.Layers[0].CelAt(f)?.Objects.Count == 1) &&
                  d.Layers[0].CelAt(1)!.Objects[0].X == 10 && d.Layers[0].CelAt(2)!.Objects[0].X == 20, "bottom object stays, the next ones go to the next frames in order");
            form.TestUndo();
            Check(d.FrameCount == 2 && d.Layers[0].CelAt(0)!.Objects.Count == 3, "undo puts them back");

            form.TestSetCurrent(0);
            form.TestSelectObjects(1);
            d.InsertFrame(2); d.InsertFrame(3);
            Check(form.CopyToFrames(MainForm.CopyTarget.All) && Enumerable.Range(1, 3).All(f => d.Layers[0].CelAt(f)?.Objects.Count == 1), "copy to all frames");
            var copy = d.Layers[0].CelAt(2)!.Objects[0];
            Check(!ReferenceEquals(copy, d.Layers[0].CelAt(0)!.Objects[1]) && copy.X == 10, "copies are separate objects in the same place");
            form.TestSetCurrent(2);
            form.TestSelectObjects(0);
            form.CopyToFrames(MainForm.CopyTarget.Next);
            form.CopyToFrames(MainForm.CopyTarget.Previous);
            Check(d.Layers[0].CelAt(3)!.Objects.Count == 2 && d.Layers[0].CelAt(1)!.Objects.Count == 2, "copy to next and previous frame");
            form.CopyToFrames(MainForm.CopyTarget.Range, 0, 1);
            Check(d.Layers[0].CelAt(0)!.Objects.Count == 4 && d.Layers[0].CelAt(1)!.Objects.Count == 3 && d.Layers[0].CelAt(2)!.Objects.Count == 1, "copy to a range skips the frame it came from");
        }

        Console.WriteLine("Reverse frames, add frames");
        {
            var d = new Document(W, H);
            for (int i = 1; i < 5; i++) d.InsertFrame(i);
            for (int i = 0; i < 5; i++) { d.Frames[i].Name = "f" + i; d.Layers[0].EnsureCel(i).Objects.Add(Box(i, 0, 2, 2, Color.Red)); }
            using var form = Form(d);
            form.TestSetCurrent(1);
            form.ReverseFrames(1, 3);
            Check(string.Join(",", d.Frames.Select(f => f.Name)) == "f0,f3,f2,f1,f4" && d.Layers[0].CelAt(1)!.Objects[0].X == 3 && form.TestCurrentFrame == 3,
                  "reversing frames 2-4 leaves 1 and 5; cels and the current frame follow");
            d.Tags.Add(new AnimTag { Name = "Start", Start = 0, End = 1 });
            form.ReverseFrames(0, 4);
            Check(string.Join(",", d.Frames.Select(f => f.Name)) == "f4,f1,f2,f3,f0", "reverse all");
            Check(d.Tags[0].Start == 3 && d.Tags[0].End == 4, $"a tag keeps its own frames when they are reversed ({d.Tags[0].Start + 1}-{d.Tags[0].End + 1})");

            form.TestSetCurrent(2);
            form.AddFrames(2, MainForm.AddWhere.BeforeCurrent, duplicate: false);
            Check(d.FrameCount == 7 && d.Layers[0].CelAt(2) == null && d.Layers[0].CelAt(3) == null && d.Frames[4].Name == "f2", "2 empty frames before the current one");
            form.TestSetCurrent(4);
            form.AddFrames(3, MainForm.AddWhere.AfterCurrent, duplicate: true);
            Check(d.FrameCount == 10 && Enumerable.Range(5, 3).All(f => d.Layers[0].CelAt(f)?.Objects[0].X == 2 && !ReferenceEquals(d.Layers[0].CelAt(f), d.Layers[0].CelAt(4))),
                  "3 copies after the current frame, each its own cel");
            form.AddFrames(1, MainForm.AddWhere.AtEnd, duplicate: false);
            Check(d.FrameCount == 11 && form.TestCurrentFrame == 10, "1 empty frame at the end");
            form.TestUndo(); form.TestUndo(); form.TestUndo();
            Check(d.FrameCount == 5, "each add is one undo step");
        }

        Console.WriteLine("Include when exporting");
        {
            var d = new Document(W, H);
            for (int i = 1; i < 4; i++) d.InsertFrame(i);
            for (int i = 0; i < 4; i++) { d.Frames[i].Delay = 100 + i * 10; d.Layers[0].EnsureCel(i).Objects.Add(Box(i * 5, 0, 4, 4, Color.FromArgb(50 * i, 200, 30))); }
            d.Tags.Add(new AnimTag { Name = "Walk", Start = 1, End = 2 });
            var t = new AudioTrack { Name = "Music" }; d.Audio.Tracks.Add(t);
            d.Audio.AddClip(t, d.Audio.AddSource(new float[AudioFormat.MsToFrames(1000) * 2], "s"), 0);
            using var form = Form(d);
            form.TestSelectFrameRange(1, 1);
            form.SetFramesExported(false);
            Check(!d.Frames[1].Export && d.Frames.Count(f => f.Export) == 3, "frame 2 left out");
            form.TestSelectFrameRange(0, 3);
            int before = form.TestUndoCount;
            form.SetFramesExported(false);
            Check(d.Frames.Count(f => f.Export) == 3 && form.TestUndoCount == before, "cannot leave every frame out");
            string P(string n) => Path.Combine(dir, n);
            var job = new ExportJob { Doc = d, From = 0, To = 3, Loop = true };
            var frames = Exporter.Render(job, null, default);
            Check(frames.Count == 3 && frames.Select(f => f.Delay).SequenceEqual(new[] { 100, 120, 130 }), "the shared render skips it (every animated format uses this)");
            foreach (var f in frames) f.Bmp.Dispose();
            Exporter.Run(new ExportJob { Doc = d, Kind = ExportKind.Gif, Path = P("x.gif"), From = 0, To = 3 }, null, default);
            using (var imp = Importer.Load(P("x.gif"))) Check(imp.Frames.Count == 3, $"GIF has 3 frames ({imp.Frames.Count})");
            Exporter.Run(new ExportJob { Doc = d, Kind = ExportKind.Apng, Path = P("x.png"), From = 0, To = 3 }, null, default);
            Exporter.Run(new ExportJob { Doc = d, Kind = ExportKind.Sequence, Path = Directory.CreateDirectory(P("seq")).FullName, From = 0, To = 3 }, null, default);
            Check(Directory.GetFiles(P("seq"), "*.png").Length == 3, "PNG sequence writes 3 files");
            var ase = AsepriteFile.Read(SaveAse(d, P("x.aseprite"))).Doc;
            Check(ase.FrameCount == 3 && ase.Tags.Count == 1 && ase.Tags[0].Start == 1 && ase.Tags[0].End == 1, "Aseprite: 3 frames; the tag shrinks to what is left of it");
            var atlas = Sprites.Atlas(d, 0, 3, "a.png");
            Check(!atlas.Json.Contains("\"frame 1\"") || atlas.UniqueFrames == 3, $"atlas leaves it out ({atlas.UniqueFrames} unique frames)");
            var src = form.TestOptSource();
            Check(src.Frames.Length == 3 && src.Delays.SequenceEqual(new[] { 100, 120, 130 }), "export preview shows 3 frames");
            var mix = Exporter.Mix(new ExportJob { Doc = d, From = 0, To = 3, IncludeAudio = true })!;
            Check(mix.Length == AudioFormat.MsToFrames(350) * 2, $"video sound drops the left-out frame's 110 ms too ({mix.Length / 2} sample frames)");
            foreach (int cur in new[] { 1, 2 })
            {
                // The ordinary Export dialog passes the whole range; one-frame formats must still take the current frame.
                Exporter.Run(new ExportJob { Doc = d, Kind = ExportKind.Png, Path = P("still.png"), From = 0, To = 3, Current = cur }, null, default);
                using var png = new Bitmap(P("still.png"));
                Check(ImageOps.GetPixels(png).SequenceEqual(d.RenderPixels(cur)), $"PNG of the current frame (frame {cur + 1}) is that frame, with frame 2 left out");
            }
            form.TestSetCurrent(1);
            var src1 = form.TestOptSource();
            Check(src1.CurrentPixels.SequenceEqual(d.RenderPixels(1)), "the export preview's still formats show the current frame even when it is left out");
            var atlasJson = System.Text.Json.Nodes.JsonNode.Parse(Sprites.Atlas(d, 0, 3, "a.png").Json)!;
            var walk = atlasJson["meta"]!["frameTags"]!.AsArray().First(t => t!["name"]!.GetValue<string>() == "Walk")!;
            Check(walk["from"]!.GetValue<int>() == 1 && walk["to"]!.GetValue<int>() == 1, $"atlas tag counts only exported frames (Walk {walk["from"]}-{walk["to"]})");
            var cues = System.Text.Json.Nodes.JsonNode.Parse(Sprites.AudioCues(d, 0, 3, "a.wav"))!;
            Check(Math.Abs(cues["durationMs"]!.GetValue<double>() - 350) < 0.01, $"atlas audio cues match the cut WAV ({cues["durationMs"]} ms)");
            string html = P("x.html");
            Exporter.Run(new ExportJob { Doc = d, Kind = ExportKind.Html, Path = html, From = 0, To = 3 }, null, default);
            Check(File.ReadAllText(html).Contains("const DELAYS = [100,120,130]"), "HTML page skips it");

            string flint = P("x.flint");
            FlintFormat.Save(d, flint);
            var back = FlintFormat.Load(flint);
            Check(back.Ok && back.Document!.Frames.Select(f => f.Export).SequenceEqual(new[] { true, false, true, true }), ".flint keeps the setting");
            using (var z = ZipFile.OpenRead(flint))
            {
                var json = JsonNode.Parse(new StreamReader(z.GetEntry("project.json")!.Open()).ReadToEnd())!;
                Check(json["format"]!.GetValue<int>() >= 11 && json["frames"]![0]!["export"] == null && json["frames"]![1]!["export"]!.GetValue<bool>() == false,
                      "format 11 or later; only left-out frames write the flag");
                // A version-10 file (no flag) loads with every frame exported.
                json["format"] = 10;
                foreach (var fr in json["frames"]!.AsArray()) fr!.AsObject().Remove("export");
                string old = P("old.flint");
                File.Copy(flint, old, true);
                using (var oz = ZipFile.Open(old, ZipArchiveMode.Update))
                {
                    oz.GetEntry("project.json")!.Delete();
                    using var w = new StreamWriter(oz.CreateEntry("project.json").Open());
                    w.Write(json.ToJsonString());
                }
                var ol = FlintFormat.Load(old);
                Check(ol.Ok && ol.Document!.Frames.All(f => f.Export), "an older file loads with every frame included");
            }
            form.TestUndo();
            Check(d.Frames.All(f => f.Export), "undo turns it back on");
            form.TestRedo();
            Check(!d.Frames[1].Export, "redo turns it off again");
            var dup = d.Frames[1].Clone();
            Check(!dup.Export, "duplicating a frame keeps the setting");
        }

        Console.WriteLine(_fails == 0 ? "all frame helper checks passed" : $"{_fails} frame helper check(s) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    private static string SaveAse(Document d, string path)
    {
        Exporter.Run(new ExportJob { Doc = d, Kind = ExportKind.Aseprite, Path = path, From = 0, To = d.FrameCount - 1 }, null, default);
        return path;
    }
}
