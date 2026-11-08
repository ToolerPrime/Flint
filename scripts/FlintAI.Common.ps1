# Shared settings and helpers for Flint's hybrid AI scripts (Windows PowerShell 5.1+).
# Dot-source this file; it only defines functions.
#
# Model roles live in scripts/ai-models.json (edit that file to reassign).
# Provider settings are only ever put on a CHILD process's environment, never
# on the current session or the user/machine environment.

$script:FlintRoot = Split-Path -Parent $PSScriptRoot

function Get-FlintAIConfig {
    $path = Join-Path $PSScriptRoot 'ai-models.json'
    if (-not (Test-Path $path)) { throw "Missing $path" }
    return Get-Content $path -Raw | ConvertFrom-Json
}

function Find-Ollama {
    $cmd = Get-Command ollama.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }
    $p = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'
    if (Test-Path $p) { return $p }
    return $null
}

function Find-Claude {
    # The real executable, not the npm .cmd shim: running a .cmd goes through
    # cmd.exe, which would re-parse the arguments.
    $exe = Join-Path $env:APPDATA 'npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe'
    if (Test-Path $exe) { return $exe }
    $cmd = Get-Command claude.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }
    return $null
}

function Find-Codex {
    # The real codex.exe inside the npm package, not the .ps1/.cmd shim.
    $exe = Join-Path $env:APPDATA 'npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe'
    if (Test-Path $exe) { return $exe }
    $cmd = Get-Command codex.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }
    return $null
}

function ConvertTo-CommandLineArg {
    # Quote one argument by the Windows CommandLineToArgvW rules.
    param([string]$Arg)
    if ($Arg -eq '') { return '""' }
    if ($Arg -notmatch '[\s"]') { return $Arg }
    $bsChar = [char]92
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    $bs = 0
    foreach ($ch in $Arg.ToCharArray()) {
        if ($ch -eq $bsChar) { $bs++; continue }
        if ($ch -eq '"') { [void]$sb.Append($bsChar, $bs * 2 + 1); [void]$sb.Append('"'); $bs = 0; continue }
        if ($bs -gt 0) { [void]$sb.Append($bsChar, $bs); $bs = 0 }
        [void]$sb.Append($ch)
    }
    if ($bs -gt 0) { [void]$sb.Append($bsChar, $bs * 2) }
    [void]$sb.Append('"')
    return $sb.ToString()
}

function Get-OllamaBaseUrl {
    # FLINT_OLLAMA_URL overrides the configured address (e.g. Ollama on another port).
    if ($env:FLINT_OLLAMA_URL) { return $env:FLINT_OLLAMA_URL }
    $cfg = Get-FlintAIConfig
    if ($cfg.ollamaUrl) { return [string]$cfg.ollamaUrl }
    return 'http://127.0.0.1:11434'
}

function Test-OllamaUp {
    param([string]$BaseUrl = (Get-OllamaBaseUrl))
    try { $null = Invoke-RestMethod "$BaseUrl/api/version" -TimeoutSec 3; return $true } catch { return $false }
}

function Start-OllamaIfNeeded {
    $url = Get-OllamaBaseUrl
    if (Test-OllamaUp $url) { return }
    $o = Find-Ollama
    if (-not $o) { throw "Ollama is not installed. Install it with: winget install Ollama.Ollama" }
    Start-Process $o -ArgumentList 'serve' -WindowStyle Hidden | Out-Null
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 500
        if (Test-OllamaUp $url) { return }
    }
    throw "Ollama did not start at $url. Start the Ollama app from the Start menu and try again."
}

function Test-OllamaModel {
    param([Parameter(Mandatory)][string]$Model)
    $url = Get-OllamaBaseUrl
    $names = @((Invoke-RestMethod "$url/api/tags" -TimeoutSec 10).models | ForEach-Object { $_.name })
    $want = if ($Model.Contains(':')) { $Model } else { "$Model`:latest" }
    return [bool]($names | Where-Object { $_ -eq $want -or $_ -eq $Model })
}

function Get-LocalClaudeEnv {
    # Environment for a Claude Code process that must use Ollama only.
    # Returned as a hashtable and applied to a ProcessStartInfo, so nothing
    # leaks into the calling session.
    param([Parameter(Mandatory)][string]$Model, [int]$CompactWindow = 56000)
    $url = Get-OllamaBaseUrl
    return [ordered]@{
        ANTHROPIC_BASE_URL                       = $url
        ANTHROPIC_AUTH_TOKEN                     = 'ollama'
        ANTHROPIC_API_KEY                        = ''
        ANTHROPIC_MODEL                          = $Model
        ANTHROPIC_DEFAULT_OPUS_MODEL             = $Model
        ANTHROPIC_DEFAULT_SONNET_MODEL           = $Model
        ANTHROPIC_DEFAULT_HAIKU_MODEL            = $Model
        ANTHROPIC_SMALL_FAST_MODEL               = $Model
        CLAUDE_CODE_SUBAGENT_MODEL               = $Model
        CLAUDE_CODE_AUTO_COMPACT_WINDOW          = "$CompactWindow"
        CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC = '1'
        DISABLE_TELEMETRY                        = '1'
        # Separate config dir: no Anthropic login, no global plugins, hooks or
        # MCP servers (small local models drown in them). The project's own
        # CLAUDE.md and .claude/ folder in the repo still load.
        CLAUDE_CONFIG_DIR                        = (Join-Path $env:USERPROFILE '.claude-local')
        FLINT_AI_PROVIDER                        = 'ollama'
    }
}

# Variables that must never reach a local child process: they could point it
# back at Anthropic, or make it think it is nested inside another session.
$script:StripFromChild = @(
    'ANTHROPIC_BASE_URL', 'ANTHROPIC_API_KEY', 'ANTHROPIC_AUTH_TOKEN', 'CLAUDE_CODE_OAUTH_TOKEN',
    'CLAUDE_CODE_USE_BEDROCK', 'CLAUDE_CODE_USE_VERTEX', 'CLAUDECODE', 'CLAUDE_CODE_ENTRYPOINT',
    'CLAUDE_CODE_SSE_PORT', 'ANTHROPIC_CUSTOM_HEADERS',
    # OpenAI API keys: a Codex worker must use the ChatGPT login, never per-token API billing.
    'OPENAI_API_KEY', 'OPENAI_BASE_URL', 'CODEX_API_KEY'
)

function New-ChildStartInfo {
    param(
        [Parameter(Mandatory)][string]$File,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory = $script:FlintRoot,
        [System.Collections.IDictionary]$Environment = @{}
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $File
    # Each argument is quoted by Windows rules and the file is started directly
    # (no shell), so argument text is never interpreted as commands. Free text
    # such as task prompts goes on stdin, never on the command line.
    $psi.Arguments = (@($Arguments) | ForEach-Object { ConvertTo-CommandLineArg ([string]$_) }) -join ' '
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    foreach ($k in $script:StripFromChild) {
        if ($psi.EnvironmentVariables.ContainsKey($k)) { $psi.EnvironmentVariables.Remove($k) }
    }
    foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[$k] = [string]$Environment[$k] }
    return $psi
}

function Stop-ProcessTree {
    param([int]$ProcessId)
    try { & taskkill.exe /PID $ProcessId /T /F 2>$null | Out-Null } catch { }
}
