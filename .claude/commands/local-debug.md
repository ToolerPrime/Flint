---
description: Hand a build error, failing test or stack trace to the free local debugger worker (Ollama)
argument-hint: <the error and how to reproduce it>
---
Delegate this to the local DEBUGGER worker:

$ARGUMENTS

1. Collect the exact error text and the command that shows it (e.g.
   `dotnet build desktop -v q --nologo`, or `desktop\bin\Debug\net8.0-windows\Flint.exe --toolstest`).
2. Run (PowerShell):
   `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-LocalWorker.ps1 -Role debugger -Task "<error text + how to reproduce + suspected files>" -Acceptance "<command> passes" -Build`
3. Verify with the same command yourself. Two local attempts at most, then escalate.
