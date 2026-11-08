/* Crash recovery, tested with a real crash.
 *   Flint.exe --crashtest <dir>
 * starts a second Flint process that builds a document, autosaves it, and is
 * then killed outright — no shutdown code runs, as in a crash or power loss.
 * The parent then checks the recovery copy is found and is pixel-exact. */
using System.Diagnostics;

namespace Flint;

public static class CrashTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    /// <summary>The known content the child writes; the parent checks for it.</summary>
    private static Document Known()
    {
        var d = new Document(40, 30);
        d.InsertFrame(1);
        d.Frames[1].Delay = 230;
        d.AddLayer("Ink");
        var px = d.Layers[1].EnsureCel(1).EnsureRaster(40, 30).Pixels;
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 40; x++)
                px.SetPixel(x, y, Color.FromArgb((x * 7 + y) % 256, x * 6, y * 8, 200));
        return d;
    }

    /// <summary>Child half: write recovery, say so, then wait to be killed.</summary>
    public static int Child(string dir)
    {
        Recovery.Dir = dir;
        var d = Known();
        Recovery.Write(d, @"C:\Art\hero.flint");
        File.WriteAllText(Path.Combine(dir, "ready"), Environment.ProcessId.ToString());
        Thread.Sleep(60_000);   // never reached in a passing run
        return 0;
    }

    public static int Run(string dir)
    {
        Console.WriteLine("Crash recovery");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        Recovery.Dir = dir;

        var exe = Environment.ProcessPath!;
        var child = Process.Start(new ProcessStartInfo(exe, $"--crashchild \"{dir}\"")
        {
            UseShellExecute = false, CreateNoWindow = true,
        })!;
        string ready = Path.Combine(dir, "ready");
        var wait = Stopwatch.StartNew();
        while (!File.Exists(ready) && wait.ElapsedMilliseconds < 30_000 && !child.HasExited) Thread.Sleep(50);
        Check(File.Exists(ready), "the child process autosaved");

        // While the child is alive its copy is not an orphan.
        Check(Recovery.Orphans().Count == 0, "a running session's copy is not offered for recovery");

        child.Kill(entireProcessTree: true);
        child.WaitForExit(10_000);
        Check(child.HasExited, "the child was killed without shutting down");

        var orphans = Recovery.Orphans();
        Check(orphans.Count == 1, $"after the crash, one recovery copy is found (got {orphans.Count})");
        if (orphans.Count == 1)
        {
            var e = orphans[0];
            Check(e.OriginalPath == @"C:\Art\hero.flint", "it remembers which file it belonged to");
            var loaded = FlintFormat.Load(e.ProjectPath);
            Check(loaded.Ok, "the recovery copy opens: " + (loaded.Error ?? "ok"));
            if (loaded.Ok)
            {
                var want = Known();
                var a = ImageOps.GetPixels(want.Layers[1].CelAt(1)!.FindRaster()!.Pixels);
                var b = ImageOps.GetPixels(loaded.Document!.Layers[1].CelAt(1)!.FindRaster()!.Pixels);
                Check(a.AsSpan().SequenceEqual(b), "recovered pixels are exact, including partial alpha");
                Check(loaded.Document.Frames[1].Delay == 230 && loaded.Document.Layers[1].Name == "Ink",
                      "recovered timing and layer names are intact");
                want.Dispose();
                loaded.Document.Dispose();
            }
            Recovery.Discard(e);
            Check(Recovery.Orphans().Count == 0, "discarding removes the copy");
        }

        // A clean session clears up after itself.
        var d = Known();
        Recovery.Write(d, null, "cleansession");
        Recovery.Clear("cleansession");
        Check(!Directory.GetFiles(dir, "cleansession*").Any(), "a clean exit leaves no recovery files");
        d.Dispose();

        Console.WriteLine(_fails == 0 ? "all recovery checks passed" : $"{_fails} CHECK(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
