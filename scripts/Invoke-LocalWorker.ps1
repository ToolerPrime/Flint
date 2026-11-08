<#
.SYNOPSIS
  Run one task on a FREE local AI worker: Claude Code powered by an Ollama model.

.DESCRIPTION
  The main (paid) Claude session calls this to hand off routine work. The
  worker is a separate `claude -p` process whose environment points only at
  Ollama (see FlintAI.Common.ps1), with a role-specific tool allowlist. Nothing
  in the caller's session or the user's environment is changed.

  Output: a short report on stdout (status, changed files, worker summary,
  build result) and a full log in .ai/worker-runs/ (git-ignored).

.EXAMPLE
  scripts\Invoke-LocalWorker.ps1 -Role coder -Task "In desktop\Foo.cs add ..." -Acceptance "dotnet build passes"
.EXAMPLE
  scripts\Invoke-LocalWorker.ps1 -Role reviewer -TaskFile .ai\task.md
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
    [int]$TimeoutSec = 0,
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FlintAI.Common.ps1')

function Fail([string]$msg, [int]$code = 2) {
    Write-Output "LOCAL WORKER: FAILED - $msg"
    exit $code
}

# --- guard against workers starting workers ---------------------------------
if ($env:FLINT_AI_PROVIDER -eq 'ollama' -or $env:FLINT_LOCAL_WORKER -eq '1') {
    Fail 'refusing to start a worker from inside a local worker (no recursive delegation).'
}

# --- inputs ------------------------------------------------------------------
if ($TaskFile) {
    if (-not (Test-Path $TaskFile)) { Fail "task file not found: $TaskFile" }
    $Task = Get-Content $TaskFile -Raw
}
if ([string]::IsNullOrWhiteSpace($Task)) { Fail 'no task given (use -Task or -TaskFile).' }
if (-not $WorkDir) { $WorkDir = $script:FlintRoot }
$WorkDir = (Resolve-Path $WorkDir).Path

$cfg = Get-FlintAIConfig
$roleCfg = $cfg.roles.$Role
if (-not $Model) { $Model = $roleCfg.model }
if ($TimeoutSec -le 0) { $TimeoutSec = [int]$roleCfg.timeoutSec }
$allowed = @($roleCfg.allowedTools)

# --- preflight ------------------------------------------------------------------
$claude = Find-Claude
if (-not $claude) { Fail 'Claude Code CLI not found. Install: npm install -g --allow-scripts=@anthropic-ai/claude-code @anthropic-ai/claude-code' }
try { Start-OllamaIfNeeded } catch { Fail $_.Exception.Message }
if (-not (Test-OllamaModel $Model)) {
    Fail "Ollama model '$Model' is not installed. See docs\AI_START_HERE.md (models section) or run: ollama list"
}

# --- prompt ------------------------------------------------------------------------
$roleText = @{
    coder    = 'You are the CODER worker. Make the requested change with the smallest correct edit. Read only the files you need. Follow CLAUDE.md rules (exact pixels, no in-place mutation of undo-shared objects, LF line endings). If asked, run `dotnet build desktop -v q --nologo` and fix your own compile errors (at most two attempts).'
    debugger = 'You are the DEBUGGER worker. Find the root cause of the error described, using the error text and the minimum of reading. Make a narrow fix only if you are confident; otherwise explain the cause. Verify with the build or test command given.'
    reviewer = 'You are the REVIEWER worker. You are read-only: do not edit files. Review the change described (use git diff). Report concrete problems as `path:line: severity: problem. fix.` and end with a one-line verdict.'
    docs     = 'You are the DOCS worker. Update only Markdown files. Keep writing short, plain English. Do not invent facts: only record what the task tells you or what is in the files.'
}[$Role]

$fileText = if ($Files.Count) { "Relevant files (start here, read others only if needed):`n" + (($Files | ForEach-Object { "- $_" }) -join "`n") } else { '' }
$acceptText = if ($Acceptance) { "Done means: $Acceptance" } else { '' }

$prompt = @"
$roleText

TASK:
$Task

$fileText
$acceptText

Rules: stay inside this folder; do not start other agents; do not commit, push or publish.
If a tool or command is refused ("requires approval"), do NOT retry it or variations of it: you are not allowed to run it. Note it under PROBLEMS and finish.
When finished, reply with exactly these sections:
SUMMARY: one to three sentences.
FILES CHANGED: list, or "none".
PROBLEMS: anything unfinished or uncertain, or "none".
"@

# --- run ---------------------------------------------------------------------------
$runDir = Join-Path $script:FlintRoot '.ai\worker-runs'
New-Item -ItemType Directory -Force $runDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $runDir "$stamp-$Role.md"

$isGit = $false
try { $null = & git -C $WorkDir rev-parse --is-inside-work-tree 2>$null; $isGit = ($LASTEXITCODE -eq 0) } catch { }
$statusBefore = if ($isGit) { @(& git -C $WorkDir status --porcelain) } else { @() }

$childEnv = Get-LocalClaudeEnv -Model $Model
$childEnv['FLINT_LOCAL_WORKER'] = '1'
# dotnet build leaves MSBuild/compiler server processes running that keep the
# output pipe open, so the worker's shell call never returns. Turn them off.
$childEnv['MSBUILDDISABLENODEREUSE'] = '1'
$childEnv['UseSharedCompilation'] = 'false'
$childEnv['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
$childEnv['DOTNET_NOLOGO'] = '1'
# Non-interactive (-p) with a role allowlist: any tool not listed is denied.
# stream-json records every step, so a stalled or failed run can be diagnosed.
# --tools limits which built-in tools exist at all (smaller prompt, fewer ways
# to go wrong); --allowedTools says which of those run without asking.
$tools = (@($roleCfg.tools) -join ',')
$cliArgs = @('-p', '--model', $Model, '--output-format', 'stream-json', '--verbose',
             '--tools', $tools, '--disable-slash-commands', '--strict-mcp-config', '--no-session-persistence',
             '--permission-mode', 'default', '--allowedTools') + $allowed
$psi = New-ChildStartInfo -File $claude -Arguments $cliArgs -WorkingDirectory $WorkDir -Environment $childEnv

$sw = [Diagnostics.Stopwatch]::StartNew()
$p = New-Object System.Diagnostics.Process
$p.StartInfo = $psi
[void]$p.Start()
$errTask = $p.StandardError.ReadToEndAsync()
$p.StandardInput.Write($prompt)
$p.StandardInput.Close()

# Read the event stream live so a looping worker can be stopped early.
$sbRaw = New-Object System.Text.StringBuilder
$timedOut = $false; $stuck = $false; $refused = 0
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ($true) {
    $lineTask = $p.StandardOutput.ReadLineAsync()
    while (-not $lineTask.Wait(500)) {
        if ((Get-Date) -gt $deadline) { $timedOut = $true; break }
    }
    if ($timedOut) { break }
    $line = $lineTask.Result
    if ($null -eq $line) { break }
    [void]$sbRaw.AppendLine($line)
    if ($line -match 'requires approval|permission to use .* has been denied') {
        $refused++
        if ($refused -ge 6) { $stuck = $true; break }
    }
}
if ($timedOut -or $stuck) { Stop-ProcessTree $p.Id }
$null = $p.WaitForExit(10000)
$raw = $sbRaw.ToString()
$stderr = if ($errTask.Wait(5000)) { $errTask.Result } else { '' }

# Pull the final answer and a step trace out of the JSON event stream.
$stdout = ''
$steps = New-Object System.Collections.Generic.List[string]
$turns = 0
foreach ($line in ($raw -split "`n")) {
    $line = $line.Trim()
    if (-not $line.StartsWith('{')) { continue }
    try { $ev = $line | ConvertFrom-Json } catch { continue }
    if ($ev.type -eq 'assistant' -and $ev.message.content) {
        $turns++
        foreach ($c in $ev.message.content) {
            if ($c.type -eq 'tool_use') {
                $what = ($c.input | ConvertTo-Json -Compress -Depth 4)
                if ($what.Length -gt 160) { $what = $what.Substring(0, 160) + '...' }
                $steps.Add("tool $($c.name) $what")
            } elseif ($c.type -eq 'text' -and $c.text.Trim()) { $stdout = $c.text }
        }
    } elseif ($ev.type -eq 'user' -and $ev.message.content) {
        foreach ($c in $ev.message.content) {
            if ($c.type -eq 'tool_result' -and $c.is_error) {
                $t = ($c.content | Out-String).Trim(); if ($t.Length -gt 200) { $t = $t.Substring(0, 200) + '...' }
                $steps.Add("  -> tool error: $t")
            }
        }
    } elseif ($ev.type -eq 'result') {
        if ($ev.result) { $stdout = [string]$ev.result }
        if ($ev.is_error) { $steps.Add("result error: $($ev.subtype)") }
    }
}
$exit = if ($timedOut -or $stuck) { -1 } else { $p.ExitCode }
$secs = [math]::Round($sw.Elapsed.TotalSeconds)

# --- what changed ---------------------------------------------------------------
$changed = @()
if ($isGit) {
    $statusAfter = @(& git -C $WorkDir status --porcelain)
    $changed = @($statusAfter | Where-Object { $statusBefore -notcontains $_ })
}

$buildResult = ''
if ($Build -and -not $timedOut -and (Test-Path (Join-Path $WorkDir 'desktop'))) {
    $b = & dotnet build (Join-Path $WorkDir 'desktop') -v q --nologo 2>&1
    $errs = @($b | Where-Object { $_ -match ' error ' } | Select-Object -First 8)
    $buildResult = if ($LASTEXITCODE -eq 0) { 'BUILD: passed' } else { "BUILD: FAILED`n" + ($errs -join "`n") }
}

$status = if ($stuck) { "STOPPED: kept retrying refused commands ($refused refusals); see Steps in the log" }
          elseif ($timedOut) { "TIMED OUT after $TimeoutSec s (process killed)" }
          elseif ($exit -ne 0) { "ERROR (exit $exit)" }
          else { 'done' }

$report = @"
LOCAL WORKER [$Role] via Ollama model '$Model': $status in $secs s
Changed files (git): $(if ($changed.Count) { ($changed -join '; ') } else { 'none' })
$buildResult
--- worker reply ---
$($stdout.Trim())
Steps taken: $($steps.Count) ($(($steps | Where-Object { $_ -like '*tool error*' }).Count) tool errors)
$(if ($stderr.Trim()) { "--- stderr (last lines) ---`n" + (($stderr.Trim() -split "`n" | Select-Object -Last 8) -join "`n") })
Full log: $log
"@

@"
# Local worker run $stamp ($Role)
- model: $Model (Ollama at $(Get-OllamaBaseUrl))
- workdir: $WorkDir
- status: $status, $secs s, exit $exit
- allowed tools: $($allowed -join ', ')
- model turns: $turns

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
if ($timedOut -or $stuck) { exit 3 }
if ($exit -ne 0) { exit 4 }
if ($buildResult -like 'BUILD: FAILED*') { exit 5 }
exit 0
