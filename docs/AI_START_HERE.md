# AI setup — start here

Flint is developed with Claude Code as the boss, and free local AI models (via
Ollama) doing routine work. Two ways to start:

| Mode | Command (PowerShell, in the Flint folder) | Costs |
|---|---|---|
| **A. Hybrid** (everyday) | `powershell -ExecutionPolicy Bypass -File scripts\Start-FlintClaude.ps1` | Claude's planning/checking uses your Anthropic plan; routine coding goes to free local workers |
| **B. Fully local** | `powershell -ExecutionPolicy Bypass -File scripts\Start-FlintLocal.ps1` | Free. No Anthropic usage at all |
| **C. ChatGPT** (Claude usage ran out) | `powershell -ExecutionPolicy Bypass -File scripts\Start-FlintCodex.ps1` | Your ChatGPT plan (Codex). No Anthropic usage. Still uses the free local workers |

Check what will be used: `powershell -ExecutionPolicy Bypass -File scripts\Get-FlintAIStatus.ps1`

How delegation works and the rules: [AI_WORKFLOW.md](AI_WORKFLOW.md).
Out of Claude usage? Read [WORKING-WITHOUT-CLAUDE.md](WORKING-WITHOUT-CLAUDE.md).
Project rules: [../CLAUDE.md](../CLAUDE.md). Design: [../ARCHITECTURE.md](../ARCHITECTURE.md).
Status: [../FEATURE_PARITY.md](../FEATURE_PARITY.md), [../KNOWN_LIMITATIONS.md](../KNOWN_LIMITATIONS.md).

## Project in brief

- C# .NET 8 WinForms, `desktop/` source, x64 single-file publish to
  `dist-desktop/Flint.exe`.
- Build: `dotnet build desktop -v q --nologo` (close Flint first; a running
  Flint locks the output).
- Tests: `cd test; powershell -ExecutionPolicy Bypass -File .\run_all.ps1`
  (must end `ALL SUITES PASSED`; each suite is also a `Flint.exe --xxxtest` switch).
- Layout check: `desktop\bin\Debug\net8.0-windows\Flint.exe --shot test\out-cs\x.png test\assets\walk_sheet.png` (must say `layout clean`).
- Rules that matter most: exact pixels, undo snapshots share objects, file
  format versioning, no Adobe assets (see CLAUDE.md).

## One-time setup on a new PC

1. Install: Git, .NET 8 SDK, Node.js (LTS), Ollama (`winget install Ollama.Ollama`),
   Claude Code (`npm install -g --allow-scripts=@anthropic-ai/claude-code @anthropic-ai/claude-code`),
   then run `claude` once and log in.
   Optional, for Mode C and the ChatGPT worker: `npm install -g @openai/codex`,
   then `codex login` and choose "Sign in with ChatGPT" (not an API key).
2. Models: `powershell -ExecutionPolicy Bypass -File scripts\Install-FlintModels.ps1`
   - downloads `huihui_ai/Qwen3.6-abliterated:35b` (~23 GB) and
     `huihui_ai/gemma-4-abliterated:12b` (~8 GB) if missing, and creates the
     64K-context worker models `agent-qwen3.6-35b` and `agent-gemma4-12b`.
   - `-All` adds the other tested models (~52 GB more); not needed.
3. Check: `scripts\Get-FlintAIStatus.ps1` shows every role "installed".
4. GPU check: run a worker once, then `ollama ps`. The PROCESSOR column should
   show GPU (on the RX 9070 XT the 35B model shows about `39%/61% CPU/GPU`,
   which is expected: it is bigger than 16 GB; the 12B model shows `100% GPU`).
   If it shows `100% CPU`, update the AMD driver and Ollama.

## Moving to another PC

Everything needed is in the repo (`scripts/`, `.claude/`, `docs/`, `CLAUDE.md`).
Copy or clone the repo (e.g. `git bundle create flint.bundle --all`, copy the
file, `git clone flint.bundle Flint`), then do the one-time setup above.
Not in the repo on purpose: your Claude login, `%USERPROFILE%\.claude-local`
(local-mode settings) and `.ai/worker-runs/` logs.
