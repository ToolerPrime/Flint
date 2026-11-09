namespace Flint;

internal static class ProjectTabsTest
{
    internal static int Run(string? shot)
    {
        int failures = 0;
        void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  ok   " : "  FAIL ") + label);
            if (!condition) failures++;
        }

        string folder = Path.Combine(Path.GetTempPath(), "flint-tabs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string previousRecovery = Recovery.Dir;
        Recovery.Dir = folder;
        try
        {
            using var window = new ProjectWindow { StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) };
            window.Show();
            Application.DoEvents();
            window.CreateProject(32, 24, true);
            var first = window.ActiveEditor;
            Check(first.ProjectTabTitle == "Untitled-1", "the first new project is named Untitled-1");
            first.TestStroke(Tool.Pencil, Color.Red, 1, new Point(2, 3), new Point(4, 3));
            string firstFile = Path.Combine(folder, "first.flint");
            Check(first.TestSaveViaEditor(firstFile), "save the first project");
            first.TestStroke(Tool.Pencil, Color.Lime, 1, new Point(5, 5), new Point(6, 5));
            first.TestAutosaveNow();

            window.CreateProject(48, 32, true);
            var second = window.ActiveEditor;
            Check(second.ProjectTabTitle == "Untitled-2", "the second new project is named Untitled-2");
            Check(second.TestStripAboveCanvas(window.ProjectStrip), "project tabs sit directly above the active canvas");
            second.TestStroke(Tool.Pencil, Color.Blue, 1, new Point(7, 4), new Point(9, 4));
            second.TestAutosaveNow();
            Check(window.ProjectCount == 2 && first != second, "New Canvas opens its own project tab");
            Check(first.TestDocument.Width == 32 && second.TestDocument.Width == 48, "tabs retain their own canvas size");
            Check(Directory.GetFiles(folder, "*.json").Length == 2, "both projects have separate autosaves");
            Check(window.PathInUse(second, firstFile), "another tab cannot save over an already open project");

            var layers = second.TestDocks.FindPage("Layers")!.Group!;
            second.TestDocks.Float(layers, new Rectangle(-5800, -5800, 240, 220));
            Check(layers.FloatingWindow!.Visible, "active project's floating Layers panel is visible");
            Check(window.OpenProject(firstFile) && window.ActiveEditor == first && window.ProjectCount == 2,
                "opening the same file focuses its existing tab");
            Check(!layers.FloatingWindow.Visible, "inactive project's floating Layers panel is hidden");
            Check(first.TestStripAboveCanvas(window.ProjectStrip), "the strip follows the selected canvas");
            var secondTab = window.ProjectStrip.Items.OfType<ToolStripButton>().Single(item => item.Text == "Untitled-2 *");
            secondTab.PerformClick();
            Check(window.ActiveEditor == second && secondTab.Checked, "clicking a project tab selects it");
            second.TestCmdKey(Keys.Control | Keys.Shift | Keys.Tab);
            using (var frame = first.TestDocument.RenderFrame(0))
                Check(frame.GetPixel(5, 5).ToArgb() == Color.Lime.ToArgb(), "reopening an open file keeps unsaved pixels");
            Check(first.TestCmdKey(Keys.Control | Keys.Z), "Ctrl+Z is handled inside the embedded editor");
            using (var frame = first.TestDocument.RenderFrame(0))
                Check(frame.GetPixel(5, 5).A == 0, "Ctrl+Z undoes the active tab's stroke");
            using (var frame = second.TestDocument.RenderFrame(0))
                Check(frame.GetPixel(7, 4).ToArgb() == Color.Blue.ToArgb(), "undo affects only the active project");
            Check(first.TestSaveViaEditor(firstFile) && Directory.GetFiles(folder, "*.json").Length == 1,
                "saving one tab leaves the other tab's recovery intact");

            first.TestCmdKey(Keys.Control | Keys.Tab);
            Check(window.ActiveEditor == second && layers.FloatingWindow.Visible, "Ctrl+Tab returns to the other tab and its floating panel");
            second.TestDocks.DockNewColumnOrEnd(layers, DockSide.Right);
            if (shot != null)
            {
                using var bitmap = new Bitmap(window.Width, window.Height);
                window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
                bitmap.Save(shot);
            }
            second.TestCmdKey(Keys.Control | Keys.W);
            Check(second.IsDisposed && window.ProjectCount == 1 && window.ActiveEditor == first,
                "Ctrl+W closes just the selected tab");
            Check(Directory.GetFiles(folder, "*.json").Length == 0, "closing a tab clears only its recovery copy");

            string otherFile = Path.Combine(folder, "other.flint");
            FlintFormat.Save(new Document(12, 10), otherFile);
            Check(window.OpenProject(otherFile) && window.ProjectCount == 2 && window.ActiveEditor.TestDocument.Width == 12,
                "opening a different saved project adds a tab");
            string invalidFile = Path.Combine(folder, "invalid.flint");
            File.WriteAllText(invalidFile, "bad project");
            var previous = window.ActiveEditor;
            Check(!window.OpenProject(invalidFile) && window.ProjectCount == 2 && window.ActiveEditor == previous,
                "a failed open keeps the previous project active");
            window.ProjectStrip.Items.OfType<ToolStripButton>().Single(b => b.Text == "×").PerformClick();
            Check(previous.IsDisposed && window.ProjectCount == 1 && first.TestStripAboveCanvas(window.ProjectStrip),
                "closing from the tab strip leaves the remaining project and its strip intact");
            first.TestCmdKey(Keys.Control | Keys.W);
            Check(window.ProjectCount == 1 && window.ActiveEditor.TestStripAboveCanvas(window.ProjectStrip),
                "closing the final project leaves a fresh canvas with a usable tab strip");
            window.Close();
        }
        finally
        {
            Recovery.Dir = previousRecovery;
            foreach (var path in Directory.GetFiles(folder)) File.Delete(path);
            Directory.Delete(folder);
        }
        Console.WriteLine(failures == 0 ? "all project tab checks passed" : $"{failures} project tab checks failed");
        return failures == 0 ? 0 : 1;
    }
}
