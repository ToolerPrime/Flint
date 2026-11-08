namespace Flint;

public sealed partial class MainForm
{
    private readonly HashSet<int> _objectSelection=new();

    private int[] SelectedObjectIndices => ReferenceEquals(_objectDocument,_doc) && _objectLayer==_activeLayer && _objectFrame==_cur
        && ActiveLayer.CelAt(_cur) is { } cel ? _objectSelection.Where(i=>i>=0 && i<cel.Objects.Count && cel.Objects[i] is RasterObject).Order().ToArray() : Array.Empty<int>();

    private void SelectObjects(IEnumerable<int> indices)
    {
        var values=indices.ToArray();
        _objectDocument=_doc;_objectLayer=_activeLayer;_objectFrame=_cur;
        _objectSelection.Clear();foreach(int i in values) if(i>=0) _objectSelection.Add(i);
        _objectIndex=values.LastOrDefault(-1);
    }

    private int[] GroupMembers(int index,bool individual)
    {
        var cel=ActiveLayer.CelAt(_cur)!;
        if(individual || cel.Objects[index] is not RasterObject { GroupPath.Count:>0 } r) return new[]{index};
        string root=r.GroupPath[0];
        return Enumerable.Range(0,cel.Objects.Count).Where(i=>cel.Objects[i] is RasterObject other && other.GroupPath.Count>0 && other.GroupPath[0]==root).ToArray();
    }

    private void GroupObjects()
    {
        if(!CanEditObject()) return;
        var indices=SelectedObjectIndices;
        if(indices.Length<2) { Say("Select at least two objects with Shift-click or the Objects list.");return; }
        var cel=ActiveLayer.CelAt(_cur)!;
        if(indices.Any(i=>((RasterObject)cel.Objects[i]).GroupPath.Count>=32)) { Say("This group is already nested too deeply.");return; }
        string id=Guid.NewGuid().ToString("N");
        var next=indices.Select(i=>(i,((RasterObject)cel.Objects[i]).WithGroups(new[]{id}.Concat(((RasterObject)cel.Objects[i]).GroupPath)))).ToArray();
        var before=Before();foreach(var (i,obj) in next) cel.Objects[i]=obj;
        PushStructure("group objects",before);Say("Grouped. Q selects the group; Alt-click selects an individual member.");
    }

    private void UngroupObjects()
    {
        if(!CanEditObject()) return;
        var cel=ActiveLayer.CelAt(_cur)!;
        var roots=SelectedObjectIndices.Select(i=>(RasterObject)cel.Objects[i]).Where(r=>r.GroupPath.Count>0).Select(r=>r.GroupPath[0]).ToHashSet();
        if(roots.Count==0) { Say("The selection is not grouped.");return; }
        var indices=Enumerable.Range(0,cel.Objects.Count).Where(i=>cel.Objects[i] is RasterObject r && r.GroupPath.Count>0 && roots.Contains(r.GroupPath[0])).ToArray();
        var before=Before();
        foreach(int i in indices) { var r=(RasterObject)cel.Objects[i];cel.Objects[i]=r.WithGroups(r.GroupPath.Skip(1)); }
        SelectObjects(indices);PushStructure("ungroup objects",before);
    }

    private void FlattenSelectedObjects()
    {
        if(!CanEditObject()) return;
        var indices=SelectedObjectIndices;
        var cel=ActiveLayer.CelAt(_cur)!;
        if(indices.Length==0) return;
        if(indices[^1]-indices[0]+1!=indices.Length || indices.Any(i=>cel.Objects[i].Blend!=BlendMode.Normal))
        { Say("Flatten adjacent objects with Normal blending to preserve their appearance.");return; }
        var bounds=Rectangle.Empty;
        foreach(int i in indices)
        {
            var r=(RasterObject)cel.Objects[i];if(!r.Visible)continue;
            var box=Rectangle.Ceiling(Content(r));bounds=bounds.IsEmpty?box:Rectangle.Union(bounds,box);
        }
        if(bounds.IsEmpty) { Say("The selected objects have no visible content.");return; }
        try
        {
            ResizePlan.Validate(bounds.Width,bounds.Height);
            var px=new byte[checked(bounds.Width*bounds.Height*4)];
            foreach(int i in indices)
            {
                var r=(RasterObject)cel.Objects[i];if(!r.Visible)continue;
                Blending.Over(px,bounds.Width,bounds.Height,ImageOps.GetPixels(r.Pixels),r.Width,r.Height,r.X-bounds.X,r.Y-bounds.Y,r.Opacity,r.Blend);
            }
            var merged=new RasterObject(ImageOps.FromPixels(bounds.Width,bounds.Height,px)) { Name="Flattened selection",X=bounds.X,Y=bounds.Y };
            var before=Before();foreach(int i in indices.Reverse())cel.Objects.RemoveAt(i);
            cel.Objects.Insert(indices[0],merged);SelectObject(indices[0]);PushStructure("flatten selection",before);
        }
        catch(ArgumentException ex) { Say(ex.Message); }
    }

    private Rectangle SelectedObjectBounds()
    {
        var cel=ActiveLayer.CelAt(_cur);var bounds=Rectangle.Empty;
        if(cel==null)return bounds;
        foreach(int i in SelectedObjectIndices)
        { var box=Rectangle.Ceiling(Content((RasterObject)cel.Objects[i]));bounds=bounds.IsEmpty?box:Rectangle.Union(bounds,box); }
        return bounds;
    }

    private void QuickScaleSelection(double factor)
    {
        if(!CanEditObject())return;
        var bounds=SelectedObjectBounds();
        try { ResizePlan.Scale(bounds.Width,bounds.Height,factor); }
        catch(ArgumentException ex) { Say(ex.Message);return; }
        var previous=_tool;
        _tool=Tool.ObjectSelect;
        bool started;
        try { started=BeginTransform("scale selection"); }
        finally { _tool=previous; }
        if(!started)return;
        _xf!.Sx=_xf.Sy=factor;_xf.ForceMode=CurrentResample;
        if(ApplyTransform())CommitTransform();else CancelTransform();
    }

    private void SelectAllObjects()
    {
        FinishFloat();_selection=null;
        var cel=ActiveLayer.CelAt(_cur);
        SelectObjects(cel==null?Array.Empty<int>():Enumerable.Range(0,cel.Objects.Count));
        RefreshObjects();_canvas.Invalidate();Say("Selected all objects in the active layer/frame.");
    }

    internal void TestSelectObjects(params int[] indices) { SelectObjects(indices);RefreshObjects(); }
    internal void TestGroupObjects()=>GroupObjects();
    internal void TestUngroupObjects()=>UngroupObjects();
    internal void TestFlattenObjects()=>FlattenSelectedObjects();
    internal void TestQuickScale(double factor)=>QuickScaleSelection(factor);
}
