# Run-AllTests.ps1
# Runs all sandbox integration tests

param(
    [ValidateSet("All", "Smoke", "HotReload", "Debounce", "FileSystem", "JobObject", "CrashRecovery")]
    [string]$Category = "All",
    [switch]$Verbose,
    [switch]$NoCleanup,
    [string]$ResultsFile = "test-results.json"
)

$ErrorActionPreference = "Stop"

# Import test framework
. "$PSScriptRoot\TestFramework.ps1"

Write-Host ""
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host " Sandbox Integration Test Suite" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host ""

#region Prerequisites Check
Write-Host "Checking prerequisites..." -ForegroundColor Yellow

# Check Windows Sandbox is available
$sandboxFeature = Get-WindowsOptionalFeature -Online -FeatureName "Containers-DisposableClientVM" -ErrorAction SilentlyContinue
if ($sandboxFeature.State -ne "Enabled") {
    Write-Host "ERROR: Windows Sandbox feature is not enabled!" -ForegroundColor Red
    Write-Host "Run: Enable-WindowsOptionalFeature -FeatureName 'Containers-DisposableClientVM' -Online"
    exit 1
}
Write-Host "  Windows Sandbox: Enabled" -ForegroundColor Green

# Check setup has been run
if (!(Test-Path $Config.SandboxConfig)) {
    Write-Host "ERROR: Sandbox not set up!" -ForegroundColor Red
    Write-Host "Run: ..\setup.ps1"
    exit 1
}
Write-Host "  Setup complete: Yes" -ForegroundColor Green

# Check .NET runtime
$dotnetExe = "C:\WinFormsMcpSandboxWorkspace\DotNet\dotnet.exe"
if (!(Test-Path $dotnetExe)) {
    Write-Host "ERROR: .NET runtime not found!" -ForegroundColor Red
    Write-Host "Run: ..\setup-dotnet.ps1"
    exit 1
}
Write-Host "  .NET runtime: Available" -ForegroundColor Green

Write-Host ""
#endregion

#region Run Tests
Start-TestRun -Name "Sandbox Integration Tests"

# Define test order
$testSuites = @(
    @{ Name = "Smoke"; Script = "Test-Smoke.ps1"; Category = "Smoke" }
    @{ Name = "HotReload"; Script = "Test-HotReload.ps1"; Category = "HotReload" }
    @{ Name = "Debounce"; Script = "Test-Debounce.ps1"; Category = "Debounce" }
    @{ Name = "FileSystem"; Script = "Test-FileSystem.ps1"; Category = "FileSystem" }
    @{ Name = "JobObject"; Script = "Test-JobObject.ps1"; Category = "JobObject" }
    @{ Name = "CrashRecovery"; Script = "Test-CrashRecovery.ps1"; Category = "CrashRecovery" }
)

# Filter by category
if ($Category -ne "All") {
    $testSuites = $testSuites | Where-Object { $_.Category -eq $Category }
}

# Ensure sandbox is stopped before starting
Write-Host "Stopping any existing sandbox..." -ForegroundColor Gray
Stop-Sandbox
Start-Sleep -Seconds 2

# Run test suites
foreach ($suite in $testSuites) {
    $scriptPath = Join-Path $PSScriptRoot $suite.Script

    if (Test-Path $scriptPath) {
        Write-Host ""
        Write-Host "Running $($suite.Name) tests..." -ForegroundColor Cyan

        try {
            . $scriptPath
        }
        catch {
            Write-Host "ERROR in $($suite.Name): $_" -ForegroundColor Red
        }
    }
    else {
        Write-Host "WARNING: Test script not found: $($suite.Script)" -ForegroundColor Yellow
    }
}

# Complete test run
$results = Complete-TestRun
#endregion

#region Cleanup
if (-not $NoCleanup) {
    Write-Host "Cleaning up..." -ForegroundColor Gray
    Stop-Sandbox
    Start-Sleep -Seconds 2
}
else {
    Write-Host "Skipping cleanup (sandbox may still be running)" -ForegroundColor Yellow
}
#endregion

#region Save Results
$resultsPath = Join-Path $PSScriptRoot $ResultsFile
$results | ConvertTo-Json -Depth 5 | Out-File -FilePath $resultsPath -Encoding UTF8
Write-Host ""
Write-Host "Results saved to: $resultsPath" -ForegroundColor Gray
#endregion

#region Exit Code
if ($results.Failed -gt 0) {
    Write-Host ""
    Write-Host "FAILED: $($results.Failed) test(s) failed" -ForegroundColor Red
    exit 1
}
else {
    Write-Host ""
    Write-Host "SUCCESS: All tests passed!" -ForegroundColor Green
    exit 0
}
#endregion
