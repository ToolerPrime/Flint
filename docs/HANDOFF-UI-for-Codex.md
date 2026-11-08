# Handoff for Codex — improve Flint's interface

Written 2026-11-07 by Claude. Tyler wants Flint (his Fireworks 8 replacement)
to look and feel like a polished, modern editor. Talk to Tyler in plain,
simple English.

Current look: `test/out-cs/interface.png` (render it yourself with the command
under "How to see your changes").

---

## 1. The project in 30 seconds

- `C:\Users\Techn\Flint`, git repo. C# .NET 8 **WinForms**, x64, single-file publish.
- Read first: `CLAUDE.md` (rules + agent table), `ARCHITECTURE.md`,
  `FEATURE_PARITY.md`, `KNOWN_LIMITATIONS.md`.
- UI code: `desktop/MainForm.cs` (building the window, `BuildUi`) plus partial
  files `MainForm.Edit.cs`, `MainForm.Effects.cs`, `MainForm.Mask.cs`,
  `MainForm.Trace.cs`, `MainForm.Fireworks.cs`; custom controls
  `LayersPanel.cs`, `LibraryPanel.cs`, `TimelinePanel.cs`; dialogs
  `EditDialogs.cs`, `ImportDialog.cs`, `NewCanvasDialog.cs`, `ProgressDialog.cs`.
- Published app: `dist-desktop\Flint.exe` (back up to `dist-backup\` before publishing).

## 2. What's on screen today (left → right, top → bottom)

- **Menu bar**: File, Edit, Select, Image, View, Layer, Filters, Frame, Audio.
- **Toolbar**: text buttons New, Open, Save, Import, Export…, Undo, Redo.
- **Left panel (186 px)**: "Tools" = 20 text buttons in a 3-column grid
  (Move, Xform, Crop, Sel ▭, Sel ◯, Lasso, Wand, Pick, Pan, Pencil, Eraser,
  Fill, Grad, Clone, Text, Line, Rect, Oval, Pivot, Hitbox); "Options" (Size,
  filled, contiguous, Fill tol, Resample, Gradient — shown for every tool);
  "Colours" (two swatches + Swap).
- **Canvas** (centre): checkerboard, pixel grid from 8×.
- **Right panel (282 px)**: tabs Layers / History / Library / Effects on top
  (a SplitContainer), and below it one long scrolling column of loose groups:
  Transform (Flip H/V, Trim, Scale, Canvas size), Slice sprite sheet,
  Background removal, Export settings.
- **Bottom**: transport row (Play, Loop, speed, Delay, All, ←/→, Add, Dup,
  Del, Tag…, zoom −/+), onion-skin row (Onion, before/after, %, tint, Audio…,
  +Track, ● Rec, microphone picker, level meter), then the time-scaled
  timeline (ruler, tag lane, frame thumbnails, audio lanes with M/S/volume).
- **Status bar**: hint text left; size, layers, frames, duration, zoom,
  memory, undo, autosave right.

## 3. Problems to fix (Tyler's goal: modern, clean, quick to use)

1. **Text-label tool buttons** look like a 2005 dialog. Want icon buttons
   with tooltips (name + shortcut), grouped like Fireworks' tool panel
   (Select / Bitmap / Vector / Web / Colours / View sections).
2. **Options don't follow the tool.** Every option shows all the time. Want a
   context bar (like Photoshop's options bar, top of canvas) that shows only
   what the active tool uses.
3. **Right column is a junk drawer.** Transform, slicing, background removal
   and export settings are loose groups under the layers. Move them into
   menus/dialogs (Image menu, Export dialog) or a collapsible "Properties"
   inspector for the selected object (Fireworks had a Properties panel at
   the bottom showing the selected object's fill, stroke, effects, opacity,
   blend).
4. **Bottom area is crowded**: two rows of small buttons above the timeline.
   Group transport into one compact bar with icon buttons; put onion-skin and
   audio-record controls behind small toggle/dropdowns.
5. **No dark theme.** Editors are dark by default now. Want a dark theme
   (and keep a light one), consistent colours, accent orange `#FF7A45`
   (already used for the active tool/layer).
6. **Panels can't be moved, resized or hidden** (no docking, no saved
   workspace). Even simple: collapsible panels + remember sizes would help.
7. Small polish: consistent spacing/fonts, DPI scaling at 125/150%, toolbar
   icons, the Layers rows (eye/lock/mask boxes are hand-drawn and tiny),
   History/Library/Effects tabs look plain.

Pick the order that gives the biggest visible win first (suggested: dark
theme + icon tools + context options bar, then right-column cleanup, then
bottom bar, then docking/workspaces).

## 4. Hard rules (do not break)

- **Do not change behaviour.** This is a visual/layout job. Every existing
  feature must still be reachable (menus, shortcuts, test hooks).
- **All tests must pass**: `test\run_all.ps1` (read its live output;
  `test\run.log` can be stale). Many tests drive the real form through
  internal `Test*` hooks in the MainForm partial files — keep those hooks
  working (update them if you move controls, don't delete them).
- **Layout checker**: `--shot` renders the real window and fails on clipped,
  overlapping or collapsed controls. It must still print `layout clean`.
- **No Adobe/Macromedia icons, logos or artwork.** Draw original icons
  (e.g. simple vector icons drawn in code, or an open-licence icon set such
  as Lucide/Tabler/Material Symbols — record the licence in
  `CREDITS_LICENSES.md` and embed them as resources).
- **Exact pixels** in the canvas: never change how stored pixels are drawn
  or composited; the canvas display path is fine to restyle around.
- WinForms only (no switching UI framework). Put new UI code in partial
  files or new control files; don't grow `MainForm.cs` further.
- Keep files LF line endings. Don't commit secrets. Commit only when Tyler
  says so; back up `dist-desktop\Flint.exe` before publishing.

## 5. How to build, test and see your changes

```powershell
cd C:\Users\Techn\Flint
taskkill /IM Flint.exe /F 2>$null          # a running Flint locks the build
dotnet build desktop -v q --nologo
cd test; powershell -ExecutionPolicy Bypass -File .\run_all.ps1   # must end "ALL SUITES PASSED"
..\desktop\bin\Debug\net8.0-windows\Flint.exe --shot out-cs\interface.png assets\walk_sheet.png
```

The `--shot` PNG is the real window rendered offscreen — use it to check your
layout without needing the window in front. Publish:

```powershell
dotnet publish desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist-desktop
```

## 6. Done means

- Before/after `--shot` screenshots shown to Tyler.
- `run_all.ps1` passes, `--shot` says `layout clean`.
- `CHANGELOG.md` entry in plain English; `KNOWN_LIMITATIONS.md` updated if
  anything is unfinished; icon licence in `CREDITS_LICENSES.md`.
- Tyler has clicked through the real window (or you say clearly that you
  didn't).
