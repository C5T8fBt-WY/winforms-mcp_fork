# Phase 1 Test Script: Sandbox Launch and Close
# Tests the SandboxManager lifecycle functionality
#
# This script simulates what the launch_app_sandboxed and close_sandbox MCP tools do.
#
# Prerequisites:
# 1. Run from admin PowerShell (sandbox requires admin)
# 2. Build and publish SharedFolderClient
# 3. Copy bootstrap.ps1 to Phase1 folder
#
# Usage:
#   .\test-phase1.ps1 [-Setup] [-LaunchOnly] [-Verbose]

param(
    [switch]$Setup,      # Run setup steps only
    [switch]$LaunchOnly, # Launch but don't close (manual testing)
    [switch]$VerboseLog  # Extra logging
)

$ErrorActionPreference = "Stop"

# Configuration
$TestRoot = "C:\TransportTest"
$Phase1Dir = Join-Path $TestRoot "Phase1"
$SharedDir = Join-Path $TestRoot "Shared"
$WsbConfig = Join-Path $TestRoot "test-phase1-launch.wsb"

# Signal file paths (host side)
$ReadySignal = Join-Path $SharedDir "mcp-ready.signal"
$ShutdownSignal = Join-Path $SharedDir "shutdown.signal"
$BootstrapLog = Join-Path $SharedDir "bootstrap.log"

# Paths in WSL
$WslRoot = "\\wsl.localhost\Ubuntu\home\jhedin\workspace\magpie-craft\winforms-mcp"

Write-Host "=== Phase 1 Test: Sandbox Launch and Close ===" -ForegroundColor Cyan
Write-Host "Test root: $TestRoot"
Write-Host ""

# ============================================
# Setup Phase
# ============================================
if ($Setup) {
    Write-Host "[SETUP] Creating test directories..." -ForegroundColor Yellow

    New-Item -ItemType Directory -Force -Path $Phase1Dir | Out-Null
    New-Item -ItemType Directory -Force -Path $SharedDir | Out-Null

    Write-Host "[SETUP] Publishing SharedFolderClient (self-contained)..." -ForegroundColor Yellow
    $publishDir = "$WslRoot\prototypes\transport-test"
    Push-Location $publishDir

    # Publish self-contained to Phase1 folder
    dotnet publish SharedFolderClient -c Release -r win-x64 --self-contained true -o $Phase1Dir 2>&1

    Pop-Location

    Write-Host "[SETUP] Copying bootstrap.ps1..." -ForegroundColor Yellow
    Copy-Item "$WslRoot\src\Rhombus.WinFormsMcp.Server\bootstrap.ps1" $Phase1Dir -Force

    Write-Host "[SETUP] Copying .wsb config..." -ForegroundColor Yellow
    Copy-Item "$WslRoot\prototypes\transport-test\test-phase1-launch.wsb" $WsbConfig -Force

    Write-Host "[SETUP] Unblocking files..." -ForegroundColor Yellow
    Get-ChildItem $TestRoot -Recurse | Unblock-File

    Write-Host ""
    Write-Host "[SETUP] Setup complete!" -ForegroundColor Green
    Write-Host "Files in $Phase1Dir :"
    Get-ChildItem $Phase1Dir | Format-Table Name, Length
    Write-Host ""
    Write-Host "Run without -Setup to execute the test."
    exit 0
}

# ============================================
# Validation
# ============================================
Write-Host "[VALIDATE] Checking prerequisites..." -ForegroundColor Yellow

if (!(Test-Path $Phase1Dir)) {
    Write-Host "[ERROR] Phase1 directory not found. Run with -Setup first." -ForegroundColor Red
    exit 1
}

if (!(Test-Path "$Phase1Dir\SharedFolderClient.exe")) {
    Write-Host "[ERROR] SharedFolderClient.exe not found. Run with -Setup first." -ForegroundColor Red
    exit 1
}

if (!(Test-Path "$Phase1Dir\bootstrap.ps1")) {
    Write-Host "[ERROR] bootstrap.ps1 not found. Run with -Setup first." -ForegroundColor Red
    exit 1
}

if (!(Test-Path $WsbConfig)) {
    Write-Host "[ERROR] .wsb config not found at $WsbConfig. Run with -Setup first." -ForegroundColor Red
    exit 1
}

Write-Host "[VALIDATE] OK" -ForegroundColor Green
Write-Host ""

# ============================================
# Clean up from previous runs
# ============================================
Write-Host "[CLEANUP] Removing old signal files..." -ForegroundColor Yellow
Remove-Item $ReadySignal -Force -ErrorAction SilentlyContinue
Remove-Item $ShutdownSignal -Force -ErrorAction SilentlyContinue
Remove-Item $BootstrapLog -Force -ErrorAction SilentlyContinue
Get-ChildItem $SharedDir -Filter "request-*.json" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $SharedDir -Filter "response-*.json" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $SharedDir -Filter "*.signal" -ErrorAction SilentlyContinue | Remove-Item -Force
Write-Host "[CLEANUP] Done" -ForegroundColor Green
Write-Host ""

# ============================================
# Launch Sandbox (simulates launch_app_sandboxed)
# ============================================
Write-Host "[LAUNCH] Starting Windows Sandbox..." -ForegroundColor Cyan
Write-Host "  Config: $WsbConfig"

$StartTime = Get-Date
$SandboxProcess = Start-Process WindowsSandbox.exe -ArgumentList "`"$WsbConfig`"" -PassThru

Write-Host "[LAUNCH] Sandbox process started (PID: $($SandboxProcess.Id))" -ForegroundColor Green
Write-Host ""

# ============================================
# Wait for MCP Ready Signal
# ============================================
Write-Host "[WAIT] Waiting for mcp-ready.signal..." -ForegroundColor Yellow
Write-Host "  Timeout: 60 seconds"
Write-Host "  Polling: $ReadySignal"

$Timeout = 60
$StartWait = Get-Date

while (((Get-Date) - $StartWait).TotalSeconds -lt $Timeout) {
    if (Test-Path $ReadySignal) {
        $WaitTime = [math]::Round(((Get-Date) - $StartWait).TotalSeconds, 1)
        Write-Host "[WAIT] Ready signal received after ${WaitTime}s!" -ForegroundColor Green

        # Read and display signal content
        $SignalContent = Get-Content $ReadySignal -Raw
        Write-Host ""
        Write-Host "Signal content:" -ForegroundColor Cyan
        Write-Host $SignalContent
        Write-Host ""
        break
    }

    # Check if sandbox crashed
    if ($SandboxProcess.HasExited) {
        Write-Host "[ERROR] Sandbox process exited unexpectedly (code: $($SandboxProcess.ExitCode))" -ForegroundColor Red

        if (Test-Path $BootstrapLog) {
            Write-Host ""
            Write-Host "Bootstrap log:" -ForegroundColor Yellow
            Get-Content $BootstrapLog
        }
        exit 1
    }

    Write-Host "." -NoNewline
    Start-Sleep -Milliseconds 500
}

if (!(Test-Path $ReadySignal)) {
    Write-Host ""
    Write-Host "[ERROR] Timeout waiting for ready signal!" -ForegroundColor Red

    if (Test-Path $BootstrapLog) {
        Write-Host ""
        Write-Host "Bootstrap log:" -ForegroundColor Yellow
        Get-Content $BootstrapLog
    }

    Write-Host ""
    Write-Host "Killing sandbox..." -ForegroundColor Yellow
    Stop-Process -Id $SandboxProcess.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

$LaunchTime = [math]::Round(((Get-Date) - $StartTime).TotalSeconds, 1)
Write-Host "[LAUNCH] Sandbox fully ready in ${LaunchTime}s" -ForegroundColor Green
Write-Host ""

# ============================================
# Test Communication (optional)
# ============================================
Write-Host "[COMMS] Testing request/response communication..." -ForegroundColor Cyan

$TestRequest = @{
    jsonrpc = "2.0"
    id = 1
    method = "ping"
    params = @{ timestamp = (Get-Date).ToString("o") }
} | ConvertTo-Json

$RequestFile = Join-Path $SharedDir "request-1.json"
$ResponseFile = Join-Path $SharedDir "response-1.json"

Write-Host "  Sending test request..."
$TestRequest | Out-File -FilePath $RequestFile -Encoding UTF8

# Wait for response
$ResponseTimeout = 10
$ResponseStart = Get-Date

while (((Get-Date) - $ResponseStart).TotalSeconds -lt $ResponseTimeout) {
    if (Test-Path $ResponseFile) {
        $ResponseTime = [math]::Round(((Get-Date) - $ResponseStart).TotalMilliseconds)
        $Response = Get-Content $ResponseFile -Raw

        Write-Host "[COMMS] Response received in ${ResponseTime}ms!" -ForegroundColor Green
        Write-Host "Response:" -ForegroundColor Cyan
        Write-Host $Response
        Write-Host ""

        # Cleanup
        Remove-Item $RequestFile -Force -ErrorAction SilentlyContinue
        Remove-Item $ResponseFile -Force -ErrorAction SilentlyContinue
        break
    }
    Start-Sleep -Milliseconds 50
}

if (!(Test-Path $ResponseFile) -and (Test-Path $RequestFile)) {
    Write-Host "[COMMS] No response received (timeout: ${ResponseTimeout}s)" -ForegroundColor Yellow
    Remove-Item $RequestFile -Force -ErrorAction SilentlyContinue
}

# ============================================
# Launch Only Mode
# ============================================
if ($LaunchOnly) {
    Write-Host "[DONE] Launch-only mode. Sandbox is running." -ForegroundColor Green
    Write-Host ""
    Write-Host "To close manually, create shutdown.signal:" -ForegroundColor Yellow
    Write-Host "  `"shutdown`" | Out-File '$ShutdownSignal'"
    Write-Host ""
    Write-Host "Or close the sandbox window."
    exit 0
}

# ============================================
# Close Sandbox (simulates close_sandbox)
# ============================================
Write-Host "[CLOSE] Sending shutdown signal..." -ForegroundColor Cyan

$ShutdownContent = @{
    timestamp = (Get-Date).ToString("o")
    reason = "test-phase1 completed"
} | ConvertTo-Json

$ShutdownContent | Out-File -FilePath $ShutdownSignal -Encoding UTF8

Write-Host "[CLOSE] Waiting for sandbox to exit (graceful timeout: 10s)..." -ForegroundColor Yellow

$CloseStart = Get-Date
$GracefulTimeout = 10

while (((Get-Date) - $CloseStart).TotalSeconds -lt $GracefulTimeout) {
    if ($SandboxProcess.HasExited) {
        $CloseTime = [math]::Round(((Get-Date) - $CloseStart).TotalSeconds, 1)
        Write-Host "[CLOSE] Sandbox exited gracefully in ${CloseTime}s" -ForegroundColor Green
        break
    }
    Start-Sleep -Milliseconds 500
}

if (!$SandboxProcess.HasExited) {
    Write-Host "[CLOSE] Graceful shutdown timed out. Force killing..." -ForegroundColor Yellow
    Stop-Process -Id $SandboxProcess.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 1000

    if ($SandboxProcess.HasExited) {
        Write-Host "[CLOSE] Sandbox force-killed successfully" -ForegroundColor Yellow
    } else {
        Write-Host "[ERROR] Failed to kill sandbox process!" -ForegroundColor Red
    }
}

# ============================================
# Summary
# ============================================
Write-Host ""
Write-Host "=== Phase 1 Test Results ===" -ForegroundColor Cyan
Write-Host "  Launch time:        ${LaunchTime}s"

$TotalTime = [math]::Round(((Get-Date) - $StartTime).TotalSeconds, 1)
Write-Host "  Total test time:    ${TotalTime}s"
Write-Host ""

if (Test-Path $BootstrapLog) {
    Write-Host "Bootstrap log contents:" -ForegroundColor Yellow
    Write-Host "------------------------"
    Get-Content $BootstrapLog
}

Write-Host ""
Write-Host "[SUCCESS] Phase 1 test completed!" -ForegroundColor Green
