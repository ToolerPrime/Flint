/* Assert-based checks for the drawing primitives. Flint.exe --drawtest */
namespace Flint;

public static class DrawTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    private static Surface Blank(int w, int h) => new(new byte[w * h * 4], w, h);

    private static int Count(Surface s, uint colour)
    {
        int n = 0;
        for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
                if (s.Get(x, y) == colour) n++;
        return n;
    }

    public static int Run()
    {
        uint red = Surface.Pack(Color.FromArgb(255, 255, 0, 0));
        uint blue = Surface.Pack(Color.FromArgb(255, 0, 0, 255));
        Console.WriteLine("Drawing primitive checks");

        // A one-pixel pencil dot must set exactly one pixel.
        {
            var s = Blank(16, 16);
            Draw.Dot(s, 8, 8, 1, red, null);
            Check(Count(s, red) == 1, "size-1 dot sets exactly 1 pixel");
            Check(s.Get(8, 8) == red, "dot lands on the requested pixel");
        }

        // A 3-wide nib is a 3x3 square centred on the point.
        {
            var s = Blank(16, 16);
            Draw.Dot(s, 8, 8, 3, red, null);
            Check(Count(s, red) == 9, "size-3 dot sets 9 pixels");
            Check(s.Get(7, 7) == red && s.Get(9, 9) == red, "size-3 nib is centred");
        }

        // Horizontal line: exact length, no antialiasing.
        {
            var s = Blank(32, 8);
            Draw.Line(s, 2, 4, 20, 4, 1, red, null);
            Check(Count(s, red) == 19, $"horizontal line sets 19 px (got {Count(s, red)})");
        }

        // A line is symmetric: drawing it backwards paints the same pixels.
        {
            var a = Blank(32, 32);
            var b = Blank(32, 32);
            Draw.Line(a, 3, 5, 28, 21, 1, red, null);
            Draw.Line(b, 28, 21, 3, 5, 1, red, null);
            Check(a.Px.AsSpan().SequenceEqual(b.Px), "line is identical drawn either way");
        }

        // Diagonal line is continuous (no gaps between consecutive pixels).
        {
            var s = Blank(32, 32);
            Draw.Line(s, 0, 0, 31, 17, 1, red, null);
            bool continuous = true;
            Point? prev = null;
            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 32; y++)
                {
                    if (s.Get(x, y) != red) continue;
                    if (prev is { } p && Math.Abs(x - p.X) > 1) continuous = false;
                    prev = new Point(x, y);
                }
            }
            Check(continuous, "diagonal line has no column gaps");
        }

        // Clipping must be honoured by every primitive.
        {
            var s = Blank(32, 32);
            var clip = Selection.FromRect(32, 32, new Rectangle(10, 10, 5, 5));
            Draw.Line(s, 0, 12, 31, 12, 1, red, clip);
            Check(Count(s, red) == 5, $"line clipped to selection paints 5 px (got {Count(s, red)})");
            Check(s.Get(9, 12) != red && s.Get(10, 12) == red, "clip boundary is exact");
        }

        // Unfilled rectangle: perimeter only, corners not doubled.
        {
            var s = Blank(32, 32);
            Draw.Rect(s, new Rectangle(4, 4, 10, 8), 1, red, false, null);
            Check(Count(s, red) == 2 * 10 + 2 * 8 - 4,
                  $"10x8 outline sets {2 * 10 + 2 * 8 - 4} px (got {Count(s, red)})");
            Check(s.Get(4, 4) == red && s.Get(13, 11) == red, "rectangle corners are set");
            Check(s.Get(8, 8) != red, "rectangle interior stays empty");
        }

        // Filled rectangle covers exactly its area.
        {
            var s = Blank(32, 32);
            Draw.Rect(s, new Rectangle(4, 4, 10, 8), 1, red, true, null);
            Check(Count(s, red) == 80, $"10x8 filled rect sets 80 px (got {Count(s, red)})");
        }

        // Ellipse outline is closed: every row it touches has two edges (or one
        // at the poles), and the interior is untouched.
        {
            var s = Blank(40, 40);
            Draw.Ellipse(s, new Rectangle(5, 5, 21, 15), 1, red, false, null);
            Check(s.Get(15, 12) != red, "ellipse interior stays empty");
            int rowsTouched = 0;
            for (int y = 0; y < 40; y++)
                for (int x = 0; x < 40; x++)
                    if (s.Get(x, y) == red) { rowsTouched++; break; }
            Check(rowsTouched == 15, $"ellipse outline spans all 15 rows (got {rowsTouched})");
        }

        // Filled ellipse is symmetric about both axes.
        {
            var s = Blank(41, 41);
            Draw.Ellipse(s, new Rectangle(0, 0, 41, 41), 1, red, true, null);
            bool symmetric = true;
            for (int y = 0; y < 41 && symmetric; y++)
                for (int x = 0; x < 41; x++)
                    if (s.Get(x, y) != s.Get(40 - x, y) || s.Get(x, y) != s.Get(x, 40 - y))
                    { symmetric = false; break; }
            Check(symmetric, "filled circle is symmetric on both axes");
        }

        // Flood fill respects boundaries.
        {
            var s = Blank(20, 20);
            Draw.Rect(s, new Rectangle(4, 4, 12, 12), 1, red, false, null);
            int painted = Draw.Fill(s, 10, 10, blue, 0, null);
            Check(painted == 100, $"fill inside a 12x12 box paints 10x10=100 (got {painted})");
            Check(s.Get(0, 0) != blue, "fill did not leak outside the boundary");
            Check(s.Get(4, 4) == red, "fill left the boundary intact");
        }

        // Filling an already-matching region terminates instead of looping.
        {
            var s = Blank(16, 16);
            int first = Draw.Fill(s, 8, 8, red, 0, null);
            int second = Draw.Fill(s, 8, 8, red, 0, null);
            Check(first == 256, $"first fill covers the whole surface (got {first})");
            Check(second == 0, "re-filling the same colour is a no-op");
        }

        // Fill with tolerance crosses a near-match but not a far one.
        {
            var s = Blank(20, 1);
            for (int x = 0; x < 20; x++)
                s.Set(x, 0, Surface.Pack(Color.FromArgb(255, 100, 100, 100)), null);
            s.Set(10, 0, Surface.Pack(Color.FromArgb(255, 104, 100, 100)), null);  // near
            s.Set(15, 0, Surface.Pack(Color.FromArgb(255, 255, 0, 0)), null);      // far
            int painted = Draw.Fill(s, 0, 0, blue, 12, null);
            Check(painted == 15, $"tolerant fill crosses the near pixel, stops at the far one (got {painted})");
        }

        // A large fill must not overflow the stack.
        {
            var s = Blank(600, 600);
            int painted = Draw.Fill(s, 300, 300, red, 0, null);
            Check(painted == 600 * 600, $"600x600 fill completes ({painted} px)");
        }

        Console.WriteLine(_fails == 0 ? "all drawing checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
