# Handoff for Codex: set yourself up as a multi-brain team

Written 2026-11-08 by Claude, for Codex. Talk to Tyler in plain, simple English
and keep answers short.

## Which model runs this handoff

Tyler runs this on **`gpt-6.1-sol`, medium effort**, not Astra. The task is
config, docs, one script change and tests. Practise what this handoff sets up:
for searching and reading, use cheaper helpers if you can. If you're stuck on the
same step twice (most likely: the multi_agent config keys in step 2), stop and
tell Tyler which step it is. He can switch to Astra for that step only.

## Why

Tyler's Codex default is `gpt-6-astra`, the frontier model. It burns through
his ChatGPT plan's Codex usage fast, because it does *everything*, including
routine edits, searches and builds. Claude Code has the same problem with Opus,
and in this repo we fixed it with tiers: the expensive model plans and checks,
and cheap or free workers do the routine work. Set Codex up the same way.

**Goal:** when Tyler runs Codex on Flint, Astra (or whichever model he picks as
boss) only plans, splits work, reviews diffs and talks to him. Routine work goes
to cheaper ChatGPT models or to the free local Ollama models. Tyler should be
able to see which brain did what.

## What already exists (read these first)

All in `C:\Users\Techn\Flint` (git repo):

- `CLAUDE.md`: project rules and Claude's tier ladder (the model to copy).
- `AGENTS.md`: what you read today. It points you at `CLAUDE.md` and the local workers.
- `docs/AI_WORKFLOW.md` and `docs/AI_START_HERE.md`: how the hybrid setup works.
- `scripts/Invoke-LocalWorker.ps1`: a **free** worker. It runs Claude Code on a
  local Ollama model in a separate, isolated process. It takes
  `-Role coder|debugger|reviewer|docs -Task -Acceptance -Files -Build` and returns
  a short report; the log goes to `.ai/worker-runs/`. Exit codes: 0 done,
  2 setup problem, 3 timeout/looping, 4 error, 5 build failed.
- `scripts/Invoke-CodexWorker.ps1`: runs *you* (`codex exec`) as a worker on the
  ChatGPT login, sandboxed, with the same report format. It has `-Model` and
  `-Effort` but **no per-role default model yet**, so it uses Astra.
- `scripts/ai-models.json`: model per role for the local workers.
- `scripts/FlintAI.Common.ps1`: shared helpers (`Find-Codex`,
  `New-ChildStartInfo`, the list of env vars to strip, and so on).
- `scripts/Start-FlintCodex.ps1`: Mode C, you as the boss.

Facts measured on this PC (Ryzen 7 9800X3D, RX 9070 XT 16 GB, 32 GB RAM, Ollama 0.40.15):

- Local model benchmark (same C# task: add a method, use it, build):
  `agent-qwen3.6-35b` passed (~105 s). `agent-gemma4-26b` and
  `agent-glm4.7-flash` wrote correct code but looped on refused commands.
  `agent-gemma4-12b` **failed**: it edited without reading.
- `~/.codex/ollama-launch.config.toml` already defines an Ollama provider
  (`http://127.0.0.1:11434/v1/`, `wire_api = "responses"`). It points at
  `agent-gemma4-12b`, the model that failed the benchmark.
- Your model list (from `~/.codex/models_cache.json`): `gpt-6-astra` (frontier),
  `gpt-6.1-sol` (workhorse), `gpt-6.1-terra`, `gpt-reserve` ("fast and
  affordable agentic coding"), `gpt-6-luna` ("fast and affordable, easier
  tasks"), plus older 5.6 models.
- `codex features list` shows `multi_agent` as stable and enabled, and
  `multi_agent_v2` as stable but off.
- Headless `codex exec` with Tyler's `[windows] sandbox = "elevated"` fails on
  every command (`setup refresh had errors`). Per-run
  `-c windows.sandbox="unelevated"` works. `Invoke-CodexWorker.ps1` already does this.

## What to do

Check your own docs and `--help` for exact config keys before writing any
config. Don't guess key names; if something isn't supported in codex-cli
0.162.0, say so.

1. **Profiles for each brain.** Create Codex profiles (`-p <name>`, layered
   from `$CODEX_HOME/<name>.config.toml`):
   - `flint-boss`: Astra, for planning and review.
   - `flint-work`: `gpt-6.1-sol` or `gpt-reserve`, for normal coding.
   - `flint-cheap`: `gpt-6-luna`, low effort, for search, docs and small edits.
   - `flint-local`: the Ollama provider with `agent-qwen3.6-35b` (needs a
     big context; the Claude side uses `num_ctx 65536`).

   Prefer new profile files over editing `~/.codex/config.toml`. If you must
   edit it, back it up first and tell Tyler exactly what changed.
2. **Your own subagents (multi_agent).** Find out how 0.162.0 defines subagent
   roles and their models. Set up roles that mirror Claude's (scout/search,
   coder, checker/build+test, reviewer, docs) on the **cheaper** models, so the
   boss model delegates instead of doing the work. Prefer project-level config in
   the Flint repo, if Codex supports it, over global config. Decide whether
   `multi_agent_v2` is worth enabling, and explain why in one line.
3. **Per-role defaults for `Invoke-CodexWorker.ps1`.** Add a `codex` section to
   `scripts/ai-models.json` (role → model + effort, e.g. docs/reviewer → luna,
   coder/debugger → sol or reserve), and use it when `-Model`/`-Effort` are not
   given. Astra only when the caller passes it explicitly or `-Effort high`.
4. **Write the tier ladder into `AGENTS.md`** for when you are the boss:
   1. free local worker (`Invoke-LocalWorker.ps1`, or your `flint-local` profile);
   2. cheap ChatGPT model;
   3. workhorse model;
   4. Astra only for architecture, file formats, hard bugs and final review.

   Rules: at most two attempts per tier, then go up one; check every result with
   `git diff` plus build/tests; one writer per set of files.
5. **Show who did what.** Each worker run's report and log already names its
   model. Make sure your subagents' output does too.
6. **Docs.** Add a short "Codex multi-brain" section to `docs/AI_WORKFLOW.md`
   and the commands to `docs/AI_START_HERE.md`.

## Tests (run for real, report honestly)

- Same benchmark for each cheap brain (luna, reserve/sol, flint-local): in a temp
  git repo, add a C# `Clamp(int v, int lo, int hi)`, then build. Record pass/fail,
  time and tokens. Don't edit Flint's app code for this.
- Boss delegation: in Mode C (`scripts\Start-FlintCodex.ps1 -p flint-boss`),
  give one small real Flint task. Show that a cheaper brain did the edit.
- `Invoke-CodexWorker.ps1 -Role docs ...` with no `-Model` uses the cheap model
  (the log says which).
- Flint still builds (`dotnet build desktop -v q --nologo`) and the test suite
  still passes (`cd test; powershell -ExecutionPolicy Bypass -File .\run_all.ps1`
  must end `ALL SUITES PASSED`).
- If something can't run, mark it **unverified** and give the exact command.

## Rules (do not break)

- ChatGPT login only. Never set up or use an OpenAI API key or other paid API.
  `Invoke-CodexWorker.ps1` strips `OPENAI_API_KEY` and refuses non-ChatGPT login;
  keep that.
- Never use `--dangerously-bypass-approvals-and-sandbox`. Keep sandboxes and
  approvals.
- Don't change Flint's application code (`desktop/`) in this task.
- Don't change Claude's setup (`.claude/`, Claude's sections of `CLAUDE.md`)
  except to add cross-links.
- No logins, credentials or machine-specific paths in git. Per-user config
  stays in `~/.codex`.
- Commit when done (author `Flint <flint@localhost>`), with a clear message.
  **Never push.**
- No big downloads (over 5 GB) without asking Tyler.

## Report back to Tyler (plain English)

1. Which brains exist now, and what each is for.
2. What changed in `~/.codex` (with backup paths) and what changed in the repo.
3. Benchmark table.
4. Which tests passed, and which are unverified (with commands).
5. The one command he runs to start Codex on Flint the cheap way.
6. Roughly how much Astra usage this should save, and why. A rough estimate is
   fine; say it's an estimate.
