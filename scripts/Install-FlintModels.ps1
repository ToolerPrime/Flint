<#
.SYNOPSIS
  Download (if needed) and create the local models Flint's AI workers use.

.DESCRIPTION
  Each worker model is an Ollama base model plus a 64K context window
  (Claude Code needs far more than Ollama's 4K default). Creating the
  "agent-*" copies costs no extra disk space.

  Sizes to download if missing: qwen3.6 35B ~23 GB (coder/debugger/reviewer/
  local mode), gemma-4 12B ~8 GB (fast alternative). -All adds gemma-4 26B,
  GLM-4.7 Flash and Qwen 3.8 27B (~52 GB more).
#>
[CmdletBinding()]
param([switch]$All, [switch]$NoDownload)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FlintAI.Common.ps1')
$o = Find-Ollama
if (-not $o) { throw 'Ollama is not installed: winget install Ollama.Ollama' }
Start-OllamaIfNeeded

$models = [ordered]@{
    'agent-qwen3.6-35b' = 'huihui_ai/Qwen3.6-abliterated:35b'
    'agent-gemma4-12b'  = 'huihui_ai/gemma-4-abliterated:12b'
}
if ($All) {
    $models['agent-gemma4-26b'] = 'huihui_ai/gemma-4-abliterated:26b'
    $models['agent-glm4.7-flash'] = 'huihui_ai/glm-4.7-flash-abliterated'
    $models['agent-qwen3.8-27b'] = 'huihui_ai/Qwen3.8-abliterated:27b'
}

$tmp = Join-Path $env:TEMP 'flint-modelfiles'
New-Item -ItemType Directory -Force $tmp | Out-Null
foreach ($name in $models.Keys) {
    $base = $models[$name]
    if (-not (Test-OllamaModel $base)) {
        if ($NoDownload) { Write-Warning "$base not installed (skipped, -NoDownload)"; continue }
        Write-Host "Downloading $base ..." -ForegroundColor Cyan
        & $o pull $base
        if ($LASTEXITCODE -ne 0) { throw "ollama pull $base failed" }
    }
    $mf = Join-Path $tmp "$name.Modelfile"
    Set-Content -Path $mf -Encoding ascii -Value @("FROM $base", 'PARAMETER num_ctx 65536')
    & $o create $name -f $mf | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "ollama create $name failed" }
    Write-Host "ready: $name  (from $base, 64K context)" -ForegroundColor Green
}
