namespace Flint;

public sealed partial class MainForm
{
    private readonly Dictionary<Keys,Action> _productivityShortcuts=new();
    private ComboBox _imageQuality=null!;
    private Resample ImageQuality=>_imageQuality==null?Resample.Nearest:(Resample)Math.Max(0,_imageQuality.SelectedIndex);

    private void BuildProductivityMenus(ToolStripMenuItem edit,ToolStripMenuItem image,ToolStripMenuItem transform)
    {
        void Command(ToolStripMenuItem parent,string name,Keys keys,Action run,params Keys[] aliases)
        {
            Add(parent,name,keys,run);_productivityShortcuts.Add(keys,run);
            foreach(var alias in aliases)_productivityShortcuts.Add(alias,run);
        }
        edit.DropDownItems.Add(new ToolStripSeparator());
        Command(edit,"&Group objects",Keys.Control|Keys.G,GroupObjects);
        Command(edit,"&Ungroup",Keys.Control|Keys.Alt|Keys.G,UngroupObjects,Keys.Control|Keys.U,Keys.Control|Keys.Shift|Keys.G);
        Command(edit,"&Flatten selection",Keys.Control|Keys.Alt|Keys.Shift|Keys.Z,FlattenSelectedObjects);
        Command(transform,"&Numeric transform…",Keys.Control|Keys.Alt|Keys.T,NumericTransform);
        transform.DropDownItems.Add(new ToolStripSeparator());
        Command(transform,"Quick scale selection: &50%",Keys.Control|Keys.Alt|Keys.D5,()=>QuickScaleSelection(0.5));
        Command(transform,"Quick scale selection: &200%",Keys.Control|Keys.Alt|Keys.D2,()=>QuickScaleSelection(2));
        image.DropDownItems.Add(new ToolStripSeparator());
        Command(image,"Fit canvas to &whole animation",Keys.Control|Keys.Shift|Keys.F,()=>FitCanvas(false));
        Command(image,"Fit canvas to &current frame",Keys.Control|Keys.Alt|Keys.Shift|Keys.F,()=>FitCanvas(true));
        Command(image,"&Trim empty canvas — all frames",Keys.Control|Keys.Shift|Keys.T,TrimCanvas);
        var clip=new ToolStripMenuItem("Cut images outside canvas");
        Command(clip,"Current &frame",Keys.Control|Keys.Alt|Keys.Shift|Keys.X,()=>ClipCanvas(true));
        Command(clip,"&Whole animation",Keys.Control|Keys.Alt|Keys.Shift|Keys.K,()=>ClipCanvas(false));image.DropDownItems.Add(clip);
        Command(image,"Image &size / quality…",Keys.Control|Keys.Alt|Keys.I,ShowImageResize);
        var quick=new ToolStripMenuItem("Quick scale whole animation");
        quick.DropDownItems.Add("50%",null,(_,_)=>QuickScaleImage(0.5));quick.DropDownItems.Add("200%",null,(_,_)=>QuickScaleImage(2));image.DropDownItems.Add(quick);
    }

    private bool TryProductivityShortcut(Keys key)
    {
        Control? focus=ActiveControl;
        while(focus is ContainerControl container && container.ActiveControl!=null)focus=container.ActiveControl;
        if(focus is TextBoxBase or UpDownBase or ComboBox)return false;
        if(!_productivityShortcuts.TryGetValue(key,out var run))return false;
        run();return true;
    }

    private void CanvasEdit(string name,Action action,bool clearSelection=false)
    {
        FinishFloat();StopPlay();var before=Before();
        try
        {
            action();if(clearSelection)SelectObject(-1);_selection=null;_cropRect=null;
            FitZoom();PushStructure(name,before);Say(name+" applied");
        }
        catch(ArgumentException ex){Say(ex.Message);}
        catch(InvalidOperationException ex){Say(ex.Message);}
        catch(OverflowException){Say("The requested dimensions or position are too large.");}
    }

    private void FitCanvas(bool current)
    {
        FinishFloat();StopPlay();
        try
        {
            var bounds=CanvasEdits.ContentBounds(_doc,current?_cur:null);
            if(bounds.IsEmpty){Say("The chosen frame range is empty.");return;}
            CanvasEdit(current?"fit canvas to current frame":"fit canvas to whole animation",()=>CanvasEdits.Fit(_doc,bounds));
        }
        catch(ArgumentException ex){Say(ex.Message);}
        catch(OverflowException){Say("The fitted canvas is too large.");}
    }

    private void TrimCanvas()
    {
        FinishFloat();StopPlay();var bounds=CanvasEdits.VisibleBounds(_doc);
        if(bounds.IsEmpty){Say("Every frame is empty.");return;}
        CanvasEdit("trim empty canvas",()=>CanvasEdits.Fit(_doc,bounds));
    }

    private void ClipCanvas(bool current)=>CanvasEdit(current?"cut outside current frame":"cut outside whole animation",()=>CanvasEdits.Clip(_doc,current?_cur:null),true);

    private void ResizeImage(int width,int height,Resample quality)=>CanvasEdit("resize image",()=>CanvasEdits.Resize(_doc,width,height,quality));

    private void QuickScaleImage(double factor)
    {
        try { var size=ResizePlan.Scale(DocW,DocH,factor);ResizeImage(size.Width,size.Height,ImageQuality); }
        catch(ArgumentException ex){Say(ex.Message);}
    }

    private void ShowImageResize()
    {
        FinishFloat();StopPlay();using var dialog=new ImageResizeDialog(DocW,DocH,ImageQuality);
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        _imageQuality.SelectedIndex=(int)dialog.Quality;
        ResizeImage(dialog.ImageWidth,dialog.ImageHeight,dialog.Quality);
    }

    internal void TestResizeImage(int width,int height,Resample quality)=>ResizeImage(width,height,quality);
    internal void TestFocusCanvasForCommands()=>_canvas.Focus();
    internal bool TestProductivityShortcut(Keys key,bool execute)=>execute?TryProductivityShortcut(key):_productivityShortcuts.ContainsKey(key);
}
