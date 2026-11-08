# Hybrid AI workflow

## Who does what

| Tier | Who | Runs on | Use for |
|---|---|---|---|
| Coordinator | Claude Code session (Mode A) | Anthropic (paid) | understand requests, plan, split into small tasks, pick a worker, check diffs and tests, integrate, talk to Tyler |
| 1 | local workers `coder`, `debugger`, `reviewer`, `docs` | Ollama on this PC (free) | routine implementation, build/test fixes, quick reviews, docs |
| 1.5 | ChatGPT worker (`scripts/Invoke-CodexWorker.ps1`, `/codex-task`) | Codex CLI on your ChatGPT login | local worker failed twice, harder tasks, second-opinion reviews; saves Anthropic usage |
| 2 | `flint-scout`, `flint-checker`, `flint-chores` (Haiku), `flint-developer` (Sonnet) | Anthropic | when a local worker failed twice, or the job needs more judgement |
| 3 | `flint-architect`, `flint-reviewer` (Opus), or Codex | Anthropic / OpenAI | architecture, file formats, rendering, performance, memory, hard bugs |

The `.claude/agents/*` subagents always run on Anthropic models. Only
`scripts/Invoke-LocalWorker.ps1` runs on Ollama.

## How a local worker runs

`scripts/Invoke-LocalWorker.ps1 -Role coder -Task "..." -Acceptance "..." -Files a.cs,b.cs -Build`

1. Checks it is not itself inside a local worker (no workers starting workers).
2. Picks the role's model and tools from `scripts/ai-models.json`; checks
   Ollama is up and the model is installed (clear error if not).
3. Starts `claude.exe -p` as a **separate process** whose environment points
   only at Ollama (`ANTHROPIC_BASE_URL=http://127.0.0.1:11434`, a dummy token,
   all model names set to the local model, separate config folder
   `%USERPROFILE%\.claude-local`). Inherited Anthropic variables are removed
   from that child. Your terminal, user and machine settings are not changed.
4. The task goes in on stdin (never on a command line, so nothing in it is run
   as a command). Tools: only the role's built-in tools exist (`--tools`), and
   only the role's allowlist runs (`--allowedTools`); anything else is
   refused, because `-p` mode cannot ask. The reviewer has no edit tools; the
   docs worker may only edit `.md` files.
5. Hard time limit per role (kills the whole process tree), then reports:
   status, time, changed files (from `git status`), build result (`-Build`),
   the worker's SUMMARY / FILES CHANGED / PROBLEMS, and a full step-by-step
   log in `.ai/worker-runs/` (git-ignored).

Exit codes: 0 done, 2 setup problem (Ollama down, model missing, bad input),
3 timed out or stopped for looping, 4 worker error, 5 build failed.
A worker that is refused the same kind of command 6 times is stopped early.

## ChatGPT worker and Mode C

`scripts/Invoke-CodexWorker.ps1` takes the same `-Role -Task -Acceptance
-Files -Build` options (plus `-Effort low|medium|high|xhigh`, `-Model`) and
gives the same report and exit codes. It runs `codex exec` with:

- your ChatGPT login only: it refuses if `codex login status` is not ChatGPT,
  and strips `OPENAI_API_KEY` / `OPENAI_BASE_URL` / `CODEX_API_KEY` from the child;
- Codex's sandbox: `read-only` for the reviewer, `workspace-write` (this folder
  only) for the rest; the unelevated Windows sandbox for that run, because the
  elevated one fails when started headless (`setup refresh had errors`);
- `--ephemeral` (no saved session), the task on stdin, a hard time limit
  (default 900 s), and a guard so a worker cannot start more workers.

Mode C (`scripts/Start-FlintCodex.ps1`) opens Codex as the boss in the Flint
folder. Codex reads `AGENTS.md`, which sends it to `CLAUDE.md` and the free
local workers. Running a local worker from Codex needs your approval (it talks
to Ollama and writes outside the folder, which Codex's sandbox blocks).

## Rules for the coordinator (saving paid usage)

1. Routine implementation goes to a local worker first.
2. One small task per worker, with the files to start from and an explicit
   "done means" check. Split bigger work.
3. Don't reread the whole project; point workers at files.
4. Check results from `git diff` and the build/tests, not by rereading files.
5. Always run the build (`-Build`) and the relevant test suite.
6. At most two local attempts per task (the second with the exact error);
   then escalate one tier.
7. Never send routine coding to Opus.
8. Independent tasks in separate worker runs; never two writers on the same
   files at once.
9. For a trivial edit, just do it — coordinating a worker can cost more.
10. Never call a paid external API that is not already set up and approved.

## Mode B (fully local)

`scripts/Start-FlintLocal.ps1` runs Claude Code itself on the local model, in
this console, with the same isolation. Local workers cannot be started from
inside it (the recursion guard refuses). Expect it to be slower and less
capable than real Claude; keep tasks small. `-Prompt "..."` runs one
read-only question and exits (used for checks).

## Model choice (tested 2026-11-07, RX 9070 XT 16 GB, 9800X3D, 32 GB)

Same real task for each (add a C# method, use it, build):

| Model | Result |
|---|---|
| `agent-qwen3.6-35b` | PASS in 107 s. Mixture-of-experts, ~60% on GPU. **Used for every role and Mode B** |
| `agent-gemma4-26b` | correct code; kept retrying a refused command until stopped (151 s). Usable second choice |
| `agent-gemma4-12b` | FAIL: edited without reading the file; fast (100% GPU) but unreliable for code |
| `agent-glm4.7-flash` | correct code; same refused-command looping (stopped). Usable |

Using one model for all roles means only one is ever loaded (no VRAM
juggling). Context window: 64K (`num_ctx 65536`); Claude Code's own
instructions take a large part of that, so smaller windows fail.

## Troubleshooting

- `FAILED - Ollama ... not installed / did not start`: open the Ollama app,
  then `scripts\Get-FlintAIStatus.ps1`.
- `model ... is not installed`: `scripts\Install-FlintModels.ps1`.
- `TIMED OUT`: the task was too big or the model got stuck; read the Steps
  section of the log in `.ai/worker-runs/`, split the task, retry once.
- Worker hangs on `dotnet build`: the runner disables MSBuild node reuse and
  the shared compiler for this reason; if you run builds another way, keep
  `MSBUILDDISABLENODEREUSE=1`.
- Which provider is a session using? `scripts\Get-FlintAIStatus.ps1` in that
  terminal; local workers' logs name the model; local sessions say
  "[local model: …]".
