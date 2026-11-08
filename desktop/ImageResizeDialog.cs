namespace Flint;

public sealed class ImageResizeDialog : ThemedForm
{
    private readonly NumericUpDown _width,_height,_percent;
    private readonly CheckBox _lock;
    private readonly ComboBox _quality;
    private bool _sync;
    public int ImageWidth=>(int)_width.Value;
    public int ImageHeight=>(int)_height.Value;
    public Resample Quality=>(Resample)_quality.SelectedIndex;

    public ImageResizeDialog(int width,int height,Resample quality)
    {
        Text="Resize image — all frames";FormBorderStyle=FormBorderStyle.FixedDialog;
        StartPosition=FormStartPosition.CenterParent;MinimizeBox=MaximizeBox=false;
        NumericUpDown Number(int x,int y,int value)=>new(){Left=x,Top=y,Width=90,Minimum=1,Maximum=ResizePlan.MaxSide,Value=Math.Clamp(value,1,ResizePlan.MaxSide)};
        void Label(string name,int y)=>Controls.Add(new Label{Text=name,Left=16,Top=y+4,Width=90});
        Label("Width / height",16);_width=Number(112,16,width);_height=Number(212,16,height);
        _width.AccessibleName="Image width";_height.AccessibleName="Image height";
        Controls.Add(_width);Controls.Add(_height);
        _lock=new MintCheckBox{Text="Keep proportions",Left=112,Top=50,Width=190,Checked=true};Controls.Add(_lock);
        Label("Scale %",82);_percent=new NumericUpDown{Left=112,Top=82,Width=90,Minimum=0.1m,Maximum=3200,DecimalPlaces=1,Value=100};Controls.Add(_percent);
        Label("Quality",118);_quality=new ComboBox{Left=112,Top=118,Width=190,DropDownStyle=ComboBoxStyle.DropDownList};
        _quality.Items.AddRange(new object[]{"Nearest — pixel edges","RotSprite — pixel art","Smooth — blended"});_quality.SelectedIndex=(int)quality;Controls.Add(_quality);
        Controls.Add(new Label{Text="All frames resize together. Frame delays stay unchanged.",Left=16,Top=154,Width=288,Height=34});
        _width.ValueChanged+=(_,_)=>
        {
            if(_sync)return;_sync=true;
            if(_lock.Checked)_height.Value=Math.Clamp(Math.Round(_width.Value*height/width),1,ResizePlan.MaxSide);
            _percent.Value=Math.Clamp(_width.Value*100/width,_percent.Minimum,_percent.Maximum);_sync=false;
        };
        _height.ValueChanged+=(_,_)=>
        {
            if(_sync)return;_sync=true;
            if(_lock.Checked)_width.Value=Math.Clamp(Math.Round(_height.Value*width/height),1,ResizePlan.MaxSide);
            _percent.Value=Math.Clamp(_height.Value*100/height,_percent.Minimum,_percent.Maximum);_sync=false;
        };
        _percent.ValueChanged+=(_,_)=>
        {
            if(_sync)return;
            try { var size=ResizePlan.Scale(width,height,(double)_percent.Value/100);_sync=true;_width.Value=size.Width;_height.Value=size.Height; }
            catch(ArgumentException ex) { MessageBox.Show(this,ex.Message,"Resize image"); }
            finally { _sync=false; }
        };
        var apply=new Button{Text="Resize",Left=134,Top=196,Width=80};
        apply.Click+=(_,_)=>{try{ResizePlan.Validate(ImageWidth,ImageHeight);DialogResult=DialogResult.OK;}catch(ArgumentException ex){MessageBox.Show(this,ex.Message,"Resize image");}};
        var cancel=new Button{Text="Cancel",Left=224,Top=196,Width=80,DialogResult=DialogResult.Cancel};
        Controls.Add(apply);Controls.Add(cancel);AcceptButton=apply;CancelButton=cancel;ClientSize=new Size(320,240);
    }
}
