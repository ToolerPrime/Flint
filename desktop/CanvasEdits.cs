namespace Flint;

/// <summary>Document-wide geometry operations. Prepare allocations before replacing live containers.</summary>
public static class CanvasEdits
{
    public static Rectangle ContentBounds(Document doc, int? frame)
    {
        if (frame is { } f && (f < 0 || f >= doc.FrameCount)) throw new ArgumentOutOfRangeException(nameof(frame));
        long left=long.MaxValue,top=long.MaxValue,right=long.MinValue,bottom=long.MinValue;
        foreach(var layer in doc.Layers)
        {
            if(!layer.Visible || layer.Opacity==0) continue;
            IEnumerable<Cel> cels=frame is { } at ? layer.CelAt(at) is { } currentCel ? new[]{currentCel} : Array.Empty<Cel>() : layer.Cels.Values;
            foreach(var cel in cels)
                foreach(var obj in cel.Objects.OfType<RasterObject>())
                {
                    if(!obj.Visible || obj.Opacity==0) continue;
                    var b=Sprites.ContentRect(ImageOps.GetPixels(obj.Pixels),obj.Width,obj.Height);
                    if(b.IsEmpty) continue;
                    long x=(long)obj.X+b.X,y=(long)obj.Y+b.Y;
                    left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x+b.Width);bottom=Math.Max(bottom,y+b.Height);
                }
        }
        if(left==long.MaxValue) return Rectangle.Empty;
        long width=right-left,height=bottom-top;
        if(width>ResizePlan.MaxSide || height>ResizePlan.MaxSide) throw new ArgumentException("The fitted canvas is too large.");
        ResizePlan.Validate((int)width,(int)height);
        return new Rectangle(checked((int)left),checked((int)top),(int)width,(int)height);
    }

    public static Rectangle VisibleBounds(Document doc)
    {
        var result=Rectangle.Empty;
        for(int frame=0;frame<doc.FrameCount;frame++)
        {
            using var bitmap=doc.RenderFrame(frame);
            var b=Sprites.ContentRect(ImageOps.GetPixels(bitmap),doc.Width,doc.Height);
            if(!b.IsEmpty) result=result.IsEmpty?b:Rectangle.Union(result,b);
        }
        return result;
    }

    public static void Fit(Document doc, Rectangle bounds)
    {
        ResizePlan.Validate(bounds.Width,bounds.Height);
        int dx=checked(-bounds.X),dy=checked(-bounds.Y);
        var replacements=new List<(Cel cel,int index,RasterObject image)>();
        var masks=new List<(Cel cel,RasterObject mask)>();
        foreach(var layer in doc.Layers)
            foreach(var cel in layer.Cels.Values)
            {
                for(int i=0;i<cel.Objects.Count;i++)
                    if(cel.Objects[i] is RasterObject r) replacements.Add((cel,i,r.Moved(dx,dy)));
                if(cel.Mask is { } m) masks.Add((cel,Document.ReframeMask(m,bounds.Width,bounds.Height,dx,dy)));
            }
        var guides=doc.Guides.Select(g=>new Guide(g.Vertical,checked(g.Pos+(g.Vertical?dx:dy)))).ToArray();
        var markers=PrepareMarkers(doc,p=>new Point(checked(p.X+dx),checked(p.Y+dy)),r=>new Rectangle(checked(r.X+dx),checked(r.Y+dy),r.Width,r.Height));
        foreach(var (cel,index,image) in replacements) cel.Objects[index]=image;
        foreach(var (cel,mask) in masks) cel.Mask=mask;
        foreach(var apply in markers) apply();
        doc.Guides=guides;doc.Width=bounds.Width;doc.Height=bounds.Height;
    }

    public static void Clip(Document doc,int? frame)
    {
        if(frame is { } f && (f<0 || f>=doc.FrameCount)) throw new ArgumentOutOfRangeException(nameof(frame));
        var canvas=new Rectangle(0,0,doc.Width,doc.Height);
        var edits=new List<(Layer layer,Dictionary<int,Cel> cells,bool shared)>();
        foreach(var layer in doc.Layers)
        {
            var cells=new Dictionary<int,Cel>(layer.Cels);
            bool changed=false;
            if(frame is { } at)
            {
                if(layer.CelAt(at) is not { } old) continue;
                var clipped=ClipCel(old,canvas,out changed);
                if(!changed) continue;
                if(layer.Shared)
                {
                    long bytes=old.Objects.OfType<RasterObject>().Sum(r=>(long)r.Width*r.Height*4+(r.Original is { } o?(long)o.Width*o.Height*4:0));
                    if(old.Mask is { } m) bytes+=(long)m.Width*m.Height*4;
                    if(bytes*Math.Max(0,doc.FrameCount-1)>256L*1024*1024) throw new InvalidOperationException("Separating this shared layer needs too much memory. Clip the whole animation instead.");
                    cells.Clear();
                    for(int i=0;i<doc.FrameCount;i++) cells[i]=i==at?clipped:old.Clone();
                }
                else cells[at]=clipped;
            }
            else
            {
                foreach(var (key,old) in layer.Cels)
                {
                    var clipped=ClipCel(old,canvas,out bool oneChanged);changed|=oneChanged;cells[key]=clipped;
                }
            }
            if(changed) edits.Add((layer,cells,frame==null && layer.Shared));
        }
        foreach(var (layer,cells,shared) in edits)
        {
            layer.Cels.Clear();foreach(var (key,cel) in cells) layer.Cels[key]=cel;layer.Shared=shared;
        }
    }

    private static Cel ClipCel(Cel old,Rectangle canvas,out bool changed)
    {
        var result=new Cel { Mask=old.Mask };changed=false;
        foreach(var obj in old.Objects)
        {
            if(obj is not RasterObject r) { result.Objects.Add(obj);continue; }
            var bounds=new Rectangle(r.X,r.Y,r.Width,r.Height);
            var keep=Rectangle.Intersect(bounds,canvas);
            if(keep==bounds) { result.Objects.Add(r);continue; }
            changed=true;
            if(keep.Width<=0 || keep.Height<=0) continue;
            result.Objects.Add(r.With(ImageOps.Crop(r.Pixels,new Rectangle(keep.X-r.X,keep.Y-r.Y,keep.Width,keep.Height)),keep.X,keep.Y));
        }
        return changed?result:old;
    }

    public static void Resize(Document doc,int width,int height,Resample quality)
    {
        ResizePlan.Validate(width,height);
        double sx=width/(double)doc.Width,sy=height/(double)doc.Height;
        var matrix=Affine.Scale(sx,sy);
        var replacements=new List<(Cel cel,int index,RasterObject image)>();
        var masks=new List<(Cel cel,RasterObject mask)>();
        foreach(var layer in doc.Layers)
            foreach(var cel in layer.Cels.Values)
            {
                for(int i=0;i<cel.Objects.Count;i++)
                    if(cel.Objects[i] is RasterObject r) replacements.Add((cel,i,r.Retransform(matrix,quality,false)));
                if(cel.Mask is { } m) masks.Add((cel,new RasterObject(ImageOps.Scale(m.Pixels,width,height,false)) { Name=m.Name }));
            }
        int S(double value)=>checked((int)Math.Round(value,MidpointRounding.AwayFromZero));
        var guides=doc.Guides.Select(g=>new Guide(g.Vertical,S(g.Pos*(g.Vertical?sx:sy)))).ToArray();
        var markers=PrepareMarkers(doc,p=>new Point(S(p.X*sx),S(p.Y*sy)),r=>new Rectangle(S(r.X*sx),S(r.Y*sy),Math.Max(1,S(r.Width*sx)),Math.Max(1,S(r.Height*sy))));
        foreach(var (cel,index,image) in replacements) cel.Objects[index]=image;
        foreach(var (cel,mask) in masks) cel.Mask=mask;
        foreach(var apply in markers) apply();
        doc.Guides=guides;doc.Width=width;doc.Height=height;
    }

    private static List<Action> PrepareMarkers(Document doc,Func<Point,Point> point,Func<Rectangle,Rectangle> rectangle)
    {
        var updates=new List<Action>();
        foreach(var frame in doc.Frames)
        {
            if(frame.Pivot is { } p) { var next=point(p);updates.Add(()=>frame.Pivot=next); }
            foreach(var hitbox in frame.Hitboxes) { var next=rectangle(hitbox.Rect);updates.Add(()=>hitbox.Rect=next); }
        }
        return updates;
    }
}
