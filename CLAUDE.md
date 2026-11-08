# Flint — instructions for Claude

Flint is a C# .NET 8 WinForms pixel editor and animator (a modern Fireworks 8
replacement). Source in `desktop/`, tests in `test/`, design in
`ARCHITECTURE.md`, honest status in `FEATURE_PARITY.md` and
`KNOWN_LIMITATIONS.md`.

## Delegation (automatic)

The main session coordinates: it decides what to do, hands work to the
cheapest worker that can do it reliably, checks the result, and talks to the
user. Delegate without being asked. Full guide: `docs/AI_WORKFLOW.md`.

**Tier 1 — free local workers (try first for routine work).** Run
`scripts/Invoke-LocalWorker.ps1 -Role <coder|debugger|reviewer|docs> -Task "..." -Acceptance "..." [-Files a,b] [-Build]`
(or the `/local-code`, `/local-debug`, `/local-review`, `/local-docs`
commands). It runs Claude Code on an Ollama model in a separate process: no
Anthropic usage. It returns a short report (status, changed files, build
result, worker summary); the full log is in `.ai/worker-runs/`.

| Work | Worker |
|---|---|
| Small, well-specified C# / WinForms change, cleanup, simple fix | local `coder` |
| Build error, failing test, stack trace | local `debugger` |
| Second look at a diff before commit (routine changes) | local `reviewer` |
| Changelog, notes, handoffs, doc rows | local `docs` |

**Tier 1.5 — ChatGPT worker (Tyler's ChatGPT plan, not Anthropic usage).**
When a local worker failed twice, or the job needs more skill than the local
model has, try this before Claude subagents:
`scripts/Invoke-CodexWorker.ps1 -Role <coder|debugger|reviewer|docs> -Task "..." -Acceptance "..." [-Files a,b] [-Effort high] [-Build]`
(or `/codex-task`). It runs Codex CLI on the ChatGPT login in Codex's sandbox
(reviewer read-only); it refuses to run on an OpenAI API key. Same report
format; log in `.ai/worker-runs/`. Also good as a second-opinion reviewer.

**Tier 2 — Claude subagents (paid, cheaper models)** when the ChatGPT worker
is unavailable or failed, or the task needs this conversation's judgement:

| Work | Agent | Model |
|---|---|---|
| Find where something is, list usages, summarise a file | `flint-scout` | Haiku |
| Build, run tests, check the exe, report pass/fail | `flint-checker` | Haiku |
| Mechanical edits already decided (renames, doc rows, typos, version numbers) | `flint-chores` | Haiku |
| Implement a feature, UI work, new tests, normal debugging | `flint-developer` | Sonnet |

**Tier 3 — premium (Opus agents, or the ChatGPT worker with `-Effort high`)** for architecture, file formats,
rendering, performance, memory and hard debugging:

| Work | Agent | Model |
|---|---|---|
| Plan a large or risky change, file-format change, hard debugging | `flint-architect` | Opus |
| Review a significant change before commit; question big decisions | `flint-reviewer` | Opus |

Rules:

- Use the cheapest worker that will get it right. Give local workers small
  tasks with explicit acceptance criteria and the files to start from; at most
  two local attempts, then escalate one tier.
- Check a worker's result from the diff (`git diff`) and the build/tests, not
  by rereading whole files.
- One writer per set of files at a time; never let two workers edit the same
  files at once.
- Never call a paid external API that the user has not set up and approved.
- Do it in the main session instead when it is a handful of tool calls, or
  needs context only this conversation has; a cold agent re-reads everything.
- Large feature flow: `flint-architect` plans → `flint-developer` builds →
  `flint-checker` runs everything → `flint-reviewer` reviews → main session
  verifies the claims itself, then reports.
- Never pass an agent's report on as fact. Check the diff and the test output.
- Agents do not commit, publish to `dist-desktop/`, or talk to the user; the
  main session does.

## Project rules

- Exact pixels: stored pixels never go through GDI+ drawing (`ImageOps` bytes).
- Undo snapshots share objects: never mutate in place outside `PaintRecorder`.
- Transformed objects keep `Original` + `Matrix`; never paint them.
- `.flint` format changes bump the version; older files must still load.
- Every feature gets a test in a `--` suite; `test/run_all.ps1` must pass.
- Before a major change: commit a checkpoint and back up
  `dist-desktop/Flint.exe` into `dist-backup/`.
- Do not report GUI testing unless the live window was actually driven.
- Do not distribute Adobe/Macromedia code, icons or assets.
- Talk to the user in very plain, simple English: short sentences, no jargon,
  explain what a thing means for them.
