<#
.SYNOPSIS
  Show what the Flint AI setup will use: Claude Code, Ollama, models per role,
  GPU use, and whether any provider override is set in this terminal.
#>
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'FlintAI.Common.ps1')

$claude = Find-Claude
Write-Host "Claude Code : $(if ($claude) { (& $claude --version) + "  ($claude)" } else { 'NOT FOUND' })"
$cx = Find-Codex
if ($cx) {
    $cxLogin = (& $cx login status 2>&1 | ForEach-Object { "$_" } | Out-String).Trim()
    $cxVer = (& $cx --version 2>&1 | ForEach-Object { "$_" } | Out-String).Trim()
}
Write-Host "Codex       : $(if ($cx) { "$cxVer - $cxLogin" + $(if ($env:OPENAI_API_KEY) { ' (OPENAI_API_KEY is set here; Flint scripts strip it)' }) } else { 'NOT FOUND (optional: Mode C and ChatGPT worker)' })"
$o = Find-Ollama
Write-Host "Ollama      :$(if ($o) { (& $o --version) -join ' ' } else { 'NOT FOUND' })"
$up = Test-OllamaUp
Write-Host "Ollama API  : $(if ($up) { 'running at ' + (Get-OllamaBaseUrl) } else { 'NOT RUNNING (start the Ollama app)' })"

Write-Host "`nThis terminal's provider settings (should be empty for Mode A):"
foreach ($k in 'ANTHROPIC_BASE_URL', 'ANTHROPIC_AUTH_TOKEN', 'ANTHROPIC_MODEL', 'CLAUDE_CONFIG_DIR', 'FLINT_AI_PROVIDER') {
    $v = [Environment]::GetEnvironmentVariable($k)
    $u = [Environment]::GetEnvironmentVariable($k, 'User')
    if ($v -or $u) { Write-Host ("  {0} = '{1}' (user-level: '{2}')" -f $k, $(if ($k -eq 'ANTHROPIC_AUTH_TOKEN') { '***' } else { $v }), $u) -ForegroundColor Yellow }
}
if (-not ($env:ANTHROPIC_BASE_URL -or [Environment]::GetEnvironmentVariable('ANTHROPIC_BASE_URL', 'User'))) {
    Write-Host '  none - a normal "claude" here uses Anthropic with your login.' -ForegroundColor DarkGreen
}

$cfg = Get-FlintAIConfig
Write-Host "`nModels (scripts\ai-models.json):"
$rows = @()
foreach ($r in 'coder', 'debugger', 'reviewer', 'docs') { $rows += [pscustomobject]@{ role = $r; model = $cfg.roles.$r.model } }
$rows += [pscustomobject]@{ role = 'local mode'; model = $cfg.localMode.model }
foreach ($row in $rows) {
    $ok = if ($up) { Test-OllamaModel $row.model } else { $false }
    Write-Host ("  {0,-11} {1,-22} {2}" -f $row.role, $row.model, $(if ($ok) { 'installed' } else { 'MISSING' })) -ForegroundColor $(if ($ok) { 'Gray' } else { 'Red' })
}

if ($up -and $o) {
    Write-Host "`nLoaded right now (PROCESSOR shows GPU use):"
    & $o ps
}
