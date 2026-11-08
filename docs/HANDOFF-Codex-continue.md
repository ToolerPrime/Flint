# Handoff for Codex: carry on building Flint

Written 2026-11-08 by Claude (out of usage soon). Read this, then `AGENTS.md`
and `CLAUDE.md`. Talk to Tyler in plain, simple English, short sentences, no
jargon. He likes being shown results (pictures of the app) and told honestly
what was and wasn't checked.

## The project in one paragraph

Flint (`C:\Users\Techn\Flint`, GitHub `ToolerPrime/Flint`, branch `main`) is a
C# .NET 8 WinForms pixel editor and animator replacing Macromedia Fireworks 8.
Source in `desktop/`, tests in `test/` plus `--xxxtest` switches in `Flint.exe`.
Tyler's goal: Fireworks' feel and shortcuts, with the strengths that made
Photoshop win (brushes, colour picker, blend modes, layer styles). Many ideas
are translated from PhotoCraft (MIT, `github.com/storytold/photocraft`); every
translated file credits it at the top and in `CREDITS_LICENSES.md`.

## Ground rules (from CLAUDE.md, enforced by Tyler)

- Exact pixels: document pixels are written as bytes (BGRA), never through GDI+ drawing. GDI+ is fine for interface drawing only.
- Undo snapshots share objects: never change a shared object in place; follow the `Before()` / `PushStructure()` / `PaintRecorder` patterns already in the code.
- `.flint` format changes bump the version; older files must still load.
- Every feature gets a test in a `--` suite; `test/run_all.ps1` must end `ALL SUITES PASSED` (it also runs the window layout check, which must say `layout clean`).
- No Adobe/Macromedia code, icons or assets in the repo. Reading the installed Fireworks for facts (shortcut lists, behaviour) is fine.
- Don't claim you tested the interface unless you drove the real window. Pictures drawn by Flint itself (`DrawToBitmap` test hooks) are fine, but say that's what they are.
- Commits: author `Flint <flint@localhost>`. **Never push without Tyler's yes.** Before replacing `dist-desktop\Flint.exe`, copy the old one into `dist-backup\`.
- Never type passwords or log in for Tyler. Never use a paid API key; Codex runs on his ChatGPT login.

## Build, test, publish

```powershell
dotnet build desktop -v q --nologo                         # close Flint first: a running Flint locks the files
cd test; powershell -ExecutionPolicy Bypass -File .\run_all.ps1   # must end ALL SUITES PASSED
desktop\bin\Debug\net8.0-windows\Flint.exe --brushtest      # one suite; see Program.cs for all switches
dotnet publish desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist-desktop
```

Keep `EnableCompressionInSingleFile`: without it the exe is 163 MB instead of 72 MB.
Useful picture hooks: `FLINT_BRUSH_UI_SHOT=<file>` with `--brushtest`, `FLINT_WS_SHOT=<folder>`
with `--workspacetest`, `--brushtest <sheet.png>` (one stroke per preset).

## What was built in the last session (all committed and pushed)

| Area | Files | Test switch |
|---|---|---|
| Fireworks 8 shortcuts ("Macromedia Standard" set), tool-letter cycling | `MainForm.Shortcuts.cs`, menu `Add(...)` calls | `--productivitytest` |
| Opening a file given on launch (double-click a Fireworks PNG) | `Program.cs`, `MainForm.Fireworks.cs` | `--fwtest` |
| Long-session leak check | `SoakTest.cs` | `--soaktest` |
| Photoshop-style colour picker (from PhotoCraft) | `ColourPickerDialog.cs` | `--colourtest` |
| Brush engine: round tips, hardness, spacing, flow vs opacity, blend mode, Behind, shape/scatter/colour/transfer dynamics, noise, wet edges, airbrush build-up, smoothing; 12 presets | `BrushEngine.cs`, `MainForm.Brush.cs`, `BrushSettingsDialog.cs`, `MiniSlider.cs` | `--brushtest` |
| Blend modes: Photoshop's full list and order (saved by name) | `Document.cs` (`BlendMode`), `Blending.cs` | `--doctest`, `--brushtest` |
| Layer effects Stroke, Satin, Gradient overlay | `Effects.cs`, `MainForm.Effects.cs` | `--toolstest` |
| Fireworks-style workspace: pages in tab groups, several columns per side, fold a side, toolbars as movable strips, drag highlight, built-in layouts (Default, Compact, Painting, Animation), saved layouts | `WorkspacePanel.cs`, `DockManager.cs`, `MainForm.Workspace.cs`, `Theme.cs` (layout storage) | `--workspacetest` |
| [ and ] brush size (written by the local Ollama model, reviewed) | `MainForm.Shortcuts.cs` | `--brushtest` |

How the workspace fits together: a `PanelPage` is one piece of interface
(Layers, Timeline, "Main toolbar"...). Pages live in `WorkspacePanel` groups
(tabs when more than one). Groups sit in `DockColumn`s; `DockManager` keeps
columns per side (outermost first), floating windows, folded sides, drag
targets (`TargetAt` / `Drop`) and layouts (`Capture` / `Apply`, JSON in
`%APPDATA%\Flint\settings.json`; tests keep them in memory only). Drags follow
the mouse with a timer (`FollowMouse`), because the tab that started a drag can
be rebuilt mid-drag: that crashed once in live testing, so keep it that way.

## Known rough edges (small, good first jobs)

- Tools docked at the top or bottom still lays its buttons out in one tall column (`MainForm.Ui.cs`, `BuildToolbox`).
- In the Compact layout, 5 tabs don't fit 294 px; the last name is cut off (`WorkspacePanel.Rebuild`: narrower tabs or a "»" overflow).
- No circle showing brush size under the mouse.
- Colour picker has no Lab/CMYK boxes (formulas are in PhotoCraft's `crates/color/src/convert.rs`).
- Layer styles: no Pattern overlay (Flint has no pattern library yet).
- `--canvastest` once failed inside `run_all.ps1` with a missing `flint_canvas_recovery` temp folder, then passed on rerun and alone. Probably a timing clash in autosave recovery; worth a look.

## Bigger next steps (plan first, then build in small tested slices)

1. **Pen pressure** for the brush (Windows pointer messages, `WM_POINTER`); the brush settings already have the dynamics to hook it to. Phase 3 leftover in `docs/PhotoCraft-port-plan.md`.
2. **More brushes**: PhotoCraft's other presets (Dry/Wet media, textures, picture tips) need sampled tips and textures from `crates/paint/src/procedural.rs` (phase 4 of the plan).
3. **Several documents at once** (Fireworks lets each open in its own window or tab): Flint is single-document today; this touches a lot of `MainForm` state.
4. Anything Tyler asks for. He compares against Fireworks 8, which is installed at `C:\Program Files (x86)\Macromedia\Fireworks 8`: open it and try the feature yourself before copying it (the shortcut sets are in `English\Keyboard Shortcuts\*.xml`; his active set is "Macromedia Standard").

## Still not done from earlier

- `docs/HANDOFF-Codex-multi-brain.md`: setting Codex up with cheaper sub-models and per-role defaults for `scripts/Invoke-CodexWorker.ps1`. Not started. Do it when Tyler wants to save ChatGPT usage.

## Free helpers you can use

- Local Ollama workers (free, slower, need small clear jobs and review):
  `scripts\Invoke-LocalWorker.ps1 -Role coder|debugger|reviewer|docs -TaskFile .ai\task.md -Acceptance "..." -Files a,b -Build`.
  Write the task into a file (`-TaskFile`); multi-line `-Task` text gets split when passed through nested PowerShell.
  Tested 2026-11-08: the coder did a real feature about 90% right; always read `git diff`. Coder and debugger may run Flint's test suites.
  Running them from Codex needs your approval (they talk to Ollama outside your sandbox).
- Tyler's guide for working without Claude: `docs/WORKING-WITHOUT-CLAUDE.md`.

## How to report back to Tyler

Short and plain: what changed, what it means for him, what you checked (and
how), what you didn't, and what's committed / pushed / in his exe. Offer the
next step as a simple yes/no.
