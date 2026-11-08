using System.Drawing.Drawing2D;

namespace Flint;

public sealed partial class MainForm
{
    private int _objectIndex = -1, _objectLayer = -1, _objectFrame = -1;
    private Document? _objectDocument;
    private ListBox _objects = null!;
    private bool _objectsBusy, _primitiveDrawing;
    private CheckBox _editableShapes = null!;
    private (PrimitiveSpec? shape, TextSource? text, List<ObjectEffect>? effects, byte opacity, BlendMode blend)? _appearance;

    private RasterObject? SelectedObject => ReferenceEquals(_objectDocument, _doc) && _objectLayer == _activeLayer && _objectFrame == _cur
        && ActiveLayer.CelAt(_cur) is { } cel && _objectIndex >= 0 && _objectIndex < cel.Objects.Count
        ? cel.Objects[_objectIndex] as RasterObject : null;

    private void SelectObject(int index)
    {
        SelectObjects(index < 0 ? Array.Empty<int>() : new[] { index });
    }

    private TabPage BuildObjectsTab()
    {
        var page = new TabPage("Objects");
        _objects = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, SelectionMode = SelectionMode.MultiExtended };
        _objects.SelectedIndexChanged += (_, _) =>
        {
            if (_objectsBusy) return;
            var rows = _objects.SelectedIndices.Cast<int>().ToArray();
            FinishFloat();
            var count = ActiveLayer.CelAt(_cur)?.Objects.Count ?? 0;
            SelectObjects(rows.Select(row => count - 1 - row));
            _selection = null; _editMask = false; RefreshObjects(); RefreshEffects(); _canvas.Invalidate();
        };
        _objects.DoubleClick += (_, _) => EditObjectProperties();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Edit properties…", null, (_, _) => EditObjectProperties());
        menu.Items.Add("Group", null, (_, _) => GroupObjects());
        menu.Items.Add("Ungroup", null, (_, _) => UngroupObjects());
        menu.Items.Add("Flatten selection", null, (_, _) => FlattenSelectedObjects());
        menu.Items.Add("Clone and nudge", null, (_, _) => CloneSelectedObject());
        menu.Items.Add("Delete object", null, (_, _) => DeleteSelectedObject());
        menu.Items.Add("Rasterize object", null, (_, _) => RasterizeSelectedObject());
        var align = new ToolStripMenuItem("Align to canvas");
        foreach (var edge in Enum.GetValues<CanvasAlign>())
        {
            var target = edge;
            align.DropDownItems.Add(edge.ToString(), null, (_, _) => AlignSelectedObject(target));
        }
        menu.Items.Add(align);
        menu.Items.Add("Copy appearance", null, (_, _) => CopyAppearance());
        menu.Items.Add("Paste appearance", null, (_, _) => PasteAppearance());
        _objects.ContextMenuStrip = menu;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, WrapContents = false };
        void Button(string text, int width, Action action)
        {
            var b = new Button { Text = text, Width = width, Height = 28, Margin = new Padding(2) };
            b.Click += (_, _) => action(); bar.Controls.Add(b);
        }
        Button("Edit…", 62, EditObjectProperties); Button("Clone", 62, CloneSelectedObject);
        Button("Actions ▾", 100, () => menu.Show(bar, new Point(0, bar.Height)));
        page.Controls.Add(_objects); page.Controls.Add(bar); page.Disposed += (_, _) => menu.Dispose();
        return page;
    }

    private void RefreshObjects()
    {
        if (_objects == null) return;
        if (SelectedObject == null) SelectObject(-1);
        _objectsBusy = true;
        try
        {
            _objects.Items.Clear();
            var cel = ActiveLayer.CelAt(_cur);
            if (cel == null) return;
            for (int i = cel.Objects.Count - 1; i >= 0; i--)
            {
                var obj = cel.Objects[i];
                string kind = obj is RasterObject r ? r.Primitive is { } s ? s.Ellipse ? "Ellipse" : "Rectangle" : r.TextSource != null ? "Text" : r.Vector != null ? "Vector" : "Bitmap" : obj.Kind.ToString();
                string group = obj is RasterObject { GroupPath.Count: > 0 } member ? $"[Group {member.GroupPath.Count}] " : "";
                _objects.Items.Add($"{group}{obj.Name}  ·  {kind}");
            }
            foreach (int i in SelectedObjectIndices) _objects.SetSelected(cel.Objects.Count - 1 - i, true);
        }
        finally { _objectsBusy = false; }
    }

    private bool PickObject(Point p)
    {
        FinishFloat(); _selection = null; _editMask = false;
        for (int layer = _doc.Layers.Count - 1; layer >= 0; layer--)
        {
            var l = _doc.Layers[layer];
            if (!l.Visible || l.Locked || l.Opacity == 0 || l.CelAt(_cur) is not { } cel) continue;
            if (l.MaskEnabled && cel.Mask is { } mask && InDoc(p) && mask.Pixels.GetPixel(p.X, p.Y).G == 0) continue;
            for (int i = cel.Objects.Count - 1; i >= 0; i--)
            {
                if (cel.Objects[i] is not RasterObject r || !r.Visible || r.Opacity == 0) continue;
                int x = p.X - r.X, y = p.Y - r.Y;
                if (x < 0 || y < 0 || x >= r.Width || y >= r.Height || r.Pixels.GetPixel(x, y).A == 0) continue;
                var previous = layer == _activeLayer ? SelectedObjectIndices : Array.Empty<int>();
                _activeLayer = layer;
                var hit = GroupMembers(i, (Mods & Keys.Alt) != 0);
                var next = new HashSet<int>(previous);
                if ((Mods & Keys.Shift) != 0)
                {
                    if (hit.All(next.Contains)) next.ExceptWith(hit); else next.UnionWith(hit);
                }
                else if ((Mods & Keys.Alt) != 0 || !next.Contains(i)) next = new HashSet<int>(hit);
                SelectObjects(next.Order()); RefreshAll(); return SelectedObject != null;
            }
        }
        SelectObject(-1); RefreshAll(); return false;
    }

    private bool CanEditObject()
    {
        FinishFloat();
        StopPlay();
        if (ActiveLayer.Locked || SelectedObject == null) { Say("Select an unlocked object with Object (Q) or the Objects tab first."); return false; }
        return true;
    }

    private void AddEditableObject(RasterObject obj)
    {
        StopPlay();
        if (ActiveLayer.Locked) { Say("This layer is locked."); return; }
        var before = Before();
        var cel = ActiveLayer.EnsureCel(_cur); cel.Objects.Add(obj);
        _selection = null; SelectObject(cel.Objects.Count - 1);
        PushStructure("add " + obj.Name.ToLowerInvariant(), before);
    }

    private void EditObjectProperties()
    {
        if (!CanEditObject()) return;
        if (SelectedObjectIndices.Length != 1) { Say("Alt-click one group member to edit its properties."); return; }
        var obj = SelectedObject!;
        using var dialog = new ObjectPropertiesDialog(obj);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { ApplyObjectProperties(dialog.Primitive, dialog.TextSource, dialog.ObjectName, dialog.PositionX, dialog.PositionY); }
        catch (ArgumentException ex) { Say(ex.Message); }
        catch (InvalidOperationException ex) { Say(ex.Message); }
    }

    private void ApplyObjectProperties(PrimitiveSpec? shape, TextSource? text, string name, int x, int y)
    {
        if (!CanEditObject()) return;
        var original = SelectedObject!;
        var next = shape != null || text != null ? EditableSources.Replace(original, shape, text) : original.Moved(0, 0);
        next = next.Moved(x - next.X, y - next.Y); next.Name = name;
        var before = Before(); ActiveLayer.CelAt(_cur)!.Objects[_objectIndex] = next;
        PushStructure("edit object properties", before);
    }

    private void CloneSelectedObject()
    {
        if (!CanEditObject()) return;
        var cel = ActiveLayer.CelAt(_cur)!;
        var ids = new Dictionary<string,string>();
        string Remap(string id) { if (!ids.TryGetValue(id,out var mapped)) ids[id]=mapped=Guid.NewGuid().ToString("N"); return mapped; }
        var copies = SelectedObjectIndices.Select(i =>
        {
            var old=(RasterObject)cel.Objects[i]; var next=old.Moved(10,10).WithGroups(old.GroupPath.Select(Remap)); next.Name += " copy"; return next;
        }).ToArray();
        var before=Before();int start=cel.Objects.Count;cel.Objects.AddRange(copies);SelectObjects(Enumerable.Range(start,copies.Length));
        PushStructure("clone objects",before);
    }

    private void DeleteSelectedObject()
    {
        if (!CanEditObject()) return;
        var indices=SelectedObjectIndices;var before=Before();var cel=ActiveLayer.CelAt(_cur)!;
        foreach(int i in indices.Reverse())cel.Objects.RemoveAt(i);SelectObject(-1);PushStructure("delete objects",before);
    }

    private void RasterizeSelectedObject()
    {
        if (!CanEditObject()) return;
        var cel=ActiveLayer.CelAt(_cur)!;
        var next=SelectedObjectIndices.Select(i=>(i,((RasterObject)cel.Objects[i]).With(ImageOps.Clone(((RasterObject)cel.Objects[i]).Pixels)))).ToArray();
        var before=Before();foreach(var (i,obj) in next)cel.Objects[i]=obj;PushStructure("rasterize objects",before);
    }

    private void AlignSelectedObject(CanvasAlign edge)
    {
        if (!CanEditObject()) return;
        var offset=CanvasAlignment.Offset(SelectedObjectBounds(),DocW,DocH,edge);
        var cel=ActiveLayer.CelAt(_cur)!;var before=Before();
        foreach(int i in SelectedObjectIndices)cel.Objects[i]=((RasterObject)cel.Objects[i]).Moved(offset.X,offset.Y);
        PushStructure("align objects",before);
    }

    private void CopyAppearance()
    {
        if (!CanEditObject()) return;
        var obj = SelectedObject!;
        _appearance = (obj.Primitive, obj.TextSource, obj.Effects?.Select(CopyEffect).ToList(), obj.Opacity, obj.Blend);
        Say("Appearance copied. Select another object, then Paste appearance.");
    }

    private void PasteAppearance()
    {
        if (!CanEditObject() || _appearance is not { } style) return;
        var cel=ActiveLayer.CelAt(_cur)!;
        var replacements=new List<(int index,RasterObject obj)>();
        foreach(int i in SelectedObjectIndices)
        {
            var old=(RasterObject)cel.Objects[i];var next=old.Moved(0,0);
            if(old.Primitive is { } shape && style.shape is { } source)
                next=EditableSources.Replace(old,shape with { Colour=source.Colour,Filled=source.Filled,Stroke=source.Stroke },null);
            else if(old.TextSource is { } text && style.text is { } font)
                next=EditableSources.Replace(old,null,font with { Text=text.Text });
            next=next.WithEffectList(style.effects?.Select(CopyEffect).ToList());next.Opacity=style.opacity;next.Blend=style.blend;
            replacements.Add((i,next));
        }
        var before=Before();foreach(var (i,obj) in replacements)cel.Objects[i]=obj;PushStructure("paste appearance",before);
    }

    private static ObjectEffect CopyEffect(ObjectEffect e) => new(e.Kind) { P = new Dictionary<string, double>(e.P) };

    private void PaintObjectOutline(Graphics g, Point origin)
    {
        if (_xf != null || SelectedObject is not { } obj) return;
        using var pen = new Pen(Color.DeepSkyBlue) { DashStyle = DashStyle.Dash };
        var box=SelectedObjectBounds();
        g.DrawRectangle(pen, origin.X + box.X * _zoom, origin.Y + box.Y * _zoom, box.Width * _zoom, box.Height * _zoom);
    }

    internal int TestObjectIndex => _objectIndex;
    internal RasterObject? TestSelectedObject => SelectedObject;
    internal void TestPickObject(Point p) => PickObject(p);
    internal void TestSelectObject(int i) { SelectObject(i); RefreshObjects(); }
    internal void TestAddEditable(RasterObject r) => AddEditableObject(r);
    internal void TestEditObject(PrimitiveSpec? s, TextSource? t, string n, int x, int y) => ApplyObjectProperties(s, t, n, x, y);
    internal void TestCloneObject() => CloneSelectedObject();
    internal void TestDeleteObject() => DeleteSelectedObject();
    internal void TestRasterizeObject() => RasterizeSelectedObject();
    internal void TestAlignObject(CanvasAlign e) => AlignSelectedObject(e);
    internal void TestCopyAppearance() => CopyAppearance();
    internal void TestPasteAppearance() => PasteAppearance();
    internal bool TestEditableShapes { get => _editableShapes.Checked; set => _editableShapes.Checked = value; }
}
