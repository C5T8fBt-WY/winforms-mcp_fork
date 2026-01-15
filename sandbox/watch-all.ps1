# watch-all.ps1
# Combined watcher that runs both watch-dev.ps1 and watch-app.ps1 as background jobs
# Forwards console output from both watchers
# Handles Ctrl+C to stop both

param(
    [string]$DeployPath = "C:\TransportTest\Server",
    [string]$AppPath = "C:\TransportTest\App",
    [string]$SharedPath = "C:\TransportTest\Shared"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Combined Sandbox Watcher ===" -ForegroundColor Cyan
Write-Host "Press Ctrl+C to stop both watchers"
Write-Host ""

$ScriptDir = $PSScriptRoot
$WatchDev = Join-Path $ScriptDir "watch-dev.ps1"
$WatchApp = Join-Path $ScriptDir "watch-app.ps1"

# Verify scripts exist
if (!(Test-Path $WatchDev)) {
    Write-Host "ERROR: watch-dev.ps1 not found at $WatchDev" -ForegroundColor Red
    exit 1
}

$hasAppWatcher = Test-Path $WatchApp

# Start background jobs
Write-Host "Starting MCP Server watcher..." -ForegroundColor Yellow
$serverJob = Start-Job -ScriptBlock {
    param($script, $deployPath, $sharedPath)
    & $script -DeployPath $deployPath -SharedPath $sharedPath
} -ArgumentList $WatchDev, $DeployPath, $SharedPath

if ($hasAppWatcher) {
    Write-Host "Starting Test App watcher..." -ForegroundColor Yellow
    $appJob = Start-Job -ScriptBlock {
        param($script, $deployPath, $sharedPath)
        & $script -DeployPath $deployPath -SharedPath $sharedPath
    } -ArgumentList $WatchApp, $AppPath, $SharedPath
} else {
    Write-Host "Test App watcher not found, skipping" -ForegroundColor Gray
    $appJob = $null
}

Write-Host ""
Write-Host "Watchers running. Output below:" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Gray
Write-Host ""

# Output forwarding loop
try {
    while ($true) {
        # Get output from server job
        $serverOutput = Receive-Job $serverJob -ErrorAction SilentlyContinue
        if ($serverOutput) {
            foreach ($line in $serverOutput) {
                Write-Host "[SERVER] $line" -ForegroundColor Cyan
            }
        }

        # Get output from app job
        if ($appJob) {
            $appOutput = Receive-Job $appJob -ErrorAction SilentlyContinue
            if ($appOutput) {
                foreach ($line in $appOutput) {
                    Write-Host "[APP] $line" -ForegroundColor Magenta
                }
            }
        }

        # Check if jobs are still running
        if ($serverJob.State -eq "Failed") {
            Write-Host "[SERVER] Job failed!" -ForegroundColor Red
            Receive-Job $serverJob -ErrorAction SilentlyContinue
        }
        if ($appJob -and $appJob.State -eq "Failed") {
            Write-Host "[APP] Job failed!" -ForegroundColor Red
            Receive-Job $appJob -ErrorAction SilentlyContinue
        }

        Start-Sleep -Milliseconds 500
    }
} finally {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Gray
    Write-Host "Stopping watchers..." -ForegroundColor Yellow

    # Stop jobs
    if ($serverJob) {
        Stop-Job $serverJob -ErrorAction SilentlyContinue
        Remove-Job $serverJob -Force -ErrorAction SilentlyContinue
        Write-Host "  Server watcher stopped"
    }

    if ($appJob) {
        Stop-Job $appJob -ErrorAction SilentlyContinue
        Remove-Job $appJob -Force -ErrorAction SilentlyContinue
        Write-Host "  App watcher stopped"
    }

    Write-Host ""
    Write-Host "All watchers stopped." -ForegroundColor Green
}
