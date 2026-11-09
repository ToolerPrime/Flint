namespace Flint;

public sealed partial class MainForm
{
    internal void ShowStartPage()
    {
        StopPlay();
        using var page = MakeStartPage();
        page.ShowDialog(ProjectOwner);
        _projectWindow?.ActiveEditor.FocusProject();
    }

    internal ThemedForm MakeStartPage()
    {
        var page = new ThemedForm
        {
            Text = "Start Flint", AccessibleName = "Start Flint",
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(500, 308), MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
        };
        var tips = Theme.MakeTip();
        page.Disposed += (_, _) => tips.Dispose();
        var title = new Label { Text = "Recent projects", Left = 16, Top = 16, Width = 468, Height = 24,
            AccessibleName = "Recent projects heading", Font = new Font(Font, FontStyle.Bold) };
        var recent = new ListBox { Left = 16, Top = 46, Width = 468, Height = 172, IntegralHeight = false,
            AccessibleName = "Recent projects", HorizontalScrollbar = true };
        foreach (string path in Theme.RecentProjects.Where(File.Exists))
            recent.Items.Add(new RecentProject(path));
        if (recent.Items.Count > 0) recent.SelectedIndex = 0;
        var hint = new Label { Left = 16, Top = 227, Width = 468, Height = 20, AutoEllipsis = true,
            AccessibleName = "Selected project path", Tag = "muted" };
        void UpdateHint()
        {
            hint.Text = recent.SelectedItem is RecentProject item ? item.Path : "Projects you open or save will appear here.";
            tips.SetToolTip(hint, hint.Text);
        }
        recent.SelectedIndexChanged += (_, _) => UpdateHint();
        UpdateHint();

        void OpenRecent()
        {
            if (recent.SelectedItem is RecentProject item && TryOpenRecent(item.Path, page))
                page.DialogResult = DialogResult.OK;
        }

        var open = Btn("Open recent", 16, 264, 102, (_, _) => OpenRecent());
        open.Enabled = recent.Items.Count != 0;
        recent.DoubleClick += (_, _) => OpenRecent();
        var browse = Btn("Browse…", 126, 264, 94, (_, _) =>
        {
            if (TryOpenProject(page)) page.DialogResult = DialogResult.OK;
        });
        var create = Btn("New canvas…", 228, 264, 122, (_, _) =>
        {
            if (TryNewCanvas(page)) page.DialogResult = DialogResult.OK;
        });
        var close = Btn("Close", 358, 264, 126, (_, _) => page.DialogResult = DialogResult.Cancel);
        foreach (var (control, name, tip) in new (Control, string, string)[]
        {
            (title, "Recent projects heading", "Projects opened or saved in Flint"),
            (recent, "Recent projects", "Choose a project. Double-click to open it."),
            (open, "Open recent project", "Open the selected project"),
            (browse, "Browse for a project", "Open a Flint project or Fireworks PNG"),
            (create, "New canvas", "Create a canvas. An image on the clipboard supplies the starting size."),
            (close, "Close start page", "Return to the editor"),
        })
        {
            control.AccessibleName = name;
            tips.SetToolTip(control, tip);
        }
        page.Controls.AddRange(new Control[] { title, recent, hint, open, browse, create, close });
        page.AcceptButton = open.Enabled ? open : create;
        page.CancelButton = close;
        return page;
    }

    private bool TryOpenRecent(string path, IWin32Window owner)
    {
        if (!File.Exists(path))
        {
            if (!Program.Headless) MessageBox.Show(owner, "This project has moved or been deleted. Use Browse to find it.",
                "Could not open project", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        return (_projectWindow != null || ConfirmDiscard(owner)) && OpenInProjectTab(path);
    }

    private sealed record RecentProject(string Path)
    {
        public override string ToString() => System.IO.Path.GetFileName(Path);
    }
}
