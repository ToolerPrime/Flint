# Flint handoff (for DeepSeek or any other AI)

Written 2026-11-09. Code state: `main` at commit `38af3a7`, tagged
`v0.13-preview.1`. This file is meant to stand on its own: paste it into a new
chat and you have what you need to continue. It replaces the older handoffs in
`docs/` (`HANDOFF-Codex-continue.md`, `HANDOFF-for-Claude.md`), which are kept
for history.

## 1. What Flint is

Flint is a native Windows image editor and frame animator that remakes the
workflows of **Macromedia Fireworks 8** (2005). The owner is **Tyler**
(GitHub `ToolerPrime`). Repo: <https://github.com/ToolerPrime/Flint>.
His working copy is `C:\Users\Techn\Flint`.

- C# on **.NET 8**, **WinForms**, x64, published as one self-contained exe.
- One NuGet package: **NAudio 2.2.1** (sound devices). **ffmpeg** is an
  optional outside tool (video export, some decoding).
- About 28,000 lines of C# in `desktop/` (115 files).
- Goal: Fireworks 8's feel and its default keyboard shortcuts ("Macromedia
  Standard"), plus the things that made Photoshop win: good brushes, colour
  picker, blend modes, layer styles.
- Many painting/colour features are **translated from PhotoCraft** (MIT,
  <https://github.com/storytold/photocraft>). Every translated file credits it
  at the top, and `CREDITS_LICENSES.md` lists it.
- No affiliation with Adobe or Macromedia. **Never put Adobe/Macromedia code,
  icons or assets in the repo.** Reading the installed Fireworks 8 for facts
  (behaviour, shortcut lists) is fine. Tyler has it at
  `C:\Program Files (x86)\Macromedia\Fireworks 8`; shortcut sets are in
  `English\Keyboard Shortcuts\*.xml`.

## 2. How to talk to Tyler

- Very plain, simple English. Short sentences. No jargon. Explain what a change
  means for him.
- He likes to see results (pictures of the app) and to be told honestly what
  was and was not checked.
- Report: what changed, what it means for him, how you checked it, what you
  did not check, and whether it is committed / pushed / in his exe. Offer the
  next step as a simple yes or no.
- **Never push to GitHub without his yes.** Never type passwords or log in for
  him. Never use a paid API key he has not set up.

## 3. Build, run, test

Needs Windows and the .NET 8 SDK. The full test suite also uses Python (with
Pillow), Node.js and ffmpeg.

```powershell
# Build (close Flint first: a running Flint locks the output files)
dotnet build desktop -v q --nologo

# Run
dotnet run --project desktop

# Full test suite: must end with "ALL SUITES PASSED"
cd test
python make_assets.py            # makes test images, first time
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_all.ps1

# One suite only
desktop\bin\Debug\net8.0-windows\Flint.exe --workspacetest

# Window layout check: must print "layout clean"
desktop\bin\Debug\net8.0-windows\Flint.exe --shot test\out-cs\x.png test\assets\walk_sheet.png

# Release exe (single file)
dotnet publish desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist-desktop
```

Notes:

- Keep `EnableCompressionInSingleFile=true`. Without it the exe is about
  155-163 MB instead of about 72 MB. (The current preview builds were made
  without it, which is why they are so big.)
- Built exes do **not** go in Git. They go on the GitHub release page. The
  `v0.13-preview.1` exe has not been uploaded yet.
- Before replacing `dist-desktop\Flint.exe`, copy the old one into
  `dist-backup\`. To make a side-by-side preview, publish to
  `dist-desktop/preview` instead.
- For scripted runs, wait for the WinForms exe to exit and check its exit code.

### Test switches

The tests are built into the exe as `--` switches, so the shipped exe can check
itself. Any `--` launch runs headless (no pop-ups). An unknown switch exits with
an error. `test/run_all.ps1` runs all of them.

| Switch | Covers |
|---|---|
| `--projecttabstest` | several projects open as tabs in one window |
| `--workspacetest` | panels, tab groups, docking, layouts, canvas background, pixel snapping |
| `--productivitytest` | Fireworks shortcuts, groups, fit/clip canvas, image size |
| `--editabletest` | editable rectangles, ellipses, text, picking objects |
| `--doctest` | document model, compositing, undo, `.flint` file |
| `--animtest` | playback timing, onion skin |
| `--audiotest` | audio mixer, decoding, storage, sync in video |
| `--memtest` | big projects, undo cost, snapshot safety |
| `--brushtest` | brush engine and presets (`--brushtest sheet.png` draws a sample sheet) |
| `--colourtest` | colour picker |
| `--soaktest` | 450 rounds of editing; memory and Windows handles must stay flat |
| `--fidelitytest` | every alpha level through every path, compared with Pillow |
| `--crashtest` | kills a real Flint process and recovers its work |
| `--formattest`, `--exporttest` | export formats and the export preview |
| `--spritetest`, `--importtest` | sprite sheets, atlas, import |
| `--drawtest`, `--canvastest`, `--toolstest`, `--edittest` | drawing, real mouse handlers, tools, transforms, filters |
| `--tracetest` | trace bitmap to vectors |
| `--fwtest` | Fireworks PNG import |
| `--guidestest` | rulers and guides |
| `--opstest`, `--selftest`, `--webmtest` | image byte ops, GIF encoder, WebM |
| `--shot <png> [image]` | draws the window and fails on clipped or overlapping controls |

Picture hooks for checking UI: `FLINT_WS_SHOT=<folder>` with
`--workspacetest`, `FLINT_BRUSH_UI_SHOT=<file>` with `--brushtest`. These are
pictures Flint draws of itself; say so if you show them. They are not live
testing.

## 4. Project rules (do not break these)

1. **Exact pixels.** Document pixels are BGRA bytes and are changed through
   `ImageOps` / byte code, never through GDI+ drawing. GDI+ changes about 92% of
   semi-transparent pixels even on a plain copy. GDI+ is fine for drawing the
   interface and for smooth resampling only. `--fidelitytest` guards this.
2. **Undo snapshots share objects. Never change a shared object in place.** The
   only in-place pixel change is a recorded paint stroke (`PaintRecorder` →
   `PixelEdit`). Everything else builds new objects (`Document.MapRasters`,
   `RasterObject.With`) and swaps them in, recorded with the existing
   `Before()` / `PushStructure()` patterns. Never dispose a bitmap a snapshot
   might hold. `--memtest` guards this.
3. **Transformed objects keep `Original` + `Matrix`.** Never paint on them.
   Re-render from the original (`RasterObject.Retransform`).
4. **`.flint` format changes bump the version, and older files must still
   load.** Newer files are refused with a clear message.
5. **Every feature gets a test** in a `--` suite, and `run_all.ps1` must end
   `ALL SUITES PASSED` (it also runs the layout check, which must say
   `layout clean`).
6. **Do not claim you tested the interface** unless you drove the real window
   with real mouse/keyboard input.
7. Before a big change: commit a checkpoint and back up the exe.
8. LF line endings (`.gitattributes` has `* text=auto eol=lf`).
9. Commit author used so far: `Flint <flint@localhost>` (some commits use
   Tyler's GitHub identity). Commit only when asked; push only with Tyler's yes.

## 5. Architecture and key files

Everything below the UI has no dependency on a window, which is why it all has
headless tests. Full design notes: `ARCHITECTURE.md`.

```
UI ─ MainForm (split into MainForm.*.cs partial files), panels, dialogs
 ├─ Document model ─ Document, Layer, Cel, RasterObject, FrameSpec, AnimTag
 │    ├─ Audio ─ AudioProject, AudioTrack, AudioClip, AudioSource
 │    ├─ Undo ─ PixelEdit, StructureEdit, PropertyEdit, AudioEdit, UndoStack (512 MB budget)
 │    └─ File ─ FlintFormat (.flint), Recovery (autosave)
 ├─ Pixels ─ ImageOps, Drawing, BrushEngine, Blending, Effects, Transform, Selection, Adjust, Trace, Quantizer, Onion
 ├─ Time ─ Playback, AudioMixer, AudioEngine
 ├─ Import ─ Importer, AudioIO, Sprites, FireworksPng
 └─ Export ─ Exporter → GifEncoder, ApngEncoder, WebpWriter, WebmExport (ffmpeg), StillExport, Sprites (atlas)
```

**Document model (`Document.cs`).** A document has Width, Height, Frames,
Tags, Audio, Library and Layers (bottom to top). A layer holds a sparse
`Dictionary<frame, Cel>`; a missing cel is an empty frame. A shared layer has
one cel (key 0) shown on every frame. A cel holds `RasterObject`s. A
`RasterObject` has Pixels, X, Y, and optionally `Original` (lossless source
image) or `Vector` (traced shapes), plus `Matrix`, resample mode, optional
editable source (`PrimitiveSpec` / `TextSource` from `EditableSources.cs`) and
group membership (`ObjectGroups.cs`). The Library is outside undo on purpose.

Where to look by area:

| Area | Files | Test |
|---|---|---|
| Main window wiring, canvas | `MainForm.cs` (`CanvasPanel`, `DrawChecker`), `MainForm.Ui.cs` | `--canvastest`, `--shot` |
| Project tabs (several projects in one window, each with its own undo, recovery and floating panels) | `ProjectWindow.cs`, `MainForm.ProjectTab.cs` | `--projecttabstest` |
| Start page, recent projects, new canvas | `MainForm.Start.cs`, `NewCanvasDialog.cs`, `Theme.cs` | `--projecttabstest` |
| Workspace: panel pages, tab groups, columns per side, folding, floating, toolbars, built-in and saved layouts | `DockManager.cs`, `WorkspacePanel.cs`, `MainForm.Workspace.cs`, `Theme.cs` (layout storage) | `--workspacetest` |
| Shortcuts (Macromedia Standard set, tool-letter cycling) | `MainForm.Shortcuts.cs` | `--productivitytest` |
| Editable shapes and text, object picking | `EditableSources.cs`, `PrimitivePaths.cs`, `TextRender.cs`, `MainForm.Objects.cs`, `ObjectPropertiesDialog.cs` | `--editabletest` |
| Grouping, canvas fit/clip, image size | `ObjectGroups.cs`, `CanvasEdits.cs`, `ResizePlan.cs`, `ImageResizeDialog.cs`, `MainForm.Grouping.cs`, `MainForm.Productivity.cs` | `--productivitytest` |
| Brush engine and settings | `BrushEngine.cs`, `MainForm.Brush.cs`, `BrushSettingsDialog.cs`, `MiniSlider.cs` | `--brushtest` |
| Blend modes (Photoshop's full list, saved by name) | `Blending.cs`, `Document.cs` (`BlendMode`) | `--doctest` |
| Live effects / layer styles | `Effects.cs`, `MainForm.Effects.cs` | `--toolstest` |
| Masks | `MainForm.Mask.cs` | `--toolstest` |
| Colour picker | `ColourPickerDialog.cs` | `--colourtest` |
| Transform, selection, filters | `Transform.cs`, `Selection.cs`, `Adjust.cs`, `MainForm.Edit.cs` | `--edittest`, `--toolstest` |
| Trace bitmap | `Trace.cs`, `MainForm.Trace.cs` | `--tracetest` |
| Onion skin | `Onion.cs`, `MainForm.Onion.cs` | `--animtest` |
| Rulers and guides | `MainForm.Rulers.cs`, `Guides.cs` | `--guidestest` |
| Timeline (owner-drawn; frame cells as wide as their delay) | `TimelinePanel.cs`, `TimelinePanel.Frames.cs` | `--canvastest` |
| Export and export preview | `Exporter.cs`, `OptimizeDialog.cs`, `MainForm.Optimize.cs`, encoders | `--formattest`, `--exporttest` |
| Fireworks PNG import | `FireworksPng.cs`, `MainForm.Fireworks.cs`, `docs/FIREWORKS_PNG_FORMAT.md` | `--fwtest` |
| Theme, icons | `Theme.cs`, `ThemeControls.cs`, `ToolIcons.cs`, `ToolIconsArt.cs` | `--shot` |
| Startup and test switches | `Program.cs` | |

Workspace detail: a `PanelPage` is one piece of interface (Layers, Timeline,
"Main toolbar"...). Pages live in `WorkspacePanel` groups (tabs when more than
one). Groups sit in `DockColumn`s; `DockManager` handles columns per side,
floating windows, folded sides, drop targets (`TargetAt` / `Drop`) and layouts
(`Capture` / `Apply`, saved as JSON in `%APPDATA%\Flint\settings.json`; tests
keep them in memory). Drags follow the mouse with a timer (`FollowMouse`)
because the tab that started a drag can be rebuilt mid-drag. That crashed once
in live testing, so keep it that way.

Other folders: `test/` (suite runner and Python checkers), `src/` + `build.mjs`
(old browser prototype, not used), `scripts/` and `local-team/` (AI worker
tooling, see section 9), `.agents/skills/` (UI design guides, see section 8).

## 6. The `.flint` file format

A zip: `project.json` (has a `"format"` number) plus `images/*.png`
(lossless), `library/*.png`, and `audio/*.wav` (32-bit float, 48 kHz stereo).
Saved next to the target and then moved into place, so a crash mid-save keeps
the old file. Code: `desktop/FlintFormat.cs`, `CurrentVersion = 10`.

| Version | Added |
|---|---|
| 1-3 | base document, frames, layers, audio |
| 4 | library images; per object `original` + `matrix` + `resample` |
| 5 | `vector` node for traced shapes |
| 6 | gradient fills, live effects |
| 7 | layer masks |
| 8 | guides |
| 9 | editable `primitive` and `textSource` on objects |
| 10 | object `groups` arrays (nested grouping) |

All older versions must keep loading. A newer version is refused with a
message. Unknown object kinds load with a warning. Workspace layouts are in
the settings file, not in projects. Autosave copies go to
`%LOCALAPPDATA%\Flint\Recovery`.

## 7. Current state (v0.13-preview.1)

Working and tested:

- Layers, frames, shared layers, tags, onion skin with settings, playback
  (loop, ping-pong, once, speed), audio tracks with waveform timeline,
  microphone recording (synthetic test only).
- Several projects open at once as tabs in one window, each with its own undo
  and recovery. Start page with recent projects.
- Fireworks-style movable workspace: Tools, Layers, Objects, Effects, History,
  Library, Properties, Timeline as panels; tab groups, several columns per
  side, fold a side, floating panels, movable toolbars, built-in layouts
  (Default, Compact, Painting, Animation) and saved layouts.
- Fireworks 8 default shortcuts with tool-letter cycling; `[` / `]` brush size.
- Editable rectangles, ellipses and text; multi-object selection; nested
  groups; clone, align, arrange, paste attributes, numeric transform.
- Lossless transforms (Nearest, RotSprite, Smooth), selections (rect, ellipse,
  lasso, wand), filters with live preview, bitmap layer masks, live effects
  and layer styles (shadow, glow, bevel, stroke, satin, gradient overlay...).
- Brush engine with dynamics and 12 presets, Photoshop-style colour picker,
  full Photoshop blend mode list.
- Rulers, guides, snapping, pixel snapping, canvas background colour.
- Trace bitmap to vectors (SVG export of traced shapes).
- Export: GIF, APNG, WebP (lossy/lossless), PNG-32/PNG-8, JPEG, BMP, TIFF,
  sheet, sequence, atlas + JSON, WAV, WebM/MP4 (ffmpeg). Export preview dialog.
- Fireworks PNG import, partly editable (see `FEATURE_PARITY.md`).
- Crash recovery (document, not its undo history). Long-session memory is flat.

The last live check (resize preview): the canvas repaints correctly while
dragging the Inspector and Timeline dividers. Tyler's own project is the final
confirmation for that fix.

## 8. Next steps

### Tyler's next request (start here)

Move **Properties below the canvas**, Fireworks style, keep the
**Frames / Timeline** switch and the existing panel behaviour, and give the
editor a more modern finish.

- Build on the current `DockManager`. A compact bottom Properties row above
  the Timeline is a good first layout.
- Make sure people with a saved layout can find the new arrangement (check
  saved layouts and Reset to default). Keep panel page identities stable so
  old saved layouts still apply.
- Make the ruler guide gesture easier to discover (View > Rulers or
  Ctrl+Alt+R; drag from the top ruler for a horizontal guide, from the left
  ruler for a vertical one). Test placing, moving and removing guides while
  zoomed and scrolled.
- Polish the theme inside Flint's dark and light palettes. Keep the canvas
  prominent. Check Properties at the minimum window size, and both divider
  directions.
- Read first: `.agents/skills/flint-workspace/SKILL.md`,
  `.agents/skills/flint-ui-style/SKILL.md`,
  `.agents/skills/flint-ui-verification/SKILL.md`. Other guides there:
  `flint-dialogs`, `flint-icons`, `flint-ux-writing`, `fireworks-parity`,
  `photocraft-port`.

### Small jobs (good warm-ups)

- **Docs are out of date in places.** `FEATURE_PARITY.md` still says
  "Multiple open documents: Missing" (project tabs now exist), "Brush (soft,
  textured): Missing" and lists only 5 blend modes. `ARCHITECTURE.md`'s
  "Project file" section still says version 5. `README.md` says panel keys
  (F2 etc.) are not available, but they are. `CHANGELOG.md` has no entry yet
  for project tabs, the start page, onion skin settings and the resize fix.
- Tools docked top or bottom still lay out in one tall column
  (`MainForm.Ui.cs`, `BuildToolbox`).
- Compact layout: 5 tabs do not fit in 294 px, the last name is cut off
  (`WorkspacePanel.Rebuild`: narrower tabs or a "»" overflow).
- No circle showing the brush size under the mouse.
- Colour picker has no Lab/CMYK boxes (formulas in PhotoCraft
  `crates/color/src/convert.rs`).
- No Pattern overlay layer style (no pattern library yet).
- `--canvastest` once failed in `run_all.ps1` with a missing
  `flint_canvas_recovery` temp folder, then passed on rerun. Probably an
  autosave/recovery timing clash.

### Bigger jobs (plan first, build in small tested slices)

1. Pen pressure for the brush (Windows `WM_POINTER` messages); the brush
   dynamics are ready to hook up. See `docs/PhotoCraft-port-plan.md`.
2. More brushes: PhotoCraft's other presets need sampled tips and textures
   (`crates/paint/src/procedural.rs`).
3. Fireworks features still missing: pen tool and node editing, polygon lasso,
   feathered selections, distribute, layer groups, symbols, tweening, slices
   and HTML export, curves, dodge/burn/smudge, a zoom tool, preferences.
   Full list: `FEATURE_PARITY.md` and `KNOWN_LIMITATIONS.md`.
4. Upload the release exe to the `v0.13-preview.1` GitHub release (build with
   compression on).

## 9. AI tooling already in the repo (optional)

The repo was built with Claude Code as the lead and cheaper helpers for
routine work. You do not need any of this to work on Flint, but it explains
some folders:

- `CLAUDE.md` / `AGENTS.md`: the rule book (same rules as section 4).
- `scripts/Invoke-LocalWorker.ps1`: runs a free local Ollama model on a small
  task (`-Role coder|debugger|reviewer|docs -TaskFile .ai\task.md -Acceptance
  "..." -Files a,b -Build`). Write multi-line tasks to a file. Always review
  `git diff` after it.
- `scripts/Invoke-CodexWorker.ps1`, `scripts/Start-Flint*.ps1`: ChatGPT/Codex
  and Claude launchers. `docs/AI_START_HERE.md`, `docs/AI_WORKFLOW.md` and
  `docs/WORKING-WITHOUT-CLAUDE.md` explain them.
- `.claude/agents/` and `.claude/commands/`: Claude Code helpers only.
- Logs go to `.ai/worker-runs/` (not in Git).

## 10. Reading order for a new session

1. This file.
2. `CLAUDE.md` (rules), `ARCHITECTURE.md` (design).
3. `FEATURE_PARITY.md`, `KNOWN_LIMITATIONS.md` (what is done and missing).
4. `CHANGELOG.md` (recent history).
5. The skill guides in `.agents/skills/` before any UI work.
