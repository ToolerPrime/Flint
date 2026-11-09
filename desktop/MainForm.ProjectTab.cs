namespace Flint;

public sealed partial class MainForm
{
    private readonly ProjectWindow? _projectWindow;
    private readonly string _recoverySession;
    private readonly string _untitledName;
    private Panel? _documentArea;
    private string? _sourcePath;
    private Form ProjectOwner => (Form?)_projectWindow ?? this;
    internal string? OpenedProjectPath => _path ?? _sourcePath;
    private string ProjectName => OpenedProjectPath is { } path ? Path.GetFileName(path) : _untitledName;
    internal string ProjectTabTitle => ProjectName + (_dirty ? " *" : "");
    internal bool IsPristineProject => !_dirty && OpenedProjectPath == null && _xf == null;

    internal bool IsProjectAt(string fullPath) =>
        string.Equals(OpenedProjectPath, fullPath, StringComparison.OrdinalIgnoreCase);

    private bool OpenInProjectTab(string path) => _projectWindow?.OpenProject(path) ?? OpenAny(path);

    internal void ClearProjectRecovery() => Recovery.Clear(_recoverySession);

    internal bool ConfirmProjectClose(IWin32Window owner)
    {
        StopPlay();
        FinishFloat();
        return ConfirmDiscard(owner);
    }

    internal void SetProjectActive(bool active)
    {
        if (!active) StopPlay();
        _docks.SetProjectActive(active);
    }

    // WinForms registers menu shortcuts against the top-level form. An embedded
    // editor needs to dispatch them through the menu for the active project.
    internal bool TryProjectMenuKey(Keys key)
    {
        if (_projectWindow == null || MainMenuStrip == null || (key & Keys.Modifiers) == 0) return false;
        return RunShortcut(MainMenuStrip.Items, key);
    }

    private static bool RunShortcut(ToolStripItemCollection items, Keys key)
    {
        foreach (var item in items.OfType<ToolStripMenuItem>())
        {
            if (!item.Enabled || !item.Available) continue;
            if (item.ShortcutKeys == key)
            {
                item.PerformClick();
                return true;
            }
            if (item.HasDropDownItems && RunShortcut(item.DropDownItems, key)) return true;
        }
        return false;
    }

    internal WorkspaceLayout CaptureProjectWorkspace() => _docks.Capture();

    internal void AttachProjectTabs(ToolStrip tabs)
    {
        _documentArea!.Controls.Add(tabs);
        _documentArea.Controls.SetChildIndex(tabs, 1);
    }

    internal bool TestStripAboveCanvas(ToolStrip tabs) =>
        tabs.Parent == _canvas.Parent && tabs.Bottom == _canvas.Top &&
        tabs.Left == _canvas.Left && tabs.Width == _canvas.Width;

    internal void FocusProject()
    {
        if (_canvas.CanFocus) _canvas.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _projectWindow != null && !IsDisposed && !Disposing)
        {
            _autosave.Dispose();
            _play.Dispose();
            _engine.Dispose();
            _docks?.DisposeProjectWindows();
            _undo.Clear();
            _doc.Dispose();
            _composite?.Dispose();
            _below?.Dispose();
            _above?.Dispose();
            foreach (var preview in _brushPreviews.Values) preview.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
