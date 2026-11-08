---
name: flint-ui-style
description: Use when adding or restyling Flint WinForms controls, options bars or panels; match its palette, spacing and dark/light themes.
---

# Flint UI style

Paths below are relative to the Flint repo root. Read `desktop/Theme.cs`,
`desktop/ThemeControls.cs` and the relevant builder in `desktop/MainForm.Ui.cs`.
Keep the native WinForms interface: dark surfaces, restrained mint selection,
compact controls and a clear canvas. Do not redesign it as a web dashboard.

Use the tokens, not copied hex values. Current palette reference:

| Token | Dark | Light | Use |
| --- | --- | --- | --- |
| Panel | #1E1F22 | #F1F2F4 | Main surface |
| Panel2 | #26282C | #FFFFFF | Input/menu surfaces |
| Panel3 | #2E3136 | #E3E5E9 | Hover/raised surface |
| Border | #3A3D43 | #C5C9D0 | Hairlines and tracks |
| Text | #D7DADF | #1F2328 | Readable primary text |
| Muted | #8B9098 | #626A73 | Secondary labels/icons |
| Accent | #3DDC97 | #17B26A | Selection and emphasis |

Use `Theme.AccentSoft(...)` for tinted selection, `Theme.OnAccent` for text
on mint, and `Theme.Danger` for destructive actions. Custom painting reads
tokens at paint time. `ThemedForm` reapplies `Theme.Apply` on theme changes;
refresh cached colours/images in `OnThemeChanged`, calling the base method.
`Tag = "keep"` opts out of ordinary styling: use only for deliberate colours
(swatches, previews), and refresh theme-dependent values yourself.

Use the existing controls:
- `IconButton`: toolbar/tool actions, `Active` for selection, `IconResolver`
  for changing icons (Play/Stop); default 30x30 with a 20px icon.
- `MintCheckBox`: boolean options or icon toggles, instead of a stock checkbox.
- `FlatGroup`: panel sections instead of a stock GroupBox border; `HeaderH=16`,
  with `Collapsible`/`PersistKey` only for intentionally foldable sections.
- `MiniSlider` (`desktop/MiniSlider.cs`): compact numeric adjustment beside a
  number box; default 80x20. Keep a keyboard-editable NumericUpDown alongside.
- `ThemedForm` (`desktop/Theme.cs`): main windows and dialogs instead of Form.
  Stock Button, NumericUpDown and ComboBox remain valid through Theme.Apply;
  use stock text buttons for OK/Cancel, as BrushSettingsDialog does.

Match local spacing before inventing new values. `BuildOptionsBar` is 40px
high; its no-wrap flow has padding (8,0,4,1), uses `Flow(c,36)` to centre
controls, with 2px side margins. Labels are 20px high, checkboxes 22px.
`BuildToolbox` uses 26px buttons with 18px icons on a 28px pitch.
Inspector bodies use (4,0,4,0) padding; `Group` uses (0,4,0,4).
Workspace headers are 25px, grips 10px and column dividers 5px.

Keep light theme working. Inspect dark and light results with
`FLINT_THEME=dark` / `light` for separate test runs. Check narrow layouts and
DPI scaling; these sizes describe current code, not permission to clip content.
