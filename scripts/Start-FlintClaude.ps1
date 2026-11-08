<#
.SYNOPSIS
  MODE A (Hybrid, recommended): normal, logged-in Claude Code in the Flint
  folder. Claude coordinates and hands routine work to free local workers via
  scripts\Invoke-LocalWorker.ps1 (see docs\AI_WORKFLOW.md).

.DESCRIPTION
  Makes sure no Ollama/provider variables leak into this Claude session (they
  are removed from this launcher's process only), starts Ollama if it is not
  running (so workers are ready), and opens Claude Code. Your Anthropic login
  and settings are used exactly as normal.
#>
# No param block: every argument (including ones like -c, "continue") goes to claude unchanged.
$ClaudeArgs = @($args)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FlintAI.Common.ps1')

# Remove provider overrides from THIS process only, so the session talks to
# Anthropic with your normal login. (User/machine settings are not touched.)
foreach ($k in $script:StripFromChild + @('ANTHROPIC_MODEL', 'ANTHROPIC_DEFAULT_OPUS_MODEL', 'ANTHROPIC_DEFAULT_SONNET_MODEL',
        'ANTHROPIC_DEFAULT_HAIKU_MODEL', 'ANTHROPIC_SMALL_FAST_MODEL', 'CLAUDE_CODE_SUBAGENT_MODEL', 'CLAUDE_CONFIG_DIR',
        'FLINT_AI_PROVIDER', 'FLINT_LOCAL_WORKER')) {
    Remove-Item "Env:$k" -ErrorAction SilentlyContinue
}

try { Start-OllamaIfNeeded; Write-Host "Ollama is running (local workers ready)." -ForegroundColor DarkGreen }
catch { Write-Host "Note: $($_.Exception.Message) Local workers will not be available until Ollama runs." -ForegroundColor Yellow }

$claude = Find-Claude
if (-not $claude) { throw 'Claude Code CLI not found. Install: npm install -g --allow-scripts=@anthropic-ai/claude-code @anthropic-ai/claude-code' }

Write-Host "Starting Claude Code (Anthropic, your normal login) in $script:FlintRoot" -ForegroundColor Cyan
Push-Location $script:FlintRoot
try { & $claude @ClaudeArgs } finally { Pop-Location }
