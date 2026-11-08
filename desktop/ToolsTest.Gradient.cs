/* The Gradient tool's patterned kinds. Part of --toolstest. */
namespace Flint;

public static partial class ToolsTest
{
    private static void GradientKindChecks(MainForm form)
    {
        Console.WriteLine("Gradient kinds");
        const int N = 33;
        byte[] Run(int index)
        {
            form.TestNewCanvas(N, N);
            form.TestSetColours(Color.Black, Color.White);
            form.TestSetGradient(index);
            form.TestDrag(Tool.Gradient, Keys.None, new Point(16, 16), new Point(32, 16));
            return form.TestLayerPixels();
        }
        int R(byte[] p, int x, int y) => p[(y * N + x) * 4 + 2];
        bool Dark(int v) => v < 40;
        bool Light(int v) => v > 215;
        bool Mid(int v) => v is > 100 and < 156;

        var p = Run(4);   // Rectangle
        Check(Dark(R(p, 16, 16)) && Light(R(p, 32, 16)) && Light(R(p, 16, 0)) && Mid(R(p, 24, 24)) && Light(R(p, 0, 0)), "Rectangle: square rings, black in the middle, white at the box edge");
        p = Run(12);      // Diamond
        Check(Dark(R(p, 16, 16)) && Mid(R(p, 24, 16)) && Light(R(p, 24, 24)), "Diamond: rings are diamonds (the diagonal reaches white where the box's does not)");
        p = Run(5);       // Cone
        Check(Dark(R(p, 32, 16)) && Light(R(p, 0, 16)) && Mid(R(p, 16, 0)) && Mid(R(p, 16, 32)), "Cone: sweeps from the start direction round to the opposite one");
        p = Run(6);       // Starburst
        Check(Dark(R(p, 16, 16)) && Light(R(p, 32, 16)) && R(p, 31, 22) < 70, "Starburst: rays that fade in from the centre, dark between rays");
        p = Run(7);       // Bars
        Check(Mid(R(p, 24, 16)) && Light(R(p, 32, 16)) && Dark(R(p, 16, 16)) && Mid(R(p, 8, 16)) && R(p, 24, 16) == R(p, 24, 3), "Bars: dark at the centre line, light at both ends of the first axis, constant across it");
        p = Run(8);       // Ripples
        Check(Dark(R(p, 16, 16)) && Mid(R(p, 24, 16)) && Light(R(p, 32, 16)) && R(p, 32, 32) is > 120 and < 180, "Ripples: rings out and back, light at one radius and fading again beyond it");
        p = Run(13);      // Pinch
        Check(Dark(R(p, 16, 16)) && Dark(R(p, 30, 16)) && Dark(R(p, 16, 4)) && Mid(R(p, 24, 8)) == false && Light(R(p, 32, 0)), "Pinch: an X, dark along both axes and light in the corners");
        p = Run(9);       // Waves
        Check(Dark(R(p, 16, 16)) && Light(R(p, 20, 16)) && R(p, 20, 16) != R(p, 20, 18), "Waves: stripes along the first axis that wobble across it");
        p = Run(10);      // Satin
        Check(Light(R(p, 16, 16)) && Dark(R(p, 24, 16)), "Satin: crossing soft folds");
        p = Run(11);      // Folds
        Check(Light(R(p, 16, 16)) && R(p, 19, 16) < 60, "Folds: sharp pleats");
        p = Run(0);
        Check(Dark(R(p, 16, 16)) && Light(R(p, 32, 16)) && Light(R(p, 0, 0)) == false, "Linear is unchanged");

        // the same kinds on a vector gradient (as Fireworks files use them) agree with the tool
        var g = new VGradient { Kind = GradientKind.Diamond, X0 = 0, Y0 = 0, X1 = 10, Y1 = 0, X2 = 0, Y2 = 10 };
        Check(Math.Abs(g.T(5, 0) - 0.5) < 1e-9 && Math.Abs(g.T(5, 5) - 1) < 1e-9 && g.T(0, 0) == 0, "VGradient.T: diamond uses both axes");
        var g2 = new VGradient { Kind = GradientKind.Rectangle, X0 = 0, Y0 = 0, X1 = 10, Y1 = 0, X2 = 0, Y2 = 4 };
        Check(Math.Abs(g2.T(5, 2) - 0.5) < 1e-9 && Math.Abs(g2.T(2, 4) - 1) < 1e-9, "VGradient.T: rectangle follows the second axis length");
        var mapped = g2.Mapped(Affine.Translate(3, 3));
        Check(mapped.Kind == GradientKind.Rectangle && Math.Abs(mapped.T(8, 5) - 0.5) < 1e-9, "a mapped gradient keeps its kind");
    }
}
