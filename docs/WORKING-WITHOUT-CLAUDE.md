# Keeping Flint going when Claude's usage runs out

Written 2026-11-08. Plain English, for Tyler.

## Which helper to use

| Helper | How to start (PowerShell, in the Flint folder) | Good for | Cost |
|---|---|---|---|
| **ChatGPT (Codex)** | `powershell -ExecutionPolicy Bypass -File scripts\Start-FlintCodex.ps1` | Most jobs. Closest to Claude in skill | Your ChatGPT plan |
| **Local model** (your PC, Ollama) | `powershell -ExecutionPolicy Bypass -File scripts\Start-FlintLocal.ps1` | Small, clearly described jobs | Free |

Both open a chat in the terminal that can read and edit Flint's files, build it
and run the tests. The local one is real Claude Code running on your own model
(`agent-qwen3.6-35b`), so it has the same tools I have. What it lacks is
brainpower: it gets lost on big or vague jobs.

## What the local model can really do (tested 2026-11-08)

Given a clear, written job ("add the [ and ] keys to change brush size"), it:

- found its way around the real code, edited 2 files, added tests, and the build passed (about 12 minutes);
- got about 90% right: it also slipped in one bug (at size 1, [ made the brush bigger) and deleted an unrelated comment.

So: **it can add to Flint, but always check its work** (see below). It is a
helper for small steps, not a replacement for planning a whole feature.

## How to ask it for something

1. **One small job at a time.** "Add X to file Y" works; "make the brushes like Photoshop" does not.
2. **Name the files.** Say which files to change, and which existing method or test to copy.
3. **Say what "done" means.** For example: "the build passes and `--brushtest` ends with `all brush checks passed`".
4. **Say what not to touch.** "Don't change anything else, don't reformat."

A good request, written into a file, can be handed to a worker directly:

```powershell
scripts\Invoke-LocalWorker.ps1 -Role coder -TaskFile .ai\my-task.md -Acceptance "build passes and --brushtest passes" -Files desktop\MainForm.Brush.cs -Build
```

(Roles: `coder` writes code, `debugger` fixes a build or test error, `reviewer`
looks over changes without editing, `docs` edits only `.md` files.)

## Checking its work (do this every time)

1. Before you start: `git status` should be clean (commit or stash first), so you can undo.
2. After: `git diff` shows exactly what it changed. Read it. Look for unrelated deletions.
3. Build: `dotnet build desktop -v q --nologo` (close Flint first).
4. Tests: `cd test; powershell -ExecutionPolicy Bypass -File .\run_all.ps1` must end `ALL SUITES PASSED`.
5. Happy? `git add -A` and `git commit -m "what it did"`. Not happy? `git restore .` throws its changes away.
6. Before replacing `dist-desktop\Flint.exe`, copy the old one into `dist-backup\`.

Ask the reviewer for a second look if you're unsure:
`scripts\Invoke-LocalWorker.ps1 -Role reviewer -Task "Review the uncommitted changes (git diff) for bugs"`.

## Next jobs, already cut small enough for a local model

Each line is one job. Give it the files named.

- Brush: show a circle the size of the brush under the mouse on the canvas (`MainForm.Brush.cs`, the canvas paint code in `MainForm.cs`).
- Brush: add 5 more presets from `docs/PhotoCraft-port-plan.md` using existing settings only, e.g. "Soft Pencil", "Ink Pen" (`BrushEngine.cs`, the `Presets` list; add each to the `--brushtest` sheet check).
- Workspace: when Tools is docked at the top or bottom, lay its buttons out in a row (`MainForm.Ui.cs`, `BuildToolbox`).
- Workspace: make tab labels narrower when a group has more than 3 tabs (`WorkspacePanel.cs`, `Rebuild`).
- Colour picker: add the Lab number boxes (L, a, b) using the formulas in the PhotoCraft notes (`ColourPickerDialog.cs`).
- Docs: keep `CHANGELOG.md` and the README shortcut table up to date after each change (`docs` role).

Bigger jobs (pen pressure, symbols, several documents at once) are better left
for Codex or for when Claude's usage is back: plan them first, then hand the
small pieces to the local model.
