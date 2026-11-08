using Flint;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected validation failure"); }

var rectangle = EditableShapeGeometry.Rectangle(8, 6, 0xFF123456);
Check(rectangle.W == 8 && rectangle.H == 6, "rectangle bounds");
Check(rectangle.Shapes.Count == 1 && rectangle.Shapes[0].Colour == 0xFF123456, "rectangle fill");
Check(rectangle.Shapes[0].Contours[0].SequenceEqual(new[] { new VPt(0,0),new VPt(8,0),new VPt(8,6),new VPt(0,6) }), "rectangle corners");
rectangle.Shapes[0].Contours[0].Clear();
Check(EditableShapeGeometry.Rectangle(8,6,0).Shapes[0].Contours[0].Count == 4, "shape lists independent");
var ellipse = EditableShapeGeometry.Ellipse(8,6,0).Shapes[0].Contours[0];
Check(ellipse.Count == 128 && ellipse.Distinct().Count() == 128, "ellipse vertices");
foreach (var p in ellipse) Check(Math.Abs(Math.Pow((p.X-4)/4,2)+Math.Pow((p.Y-3)/3,2)-1)<1e-12, "ellipse equation");
Reject(()=>EditableShapeGeometry.Rectangle(0,1,0));
Reject(()=>EditableShapeGeometry.Ellipse(1,16385,0));

var text = new EditableTextSpec("hello", "Arial", 12, false, true, 123);
var edited = text.WithText("world");
Check(text.Text == "hello" && edited.Text == "world" && edited.Italic && edited.Colour == 123 && edited.FontFamily == "Arial" && edited.FontSize == 12, "text immutable editing");
Reject(()=>new EditableTextSpec(null!,"Arial",12,false,false,0));
Reject(()=>new EditableTextSpec("x"," ",12,false,false,0));
foreach(var size in new[]{0f,-1f,float.NaN,float.PositiveInfinity,4097f}) Reject(()=>new EditableTextSpec("x","Arial",size,false,false,0));

for (int a=0; a<256; a++) for(int m=0; m<256; m++) {
    byte[] source = {17,33,245,(byte)a}; byte[] mask = {(byte)m};
    var result = MaskPixels.Apply(source,mask);
    Check(result[0]==17 && result[1]==33 && result[2]==245, "mask preserves RGB");
    Check(result[3]==(int)Math.Floor(a*m/255.0+0.5), "mask alpha rounding");
    Check(source[3]==a && mask[0]==m && !ReferenceEquals(source,result), "mask immutability");
}
Reject(()=>MaskPixels.Apply(new byte[3],new byte[1]));
Reject(()=>MaskPixels.Apply(new byte[4],Array.Empty<byte>()));
Reject(()=>MaskPixels.Apply(null!,Array.Empty<byte>()));
var empty=Array.Empty<byte>(); Check(!ReferenceEquals(empty,MaskPixels.Apply(empty,empty)),"empty output independent");
Console.WriteLine("PASS: geometry, text invariants, and all 65,536 alpha/mask combinations");

namespace Flint {
    public readonly record struct VPt(double X, double Y);
    public sealed class VShape { public uint Colour; public List<List<VPt>> Contours = new(); }
    public sealed class VectorArt { public int W,H; public List<VShape> Shapes = new(); }
}
