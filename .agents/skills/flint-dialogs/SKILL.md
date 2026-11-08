---
name: flint-dialogs
description: Use when adding or changing a Flint dialog, especially live previews, OK/Cancel behaviour, accessible controls or number-and-slider pairs.
---

# Flint dialogs

Read `desktop/BrushSettingsDialog.cs` as the live-preview model,
`desktop/ColourPickerDialog.cs` for the existing colour picker, and
`desktop/Theme.cs` for `ThemedForm`. Paths are relative to the repo root.

Derive from `ThemedForm`. For a fixed settings dialog use `FixedDialog`,
`CenterParent`, no minimise/maximise buttons and `ShowInTaskbar=false`.
Call `ShowDialog(owner)` and dispose the dialog. Use the existing theme
controls and sufficient layout space at supported DPI/font sizes.

BrushSettingsDialog's concrete layout has 12px outer inset, 28px number rows,
26px toggle rows, and a 640px client width. Its number rows use a 118px label,
60px number box, 86px slider and unit label. Adapt to content; do not squeeze
a new section into fixed coordinates without increasing space or scrolling.

Create text buttons with `DialogResult.OK` / `DialogResult.Cancel`, then set
`AcceptButton = ok; CancelButton = cancel;`. The model uses 84px buttons with
8px between them. Enter accepts, Escape cancels, and closing with X cancels.

For live preview, retain the original state and edit a separate value:
`_s = _original = start` is safe for immutable BrushSettings records; the
model changes them with `s with { ... }`. Mutable state needs a real snapshot.
`Change` applies the candidate and redraws; `FormClosed` restores `_original`
whenever `DialogResult != DialogResult.OK`. Dispose replaced preview bitmaps.
Do not change shared undo objects in place or leave preview changes behind
after Cancel. Check the caller's commit/undo contract as well as the dialog.

Pair `NumericUpDown` with `MiniSlider`: identical min/max, units and value;
number changes update slider, slider changes update number. Use `_sync` to
avoid applying changes during refresh. The model's `Number` helper is a real
example; `Spacing` uses logarithmic adjustment. MiniSlider rounds pointer
values to whole numbers; adapt deliberately for fractional channels.

Give every new control a meaningful `AccessibleName` and a tooltip using
`Theme.MakeTip()` (including previews, labels and action buttons). Give a
slider a distinct name such as "Spacing slider". Preserve logical tab order;
MiniSlider is not a keyboard substitute for the number box. Existing dialogs
have gaps in names/tooltips: copy the behaviour, not those omissions.

Verify typing, slider movement, range limits, refresh without event loops,
Enter, Escape, X, preview rollback and accepted state. Use `flint-ui-style`
and `flint-ui-verification`; check document undo/redo when the dialog edits it.
