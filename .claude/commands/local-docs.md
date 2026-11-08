---
description: Hand a documentation update to the free local docs worker (Ollama, Markdown only)
argument-hint: <what to record and where>
---
Delegate this to the local DOCS worker:

$ARGUMENTS

Run (PowerShell):
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-LocalWorker.ps1 -Role docs -Task "<exact facts to record, which file and section>" -Files <doc files>`

Give it the facts; it must not invent any. Check the result with `git diff`.
