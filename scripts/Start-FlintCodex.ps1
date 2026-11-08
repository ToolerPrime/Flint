<#
.SYNOPSIS
  MODE C (ChatGPT): OpenAI Codex as the boss instead of Claude, for when your
  Claude usage runs out. Uses your ChatGPT login (ChatGPT plan's Codex
  allowance), never per-token OpenAI API billing.

.DESCRIPTION
  Codex reads AGENTS.md in the repo, which points it at the same rules
  (CLAUDE.md) and the same free local workers (scripts\Invoke-LocalWorker.ps1).
  Starts Ollama if needed so those workers are ready. Codex keeps its normal
  sandbox and asks before anything outside it. Extra arguments go to codex.
#>
# No param block: every argument (including ones like -c) goes to codex unchanged.
$CodexArgs = @($args)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FlintAI.Common.ps1')

$codex = Find-Codex
if (-not $codex) { throw 'Codex CLI not found. Install: npm install -g @openai/codex, then run: codex login' }

$ErrorActionPreference = 'Continue'
$login = (& $codex login status 2>&1 | ForEach-Object { "$_" } | Out-String).Trim()
$ErrorActionPreference = 'Stop'
if ($login -notmatch 'ChatGPT') { throw "Codex is not logged in with ChatGPT ('$login'). Run: codex login" }

# Keep API keys and Claude-session variables out of this process only, so Codex
# uses the ChatGPT login. (User/machine settings are not touched.)
foreach ($k in $script:StripFromChild + @('FLINT_AI_PROVIDER', 'FLINT_LOCAL_WORKER', 'FLINT_CODEX_WORKER')) {
    Remove-Item "Env:$k" -ErrorAction SilentlyContinue
}

try { Start-OllamaIfNeeded; Write-Host 'Ollama is running (local workers ready).' -ForegroundColor DarkGreen }
catch { Write-Host "Note: $($_.Exception.Message) Local workers will not be available until Ollama runs." -ForegroundColor Yellow }

Write-Host "Starting Codex (ChatGPT login) in $script:FlintRoot" -ForegroundColor Cyan
& $codex -C $script:FlintRoot @CodexArgs
