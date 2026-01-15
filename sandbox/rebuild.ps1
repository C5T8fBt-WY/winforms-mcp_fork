# rebuild.ps1
# Quick rebuild and deploy both Server and App to sandbox folders
# Run this from Windows after making code changes

param(
    [string]$TransportTestPath = "C:\TransportTest",
    [switch]$ServerOnly,
    [switch]$AppOnly
)

$ErrorActionPreference = "Stop"

$ServerPath = Join-Path $TransportTestPath "Server"
$AppPath = Join-Path $TransportTestPath "App"
$SharedPath = Join-Path $TransportTestPath "Shared"

$RepoRoot = Split-Path $PSScriptRoot -Parent
$ServerProject = Join-Path $RepoRoot "src\Rhombus.WinFormsMcp.Server\Rhombus.WinFormsMcp.Server.csproj"
$AppProject = Join-Path $RepoRoot "src\Rhombus.WinFormsMcp.TestApp\Rhombus.WinFormsMcp.TestApp.csproj"

Write-Host "=== Quick Rebuild ===" -ForegroundColor Cyan

# Build Server
if (!$AppOnly) {
    Write-Host ""
    Write-Host "Building MCP Server..." -ForegroundColor Yellow

    $tempBuild = Join-Path $ServerPath "_build_temp"
    if (Test-Path $tempBuild) { Remove-Item $tempBuild -Recurse -Force }

    dotnet publish $ServerProject -c Release -r win-x64 --no-self-contained -o $tempBuild --nologo -v q

    if ($LASTEXITCODE -eq 0) {
        Get-ChildItem $tempBuild | Copy-Item -Destination $ServerPath -Recurse -Force
        Remove-Item $tempBuild -Recurse -Force
        Write-Host "  Server deployed" -ForegroundColor Green

        # Trigger server reload
        "$(Get-Date -Format 'o')" | Out-File (Join-Path $SharedPath "server.trigger")
        Write-Host "  Reload triggered" -ForegroundColor Gray
    } else {
        Write-Host "  Server build FAILED!" -ForegroundColor Red
        exit 1
    }
}

# Build App
if (!$ServerOnly) {
    Write-Host ""
    Write-Host "Building Test App..." -ForegroundColor Yellow

    $tempBuild = Join-Path $AppPath "_build_temp"
    if (Test-Path $tempBuild) { Remove-Item $tempBuild -Recurse -Force }

    dotnet publish $AppProject -c Release -r win-x64 --no-self-contained -o $tempBuild --nologo -v q

    if ($LASTEXITCODE -eq 0) {
        Get-ChildItem $tempBuild | Copy-Item -Destination $AppPath -Recurse -Force
        Remove-Item $tempBuild -Recurse -Force
        Write-Host "  App deployed" -ForegroundColor Green

        # Trigger app reload
        "$(Get-Date -Format 'o')" | Out-File (Join-Path $SharedPath "app.trigger")
        Write-Host "  Reload triggered" -ForegroundColor Gray
    } else {
        Write-Host "  App build FAILED!" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "Done! Sandbox will reload automatically." -ForegroundColor Green
