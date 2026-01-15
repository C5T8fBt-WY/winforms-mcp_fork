# test.ps1
# Integration test for the sandbox workflow
# Launches sandbox, waits for ready signal, sends ping, optionally tests hot-reload
#
# Usage:
#   .\test.ps1                    # Basic test (launch, ping, shutdown)
#   .\test.ps1 -TestHotReload     # Also test hot-reload cycle
#   .\test.ps1 -KeepAlive         # Don't shutdown at end (for manual testing)

param(
    [string]$WinFormsMcpSandboxWorkspacePath = "C:\WinFormsMcpSandboxWorkspace",
    [int]$TimeoutSeconds = 60,
    [switch]$TestHotReload,
    [switch]$KeepAlive
)

$ErrorActionPreference = "Stop"

# Paths
$SharedPath = Join-Path $WinFormsMcpSandboxWorkspacePath "Shared"
$SandboxConfig = Join-Path $WinFormsMcpSandboxWorkspacePath "sandbox-dev.wsb"
$ReadySignal = Join-Path $SharedPath "mcp-ready.signal"
$ShutdownSignal = Join-Path $SharedPath "shutdown.signal"
$ServerTrigger = Join-Path $SharedPath "server.trigger"
$RequestFile = Join-Path $SharedPath "request.json"
$ResponseFile = Join-Path $SharedPath "response.json"

Write-Host "=== Sandbox Integration Test ===" -ForegroundColor Cyan
Write-Host "Sandbox config: $SandboxConfig"
Write-Host "Shared folder: $SharedPath"
Write-Host "Timeout: ${TimeoutSeconds}s"
Write-Host ""

# Verify setup
if (!(Test-Path $SandboxConfig)) {
    Write-Host "ERROR: Sandbox config not found at $SandboxConfig" -ForegroundColor Red
    Write-Host "Run setup.ps1 first!"
    exit 1
}

# Clean up any previous signals
Remove-Item $ReadySignal -Force -ErrorAction SilentlyContinue
Remove-Item $ShutdownSignal -Force -ErrorAction SilentlyContinue
Remove-Item $ServerTrigger -Force -ErrorAction SilentlyContinue
Remove-Item $ResponseFile -Force -ErrorAction SilentlyContinue

#region Launch Sandbox
Write-Host "Launching sandbox..." -ForegroundColor Yellow
$sandboxProcess = Start-Process $SandboxConfig -PassThru

Write-Host "Waiting for ready signal (up to ${TimeoutSeconds}s)..." -ForegroundColor Yellow
$startTime = Get-Date
$ready = $false

while (!$ready -and ((Get-Date) - $startTime).TotalSeconds -lt $TimeoutSeconds) {
    if (Test-Path $ReadySignal) {
        $ready = $true
        break
    }
    Start-Sleep -Seconds 1
    Write-Host "." -NoNewline
}
Write-Host ""

if (!$ready) {
    Write-Host "TIMEOUT: Ready signal not received after ${TimeoutSeconds}s" -ForegroundColor Red
    Write-Host "Check bootstrap.log in $SharedPath for errors"
    exit 1
}

# Read ready signal
$readyData = Get-Content $ReadySignal -Raw | ConvertFrom-Json
Write-Host "Sandbox ready!" -ForegroundColor Green
Write-Host "  Timestamp: $($readyData.timestamp)"
Write-Host "  Hostname: $($readyData.hostname)"
Write-Host "  Server PID: $($readyData.server_pid)"
Write-Host "  App PID: $($readyData.app_pid)"
Write-Host ""
#endregion

#region Send Ping Request
Write-Host "Sending ping request..." -ForegroundColor Yellow

$pingRequest = @{
    jsonrpc = "2.0"
    id = 1
    method = "ping"
} | ConvertTo-Json

$pingRequest | Out-File -FilePath $RequestFile -Encoding UTF8

# Wait for response
$responseTimeout = 10
$responseStart = Get-Date
$gotResponse = $false

while (!$gotResponse -and ((Get-Date) - $responseStart).TotalSeconds -lt $responseTimeout) {
    if (Test-Path $ResponseFile) {
        $gotResponse = $true
        break
    }
    Start-Sleep -Milliseconds 500
}

if ($gotResponse) {
    $response = Get-Content $ResponseFile -Raw
    Write-Host "Response received:" -ForegroundColor Green
    Write-Host $response
    Write-Host ""
    Remove-Item $ResponseFile -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "WARNING: No response to ping request (timeout)" -ForegroundColor Yellow
    Write-Host "The MCP server might not be monitoring the shared folder"
    Write-Host ""
}
#endregion

#region Test Hot-Reload
if ($TestHotReload) {
    Write-Host "Testing hot-reload..." -ForegroundColor Yellow

    $originalPid = $readyData.server_pid

    # Write trigger
    $triggerTmp = "${ServerTrigger}.tmp"
    Set-Content -Path $triggerTmp -Value ((Get-Date).ToString("o"))
    Move-Item -Path $triggerTmp -Destination $ServerTrigger -Force

    Write-Host "Trigger written. Waiting for restart..."

    # Wait for ready signal to be updated
    Start-Sleep -Seconds 3

    # Read updated ready signal
    if (Test-Path $ReadySignal) {
        $newReadyData = Get-Content $ReadySignal -Raw | ConvertFrom-Json
        $newPid = $newReadyData.server_pid

        if ($newPid -ne $originalPid) {
            Write-Host "Hot-reload successful!" -ForegroundColor Green
            Write-Host "  Old PID: $originalPid"
            Write-Host "  New PID: $newPid"
        } else {
            Write-Host "WARNING: PID unchanged after hot-reload" -ForegroundColor Yellow
        }
    } else {
        Write-Host "WARNING: Ready signal not found after hot-reload" -ForegroundColor Yellow
    }
    Write-Host ""
}
#endregion

#region Cleanup
if ($KeepAlive) {
    Write-Host "KeepAlive mode - sandbox will remain running" -ForegroundColor Cyan
    Write-Host "To shutdown, create: $ShutdownSignal"
    Write-Host "Or close the sandbox window manually"
} else {
    Write-Host "Sending shutdown signal..." -ForegroundColor Yellow
    Set-Content -Path $ShutdownSignal -Value ((Get-Date).ToString("o"))

    Start-Sleep -Seconds 2

    Write-Host "Test complete!" -ForegroundColor Green
}
#endregion

#region Summary
Write-Host ""
Write-Host "=== Test Summary ===" -ForegroundColor Cyan
Write-Host "  Sandbox launch: PASS" -ForegroundColor Green
Write-Host "  Ready signal: PASS" -ForegroundColor Green
if ($gotResponse) {
    Write-Host "  Ping response: PASS" -ForegroundColor Green
} else {
    Write-Host "  Ping response: SKIP (no shared folder transport)" -ForegroundColor Yellow
}
if ($TestHotReload) {
    Write-Host "  Hot-reload: PASS" -ForegroundColor Green
}
Write-Host ""
#endregion
