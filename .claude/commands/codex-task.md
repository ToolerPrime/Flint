---
description: Hand a task to the ChatGPT worker (Codex CLI, your ChatGPT plan, not Anthropic usage)
argument-hint: <what to change, which files, what "done" means>
---
Delegate this to the CHATGPT (Codex) worker:

$ARGUMENTS

Use it when a local worker failed twice, or the job needs more skill than the
local model has, before spending Claude subagent usage. Run (PowerShell):
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-CodexWorker.ps1 -Role <coder|debugger|reviewer|docs> -Task "<task>" -Acceptance "<done means>" -Files <files> [-Effort high] [-Build]`

Check the result yourself with `git diff` and the BUILD line, then report to
the user in plain English.
