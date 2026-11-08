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
        Check(form.TestProductivityShortcut(Keys.Control|Keys.Alt|Keys.T,false),"Fireworks numeric-transform shortcut is registered");
        Check(form.TestProductivityShortcut(Keys.Control|Keys.U,false) && form.TestProductivityShortcut(Keys.Control|Keys.Alt|Keys.G,false),"both Fireworks ungroup shortcut sets are accepted");
        form.Close(); Console.WriteLine(fails==0?"all productivity checks passed":$"{fails} productivity checks failed"); return fails==0?0:1;
    }
}
