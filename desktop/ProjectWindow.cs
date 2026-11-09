namespace Flint;

/// <summary>One window, with an independent editor and recovery session per project.</summary>
internal sealed class ProjectWindow : ThemedForm
{
    private readonly List<MainForm> _editors = new();
    private readonly Dictionary<MainForm, ToolStripButton> _buttons = new();
    private readonly ToolStrip _tabs = new()
    {
        Dock = DockStyle.Top, Height = 29, AutoSize = false, GripStyle = ToolStripGripStyle.Hidden,
        Padding = new Padding(4, 1, 4, 1), ShowItemToolTips = true,
        AccessibleName = "Open projects", AccessibleRole = AccessibleRole.PageTabList,
    };
    private readonly ToolStripButton _close = new("×")
    {
        Alignment = ToolStripItemAlignment.Right, Overflow = ToolStripItemOverflow.Never,
        AccessibleName = "Close current project", ToolTipText = "Close current project (Ctrl+W)",
        Padding = new Padding(6, 0, 6, 0),
    };
    private MainForm? _starter;
    private MainForm? _active;
    private int _nextNumber = 1;
    private bool _closing;

    internal ProjectWindow()
    {
        Text = "Flint";
        MinimumSize = new Size(1128, 730);
        Size = new Size(1380, 888);
        StartPosition = FormStartPosition.CenterScreen;
        _tabs.Items.Add(_close);
        _close.Click += (_, _) => CloseProject(ActiveEditor);
        _starter = AddEditor();
    }

    internal MainForm ActiveEditor => _active!;
    internal int ProjectCount => _editors.Count;
    internal ToolStrip ProjectStrip => _tabs;

    private MainForm AddEditor()
    {
        var editor = new MainForm(this, _nextNumber++)
        {
            TopLevel = false, FormBorderStyle = FormBorderStyle.None,
            MinimumSize = Size.Empty, Dock = DockStyle.Fill,
        };
        var button = new ToolStripButton { AccessibleRole = AccessibleRole.PageTab, Padding = new Padding(10, 0, 10, 0) };
        button.Click += (_, _) => SelectProject(editor);
        void UpdateCaption()
        {
            string caption = editor.ProjectTabTitle;
            button.Text = caption.Length > 30 ? caption[..24] + "…" + caption[^5..] : caption;
            button.AccessibleName = caption;
            button.ToolTipText = editor.OpenedProjectPath ?? caption;
            if (_active == editor) Text = editor.Text;
        }
        editor.TextChanged += (_, _) => UpdateCaption();
        UpdateCaption();

        _editors.Add(editor);
        _buttons.Add(editor, button);
        _tabs.Items.Insert(_tabs.Items.Count - 1, button);
        Controls.Add(editor);
        SelectProject(editor);
        return editor;
    }

    private void SelectProject(MainForm editor)
    {
        if (_active == editor)
        {
            editor.FocusProject();
            return;
        }

        var previous = _active;
        previous?.SetProjectActive(false);
        _active = editor;
        editor.AttachProjectTabs(_tabs);
        editor.Show();
        editor.BringToFront();
        previous?.Hide();
        foreach (var (project, button) in _buttons) button.Checked = project == editor;
        Text = editor.Text;
        editor.SetProjectActive(true);
        editor.FocusProject();
    }

    private MainForm UseStarterOrAdd() =>
        _starter is { IsPristineProject: true } starter ? starter : AddEditor();

    internal void CreateProject(int width, int height, bool transparent)
    {
        var editor = UseStarterOrAdd();
        editor.CreateCanvas(width, height, transparent);
        _starter = null;
        SelectProject(editor);
    }

    internal bool OpenProject(string path)
    {
        path = Path.GetFullPath(path);
        var existing = _editors.Find(editor => editor.IsProjectAt(path));
        if (existing != null)
        {
            SelectProject(existing);
            return true;
        }

        var previous = ActiveEditor;
        var target = UseStarterOrAdd();
        if (!target.OpenAny(path))
        {
            if (target != _starter) RemoveProject(target);
            SelectProject(previous);
            return false;
        }
        _starter = null;
        SelectProject(target);
        return true;
    }

    internal bool PathInUse(MainForm requesting, string path) =>
        _editors.Any(editor => editor != requesting && editor.IsProjectAt(path));

    internal void StartSession(string? path)
    {
        var initial = ActiveEditor;
        initial.OfferRecovery(() =>
        {
            var editor = UseStarterOrAdd();
            _starter = null;
            SelectProject(editor);
            return editor;
        });
        if (path != null) OpenProject(path);
        else if (_starter == initial) initial.ShowStartPage();
    }

    internal bool CloseProject(MainForm editor)
    {
        if (!editor.ConfirmProjectClose(this)) return false;
        RemoveProject(editor);
        if (_editors.Count == 0) _starter = AddEditor();
        return true;
    }

    private void RemoveProject(MainForm editor)
    {
        int index = _editors.IndexOf(editor);
        if (index < 0) return;
        if (_starter == editor) _starter = null;
        _editors.RemoveAt(index);
        var button = _buttons[editor];
        _buttons.Remove(editor);
        _tabs.Items.Remove(button);
        button.Dispose();

        // Move the strip before disposing the editor that currently contains it.
        if (_active == editor)
        {
            if (_editors.Count > 0) SelectProject(_editors[Math.Min(index, _editors.Count - 1)]);
            else
            {
                editor.SetProjectActive(false);
                _tabs.Parent = null;
                _active = null;
            }
        }
        editor.ClearProjectRecovery();
        editor.Dispose();
    }

    internal bool HandleProjectKey(Keys key)
    {
        switch (key)
        {
            case Keys.Control | Keys.W:
                CloseProject(ActiveEditor);
                return true;
            case Keys.Control | Keys.Tab:
            case Keys.Control | Keys.Shift | Keys.Tab:
                int delta = (key & Keys.Shift) != 0 ? -1 : 1;
                int current = _editors.IndexOf(ActiveEditor);
                SelectProject(_editors[(current + delta + _editors.Count) % _editors.Count]);
                return true;
            default:
                return false;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        HandleProjectKey(keyData) || base.ProcessCmdKey(ref msg, keyData);

    protected override void OnShown(EventArgs e)
    {
        ActiveEditor.SetProjectActive(true);
        ActiveEditor.FocusProject();
        base.OnShown(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || _closing) return;
        _closing = true;
        try
        {
            var original = ActiveEditor;
            foreach (var editor in _editors)
            {
                if (!editor.ConfirmProjectClose(this))
                {
                    SelectProject(editor);
                    e.Cancel = true;
                    return;
                }
            }
            if (!Program.Headless) Theme.LastWorkspace = original.CaptureProjectWorkspace();
            foreach (var editor in _editors) editor.ClearProjectRecovery();
        }
        finally { _closing = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tabs.Dispose();
        base.Dispose(disposing);
    }
}
