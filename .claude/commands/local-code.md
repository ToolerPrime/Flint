---
description: Hand a small, well-defined coding task to the free local coder worker (Ollama)
argument-hint: <what to change, which files, what "done" means>
---
Delegate this to the local CODER worker instead of doing it yourself:

$ARGUMENTS

Steps:
1. Turn the request into one small task: name the files to start from and an
   explicit acceptance check. Split it if it is more than one focused change.
2. Run (PowerShell):
   `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-LocalWorker.ps1 -Role coder -Task "<task>" -Acceptance "<done means>" -Files <files> -Build`
3. Check the result yourself with `git diff` and the BUILD line. If it failed,
   give the worker one focused retry with the error; after two failures,
   escalate (Tier 2 in CLAUDE.md).
4. Report to the user in plain English: what changed, build result, anything left.
