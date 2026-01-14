# Named Pipe Transport Test - Setup and Run Script
# Run this from Windows PowerShell (not WSL)

param(
    [switch]$BuildOnly,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "=== Named Pipe Transport Test ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build projects
if (-not $SkipBuild) {
    Write-Host "Step 1: Building projects..." -ForegroundColor Yellow

    Push-Location $ScriptDir

    Write-Host "  Building NamedPipeHostServer..."
    dotnet build NamedPipeHostServer -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Build failed for NamedPipeHostServer" }

    Write-Host "  Building NamedPipeClientTest..."
    dotnet build NamedPipeClientTest -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Build failed for NamedPipeClientTest" }

    Pop-Location

    Write-Host "  Build complete!" -ForegroundColor Green
    Write-Host ""
}

if ($BuildOnly) {
    Write-Host "Build-only mode. Exiting." -ForegroundColor Yellow
    exit 0
}

# Step 2: Set up test folders
Write-Host "Step 2: Setting up test folders..." -ForegroundColor Yellow

$ClientFolder = "C:\TransportTest\Client"
$OutputFolder = "C:\TransportTest\Output"

New-Item -ItemType Directory -Force -Path $ClientFolder | Out-Null
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null

# Copy client binary
$ClientBin = Join-Path $ScriptDir "NamedPipeClientTest\bin\Release\net8.0-windows"
Copy-Item "$ClientBin\*" $ClientFolder -Recurse -Force

Write-Host "  Client binary copied to: $ClientFolder" -ForegroundColor Green
Write-Host "  Output folder created: $OutputFolder" -ForegroundColor Green
Write-Host ""

# Step 3: Start host server in new window
Write-Host "Step 3: Starting host server..." -ForegroundColor Yellow

$HostServerExe = Join-Path $ScriptDir "NamedPipeHostServer\bin\Release\net8.0-windows\NamedPipeHostServer.exe"
Start-Process -FilePath $HostServerExe -WindowStyle Normal

Write-Host "  Host server started in new window" -ForegroundColor Green
Write-Host ""

# Wait a moment for server to initialize
Start-Sleep -Seconds 2

# Step 4: Launch sandbox
Write-Host "Step 4: Launching Windows Sandbox..." -ForegroundColor Yellow

$WsbPath = Join-Path $ScriptDir "test-named-pipe.wsb"
Start-Process -FilePath "WindowsSandbox.exe" -ArgumentList $WsbPath

Write-Host "  Sandbox launched with client test" -ForegroundColor Green
Write-Host ""

# Step 5: Wait for results
Write-Host "Step 5: Waiting for test results..." -ForegroundColor Yellow
Write-Host "  (Sandbox takes 10-15 seconds to boot)" -ForegroundColor Gray
Write-Host ""

$ResultFile = Join-Path $OutputFolder "named-pipe-test-result.json"
$MaxWaitSeconds = 120
$WaitedSeconds = 0

while ($WaitedSeconds -lt $MaxWaitSeconds) {
    if (Test-Path $ResultFile) {
        Write-Host ""
        Write-Host "=== TEST COMPLETE ===" -ForegroundColor Cyan
        Write-Host ""

        $Result = Get-Content $ResultFile | ConvertFrom-Json

        if ($Result.connection_successful) {
            Write-Host "RESULT: SUCCESS - Named pipes work!" -ForegroundColor Green
            Write-Host ""
            Write-Host "Latency Statistics:" -ForegroundColor Yellow
            Write-Host "  P50: $($Result.latency_ms.p50)ms"
            Write-Host "  P95: $($Result.latency_ms.p95)ms"
            Write-Host "  P99: $($Result.latency_ms.p99)ms"
            Write-Host "  Max: $($Result.latency_ms.max)ms"
            Write-Host ""
            Write-Host "Target: <100ms P95" -ForegroundColor Yellow
            if ($Result.pass) {
                Write-Host "VERDICT: PASS" -ForegroundColor Green
            } else {
                Write-Host "VERDICT: FAIL (latency too high)" -ForegroundColor Red
            }
        } else {
            Write-Host "RESULT: FAILED - Named pipes do NOT work across sandbox boundary" -ForegroundColor Red
            Write-Host ""
            Write-Host "Conclusion: $($Result.conclusion)" -ForegroundColor Yellow
            Write-Host "Recommendation: $($Result.recommendation)" -ForegroundColor Yellow
        }

        Write-Host ""
        Write-Host "Full results: $ResultFile" -ForegroundColor Gray
        exit 0
    }

    Start-Sleep -Seconds 5
    $WaitedSeconds += 5
    Write-Host "  Waiting... ($WaitedSeconds/$MaxWaitSeconds seconds)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "TIMEOUT: No result file generated after $MaxWaitSeconds seconds" -ForegroundColor Red
Write-Host "Check the sandbox window for errors." -ForegroundColor Yellow
exit 1
