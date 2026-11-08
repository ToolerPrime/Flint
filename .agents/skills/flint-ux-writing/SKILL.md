---
name: flint-ux-writing
description: Use when writing or reviewing Flint labels, tooltips, status messages and menu names so Tyler sees short, plain English and familiar Fireworks terms.
---

# Flint words

Read the relevant strings in `desktop/MainForm.Ui.cs`,
`desktop/MainForm.Workspace.cs`, `desktop/WorkspacePanel.cs` and
`desktop/BrushSettingsDialog.cs`. Paths are relative to the repo root.

Say what the action does for Tyler. Use short English, sentence case and
Flint's spelling: colour, centre, neighbouring. Use a verb plus its object
for actions: "Add layer", "Move frame earlier", "Save workspace layout…".
Use Fireworks names where the same feature exists (Pointer, Paint bucket,
Rubber stamp, Layers, Workspace layouts); verify actual behaviour/shortcuts
with `fireworks-parity`. Do not imply parity for a different operation.

Keep labels compact but understandable. Tooltips explain the effect, unit,
scope or gesture; show only shortcuts that actually work. Existing examples:
- "Crop (C): drag the area to keep, Enter" in MainForm.Ui.cs.
- "Gap between dabs, as a percentage of the brush size" for Spacing.
- "Use this delay for every frame" explains the transport's "All" button.
- "Secondary paint colour (right mouse; does not fill the canvas)" avoids
  confusing the secondary swatch with the Canvas background setting.

Before/after suggestions grounded in current strings (not edits already made):

| Current code | Suggested wording | Reason |
| --- | --- | --- |
| "Xform" in BuildToolbox | "Transform" | Avoid an abbreviation |
| "Fg/Bg jitter" in BrushSettingsDialog | "Colour mix jitter" | Explain the setting; tooltip says it mixes towards the other swatch |
| "Sheet cols" in BuildInspectorPanels | "Sheet columns" | Make the unit clear |
| `workspace layout "{name}"` in ApplyLayout | `Using workspace layout "{name}"` | Say what happened |

Measure longer text and adjust layout; wording must not make tabs or labels
unreachable. Do not expand every short label when its tooltip is sufficient.
Use an ellipsis for an action that needs further choices; preserve menu `&`
mnemonics and avoid duplicates. Status messages should say what happened and,
on failure, what the user can do next. Never claim "saved" or "restored"
before success. Keep implementation names and raw internal jargon out of UI.
