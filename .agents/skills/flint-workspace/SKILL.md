---
name: flint-workspace
description: Use when adding or changing Flint dockable panels, tab groups, toolbars or workspace layouts, or fixing disappearing content such as Layers after docking.
---

# Flint workspace

Read `desktop/WorkspacePanel.cs`, `desktop/DockManager.cs`,
`desktop/MainForm.Workspace.cs`, `desktop/MainForm.Ui.cs` and
`desktop/WorkspaceTest.cs` from the repo root.

The ownership chain matters:
- `PanelPage` owns a named Content control, Min size and optional StripHeight.
  Its constructor sets Content.Dock to Fill. Names are saved layout identities.
- `WorkspacePanel` groups pages as tabs and attaches only the active content
  to its body. It has a 25px header or a 10px strip grip.
- `DockColumn` stacks groups, uses 5px dividers and weights, and has column
  extent/minimum logic. Top/bottom rows stack horizontally.
- `DockManager` registers pages, moves/groups/floats them, handles targets and
  applies/captures layouts. Preserve controls and state when reparenting.

Add ordinary inspector content through `BuildInspectorPanels`, then register
in `BuildDocking` (its inspector loop calls AddPage). Other pages can use
`_docks.AddPage(name, content, min)`. Existing Layers declares (220,120);
Properties (230,120); Timeline (500,120). These declarations are starting
points, not proof that controls fit in every layout.

For a toolbar pass a positive strip height: Main toolbar uses
`AddPage("Main toolbar", mainToolbar, new Size(300,32),32)`, Tool options
uses (300,40),40. Strip groups stay separate from ordinary tab groups. Do
not assume a tall tool palette becomes a horizontal toolbar by moving it.

Place the page deliberately in all `BuiltInLayouts`: Default, Compact,
Painting and Animation. Use G/MakeLayout helpers and explicit Hidden entries
where intended. Keep saved names stable; test older layouts missing the new
page and unknown page names. Theme.LastWorkspace stores the last layout;
headless tests use in-memory saved workspaces, not Tyler's settings.

`BuildWindowMenu` enumerates registered pages automatically (strips under
Toolbars). Add a collision-free shortcut to its keys map when specified or
appropriate, matching Fireworks where applicable. That map also registers
`_productivityShortcuts`; display text alone does not bind a key. Existing
Layers=F2, Properties=Ctrl+F3; F4 toggles all non-strip panels. Some existing
panels have no shortcut: do not invent a conflicting one to fill the map.

Check MinContent, DockColumn.MinExtent/Extent, DragBar and Relayout together.
Relayout distributes available space by weight and can still squeeze content.
Test the list/buttons inside Layers, not just its group's Visible flag.
Check each side, narrow columns, tab overflow, floating, close/reopen,
fold/unfold, growing Timeline and restoration without disposing content.
Keep DockManager's timer-based FollowMouse drag tracking: rebuilding a tab
can dispose the original event source during a drag.

Extend WorkspaceTest for new pages/behaviour, including its explicit current
10-page assertion, built-in placement, active tabs, content identity and
capture/apply round trips. Run `--workspacetest`, full tests and
`flint-ui-verification`; programmatic Drop tests do not prove live dragging.
