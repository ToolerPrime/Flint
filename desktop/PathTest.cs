/* Paths: model, exact rendering, transforms, .flint, Combine Path, Alter
 * Path, and the Pen / Subselection tools through the real form.
 * Flint.exe --pathtest <dir> */
using System.Collections.Immutable;

namespace Flint;

public static class PathTest
{
    private static int _fails;
    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    internal static PathSource Rect(double l, double t, double r, double b, uint fill = 0xFF2040E0, uint stroke = 0, double width = 0) =>
        new(ImmutableArray.Create(new SubPath(ImmutableArray.Create(PathNode.Corner(l, t), PathNode.Corner(r, t), PathNode.Corner(r, b), PathNode.Corner(l, b)), true)), fill, stroke, width);

    internal static PathSource Circle(double cx, double cy, double rad, uint fill = 0xFF2040E0) =>
        PathSource.FromPrimitive(new PrimitiveSpec(true, (int)(rad * 2), (int)(rad * 2), fill, true, 1)).Shifted(cx - rad, cy - rad);

    /// <summary>Area of a path's fill (shoelace, even-odd by signed sum of outer/hole loops) after flattening.</summary>
    internal static double Area(PathSource p) =>
        Math.Abs(p.Subpaths.Sum(s => { var q = PathSources.Flatten(s, 0.01); double a = 0; for (int i = 0; i < q.Count; i++) { var u = q[i]; var v = q[(i + 1) % q.Count]; a += u.X * v.Y - v.X * u.Y; } return a / 2; }));

    /// <summary>Pixels covered (centre inside) by a hard-edged render at canvas scale.</summary>
    private static HashSet<(int, int)> Cover(PathSource p)
    {
        var o = PathSources.Create(p, Affine.Identity, Resample.Nearest);
        var px = ImageOps.GetPixels(o.Pixels);
        var set = new HashSet<(int, int)>();
        for (int y = 0; y < o.Height; y++) for (int x = 0; x < o.Width; x++) if (px[(y * o.Width + x) * 4 + 3] != 0) set.Add((x + o.X, y + o.Y));
        return set;
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);

        Console.WriteLine("Model and rendering");
        {
            var rect = PathSources.Create(Rect(2, 3, 12, 9), Affine.Identity, Resample.Nearest);
            var prim = EditableSources.Shape(new PrimitiveSpec(false, 10, 6, 0xFF2040E0, true, 1), Affine.Translate(2, 3));
            Check(rect.X == prim.X && rect.Y == prim.Y && ImageOps.GetPixels(rect.Pixels).SequenceEqual(ImageOps.GetPixels(prim.Pixels)),
                  "a filled rectangle path on whole pixels draws exactly like the rectangle tool's shape");
            var half = PathSources.Create(Rect(0, 0, 4, 4, 0x80FF0000), Affine.Identity, Resample.Smooth);
            Check(ImageOps.GetPixels(half.Pixels).Where((_, i) => i % 4 == 3).All(a => a == 128), "a fill at 50% alpha is exactly 128 everywhere");
            var circ = PathSources.Create(Circle(20, 20, 10), Affine.Identity, Resample.Nearest);
            var primE = EditableSources.Shape(new PrimitiveSpec(true, 20, 20, 0xFF2040E0, true, 1), Affine.Translate(10, 10));
            int same = 0, total = 0;
            var a = Cover(Circle(20, 20, 10));
            var pe = ImageOps.GetPixels(primE.Pixels);
            var b = new HashSet<(int, int)>();
            for (int y = 0; y < primE.Height; y++) for (int x = 0; x < primE.Width; x++) if (pe[(y * primE.Width + x) * 4 + 3] != 0) b.Add((x + primE.X, y + primE.Y));
            total = a.Union(b).Count(); same = a.Intersect(b).Count();
            Check(same >= total * 0.97, $"a bezier circle covers the same pixels as the ellipse tool's ({same} of {total})");
            var turned = rect.Retransform(Affine.Rotate(90), rect.Mode).Retransform(Affine.Rotate(-90), rect.Mode);
            Check(turned.Vector?.Path != null && turned.X == rect.X && ImageOps.GetPixels(turned.Pixels).SequenceEqual(ImageOps.GetPixels(rect.Pixels)),
                  "turning a path and back gives the same pixels, and it stays editable");
            Check(rect.With(ImageOps.Clone(rect.Pixels)).Vector == null, "a destructive edit makes plain pixels (no path)");
            bool threw = false;
            try { PathSources.Create(Rect(0, 0, double.NaN, 4), Affine.Identity, Resample.Nearest); } catch (ArgumentException) { threw = true; }
            Check(threw, "a path with a bad point is refused");
            var stroked = PathSources.Create(Rect(4.5, 4.5, 14.5, 10.5, 0, 0xFF00FF00, 1), Affine.Identity, Resample.Nearest);
            var sp = ImageOps.GetPixels(stroked.Pixels);
            int inked = sp.Where((_, i) => i % 4 == 3).Count(v => v != 0);
            Check(inked == 32, $"a 1 px stroke centred on half pixels draws a one-pixel outline (11x7 border = 32 px; got {inked})");
        }

        Console.WriteLine("Saved in .flint");
        {
            var d = new Document(40, 30);
            var src = new PathSource(ImmutableArray.Create(
                new SubPath(ImmutableArray.Create(PathNode.Corner(2, 2), new PathNode(new VPt(20, 2), new VPt(15, 0), new VPt(25, 4)), PathNode.Corner(20, 20)), true),
                new SubPath(ImmutableArray.Create(PathNode.Corner(30, 5), PathNode.Corner(35, 25)), false)), 0xFF112233, 0xFF445566, 2.5);
            d.Layers[0].EnsureCel(0).Objects.Add(PathSources.Create(src, Affine.Rotate(10).Then(Affine.Translate(3, 1)), Resample.Smooth));
            string path = Path.Combine(dir, "paths.flint");
            FlintFormat.Save(d, path);
            var back = FlintFormat.Load(path);
            var o = (RasterObject)back.Document!.Layers[0].CelAt(0)!.Objects[0];
            var orig = (RasterObject)d.Layers[0].CelAt(0)!.Objects[0];
            Check(back.Ok && o.Vector?.Path is { } p2 && p2.ToJson().ToJsonString() == orig.Vector!.Path!.ToJson().ToJsonString(),
                  "nodes, handles, closed flags, colours and stroke width round-trip");
            Check(o.Mode == Resample.Smooth && back.Document.RenderPixels(0).SequenceEqual(d.RenderPixels(0)), "it draws the same after loading");
        }

        Console.WriteLine("Damaged path data");
        foreach (var bad in new[] { "{\"subpaths\":5}", "{\"subpaths\":[{\"n\":[1,2,3]}]}", "{\"subpaths\":[]}", "{\"subpaths\":[{\"n\":[1,2,3,4,5,\"x\"]}]}" })
            Check(PathSource.FromJson(System.Text.Json.Nodes.JsonNode.Parse(bad) as System.Text.Json.Nodes.JsonObject) == null, $"damaged path data is ignored, not trusted ({bad})");

        Console.WriteLine("Combine path");
        {
            var A = Rect(0, 0, 10, 10); var B = Rect(5, 5, 15, 15);
            List<List<VPt>> P(PathSource s) => PathOps.FillPolygons(s, Affine.Identity);
            var u = PathOps.Combine(CombineOp.Union, new[] { P(A), P(B) }, 0xFF000000)[0]!;
            var i = PathOps.Combine(CombineOp.Intersect, new[] { P(A), P(B) }, 0xFF000000)[0]!;
            var pu = PathOps.Combine(CombineOp.Punch, new[] { P(A), P(B) }, 0xFF000000)[0]!;
            var cr = PathOps.Combine(CombineOp.Crop, new[] { P(A), P(B) }, 0xFF000000)[0]!;
            Check(Math.Abs(Area(u) - 175) < 1e-6 && Math.Abs(Area(i) - 25) < 1e-6 && Math.Abs(Area(pu) - 75) < 1e-6 && Math.Abs(Area(cr) - 25) < 1e-6,
                  $"overlapping squares: union 175, intersect 25, punch 75, crop 25 ({Area(u):0.##}, {Area(i):0.##}, {Area(pu):0.##}, {Area(cr):0.##})");
            Check(u.NodeCount == 8 && i.NodeCount == 4, $"straight edges come back as corners only (union {u.NodeCount} nodes, intersect {i.NodeCount})");
            Check(Cover(u).SetEquals(Cover(A).Union(Cover(B))) && Cover(pu).SetEquals(Cover(A).Except(Cover(B))), "the pixels covered match the pixel union and difference exactly");
            var side = PathOps.Combine(CombineOp.Union, new[] { P(Rect(0, 0, 10, 10)), P(Rect(10, 0, 20, 10)) }, 0xFF000000)[0]!;
            Check(side.NodeCount == 4, $"squares sharing an edge union into one 4-corner shape ({side.NodeCount})");
            var CA = Circle(20, 20, 10); var CB = Circle(28, 20, 10);
            var cu = PathOps.Combine(CombineOp.Union, new[] { P(CA), P(CB) }, 0xFF000000)[0]!;
            var ci = PathOps.Combine(CombineOp.Intersect, new[] { P(CA), P(CB) }, 0xFF000000)[0]!;
            double sum = Area(cu) + Area(ci), want = Area(CA) + Area(CB);
            Check(Math.Abs(sum - want) / want < 0.001 && Math.Abs(Area(CA) - Math.PI * 100) / (Math.PI * 100) < 0.002,
                  $"circles: union + intersection = both circles within 0.1% ({sum:0.0} vs {want:0.0})");
            var cov = Cover(cu); var cov2 = Cover(CA).Union(Cover(CB));
            Check(cov.Except(cov2).Count() + cov2.Except(cov).Count() <= cov2.Count() * 0.02, "curved union matches the pixel union on all but a few edge pixels");
            Check(cu.NodeCount < 30, $"the curved result is smooth curves with few points ({cu.NodeCount})");
            var hole = PathOps.Combine(CombineOp.Punch, new[] { P(Rect(0, 0, 20, 20)), P(Rect(5, 5, 15, 15)) }, 0xFF000000)[0]!;
            Check(!Cover(hole).Contains((10, 10)) && Cover(hole).Contains((2, 2)), "punching a square out of the middle leaves a hole");
            Check(PathOps.Combine(CombineOp.Intersect, new[] { P(Rect(0, 0, 5, 5)), P(Rect(10, 10, 15, 15)) }, 0)[0] == null, "shapes that do not overlap intersect to nothing");
        }

        {
            // A big stroked circle expands in reasonable time (one render, not one per piece).
            var big = new PathSource(Circle(250, 250, 200).Subpaths, 0, 0xFF000000, 4);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var exBig = PathOps.ExpandStroke(big, Affine.Identity, 4, 0xFF000000);
            Check(exBig != null && sw.ElapsedMilliseconds < 15000, $"expanding a 400 px circle's stroke takes {sw.ElapsedMilliseconds} ms");
        }

        Console.WriteLine("Alter path");
        {
            var many = new PathSource(ImmutableArray.Create(new SubPath(Enumerable.Range(0, 100).Select(k =>
                PathNode.Corner(30 + 20 * Math.Cos(2 * Math.PI * k / 100), 30 + 20 * Math.Sin(2 * Math.PI * k / 100))).ToImmutableArray(), true)), 0xFF000000, 0, 0);
            var simple = PathOps.Simplify(many, 0.25);
            Check(simple.NodeCount <= 8 && Math.Abs(Area(simple) - Area(many)) / Area(many) < 0.01, $"simplify: a 100-point circle becomes {simple.NodeCount} points, same area within 1%");
            var sq = PathOps.Simplify(Rect(0, 0, 10, 10), 1);
            Check(sq.NodeCount == 4, "simplify keeps a square's 4 corners");
            var line = new PathSource(ImmutableArray.Create(new SubPath(ImmutableArray.Create(PathNode.Corner(10, 10), PathNode.Corner(30, 10)), false)), 0, 0xFF000000, 4);
            var ex = PathOps.ExpandStroke(line, Affine.Identity, 4, 0xFF000000)!;
            double wantE = 80 + 4 * Math.PI;
            Check(Math.Abs(Area(ex) - wantE) / wantE < 0.003 && ex.Stroke == 0 && ex.HasFill, $"expand stroke: a 20 px line 4 wide becomes a filled outline of area {Area(ex):0.0} (want {wantE:0.0})");
            var inset = PathOps.Offset(PathOps.FillPolygons(Rect(0, 0, 20, 20), Affine.Identity), -2, 0xFF000000)!;
            Check(Math.Abs(Area(inset) - 256) < 1e-6 && inset.NodeCount == 4, $"inset 2: a 20x20 square becomes 16x16 ({Area(inset):0.##})");
            var outset = PathOps.Offset(PathOps.FillPolygons(Rect(0, 0, 20, 20), Affine.Identity), 2, 0xFF000000)!;
            double wantO = 400 + 160 + 4 * Math.PI;
            Check(Math.Abs(Area(outset) - wantO) / wantO < 0.002, $"outset 2 with round corners: area {Area(outset):0.0} (want {wantO:0.0})");
            Check(PathOps.Offset(PathOps.FillPolygons(Rect(0, 0, 20, 20), Affine.Identity), -11, 0) == null, "insetting more than half the width leaves nothing");
            var ring = new PathSource(Rect(0, 0, 20, 20).Subpaths.AddRange(Rect(5, 5, 15, 15).Subpaths), 0xFF000000, 0, 0);
            var ringIn = PathOps.Offset(PathOps.FillPolygons(ring, Affine.Identity), -2, 0xFF000000)!;
            Check(Math.Abs(Area(ringIn) - (256 - 196)) < 1e-6 && ringIn.Subpaths.Length == 2, $"inset 2 on a square with a hole: area 60, hole corners sharp ({Area(ringIn):0.##})");
            var closedLine = PathOps.ExpandStroke(Rect(10, 10, 30, 30, 0, 0xFF000000, 4), Affine.Identity, 4, 0xFF000000)!;
            Check(Math.Abs(Area(closedLine) - (24 * 24 - 4 * (4 - Math.PI) - 16 * 16)) < 0.5,
                  $"expand stroke on a closed square: a ring with rounded outer corners ({Area(closedLine):0.0})");
            var many2 = new PathSource(Enumerable.Range(0, 300).SelectMany(k => Rect(k % 20 * 6, k / 20 * 6, k % 20 * 6 + 4, k / 20 * 6 + 4).Subpaths).ToImmutableArray(), 0, 0xFF000000, 1);
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            var exMany = PathOps.ExpandStroke(many2, Affine.Identity, 1, 0xFF000000);
            Check(exMany != null && sw2.ElapsedMilliseconds < 5000, $"expand stroke on 300 small shapes takes {sw2.ElapsedMilliseconds} ms");
            var sq20 = Rect(0, 0, 20, 20);
            var tilted = PathOps.FillPolygons(sq20, Affine.Rotate(1e-5 * 180 / Math.PI));
            var sliver = PathOps.Combine(CombineOp.Punch, new[] { PathOps.FillPolygons(sq20, Affine.Identity), tilted }, 0xFF000000)[0];
            Check(sliver == null || sliver.Subpaths.All(sp => Area(new PathSource(ImmutableArray.Create(sp), 0, 0, 0)) >= 0.01), "punching a square out of a barely turned copy leaves no slivers");
        }

        Console.WriteLine("Pen tool");
        using var form = new MainForm { SuppressPrompts = true, StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) };
        form.Show();
        Application.DoEvents();
        {
            var d = new Document(60, 40);
            form.TestUseDocument(d);
            form.TestZoom(4);
            form.TestSetColours(Color.FromArgb(255, 200, 30, 30), Color.White);
            int undo = form.TestUndoCount;
            form.TestMouse(Tool.Pen, form.TestScreen(10, 10));
            form.TestMouse(Tool.Pen, form.TestScreen(30, 10));
            form.TestMouse(Tool.Pen, form.TestScreen(30, 30));
            Check(form.TestPenActive && d.Layers[0].CelAt(0) == null && form.TestUndoCount == undo, "while drawing, nothing is added to the document");
            form.TestMouse(Tool.Pen, form.TestScreen(10.2, 10.1));
            var o = d.Layers[0].CelAt(0)?.Objects.LastOrDefault() as RasterObject;
            var src = o?.Vector?.Path;
            Check(src != null && src.Subpaths.Length == 1 && src.Subpaths[0].Closed && src.NodeCount == 3 && form.TestUndoCount == undo + 1,
                  $"three clicks and a click on the first point make one closed 3-point path, one undo step ({src?.NodeCount} points)");
            var m = o!.Matrix;
            var pts = src!.Subpaths[0].Nodes.Select(n => m.Apply(n.P.X, n.P.Y)).ToList();
            Check(pts.Select(q => ((int)Math.Round(q.x), (int)Math.Round(q.y))).SequenceEqual(new[] { (10, 10), (30, 10), (30, 30) }), "points land on whole-pixel corners where clicked");
            form.TestMouse(Tool.Pen, form.TestScreen(40, 5), form.TestScreen(44, 5), form.TestScreen(46, 7));
            form.TestMouse(Tool.Pen, form.TestScreen(50, 20));
            form.TestPathKey(Keys.Enter);
            var open = (d.Layers[0].CelAt(0)!.Objects.Last() as RasterObject)!.Vector!.Path!;
            var n0 = open.Subpaths[0].Nodes[0];
            Check(!open.Subpaths[0].Closed && open.NodeCount == 2 && n0.HasOut && Math.Abs(n0.In.X - (2 * n0.P.X - n0.Out.X)) < 1e-9 && Math.Abs(n0.In.Y - (2 * n0.P.Y - n0.Out.Y)) < 1e-9,
                  "dragging makes a smooth point (handles mirror each other); Enter finishes an open path");
            int count = d.Layers[0].CelAt(0)!.Objects.Count;
            form.TestMouse(Tool.Pen, form.TestScreen(5, 35));
            form.TestPathKey(Keys.Escape);
            Check(d.Layers[0].CelAt(0)!.Objects.Count == count, "a single click then Esc adds nothing");
            form.TestMouse(Tool.Pen, form.TestScreen(5, 35));
            form.TestMouse(Tool.Pen, form.TestScreen(15, 35));
            form.TestMouse(Tool.Pen, form.TestScreen(25, 35));
            form.TestPathKey(Keys.Back);
            form.TestSetActiveLayer(0);
            d.AddLayer("Other");
            form.TestSetActiveLayer(1);
            form.TestToolKey(Keys.A);
            Check(form.TestActiveLayer == 1, "finishing a path drawn on another layer leaves you on the layer you picked");
            var last = (d.Layers[0].CelAt(0)!.Objects.Last() as RasterObject)!.Vector!.Path!;
            Check(last.NodeCount == 2 && d.Layers[0].CelAt(0)!.Objects.Count == count + 1, "Backspace removes the last point; switching tool finishes the path");
        }

        Console.WriteLine("Subselection tool");
        {
            var d = new Document(60, 40);
            d.Layers[0].EnsureCel(0).Objects.Add(PathSources.Create(Rect(10, 10, 30, 30), Affine.Identity, Resample.Nearest));
            form.TestUseDocument(d);
            form.TestZoom(4);
            form.TestSelectObjects(0);
            var start = d.Layers[0].CelAt(0)!.Objects[0];
            int undo = form.TestUndoCount;
            form.TestMouse(Tool.Subselect, form.TestScreen(30, 30), form.TestScreen(34, 33));
            var moved = (RasterObject)d.Layers[0].CelAt(0)!.Objects[0];
            var corner = moved.Vector!.Path!.Subpaths[0].Nodes[2].P;
            var (cx, cy) = moved.Matrix.Apply(corner.X, corner.Y);
            Check(Math.Abs(cx - 34) < 1e-6 && Math.Abs(cy - 33) < 1e-6 && form.TestUndoCount == undo + 1, $"dragging a corner moves just that point ({cx},{cy}); one undo step");
            form.TestUndo();
            Check(ReferenceEquals(d.Layers[0].CelAt(0)!.Objects[0], start), "undo brings back the very same object");
            // A stroked rectangle shape keeps its square corners when one point is moved.
            var box = EditableSources.Shape(new PrimitiveSpec(false, 20, 14, 0xFF000000, false, 3), Affine.Translate(30, 20));
            d.Layers[0].CelAt(0)!.Objects.Add(box);
            form.TestSelectObjects(1);
            var px0 = d.RenderPixels(0);
            form.TestMouse(Tool.Subselect, form.TestScreen(50, 34), form.TestScreen(52, 36));
            var px1 = d.RenderPixels(0);
            bool cornersSame = new[] { (30, 20), (31, 21), (49, 20), (30, 33) }.All(q => { int i = (q.Item2 * 60 + q.Item1) * 4; return px0[i + 3] == px1[i + 3]; });
            Check(cornersSame && ((RasterObject)d.Layers[0].CelAt(0)!.Objects[1]).Vector?.Path != null, "a stroked rectangle becomes a path when edited and keeps its other square corners");
            form.TestUndo();
            // Esc during a drag puts the very same object back.
            var held = d.Layers[0].CelAt(0)!.Objects[0];
            int u0 = form.TestUndoCount;
            form.TestSelectObjects(0);
            form.TestDragStart(Tool.Subselect, form.TestScreen(10, 10), form.TestScreen(14, 12));
            form.TestPathKey(Keys.Escape);
            form.TestDragEnd(form.TestScreen(14, 12));
            Check(ReferenceEquals(d.Layers[0].CelAt(0)!.Objects[0], held) && form.TestUndoCount == u0, "Esc while dragging a point cancels it, with no undo step");
            // Rotated path: the point follows the mouse on screen.
            d.Layers[0].CelAt(0)!.Objects[0] = PathSources.Create(Rect(0, 0, 20, 10), Affine.Rotate(90).Then(Affine.Translate(40, 5)), Resample.Nearest);
            form.TestSelectObjects(0);
            var rp = (RasterObject)d.Layers[0].CelAt(0)!.Objects[0];
            var n1 = rp.Vector!.Path!.Subpaths[0].Nodes[1].P;
            var (sx, sy) = rp.Matrix.Apply(n1.X, n1.Y);
            form.TestMouse(Tool.Subselect, form.TestScreen(sx, sy), form.TestScreen(sx + 3, sy + 2));
            var rq = (RasterObject)d.Layers[0].CelAt(0)!.Objects[0];
            var n1b = rq.Vector!.Path!.Subpaths[0].Nodes[1].P;
            var (tx, ty) = rq.Matrix.Apply(n1b.X, n1b.Y);
            Check(Math.Abs(tx - (sx + 3)) < 1e-6 && Math.Abs(ty - (sy + 2)) < 1e-6, "on a turned path the point follows the mouse");
            // Smooth handle keeps its opposite in line.
            var curve = new PathSource(ImmutableArray.Create(new SubPath(ImmutableArray.Create(PathNode.Corner(5, 20), new PathNode(new VPt(25, 10), new VPt(15, 10), new VPt(35, 10)), PathNode.Corner(45, 20)), false)), 0, 0xFF000000, 2);
            d.Layers[0].CelAt(0)!.Objects[0] = PathSources.Create(curve, Affine.Identity, Resample.Smooth);
            form.TestSelectObjects(0);
            form.TestMouse(Tool.Subselect, form.TestScreen(25, 10));          // select the middle point so its handles show
            form.TestMouse(Tool.Subselect, form.TestScreen(35, 10), form.TestScreen(35, 4));
            var sm = (RasterObject)d.Layers[0].CelAt(0)!.Objects[0];
            var mid = sm.Vector!.Path!.Subpaths[0].Nodes[1];
            Check(PathSources.Smooth(mid) && Math.Abs(Math.Sqrt(Math.Pow(mid.In.X - mid.P.X, 2) + Math.Pow(mid.In.Y - mid.P.Y, 2)) - 10) < 1e-6,
                  "dragging one handle of a smooth point turns the other with it, keeping its length");
            // Marquee selects points; Delete removes them.
            form.TestMouse(Tool.Subselect, form.TestScreen(1, 15), form.TestScreen(28, 25));
            Check(form.TestNodeSelection.Count == 1, $"a box round a point selects it ({form.TestNodeSelection.Count})");
            form.TestPathKey(Keys.Delete);
            Check(((RasterObject)d.Layers[0].CelAt(0)!.Objects[0]).Vector!.Path!.NodeCount == 2, "Delete removes the selected point");
            // Double-click the outline adds a point without changing the drawing.
            var line = (RasterObject)d.Layers[0].CelAt(0)!.Objects[0];
            var before = d.RenderPixels(0);
            var lp = line.Vector!.Path!;
            var midPt = PathSources.Bez(lp.Subpaths[0].Nodes[0].P, lp.Subpaths[0].Nodes[0].Out, lp.Subpaths[0].Nodes[1].In, lp.Subpaths[0].Nodes[1].P, 0.5);
            var (mx, my) = line.Matrix.Apply(midPt.X, midPt.Y);
            Check(form.TestSubDoubleClick(form.TestScreen(mx, my)) && ((RasterObject)d.Layers[0].CelAt(0)!.Objects[0]).Vector!.Path!.NodeCount == 3, "double-clicking the outline adds a point");
            var after = d.RenderPixels(0);
            int diff = 0; for (int i = 3; i < after.Length; i += 4) if (Math.Abs(after[i] - before[i]) > 8) diff++;
            Check(diff <= 4, $"the drawing barely changes ({diff} pixels)");
            // A long straight edge at high zoom can be clicked anywhere along it.
            d.Layers[0].CelAt(0)!.Objects.Clear();
            d.Layers[0].CelAt(0)!.Objects.Add(PathSources.Create(Rect(2, 2, 58, 38), Affine.Identity, Resample.Nearest));
            form.TestSelectObjects(0);
            form.TestZoom(8);
            Check(form.TestSubDoubleClick(form.TestScreen(2 + 56 * 0.4, 2)) && ((RasterObject)d.Layers[0].CelAt(0)!.Objects[0]).Vector!.Path!.NodeCount == 5,
                  "double-click at 40% along a long edge at 8x zoom adds a point there");
            var added = ((RasterObject)d.Layers[0].CelAt(0)!.Objects[0]).Vector!.Path!.Subpaths[0].Nodes[1].P;
            var addedC = ((RasterObject)d.Layers[0].CelAt(0)!.Objects[0]).Matrix.Apply(added.X, added.Y);
            Check(Math.Abs(addedC.x - (2 + 56 * 0.4)) < 0.2, $"the new point is where you clicked ({addedC.x:0.00})");
            form.TestZoom(4);
            Check(form.TestToolKey(Keys.A) && form.TestTool == Tool.Subselect && form.TestToolKey(Keys.P) && form.TestTool == Tool.Pen, "A picks Subselection, P picks the Pen");
        }

        Console.WriteLine("Combine and alter through the menus");
        {
            var d = new Document(60, 40);
            d.Layers[0].EnsureCel(0).Objects.Add(PathSources.Create(Rect(0, 0, 10, 10, 0xFF0000FF), Affine.Identity, Resample.Nearest));
            d.Layers[0].EnsureCel(0).Objects.Add(EditableSources.Shape(new PrimitiveSpec(false, 10, 10, 0xFF00FF00, true, 1), Affine.Translate(5, 5)));
            form.TestUseDocument(d);
            Cel cel() => d.Layers[0].CelAt(0)!;   // undo swaps in fresh cel containers
            form.TestSelectObjects(0, 1);
            var objs = cel().Objects.ToList();
            Check(form.CombinePaths(CombineOp.Union) && cel().Objects.Count == 1 && Math.Abs(Area(((RasterObject)cel().Objects[0]).Vector!.Path!) - 175) < 1e-6,
                  "Union of a path and a rectangle shape gives one path of area 175");
            Check(((RasterObject)cel().Objects[0]).Vector!.Path!.Fill == 0xFF0000FF, "it takes the bottom shape's colour");
            form.TestUndo();
            Check(cel().Objects.Count == 2 && ReferenceEquals(cel().Objects[0], objs[0]) && ReferenceEquals(cel().Objects[1], objs[1]), "undo puts back the exact objects");
            form.TestSelectObjects(0, 1);
            form.CombinePaths(CombineOp.Punch);
            Check(cel().Objects.Count == 1 && Math.Abs(Area(((RasterObject)cel().Objects[0]).Vector!.Path!) - 75) < 1e-6, "Punch removes the top shape from the one below");
            form.TestUndo();
            // Punch with an outline-only path in the middle: it stays an outline.
            var outline = PathSources.Create(Rect(2, 2, 14, 14, 0, 0xFFFF0000, 1), Affine.Identity, Resample.Nearest);
            cel().Objects.Insert(1, outline);
            form.TestSelectObjects(0, 1, 2);
            form.CombinePaths(CombineOp.Punch);
            var mids = cel().Objects.Cast<RasterObject>().Select(o => o.Vector?.Path).ToList();
            Check(mids.Count == 2 && mids[1] is { HasFill: false, Stroke: 0xFFFF0000 }, "Punch keeps each lower path's own look (an outline stays an outline)");
            form.TestUndo();
            cel().Objects.RemoveAt(1);
            form.TestSelectObjects(0);
            Check(!form.CombinePaths(CombineOp.Union), "combining needs two or more paths");
            form.TestSelectObjects(0);
            Check(form.AlterPaths("inset", 2) && Math.Abs(Area(((RasterObject)cel().Objects[0]).Vector!.Path!) - 36) < 1e-6, "Inset path by 2 through the menu");
            form.TestUndo();
            form.TestSelectObjects(0);
            Check(form.SetPathStyle(0, 0xFF00FF00, 3) && ((RasterObject)cel().Objects[0]).Vector!.Path! is { HasFill: false, Stroke: 0xFF00FF00, StrokeWidth: 3 },
                  "a path's fill and stroke can be changed afterwards");
            form.TestUndo();
            var bitmap = new RasterObject(ImageOps.NewBitmap(5, 5)) { Name = "Bitmap" };
            cel().Objects.Add(bitmap);
            form.TestSelectObjects(0, 2);
            Check(!form.CombinePaths(CombineOp.Union) && cel().Objects.Count == 3, "a plain bitmap is refused with a reason");
        }

        Console.WriteLine(_fails == 0 ? "all path checks passed" : $"{_fails} path check(s) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
