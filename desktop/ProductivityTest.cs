namespace Flint;

public static class ProductivityTest
{
    public static int Run()
    {
        int fails = 0;
        void Check(bool condition, string message) { Console.WriteLine($"{(condition ? "  ok   " : "  FAIL ")}{message}"); if (!condition) fails++; }
        using var form = new MainForm { SuppressPrompts = true, StartPosition = FormStartPosition.Manual, Location = new Point(-6000,-6000) };
        form.Show(); Application.DoEvents(); form.TestNewCanvas(32,32);
        var spec = new PrimitiveSpec(false,6,6,0xFFFF0000,true,1);
        form.TestAddEditable(EditableSources.Shape(spec,Affine.Translate(2,2)));
        form.TestAddEditable(EditableSources.Shape(spec,Affine.Translate(12,2)));
        form.TestSelectObjects(0,1); form.TestFocusCanvasForCommands();
        Check(form.TestProductivityShortcut(Keys.Control|Keys.G,true),"Ctrl+G runs the registered group command");
        var cel = form.TestDocument.Layers[0].CelAt(0)!;
        Check(cel.Objects.Cast<RasterObject>().All(r=>r.GroupPath.Count==1) && ((RasterObject)cel.Objects[0]).GroupPath[0]==((RasterObject)cel.Objects[1]).GroupPath[0], "group retains both editable members");
        form.TestMouse(Tool.ObjectSelect,form.TestScreen(3,3),form.TestScreen(5,6));
        Check(cel.Objects[0].X==4 && cel.Objects[1].X==14, "picking one group member moves the entire group");
        form.TestUndo(); form.TestSelectObjects(0,1); form.TestGroupObjects();
        Check(form.TestSelectedObject!.GroupPath.Count==2, "groups can nest without flattening");
        form.TestUngroupObjects();
        Check(form.TestSelectedObject!.GroupPath.Count==1, "ungroup removes one nesting level");
        string path=Path.Combine(Path.GetTempPath(),$"flint-group-{Guid.NewGuid():N}.flint");
        try { FlintFormat.Save(form.TestDocument,path); var loaded=FlintFormat.Load(path); Check(FlintFormat.CurrentVersion==10 && ((RasterObject)loaded.Document!.Layers[0].CelAt(0)!.Objects[0]).GroupPath.Count==1,"format 10 round-trips group membership"); loaded.Document?.Dispose(); }
        finally { File.Delete(path); }
        form.TestSelectObjects(0,1); using var before=form.TestDocument.RenderFrame(0); form.TestFlattenObjects(); using var after=form.TestDocument.RenderFrame(0);
        Check(ImageOps.GetPixels(before).SequenceEqual(ImageOps.GetPixels(after)) && form.TestDocument.Layers[0].CelAt(0)!.Objects.Count==1,"flatten selection preserves appearance");
        form.TestUndo(); Check(form.TestDocument.Layers[0].CelAt(0)!.Objects.Count==2,"flatten selection is undoable");

        var doc=new Document(10,10); doc.InsertFrame(1);doc.Frames[0].Delay=70;doc.Frames[1].Delay=130;
        doc.Layers[0].EnsureCel(0).Objects.Add(EditableSources.Shape(spec,Affine.Translate(-3,1)));
        doc.Layers[0].EnsureCel(1).Objects.Add(EditableSources.Shape(spec,Affine.Translate(15,12)));
        Check(CanvasEdits.ContentBounds(doc,0)==new Rectangle(-3,1,6,6),"current-frame fit includes off-canvas content");
        Check(CanvasEdits.ContentBounds(doc,null)==new Rectangle(-3,1,24,17),"whole-animation fit includes every frame");
        CanvasEdits.Fit(doc,CanvasEdits.ContentBounds(doc,null));
        using(var first=doc.RenderFrame(0))using(var second=doc.RenderFrame(1))
            Check(first.Width==24 && second.Width==24 && !Sprites.ContentRect(ImageOps.GetPixels(second),second.Width,second.Height).IsEmpty,"animation fit supplies one logical export canvas containing every frame");
        CanvasEdits.Fit(doc,CanvasEdits.ContentBounds(doc,0));
        Check(doc.Width==6 && doc.Height==6 && doc.Layers[0].CelAt(1)!.Objects[0].X==18,"fitting current frame shifts other frames without cutting their source");
        CanvasEdits.Clip(doc,0);
        Check(doc.Layers[0].CelAt(1)!.Objects.Count==1,"current-frame clipping leaves other frames intact");
        CanvasEdits.Clip(doc,null);
        Check(doc.Layers[0].CelAt(1)!.Objects.Count==0,"whole-animation clipping removes outside objects");
        CanvasEdits.Resize(doc,12,12,Resample.Nearest);
        Check(doc.Frames.Select(f=>f.Delay).SequenceEqual(new[]{70,130}),"canvas/image operations preserve GIF frame timing");doc.Dispose();
        doc=new Document(10,10); doc.InsertFrame(1); doc.Layers[0].Shared=true;
        doc.Layers[0].EnsureCel(0).Objects.Add(EditableSources.Shape(spec,Affine.Translate(-2,1)));
        CanvasEdits.Clip(doc,1);
        Check(!doc.Layers[0].Shared && doc.Layers[0].CelAt(0)!.Objects[0].X==-2 && doc.Layers[0].CelAt(1)!.Objects[0].X==0,"current-frame clipping separates shared content and preserves the other frame"); doc.Dispose();
        doc=new Document(10,10); doc.Layers[0].EnsureCel(0).Objects.Add(EditableSources.Shape(spec,Affine.Translate(-2,1)));
        CanvasEdits.Clip(doc,null);
        var clipped=(RasterObject)doc.Layers[0].CelAt(0)!.Objects[0];
        Check(clipped.X==0 && clipped.Width==4 && clipped.Primitive==null && clipped.Original==null,"partial clipping permanently cuts source and keeps exact inside pixels"); doc.Dispose();

        form.TestNewCanvas(32,32); form.TestAddEditable(EditableSources.Shape(spec,Affine.Translate(2,2)));
        form.TestSelectObjects(0); form.TestQuickScale(2);
        Check(form.TestSelectedObject!.Width==12 && form.TestSelectedObject.Primitive!=null,"quick scaling retains editable source");
        form.TestResizeImage(64,48,Resample.Smooth);
        Check(form.TestDocument.Width==64 && form.TestDocument.Height==48 && form.TestSelectedObject!.Primitive!=null,"image resizing keeps editable objects and changes canvas dimensions");
        form.TestUndo(); Check(form.TestDocument.Width==32 && form.TestDocument.Height==32,"image resize is one undo step");
        Check(ResizePlan.Scale(3,5,0.5)==new Size(2,3),"quick scale rounds half pixels consistently");
        bool rejected=false; try { ResizePlan.Scale(10,10,double.PositiveInfinity); } catch(ArgumentException) { rejected=true; } Check(rejected,"invalid scale is rejected before allocation");
        form.TestSelectObjects(0); form.TestFocusCanvasForCommands();
        Check(form.TestProductivityShortcut(Keys.Control|Keys.Shift|Keys.T,false),"Fireworks numeric-transform shortcut (Ctrl+Shift+T) is registered");
        Check(form.TestProductivityShortcut(Keys.Control|Keys.Shift|Keys.G,false),"Fireworks ungroup shortcut (Ctrl+Shift+G) is registered");
        FireworksShortcuts(form,spec,Check);
        form.Close(); Console.WriteLine(fails==0?"all productivity checks passed":$"{fails} productivity checks failed"); return fails==0?0:1;
    }

    /// <summary>Fireworks 8 "Macromedia Standard" keys (tool letters, Clone/Duplicate, Arrange, zoom, frames).</summary>
    private static void FireworksShortcuts(MainForm form,PrimitiveSpec spec,Action<bool,string> Check)
    {
        foreach(var k in new[]{Keys.Control|Keys.Alt|Keys.T,Keys.Control|Keys.Alt|Keys.F,Keys.Control|Keys.Shift|Keys.D,Keys.Control|Keys.Alt|Keys.D,
            Keys.Control|Keys.Shift|Keys.Z,Keys.Control|Keys.Alt|Keys.D1,Keys.Control|Keys.Alt|Keys.D6,Keys.Control|Keys.Up,Keys.Control|Keys.Shift|Keys.Down,
            Keys.Alt|Keys.Right,Keys.Control|Keys.Alt|Keys.Shift|Keys.V,Keys.Control|Keys.D4,Keys.PageDown,Keys.Control|Keys.Alt|Keys.P})
            Check(form.TestProductivityShortcut(k,false),$"Fireworks shortcut {k} is registered");

        var letters=new (Keys key,Tool tool)[]{(Keys.V,Tool.ObjectSelect),(Keys.V,Tool.Move),(Keys.Q,Tool.Transform),(Keys.L,Tool.Lasso),(Keys.N,Tool.Line),
            (Keys.U,Tool.Rectangle),(Keys.U,Tool.Ellipse),(Keys.M,Tool.Marquee),(Keys.M,Tool.EllipseSelect),(Keys.G,Tool.Fill),(Keys.G,Tool.Gradient),
            (Keys.B,Tool.Pencil),(Keys.E,Tool.Eraser),(Keys.S,Tool.Clone),(Keys.W,Tool.Wand),(Keys.I,Tool.Eyedropper),(Keys.H,Tool.Pan),(Keys.C,Tool.Crop),(Keys.T,Tool.Text),(Keys.A,Tool.ObjectSelect)};
        form.TestToolKey(Keys.E); bool all=true; foreach(var (key,tool) in letters){form.TestToolKey(key); if(form.TestTool!=tool){all=false;Console.WriteLine($"         {key} gave {form.TestTool}, wanted {tool}");}}
        Check(all,"Fireworks tool letters pick the right tools, and a repeated letter cycles its group");
        form.TestSetColours(Color.Red,Color.Blue); form.TestToolKey(Keys.X);
        Check(form.TestColours==(Color.Blue,Color.Red),"X swaps colours");
        form.TestToolKey(Keys.D); Check(form.TestColours==(Color.Black,Color.White),"D resets to black and white");

        form.TestNewCanvas(32,32);
        form.TestAddEditable(EditableSources.Shape(spec,Affine.Translate(2,2)));
        form.TestAddEditable(EditableSources.Shape(spec with { Colour=0xFF00FF00 },Affine.Translate(4,4)));
        var cel=form.TestDocument.Layers[0].CelAt(0)!;
        form.TestSelectObjects(0); form.TestFocusCanvasForCommands();
        form.TestProductivityShortcut(Keys.Control|Keys.Shift|Keys.D,true);
        Check(cel.Objects.Count==3 && cel.Objects[2].X==cel.Objects[0].X,"Ctrl+Shift+D clones in place");
        form.TestProductivityShortcut(Keys.Control|Keys.Alt|Keys.D,true);
        Check(cel.Objects.Count==4 && cel.Objects[3].X==cel.Objects[2].X+10,"Ctrl+Alt+D duplicates 10 pixels down-right");
        form.TestProductivityShortcut(Keys.Alt|Keys.Shift|Keys.Right,true);
        Check(cel.Objects.Count==5 && cel.Objects[4].X==cel.Objects[3].X+10,"Alt+Shift+arrow clones and nudges 10 pixels");
        form.TestUndo();form.TestUndo();form.TestUndo();
        cel=form.TestDocument.Layers[0].CelAt(0)!; var green=cel.Objects[1]; form.TestSelectObjects(1);
        form.TestProductivityShortcut(Keys.Control|Keys.Shift|Keys.Down,true);
        cel=form.TestDocument.Layers[0].CelAt(0)!; Check(ReferenceEquals(cel.Objects[0],green),"Ctrl+Shift+Down sends the object to the back");
        form.TestUndo(); cel=form.TestDocument.Layers[0].CelAt(0)!; Check(ReferenceEquals(cel.Objects[1],green),"arranging is one undo step");
        Check(MainForm.ArrangeOrder(4,new HashSet<int>{1},1).SequenceEqual(new[]{0,2,1,3}) && MainForm.ArrangeOrder(4,new HashSet<int>{3},1).SequenceEqual(new[]{0,1,2,3})
            && MainForm.ArrangeOrder(4,new HashSet<int>{0,2},int.MaxValue).SequenceEqual(new[]{1,3,0,2}),"arrange order: forward, already on top, bring to front");
        form.TestSelectObjects(0); form.TestProductivityShortcut(Keys.Control|Keys.Alt|Keys.D3,true);
        Check(form.TestSelectedObject!.X+form.TestSelectedObject.Width==32,"Ctrl+Alt+3 aligns right");
        form.TestProductivityShortcut(Keys.Control|Keys.D4,true); Check(form.TestZoomLevel==4,"Ctrl+4 zooms to 400%");
    }
}
