---
description: Ask the free local reviewer worker (Ollama, read-only) to review the current diff
argument-hint: [what the change is supposed to do]
---
Ask the local REVIEWER worker for a second look:

$ARGUMENTS

Run (PowerShell):
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-LocalWorker.ps1 -Role reviewer -Task "Review the uncommitted change (git diff). Intent: <what it should do>. Check correctness, CLAUDE.md rules, missing tests."`

Treat its findings as leads, not facts: confirm each one in the diff before
acting. For file-format, undo, rendering or other risky changes, use the Opus
`flint-reviewer` agent instead.
