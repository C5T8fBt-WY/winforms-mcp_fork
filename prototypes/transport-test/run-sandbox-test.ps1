# run-sandbox-test.ps1
# Automated test runner for Phase 0 transport tests
#
# Usage:
#   From admin PowerShell:
#   .\run-sandbox-test.ps1 -Test shared-folder
#   .\run-sandbox-test.ps1 -Test named-pipe
#
# From WSL (will prompt for admin):
#   powershell.exe -ExecutionPolicy Bypass -File "\\wsl.localhost\Ubuntu\home\jhedin\workspace\magpie-craft\winforms-mcp\prototypes\transport-test\run-sandbox-test.ps1" -Test shared-folder

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("shared-folder", "named-pipe")]
    [string]$Test
)

$ErrorActionPreference = "Stop"

# Paths
$ProjectRoot = $PSScriptRoot
$TestFolderRoot = "C:\TransportTest"

Write-Host "=== MCP Transport Test Runner ===" -ForegroundColor Cyan
Write-Host "Test: $Test"
Write-Host "Project Root: $ProjectRoot"
Write-Host ""

# Check if running as admin
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "WARNING: Not running as Administrator!" -ForegroundColor Yellow
    Write-Host "Windows Sandbox 0.5.3.0 has a bug that causes coreclr.dll crashes when launched from non-admin." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Re-launching as Administrator..." -ForegroundColor Yellow

    Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File `"$PSCommandPath`" -Test $Test"
    exit 0
}

Write-Host "[OK] Running as Administrator" -ForegroundColor Green
Write-Host ""

# Step 1: Create test folders
Write-Host "Step 1: Creating test folders..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path "$TestFolderRoot\Shared" | Out-Null
New-Item -ItemType Directory -Force -Path "$TestFolderRoot\SharedClient" | Out-Null
New-Item -ItemType Directory -Force -Path "$TestFolderRoot\Client" | Out-Null
New-Item -ItemType Directory -Force -Path "$TestFolderRoot\Output" | Out-Null
Write-Host "[OK] Test folders created" -ForegroundColor Green
Write-Host ""

# Step 2: Build projects
Write-Host "Step 2: Building projects..." -ForegroundColor Cyan
Push-Location $ProjectRoot

if ($Test -eq "shared-folder") {
    dotnet build SharedFolderHost -c Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "SharedFolderHost build failed" }
    dotnet build SharedFolderClient -c Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "SharedFolderClient build failed" }
    Write-Host "[OK] Shared folder projects built" -ForegroundColor Green
} else {
    dotnet build NamedPipeHostServer -c Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "NamedPipeHostServer build failed" }
    dotnet build NamedPipeClientTest -c Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "NamedPipeClientTest build failed" }
    Write-Host "[OK] Named pipe projects built" -ForegroundColor Green
}

Pop-Location
Write-Host ""

# Step 3: Copy client binaries
Write-Host "Step 3: Copying client binaries to host folder..." -ForegroundColor Cyan

if ($Test -eq "shared-folder") {
    $clientSource = Join-Path $ProjectRoot "SharedFolderClient\bin\Release\net8.0-windows\*"
    $clientDest = "$TestFolderRoot\SharedClient\"
    Copy-Item $clientSource $clientDest -Recurse -Force
    Write-Host "[OK] SharedFolderClient copied to $clientDest" -ForegroundColor Green
} else {
    $clientSource = Join-Path $ProjectRoot "NamedPipeClientTest\bin\Release\net8.0-windows\*"
    $clientDest = "$TestFolderRoot\Client\"
    Copy-Item $clientSource $clientDest -Recurse -Force
    Write-Host "[OK] NamedPipeClientTest copied to $clientDest" -ForegroundColor Green
}
Write-Host ""

# Step 4: Clean up old files
Write-Host "Step 4: Cleaning up old test files..." -ForegroundColor Cyan
Get-ChildItem "$TestFolderRoot\Shared\*" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem "$TestFolderRoot\Output\*" -ErrorAction SilentlyContinue | Remove-Item -Force
Write-Host "[OK] Old files cleaned" -ForegroundColor Green
Write-Host ""

# Step 5: Start host-side component
Write-Host "Step 5: Starting host-side component..." -ForegroundColor Cyan

if ($Test -eq "shared-folder") {
    $hostExe = Join-Path $ProjectRoot "SharedFolderHost\bin\Release\net8.0-windows\SharedFolderHost.exe"
    Write-Host "Starting: $hostExe"
    Write-Host ""
    Write-Host "=== Starting SharedFolderHost ===" -ForegroundColor Yellow
    Write-Host "This will run the host-side server."
    Write-Host "Press Ctrl+C to stop after sandbox test completes."
    Write-Host ""

    # Start host in new window so we can launch sandbox
    Start-Process -FilePath $hostExe -ArgumentList "$TestFolderRoot\Shared" -NoNewWindow -PassThru | Out-Null
    Start-Sleep -Seconds 2
} else {
    $hostExe = Join-Path $ProjectRoot "NamedPipeHostServer\bin\Release\net8.0-windows\NamedPipeHostServer.exe"
    Write-Host "Starting: $hostExe"
    Write-Host ""
    Write-Host "=== Starting NamedPipeHostServer ===" -ForegroundColor Yellow
    Write-Host "This will run the host-side pipe server."
    Write-Host "Press Ctrl+C to stop after sandbox test completes."
    Write-Host ""

    Start-Process -FilePath $hostExe -NoNewWindow -PassThru | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "[OK] Host component started" -ForegroundColor Green
Write-Host ""

# Step 6: Launch sandbox
Write-Host "Step 6: Launching Windows Sandbox..." -ForegroundColor Cyan

if ($Test -eq "shared-folder") {
    $wsbPath = Join-Path $ProjectRoot "test-shared-folder.wsb"
} else {
    $wsbPath = Join-Path $ProjectRoot "test-named-pipe.wsb"
}

Write-Host "Launching: $wsbPath"
Write-Host ""

# Launch sandbox
Start-Process -FilePath "WindowsSandbox.exe" -ArgumentList "`"$wsbPath`""

Write-Host "[OK] Sandbox launched!" -ForegroundColor Green
Write-Host ""
Write-Host "=== Sandbox is starting ===" -ForegroundColor Cyan
Write-Host "The sandbox will:"
Write-Host "  1. Boot up (takes ~10-15 seconds)"
Write-Host "  2. Run the LogonCommand to start the client"
Write-Host "  3. Client will communicate with host via $Test transport"
Write-Host ""
Write-Host "Watch the host window for results."
Write-Host "Results will also be saved to: $TestFolderRoot\Output\"
Write-Host ""
Write-Host "Press Enter to exit this script (host and sandbox will continue running)..."
Read-Host

Write-Host "Done! Check $TestFolderRoot\Output\ for test results." -ForegroundColor Green
