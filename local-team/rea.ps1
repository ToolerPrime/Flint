$ErrorActionPreference = 'Stop'
$runtime = Join-Path $PSScriptRoot '.tools\rea\node-v24.22.1-win-x64\node.exe'
$entry = Join-Path $PSScriptRoot '.tools\rea\node_modules\rea-agents\scripts\rea.mjs'
& $runtime $entry @args
exit $LASTEXITCODE
