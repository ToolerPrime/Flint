using System.Runtime.InteropServices;

namespace Flint;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);

    /// <summary>
    /// True for every --test launch. Modal dialogs check it, because a prompt
    /// nobody can answer hangs an automated run forever.
    /// </summary>
    internal static bool Headless;

    [STAThread]
    private static int Main(string[] args)
    {
        Headless = args.Length > 0 && args[0].StartsWith("--");
        if (args.Length >= 1 && args[0] == "--productivitytest")
        {
            AttachConsole(-1); ApplicationConfiguration.Initialize(); return ProductivityTest.Run();
        }

        if (args.Length >= 1 && args[0] == "--editabletest")
        {
            AttachConsole(-1);
            ApplicationConfiguration.Initialize();
            return EditableObjectsTest.Run();
        }

        if (args.Length >= 1 && args[0] == "--workspacetest")
        {
            AttachConsole(-1);
            ApplicationConfiguration.Initialize();
            return WorkspaceTest.Run();
        }

        if (args.Length >= 1 && args[0] == "--selftest")
        {
            AttachConsole(-1);   // a WinExe has no console of its own; borrow the parent's
            return SelfTest.Run(args.Length >= 2 ? args[1] : "selftest-out");
        }

        if (args.Length >= 3 && args[0] == "--opstest")
        {
            AttachConsole(-1);
            return OpsTest.Run(args[1], args[2]);
        }

        if (args.Length >= 1 && args[0] == "--importtest")
        {
            AttachConsole(-1);
            return ImportTest.Run(args.Length >= 2 ? args[1] : "import-out");
        }

        if (args.Length >= 1 && args[0] == "--toolstest")
        {
            AttachConsole(-1);
            return ToolsTest.Run();
        }

        if (args.Length >= 1 && args[0] == "--edittest")
        {
            AttachConsole(-1);
            return EditTest.Run(args.Length >= 2 ? args[1] : "edit-out");
        }

        if (args.Length >= 1 && args[0] == "--tracetest")
        {
            AttachConsole(-1);
            return TraceTest.Run(args.Length >= 2 ? args[1] : "trace-out");
        }

        if (args.Length >= 1 && args[0] == "--fwtest")
        {
            AttachConsole(-1);
            return FireworksTest.Run();
        }

        if (args.Length >= 1 && args[0] == "--spritetest")
        {
            AttachConsole(-1);
            return SpriteTest.Run(args.Length >= 2 ? args[1] : "sprite-out");
        }

        if (args.Length >= 1 && args[0] == "--formattest")
        {
            AttachConsole(-1);
            return FormatTest.Run(args.Length >= 2 ? args[1] : "format-out");
        }

        if (args.Length >= 1 && args[0] == "--fidelitytest")
        {
            AttachConsole(-1);
            return FidelityTest.Run(args.Length >= 2 ? args[1] : "fidelity-out");
        }

        if (args.Length >= 2 && args[0] == "--crashchild")
            return CrashTest.Child(args[1]);

        if (args.Length >= 1 && args[0] == "--crashtest")
        {
            AttachConsole(-1);
            return CrashTest.Run(args.Length >= 2 ? args[1] : "crashtest-out");
        }

        if (args.Length >= 1 && args[0] == "--memtest")
        {
            AttachConsole(-1);
            return MemTest.Run(args.Length >= 2 ? args[1] : "memtest-out");
        }

        if (args.Length >= 1 && args[0] == "--audiotest")
        {
            AttachConsole(-1);
            return AudioTest.Run(args.Length >= 2 ? args[1] : "audiotest-out");
        }

        if (args.Length >= 1 && args[0] == "--animtest")
        {
            AttachConsole(-1);
            return AnimTest.Run();
        }

        if (args.Length >= 1 && args[0] == "--doctest")
        {
            AttachConsole(-1);
            return DocTest.Run(args.Length >= 2 ? args[1] : "doctest-out");
        }

        if (args.Length >= 1 && args[0] == "--drawtest")
        {
            AttachConsole(-1);
            return DrawTest.Run();
        }

        if (args.Length >= 1 && args[0] == "--exporttest")
        {
            AttachConsole(-1);
            ApplicationConfiguration.Initialize();
            return ExportTest.Run(args.Length >= 2 ? args[1] : "exporttest-out");
        }

        if (args.Length >= 1 && args[0] == "--guidestest")
        {
            AttachConsole(-1);
            ApplicationConfiguration.Initialize();
            return GuidesTest.Run(args.Length >= 2 ? args[1] : "guidestest-out");
        }

        if (args.Length >= 3 && args[0] == "--webmtest")
        {
            AttachConsole(-1);
            return WebmTest.Run(args[1], args[2]);
        }

        if (args.Length >= 1 && args[0] == "--canvastest")
        {
            AttachConsole(-1);
            ApplicationConfiguration.Initialize();
            return CanvasTest.Run();
        }

        if (args.Length >= 2 && args[0] == "--shot")
        {
            AttachConsole(-1);
            ApplicationConfiguration.Initialize();
            return LayoutShot.Run(args[1], args.Length >= 3 ? args[2] : null);
        }

        if (Headless)
        {
            // An unknown switch must not fall through to opening a window that
            // an automated run would wait on forever.
            AttachConsole(-1);
            Console.WriteLine($"unknown option {args[0]}");
            return 2;
        }

        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        // A file given on launch (double-click, "Open with", dropped on Flint.exe).
        if (args.Length >= 1 && File.Exists(args[0])) form.Shown += (_, _) => form.OpenAny(Path.GetFullPath(args[0]));
        Application.Run(form);
        return 0;
    }
}
