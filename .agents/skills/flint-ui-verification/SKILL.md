---
name: flint-ui-verification
description: Use to verify Flint interface changes with builds, layout checks, DrawToBitmap images, regression tests and safe live interaction, reporting the limits of each check.
---

# Verify Flint UI work

Run from the repo root. Read `desktop/LayoutShot.cs`, `desktop/Program.cs`,
`desktop/BrushTest.cs`, `desktop/WorkspaceTest.cs` and `test/run_all.ps1`
for current hooks. Keep screenshots/logs outside tracked source unless needed.

1. Close the development build safely before building; do not kill unsaved
   work. Run `dotnet build desktop -v q --nologo` and check its exit code.
2. Use the newly built executable, normally
   `desktop/bin/Debug/net8.0-windows/Flint.exe`, not an older distributed copy.
3. Run `Flint.exe --shot <out.png> [sheet.png]`. It must exit zero and say
   **layout clean**. Read failures and inspect the PNG. LayoutShot checks
   clipping of absolute controls, overlaps and tiny docked containers; it
   deliberately skips some controls and cannot prove every layout works.
4. Run the feature suite and full regression command from `test/`:
   `powershell -ExecutionPolicy Bypass -File ./run_all.ps1`.
   Wait for completion; require `ALL SUITES PASSED` and successful exit.

Generate and actually inspect the appropriate DrawToBitmap pictures. In
PowerShell set process-local environment variables, restoring prior values
afterwards. Create the output folder first. With `$exe` set to the build:

```powershell
$env:FLINT_BRUSH_UI_SHOT = Join-Path $shotDir 'brush.png'
& $exe --brushtest
$env:FLINT_WS_SHOT = $shotDir
& $exe --workspacetest
```

The brush hook writes the options/preset sheet and `brush-settings.png`.
The workspace hook writes `ws-default.png` and `ws-custom.png` at its current
test checkpoints; this is not automatic coverage of all built-in layouts.
Repeat relevant checks under `FLINT_THEME=dark` and `light`. `FLINT_FRAMES=1`
with --shot exercises Frames view. Existing hooks do not photograph every
dialog; add a bounded hook when the changed UI needs one, within task scope.

For workspace changes, inspect Layers' actual list and buttons after docking,
tab switching, shrinking, folding and restoring; a visible header is not enough.
Exercise Default, Compact, Painting and Animation and relevant window sizes.

Drive a live window only through allowed computer-use tools and when safe for
Tyler's current work. Check real editing, keyboard focus, cancel, undo/redo,
save/reopen and the affected gesture. If blocked, finish permitted tests and
state exactly which live steps remain. Do not call DrawToBitmap a live test.

Report build, automated tests, inspected images and live actions separately,
with failures and omissions. Passing foundation tests does not finish a UI
feature. Documentation-only skill changes need skill validation/discovery,
not an app rebuild or a claim that the app's UI was tested.
