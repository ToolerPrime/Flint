# Flint handoff for Claude

**2026-11-08 · `ToolerPrime/Flint`, `main`**

Flint is a Windows pixel editor and animator inspired by Fireworks 8. The active development checkout on Tyler's PC is `C:/Users/Techn/Flint`. Start from the current `main` on GitHub when working elsewhere. Read `AGENTS.md`, `CLAUDE.md`, and `ARCHITECTURE.md`. This handoff supersedes the single-document status in `docs/HANDOFF-Codex-continue.md`.

## Current editor

| Area | Where to look |
|---|---|
| One window with independent project tabs, undo, recovery, and floating panels for each project | `desktop/ProjectWindow.cs`, `MainForm.ProjectTab.cs`, `DockManager.cs`, `ProjectTabsTest.cs` |
| Startup page with recent projects and clipboard-sized new canvases | `MainForm.Start.cs`, `NewCanvasDialog.cs`, `Theme.cs` |
| Export preview with access to other formats and direct export for oversized preview jobs | `MainForm.Optimize.cs`, `OptimizeDialog.cs`, `ExportTest.cs` |
| Onion skin settings and ghosts over shared backgrounds | `MainForm.Onion.cs`, `Onion.cs`, `Document.cs`, `AnimTest.cs` |
| Canvas repaint during Inspector and Timeline divider drags | `CanvasPanel` and `DrawChecker` in `MainForm.cs` |
| Docking, saved layouts, toolbars, and built-in workspace presets | `DockManager.cs`, `WorkspacePanel.cs`, `MainForm.Workspace.cs` |
| Existing rulers, draggable guides, guide snapping and persistence | `MainForm.Rulers.cs`, `Guides.cs`, `GuidesTest.cs` |

The editor also has editable rectangles, ellipses and text, grouping, bitmap masks, Fireworks-style shortcuts, brush dynamics, and the PhotoCraft-derived colour picker. Current project format is 10. `CREDITS_LICENSES.md` records reused work and its notices.

The full regression suite passed on the resize preview source. Live checks used a disposable 2344 × 1255 project with both scrollbars visible. I resized the Inspector and Timeline and repeated the Inspector drag while scrolled to the right edge. The updated preview showed no trails in those checks. I could not reliably reproduce the reported trails in the older build, so keep the user's real project as the final confirmation. Dark and light layout captures were inspected separately.

## Next request from Tyler

Move **Properties below the canvas** for a Fireworks-like workspace, preserve the **Frames / Timeline** switch and existing panel behavior, and give the editor a more modern finish. The layout redesign is pending. Use `.agents/skills/flint-workspace/SKILL.md`, `.agents/skills/flint-ui-style/SKILL.md`, and `.agents/skills/flint-ui-verification/SKILL.md`.

- Build on the current dock manager. A compact bottom Properties row above the existing Timeline panel is a reasonable first layout. Check saved layouts and the reset/default path so users with a saved workspace can discover the new arrangement. Keep panel page identities stable.
- Reuse the existing guide gesture. **View > Rulers** or **Ctrl+Alt+R** shows the rulers. Drag from the top ruler for a horizontal guide, or from the left ruler for a vertical guide. Test placing, moving and removing guides while zoomed and scrolled. Make the gesture discoverable in the new workspace.
- Refine the theme within Flint's dark and light palettes. Keep the canvas prominent and check Properties at the minimum window size. Test actual controls, the Timeline/Frames switch, and both divider directions after moving Properties.

## Build and verify

Use Windows and the .NET 8 SDK. Python with Pillow and ffmpeg are used by the full suite. From the repository root:

```powershell
dotnet build desktop -v q --nologo
Push-Location test
python make_assets.py
powershell -NoProfile -ExecutionPolicy Bypass -File ./run_all.ps1
Pop-Location
```

Wait for `ALL SUITES PASSED`. The most relevant focused switches are `--projecttabstest`, `--workspacetest`, `--guidestest`, `--animtest`, and `--shot <file.png>`. For scripted runs, wait for the WinForms executable and check its exit code. Exercise the real window separately from the automated layout images.

To make a separate preview without locking a running build:

```powershell
dotnet publish desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist-desktop/preview
```

Prefer small Ollama assignments through `local-team/tools/local_workers.py` or the repository's local worker script. Give each worker named files and a concrete deliverable. Run one large model at a time. Review the diff and run tests before adopting a result. Keep undo snapshots immutable, use exact BGRA operations for stored pixels, and keep private sessions, binaries, and installed Fireworks assets out of Git.
