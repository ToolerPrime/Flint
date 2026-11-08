<#
.SYNOPSIS
  Run one task on a ChatGPT worker: OpenAI Codex CLI using your ChatGPT login.

.DESCRIPTION
  Uses your ChatGPT plan's Codex allowance, not Anthropic usage and not
  per-token OpenAI API billing (the script refuses to run unless Codex is
  logged in with ChatGPT, and strips OPENAI_API_KEY from the child).
  Runs `codex exec` in Codex's own sandbox: read-only for the reviewer,
  workspace-write (this folder only) for the other roles. The task goes in on
  stdin. Output: a short report on stdout and a full log in .ai/worker-runs/.

.EXAMPLE
  scripts\Invoke-CodexWorker.ps1 -Role coder -Task "In desktop\Foo.cs add ..." -Acceptance "build passes" -Build
.EXAMPLE
  scripts\Invoke-CodexWorker.ps1 -Role reviewer -Task "Review the uncommitted diff" -Effort high
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('coder', 'debugger', 'reviewer', 'docs')][string]$Role,
    [string]$Task,
    [string]$TaskFile,
    [string]$Acceptance = '',
    [string[]]$Files = @(),
    [string]$WorkDir,
    [string]$Model,
    [ValidateSet('', 'low', 'medium', 'high', 'xhigh')][string]$Effort = '',
    [int]$TimeoutSec = 900,
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FlintAI.Common.ps1')

function Fail([string]$msg, [int]$code = 2) {
    Write-Output "CODEX WORKER: FAILED - $msg"
    exit $code
}

# --- guards ------------------------------------------------------------------
if ($env:FLINT_CODEX_WORKER -eq '1' -or $env:FLINT_LOCAL_WORKER -eq '1') {
    Fail 'refusing to start a worker from inside another worker (no recursive delegation).'
}

if ($TaskFile) {
    if (-not (Test-Path $TaskFile)) { Fail "task file not found: $TaskFile" }
    $Task = Get-Content $TaskFile -Raw
}
if ([string]::IsNullOrWhiteSpace($Task)) { Fail 'no task given (use -Task or -TaskFile).' }
if (-not $WorkDir) { $WorkDir = $script:FlintRoot }
$WorkDir = (Resolve-Path $WorkDir).Path

$codex = Find-Codex
if (-not $codex) { Fail 'Codex CLI not found. Install: npm install -g @openai/codex, then run: codex login' }
# codex prints the status on stderr; PowerShell 5.1 turns that into an error under 'Stop'.
$ErrorActionPreference = 'Continue'
$login = (& $codex login status 2>&1 | ForEach-Object { "$_" } | Out-String).Trim()
$ErrorActionPreference = 'Stop'
if ($login -notmatch 'ChatGPT') {
    Fail "Codex is not logged in with ChatGPT ('$login'). Run: codex login  (choose 'Sign in with ChatGPT'). API-key billing is not used by this script."
}

# --- prompt ------------------------------------------------------------------
$roleText = @{
    coder    = 'You are the CODER worker. Make the requested change with the smallest correct edit. Follow CLAUDE.md rules (exact pixels, no in-place mutation of undo-shared objects, LF line endings). If asked, run `dotnet build desktop -v q --nologo` and fix your own compile errors.'
    debugger = 'You are the DEBUGGER worker. Find the root cause of the error described. Make a narrow fix only if you are confident; otherwise explain the cause. Verify with the build or test command given.'
    reviewer = 'You are the REVIEWER worker. Do not edit files. Review the change described (use git diff). Report concrete problems as `path:line: severity: problem. fix.` and end with a one-line verdict.'
    docs     = 'You are the DOCS worker. Update only Markdown files. Short, plain English. Do not invent facts.'
}[$Role]
$fileText = if ($Files.Count) { "Relevant files (start here):`n" + (($Files | ForEach-Object { "- $_" }) -join "`n") } else { '' }
$acceptText = if ($Acceptance) { "Done means: $Acceptance" } else { '' }
$prompt = @"
$roleText

TASK:
$Task

$fileText
$acceptText

Rules: stay inside this folder; do not start other agents or worker scripts; do not commit, push or publish.
When finished, reply with exactly these sections:
SUMMARY: one to three sentences.
FILES CHANGED: list, or "none".
PROBLEMS: anything unfinished or uncertain, or "none".
"@

# --- run ---------------------------------------------------------------------
$runDir = Join-Path $script:FlintRoot '.ai\worker-runs'
New-Item -ItemType Directory -Force $runDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $runDir "$stamp-codex-$Role.md"
$lastMsg = Join-Path $runDir "$stamp-codex-$Role.last.txt"

$isGit = $false
try { $null = & git -C $WorkDir rev-parse --is-inside-work-tree 2>$null; $isGit = ($LASTEXITCODE -eq 0) } catch { }
$statusBefore = if ($isGit) { @(& git -C $WorkDir status --porcelain) } else { @() }

$sandbox = if ($Role -eq 'reviewer') { 'read-only' } else { 'workspace-write' }
# The 'elevated' Windows sandbox needs an admin setup helper that fails when
# started headless ("setup refresh had errors"); the unelevated one still limits
# writes to this folder. Override applies to this run only, not ~/.codex/config.toml.
$cliArgs = @('exec', '--json', '--color', 'never', '--ephemeral', '--sandbox', $sandbox, '-c', 'windows.sandbox="unelevated"',
             '-C', $WorkDir, '-o', $lastMsg)
if (-not $isGit) { $cliArgs += '--skip-git-repo-check' }
if ($Model) { $cliArgs += @('--model', $Model) }
if ($Effort) { $cliArgs += @('-c', "model_reasoning_effort=`"$Effort`"") }
$cliArgs += '-'

$childEnv = @{
    FLINT_CODEX_WORKER          = '1'
    FLINT_AI_PROVIDER           = 'chatgpt'
    MSBUILDDISABLENODEREUSE     = '1'
    UseSharedCompilation        = 'false'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_NOLOGO               = '1'
}
$psi = New-ChildStartInfo -File $codex -Arguments $cliArgs -WorkingDirectory $WorkDir -Environment $childEnv

$sw = [Diagnostics.Stopwatch]::StartNew()
$p = New-Object System.Diagnostics.Process
$p.StartInfo = $psi
[void]$p.Start()
$errTask = $p.StandardError.ReadToEndAsync()
$outTask = $p.StandardOutput.ReadToEndAsync()
$p.StandardInput.Write($prompt)
$p.StandardInput.Close()
$timedOut = -not $p.WaitForExit($TimeoutSec * 1000)
if ($timedOut) { Stop-ProcessTree $p.Id; $null = $p.WaitForExit(10000) }
$raw = if ($outTask.Wait(5000)) { $outTask.Result } else { '' }
$stderr = if ($errTask.Wait(5000)) { $errTask.Result } else { '' }
$exit = if ($timedOut) { -1 } else { $p.ExitCode }
$secs = [math]::Round($sw.Elapsed.TotalSeconds)

# Step trace and token use from the JSONL event stream.
$steps = New-Object System.Collections.Generic.List[string]
$usage = ''
foreach ($line in ($raw -split "`n")) {
    $line = $line.Trim()
    if (-not $line.StartsWith('{')) { continue }
    try { $ev = $line | ConvertFrom-Json } catch { continue }
    if ($ev.type -eq 'item.completed' -and $ev.item) {
        $it = $ev.item
        $what = switch ($it.type) {
            'command_execution' { "command: $($it.command) (exit $($it.exit_code))" }
            'file_change'       { 'edit: ' + (@($it.changes | ForEach-Object { "$($_.kind) $($_.path)" }) -join ', ') }
            'agent_message'     { $null }
            'reasoning'         { $null }
            default             { [string]$it.type }
        }
        if ($what) { if ($what.Length -gt 200) { $what = $what.Substring(0, 200) + '...' }; $steps.Add($what) }
    } elseif ($ev.type -eq 'turn.completed' -and $ev.usage) {
        $usage = "tokens in $($ev.usage.input_tokens) (cached $($ev.usage.cached_input_tokens)), out $($ev.usage.output_tokens)"
    } elseif ($ev.type -eq 'turn.failed' -or $ev.type -eq 'error') {
        $steps.Add('error: ' + $line.Substring(0, [math]::Min(300, $line.Length)))
    }
}
$reply = if (Test-Path $lastMsg) { Get-Content $lastMsg -Raw } else { '' }

$changed = @()
if ($isGit) { $changed = @(@(& git -C $WorkDir status --porcelain) | Where-Object { $statusBefore -notcontains $_ }) }

$buildResult = ''
if ($Build -and -not $timedOut -and (Test-Path (Join-Path $WorkDir 'desktop'))) {
    $b = & dotnet build (Join-Path $WorkDir 'desktop') -v q --nologo 2>&1
    $errs = @($b | Where-Object { $_ -match ' error ' } | Select-Object -First 8)
    $buildResult = if ($LASTEXITCODE -eq 0) { 'BUILD: passed' } else { "BUILD: FAILED`n" + ($errs -join "`n") }
}

$status = if ($timedOut) { "TIMED OUT after $TimeoutSec s (process killed)" } elseif ($exit -ne 0) { "ERROR (exit $exit)" } else { 'done' }
$stderrTail = if ($exit -ne 0 -and $stderr.Trim()) { "--- stderr (last lines) ---`n" + (($stderr.Trim() -split "`n" | Select-Object -Last 8) -join "`n") } else { '' }
$report = @"
CODEX WORKER [$Role] via ChatGPT ($sandbox sandbox): $status in $secs s
Changed files (git): $(if ($changed.Count) { $changed -join '; ' } else { 'none' })
$buildResult
--- worker reply ---
$(([string]$reply).Trim())
Steps: $($steps.Count)$(if ($usage) { "; $usage" })
$stderrTail
Full log: $log
"@

@"
# Codex worker run $stamp ($Role)
- sandbox: $sandbox, workdir: $WorkDir
- status: $status, $secs s, exit $exit
- $usage

## Prompt
$prompt

## Report
$report

## Steps
$($steps -join "`n")

## Raw event stream
$raw

## Full stderr
$stderr
"@ | Set-Content -Path $log -Encoding utf8

Write-Output $report
if ($timedOut) { exit 3 }
if ($exit -ne 0) { exit 4 }
if ($buildResult -like 'BUILD: FAILED*') { exit 5 }
exit 0
