param([string[]]$Jobs = @('shapes', 'text', 'masks', 'review'))
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    foreach ($job in $Jobs) {
        if ($job -notmatch '^[a-z0-9-]+$') { throw 'Invalid job name' }
        Write-Host "Local worker: $job"
        python tools/local_workers.py "jobs/$job.json"
        if ($LASTEXITCODE -ne 0) { throw "Worker $job failed or was incomplete; inspect runs before continuing" }
    }
} finally { Pop-Location }
