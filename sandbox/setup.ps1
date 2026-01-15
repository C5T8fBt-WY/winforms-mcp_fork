# setup.ps1
# Full initial setup for Windows Sandbox development environment
# Run this once before using the sandbox workflow
#
# Creates:
# - C:\WinFormsMcpSandboxWorkspace\Server - MCP server binaries (read-only in sandbox)
# - C:\WinFormsMcpSandboxWorkspace\App - Test app binaries (read-only in sandbox)
# - C:\WinFormsMcpSandboxWorkspace\DotNet - .NET runtime (read-only in sandbox)
# - C:\WinFormsMcpSandboxWorkspace\Shared - Communication folder (read-write in sandbox)

param(
    # Path can be overridden via parameter or WINFORMS_MCP_SANDBOX_PATH environment variable
    [string]$WorkspacePath = $(if ($env:WINFORMS_MCP_SANDBOX_PATH) { $env:WINFORMS_MCP_SANDBOX_PATH } else { "C:\WinFormsMcpSandboxWorkspace" }),
    [switch]$SkipDotNet,    # Skip .NET download (if already installed)
    [switch]$SkipBuild,     # Skip initial build (if already built)
    [switch]$Force          # Force re-download/rebuild
)

$ErrorActionPreference = "Stop"

Write-Host "=== Sandbox Development Setup ===" -ForegroundColor Cyan
Write-Host "Base path: $WorkspacePath"
Write-Host ""

# Define paths
$ServerPath = Join-Path $WorkspacePath "Server"
$AppPath = Join-Path $WorkspacePath "App"
$DotNetPath = Join-Path $WorkspacePath "DotNet"
$SharedPath = Join-Path $WorkspacePath "Shared"

# Project paths (relative to this script)
$ScriptDir = $PSScriptRoot
$RepoRoot = Split-Path $ScriptDir -Parent
$ServerProject = Join-Path $RepoRoot "src\Rhombus.WinFormsMcp.Server\Rhombus.WinFormsMcp.Server.csproj"
$AppProject = Join-Path $RepoRoot "src\Rhombus.WinFormsMcp.TestApp\Rhombus.WinFormsMcp.TestApp.csproj"

Write-Host "Script directory: $ScriptDir"
Write-Host "Repository root: $RepoRoot"
Write-Host ""

#region Create Directory Structure
Write-Host "Creating directory structure..." -ForegroundColor Yellow

$directories = @($ServerPath, $AppPath, $DotNetPath, $SharedPath)
foreach ($dir in $directories) {
    if (!(Test-Path $dir)) {
        Write-Host "  Creating: $dir"
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    } else {
        Write-Host "  Exists: $dir"
    }
}
Write-Host ""
#endregion

#region Setup .NET Runtime
if (!$SkipDotNet) {
    Write-Host "Setting up .NET runtime..." -ForegroundColor Yellow

    $dotnetExe = Join-Path $DotNetPath "dotnet.exe"
    if ((Test-Path $dotnetExe) -and !$Force) {
        Write-Host "  .NET runtime already installed"
        $version = & $dotnetExe --version 2>$null
        Write-Host "  Version: $version"
    } else {
        Write-Host "  Running setup-dotnet.ps1..."
        $setupDotNet = Join-Path $ScriptDir "setup-dotnet.ps1"
        if ($Force) {
            & $setupDotNet -DotNetPath $DotNetPath -Force
        } else {
            & $setupDotNet -DotNetPath $DotNetPath
        }
    }
    Write-Host ""
} else {
    Write-Host "Skipping .NET setup (use -SkipDotNet:$false to enable)" -ForegroundColor Gray
    Write-Host ""
}
#endregion

#region Build and Deploy Projects
if (!$SkipBuild) {
    Write-Host "Building and deploying projects..." -ForegroundColor Yellow

    # Build Server
    Write-Host ""
    Write-Host "Building MCP Server..." -ForegroundColor Yellow
    Write-Host "  Project: $ServerProject"

    if (!(Test-Path $ServerProject)) {
        Write-Host "  ERROR: Server project not found!" -ForegroundColor Red
    } else {
        $tempBuild = Join-Path $ServerPath "_build_temp"
        if (Test-Path $tempBuild) { Remove-Item $tempBuild -Recurse -Force }

        dotnet publish $ServerProject -c Release -r win-x64 --no-self-contained -o $tempBuild --nologo

        if ($LASTEXITCODE -eq 0) {
            # Copy to final location
            Get-ChildItem $tempBuild | Copy-Item -Destination $ServerPath -Recurse -Force
            Remove-Item $tempBuild -Recurse -Force
            Write-Host "  Server deployed to $ServerPath" -ForegroundColor Green
        } else {
            Write-Host "  Server build FAILED!" -ForegroundColor Red
        }
    }

    # Copy bootstrap.ps1 to Server folder
    $bootstrapSrc = Join-Path $ScriptDir "bootstrap.ps1"
    $bootstrapDest = Join-Path $ServerPath "bootstrap.ps1"
    if (Test-Path $bootstrapSrc) {
        Copy-Item $bootstrapSrc $bootstrapDest -Force
        Write-Host "  Copied bootstrap.ps1 to Server folder"
    }

    # Build App
    Write-Host ""
    Write-Host "Building Test App..." -ForegroundColor Yellow
    Write-Host "  Project: $AppProject"

    if (!(Test-Path $AppProject)) {
        Write-Host "  WARNING: App project not found, skipping" -ForegroundColor Yellow
    } else {
        $tempBuild = Join-Path $AppPath "_build_temp"
        if (Test-Path $tempBuild) { Remove-Item $tempBuild -Recurse -Force }

        dotnet publish $AppProject -c Release -r win-x64 --no-self-contained -o $tempBuild --nologo

        if ($LASTEXITCODE -eq 0) {
            # Copy to final location
            Get-ChildItem $tempBuild | Copy-Item -Destination $AppPath -Recurse -Force
            Remove-Item $tempBuild -Recurse -Force
            Write-Host "  App deployed to $AppPath" -ForegroundColor Green
        } else {
            Write-Host "  App build FAILED!" -ForegroundColor Red
        }
    }
    Write-Host ""
} else {
    Write-Host "Skipping build (use -SkipBuild:$false to enable)" -ForegroundColor Gray
    Write-Host ""
}
#endregion

#region Copy Sandbox Config
Write-Host "Copying sandbox configuration..." -ForegroundColor Yellow

$wsbSrc = Join-Path $ScriptDir "sandbox-dev.wsb"
$wsbDest = Join-Path $WorkspacePath "sandbox-dev.wsb"

if (Test-Path $wsbSrc) {
    Copy-Item $wsbSrc $wsbDest -Force
    Write-Host "  Copied sandbox-dev.wsb to $WorkspacePath"
} else {
    Write-Host "  WARNING: sandbox-dev.wsb not found in $ScriptDir" -ForegroundColor Yellow
}
Write-Host ""
#endregion

#region Summary
Write-Host "=== Setup Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Directory structure:"
Write-Host "  $WorkspacePath"
Write-Host "  +-- Server/        (MCP server binaries)"
Write-Host "  +-- App/           (Test app binaries)"
Write-Host "  +-- DotNet/        (.NET runtime)"
Write-Host "  +-- Shared/        (Communication folder)"
Write-Host "  +-- sandbox-dev.wsb"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Start the sandbox:      $wsbDest"
Write-Host "  2. Start the watchers:     .\watch-all.ps1"
Write-Host "     Or individually:"
Write-Host "       .\watch-dev.ps1      (MCP server)"
Write-Host "       .\watch-app.ps1      (Test app)"
Write-Host ""
Write-Host "The sandbox will hot-reload when you make code changes."
Write-Host ""
#endregion
