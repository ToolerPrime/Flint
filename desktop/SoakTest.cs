using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Flint;

/// <summary>
/// Long-session leak check (Fireworks 8 eventually runs out of memory however
/// it is used). Repeats a mix of everyday editing many times and checks that
/// memory and Windows GDI/USER handles stop growing once warmed up.
/// </summary>
public static class SoakTest
{
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);

    private static int _fails;
    private static void Check(bool ok, string what) { Console.WriteLine($"{(ok ? "  ok   " : "  FAIL ")}{what}"); if (!ok) _fails++; }

    private readonly record struct Usage(long Managed, long Private, uint Gdi, uint User);

    private static Usage Measure()
    {
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        using var p = Process.GetCurrentProcess();
        return new Usage(GC.GetTotalMemory(true), p.PrivateMemorySize64, GetGuiResources(p.Handle, 0), GetGuiResources(p.Handle, 1));
    }

    private static string Mb(long b) => (b / 1048576.0).ToString("0.0");

    public static int Run(int cycles = 450)
    {
        _fails = 0;
        string fw = @"C:\Program Files (x86)\Macromedia\Fireworks 8\Samples\Pocket_PC.png";
        bool haveFw = File.Exists(fw);
        using var form = new MainForm { SuppressPrompts = true, StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) };
        form.Show(); Application.DoEvents();
        var spec = new PrimitiveSpec(false, 20, 14, 0xFF3080FF, true, 2);

        void Cycle(int n)
        {
            form.TestNewCanvas(256, 256);
            for (int i = 0; i < 3; i++) form.TestAddEditable(EditableSources.Shape(spec with { Width = 20 + i * 5 }, Affine.Translate(10 + i * 30, 10 + i * 20)));
            form.TestSelectObjects(0, 1); form.TestGroupObjects(); form.TestUngroupObjects();
            form.TestSelectObjects(2); form.TestFxAdd("Drop shadow");
            form.TestDrag(Tool.Pencil, Keys.None, new Point(5, 200), new Point(60, 210), new Point(120, 190));
            form.TestDrag(Tool.Rectangle, Keys.None, new Point(100, 100), new Point(150, 140));
            form.TestDrag(Tool.Marquee, Keys.None, new Point(0, 0), new Point(80, 80));
            form.TestDrag(Tool.Move, Keys.None, new Point(20, 20), new Point(30, 25));
            for (int i = 0; i < 4; i++) form.TestUndo();
            for (int i = 0; i < 4; i++) form.TestRedo();
            using (var frame = form.TestDocument.RenderFrame(0)) { }
            form.Refresh(); Application.DoEvents();
            if (haveFw && n % 5 == 0) { form.OpenFireworks(fw); form.Refresh(); Application.DoEvents(); }
        }

        Console.WriteLine($"Long session: {cycles} rounds of editing{(haveFw ? ", opening a Fireworks PNG every 5th round" : "")}");
        int warm = cycles / 3; // some UI (menus, tooltips) is created on first use, so measure after it settles
        for (int n = 0; n < warm; n++) Cycle(n);
        var a = Measure();
        var sw = Stopwatch.StartNew();
        for (int n = warm; n < cycles; n++)
        {
            Cycle(n);
            if ((n - warm + 1) % ((cycles - warm) / 4) == 0)
            {
                var m = Measure();
                Console.WriteLine($"  info round {n + 1}: managed {Mb(m.Managed)} MB, private {Mb(m.Private)} MB, GDI {m.Gdi}, USER {m.User}");
            }
        }
        var b = Measure();
        Console.WriteLine($"  info after warm-up: managed {Mb(a.Managed)} MB, private {Mb(a.Private)} MB, GDI {a.Gdi}, USER {a.User}; {cycles - warm} more rounds took {sw.ElapsedMilliseconds / 1000.0:0.0} s");
        Check(b.Gdi <= a.Gdi + 20, $"GDI handles stay flat ({a.Gdi} -> {b.Gdi})");
        Check(b.User <= a.User + 20, $"USER handles stay flat ({a.User} -> {b.User})");
        Check(b.Managed - a.Managed < 16L << 20, $"managed memory stays flat ({Mb(a.Managed)} -> {Mb(b.Managed)} MB)");
        Check(b.Private - a.Private < 64L << 20, $"process memory stays flat ({Mb(a.Private)} -> {Mb(b.Private)} MB)");
        form.Close();
        Console.WriteLine(_fails == 0 ? "all long-session checks passed" : $"{_fails} long-session checks failed");
        return _fails == 0 ? 0 : 1;
    }
}
