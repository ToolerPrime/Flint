# Local Ollama workers and REA notes

Run from this folder with Python 3 and Ollama listening on 127.0.0.1:11434:

```powershell
python tools/local_workers.py jobs/resize-plan-fix.json
```

Jobs name the local model to use. Adjust model names to your installed Ollama
models and any machine-specific file paths before running. Requests stay on
localhost; workers cannot call tools, edit source, or access connected accounts.
Generated results go to ignored runs/. The coordinator must review, build and
test each proposal before integration. needs_review is not acceptance.

```powershell
python -m unittest discover -s tests -v
dotnet run --project tests/WorkerContracts
```

The three proposal helpers are isolated foundations, not the full editor.
REA launchers expect the privately installed runtimes described in
research/rea-status.md. Downloaded tools, original Fireworks files, native
analysis caches, raw model runs and logs are intentionally absent.
Historical research notes retain the original workstation paths and test log
references for context; those logs and private runtime fixtures are not bundled.
