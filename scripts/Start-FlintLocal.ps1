<#
.SYNOPSIS
  MODE B (Completely local): Claude Code in the Flint folder, powered only by
  an Ollama model. Uses no Anthropic allowance. Good for when your Claude
  usage runs out.

.DESCRIPTION
  Applies the Ollama provider settings to the Claude Code CHILD process only
  (nothing persists in your terminal, user or machine settings), with a
  separate config folder (%USERPROFILE%\.claude-local) so your normal Claude
  login, plugins and settings are untouched. Your normal permission prompts
  still apply.

.EXAMPLE
  scripts\Start-FlintLocal.ps1
.EXAMPLE
  scripts\Start-FlintLocal.ps1 -Model agent-gemma4-12b
.EXAMPLE
  scripts\Start-FlintLocal.ps1 -Prompt "List the C# files in desktop and say what MainForm.Ui.cs builds"
#>
[CmdletBinding()]
param(
    [string]$Model,
    [string]$Prompt,
    [int]$TimeoutSec = 600
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FlintAI.Common.ps1')

$cfg = Get-FlintAIConfig
if (-not $Model) { $Model = $cfg.localMode.model }

$claude = Find-Claude
if (-not $claude) { throw 'Claude Code CLI not found. Install: npm install -g --allow-scripts=@anthropic-ai/claude-code @anthropic-ai/claude-code' }
Start-OllamaIfNeeded
if (-not (Test-OllamaModel $Model)) { throw "Ollama model '$Model' is not installed. Run: ollama list   (see docs\AI_START_HERE.md for the models to create)" }

$childEnv = Get-LocalClaudeEnv -Model $Model
$childEnv['MSBUILDDISABLENODEREUSE'] = '1'
$childEnv['UseSharedCompilation'] = 'false'

if ($Prompt) {
    # One-shot, non-interactive, read-only question (used for checks and tests).
    $psi = New-ChildStartInfo -File $claude -WorkingDirectory $script:FlintRoot -Environment $childEnv `
        -Arguments @('-p', '--model', $Model, '--output-format', 'text', '--tools', 'Read,Glob,Grep',
                     '--disable-slash-commands', '--strict-mcp-config', '--no-session-persistence',
                     '--permission-mode', 'default', '--allowedTools', 'Read', 'Glob', 'Grep')
    $p = [Diagnostics.Process]::Start($psi)
    $o = $p.StandardOutput.ReadToEndAsync(); $e = $p.StandardError.ReadToEndAsync()
    $p.StandardInput.Write($Prompt); $p.StandardInput.Close()
    if (-not $p.WaitForExit($TimeoutSec * 1000)) { Stop-ProcessTree $p.Id; throw "Local Claude timed out after $TimeoutSec s." }
    Write-Output "[local model: $Model via Ollama]"
    Write-Output $o.Result.Trim()
    if ($p.ExitCode -ne 0) { Write-Output $e.Result.Trim(); exit $p.ExitCode }
    exit 0
}

# Interactive: run Claude Code in this console with the provider variables set
# on the child only.
Write-Host "Starting Claude Code on LOCAL model '$Model' (Ollama, no Anthropic usage)." -ForegroundColor Green
Write-Host "Your normal Claude settings are untouched; this session uses %USERPROFILE%\.claude-local." -ForegroundColor DarkGray
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $claude
$psi.Arguments = (@('--model', $Model) | ForEach-Object { ConvertTo-CommandLineArg $_ }) -join ' '
$psi.WorkingDirectory = $script:FlintRoot
$psi.UseShellExecute = $false
foreach ($k in $script:StripFromChild) { if ($psi.EnvironmentVariables.ContainsKey($k)) { $psi.EnvironmentVariables.Remove($k) } }
foreach ($k in $childEnv.Keys) { $psi.EnvironmentVariables[$k] = [string]$childEnv[$k] }
$p = [Diagnostics.Process]::Start($psi)
$p.WaitForExit()
exit $p.ExitCode
