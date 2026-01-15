# setup-dotnet.ps1
# One-time .NET runtime download for Windows Sandbox development
# Downloads .NET 8 runtime to C:\WinFormsMcpSandboxWorkspace\DotNet for mapping into sandbox

param(
    [string]$DotNetPath = "C:\WinFormsMcpSandboxWorkspace\DotNet",
    [string]$DotNetVersion = "8.0",
    [switch]$Force  # Force re-download even if already exists
)

$ErrorActionPreference = "Stop"

Write-Host "=== .NET Runtime Setup for Sandbox ===" -ForegroundColor Cyan
Write-Host "Target: $DotNetPath"
Write-Host "Version: $DotNetVersion"
Write-Host ""

# Check if already installed
$dotnetExe = Join-Path $DotNetPath "dotnet.exe"
if ((Test-Path $dotnetExe) -and -not $Force) {
    Write-Host ".NET runtime already exists at $DotNetPath" -ForegroundColor Green
    Write-Host "Use -Force to re-download"

    # Show version
    $version = & $dotnetExe --version 2>$null
    if ($version) {
        Write-Host "Installed version: $version"
    }
    exit 0
}

# Create directory
if (!(Test-Path $DotNetPath)) {
    Write-Host "Creating directory: $DotNetPath"
    New-Item -ItemType Directory -Force -Path $DotNetPath | Out-Null
}

# Download dotnet-install.ps1
$installScript = Join-Path $env:TEMP "dotnet-install.ps1"
Write-Host "Downloading dotnet-install.ps1..." -ForegroundColor Yellow

try {
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript
} catch {
    Write-Host "Failed to download install script: $_" -ForegroundColor Red
    exit 1
}

# Install .NET runtime (not SDK - smaller footprint)
Write-Host "Installing .NET $DotNetVersion runtime..." -ForegroundColor Yellow
Write-Host "This may take a few minutes..."

try {
    # Install the Windows Desktop runtime (includes WinForms/WPF support)
    # Use 'windowsdesktop' instead of 'dotnet' for WinForms apps
    & $installScript -InstallDir $DotNetPath -Runtime windowsdesktop -Channel $DotNetVersion -NoPath

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Installation failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "Installation failed: $_" -ForegroundColor Red
    exit 1
}

# Verify installation
if (Test-Path $dotnetExe) {
    Write-Host ""
    Write-Host "=== Installation Successful ===" -ForegroundColor Green
    $version = & $dotnetExe --version 2>$null
    Write-Host "Installed: $version"
    Write-Host "Location: $DotNetPath"
    Write-Host ""
    Write-Host "To use in sandbox, map this folder read-only:"
    Write-Host "  Host: $DotNetPath"
    Write-Host "  Sandbox: C:\DotNet"
} else {
    Write-Host "Installation completed but dotnet.exe not found!" -ForegroundColor Red
    exit 1
}

# Cleanup
Remove-Item $installScript -Force -ErrorAction SilentlyContinue
