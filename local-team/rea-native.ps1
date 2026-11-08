$ErrorActionPreference = 'Stop'
$env:GHIDRA_INSTALL_DIR = Join-Path $PSScriptRoot '.tools\rea\ghidra_12.1.4_PUBLIC'
$env:JAVA_HOME = Join-Path $PSScriptRoot '.tools\rea\jdk-21.0.13+9'
$env:REA_ANALYSIS_PROVIDER = 'ghidra'
$env:GHIDRA_HEADLESS_MAXMEM = '2G'
$env:REA_GHIDRA_STARTUP_TIMEOUT_MS = '330000'
$env:REA_GHIDRA_DECOMPILE_TIMEOUT_SECONDS = '30'
$env:REA_GHIDRA_DISABLED_ANALYZERS = 'WindowsResourceReference'
$env:TEMP = Join-Path $PSScriptRoot 'research\runtime'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
& (Join-Path $PSScriptRoot 'rea.ps1') @args
exit $LASTEXITCODE
