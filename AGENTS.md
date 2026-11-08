# Instructions for Codex (and other AGENTS.md agents)

This project's full rule book is [CLAUDE.md](CLAUDE.md). Read it first and
follow it; where it says "Claude" or "the main session", it means you. Setup and
commands: [docs/AI_START_HERE.md](docs/AI_START_HERE.md). How work is handed
out: [docs/AI_WORKFLOW.md](docs/AI_WORKFLOW.md).

The owner (Tyler) wants plain, simple English and short answers.

## When you are the boss (Mode C, `scripts/Start-FlintCodex.ps1`)

- Hand routine work to the free local workers, same as Claude does:
  `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-LocalWorker.ps1 -Role <coder|debugger|reviewer|docs> -Task "..." -Acceptance "..." [-Files a,b] [-Build]`
  It talks to Ollama on 127.0.0.1 and writes to `%USERPROFILE%\.claude-local`,
  so it needs to run outside your sandbox: ask for approval when you run it.
- Do not use the `.claude/agents/*` subagents or `scripts/Invoke-CodexWorker.ps1`
  from here (those are Claude's paid helpers and your own worker script).
- Check every worker result with `git diff` and the build/tests.

## When you are a worker (started by `scripts/Invoke-CodexWorker.ps1`)

Do only the task you were given, in this folder. Do not start other agents or
worker scripts, and do not commit, push or publish.

## Always

- Build: `dotnet build desktop -v q --nologo` (close Flint first).
- Tests: `cd test; powershell -ExecutionPolicy Bypass -File .\run_all.ps1`
  (must end `ALL SUITES PASSED`).
- Exact pixels, undo snapshots share objects (never mutate them in place),
  version the file format, no Adobe/Macromedia assets, LF line endings.
- Commit only when asked. Never push without Tyler's permission.

## Design skills

Project skills live in `.agents/skills/`. Use `$skill-name` in a request, or
let Codex select them for matching interface work. Paths below are relative
to this repo; start Codex here (or in a subfolder) for project discovery.

- [flint-ui-style](.agents/skills/flint-ui-style/SKILL.md): palette, spacing and themed controls, including light theme.
- [flint-dialogs](.agents/skills/flint-dialogs/SKILL.md): dialog layout, accessible inputs, live preview and Cancel rollback.
- [flint-icons](.agents/skills/flint-icons/SKILL.md): original 24-unit code-drawn icons and key registration.
- [flint-ux-writing](.agents/skills/flint-ux-writing/SKILL.md): short labels, useful tooltips and familiar names.
- [fireworks-parity](.agents/skills/fireworks-parity/SKILL.md): observed Fireworks 8 behaviour and Macromedia Standard shortcuts.
- [photocraft-port](.agents/skills/photocraft-port/SKILL.md): source-based translations, attribution and the port plan.
- [flint-ui-verification](.agents/skills/flint-ui-verification/SKILL.md): builds, layout checks, pictures and honest live-test reporting.
- [flint-workspace](.agents/skills/flint-workspace/SKILL.md): panels, toolbars, layouts, shortcuts and content visibility.

For simple support jobs, prefer bounded free local workers with named source
files and reviewed artifacts; no automatic paid fallback. These skills guide
future UI work and do not themselves authorise unrelated app changes.
