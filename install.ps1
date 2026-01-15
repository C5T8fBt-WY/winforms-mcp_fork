# install.ps1
# Complete installation script for WinForms MCP with Windows Sandbox support
#
# This script:
# 1. Checks/enables Windows Sandbox feature (requires admin + reboot)
# 2. Creates directory structure at C:\TransportTest
# 3. Downloads .NET 8 runtime for sandbox
# 4. Builds and deploys MCP server and test app
# 5. Creates MCP bridge script for Claude Code
# 6. Configures Claude Code MCP settings
#
# Usage:
#   # Run as Administrator
#   .\install.ps1
#
#   # Skip specific steps
#   .\install.ps1 -SkipSandboxCheck    # Don't check/enable Windows Sandbox
#   .\install.ps1 -SkipClaudeConfig    # Don't configure Claude Code MCP
#   .\install.ps1 -Force               # Force re-download/rebuild everything

param(
    [string]$TransportTestPath = "C:\TransportTest",
    [switch]$SkipSandboxCheck,
    [switch]$SkipClaudeConfig,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  WinForms MCP Sandbox Installation" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

#region Check Admin
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "WARNING: Not running as Administrator" -ForegroundColor Yellow
    Write-Host "Some features (like enabling Windows Sandbox) require admin privileges."
    Write-Host ""
    $continue = Read-Host "Continue anyway? (y/N)"
    if ($continue -ne "y" -and $continue -ne "Y") {
        Write-Host "Run this script as Administrator for full installation."
        exit 1
    }
}
#endregion

#region Step 1: Windows Sandbox Check
if (-not $SkipSandboxCheck) {
    Write-Host "Step 1: Checking Windows Sandbox..." -ForegroundColor Yellow

    # Check Windows edition
    $edition = (Get-CimInstance Win32_OperatingSystem).Caption
    Write-Host "  Windows Edition: $edition"

    if ($edition -match "Home") {
        Write-Host ""
        Write-Host "  ERROR: Windows Sandbox requires Windows Pro, Enterprise, or Education." -ForegroundColor Red
        Write-Host "  Windows Home edition does NOT support Windows Sandbox."
        Write-Host ""
        Write-Host "  Options:"
        Write-Host "    1. Upgrade to Windows Pro"
        Write-Host "    2. Use direct MCP (without sandbox isolation)"
        Write-Host ""
        exit 1
    }

    # Check if sandbox feature is enabled
    $sandboxFeature = Get-WindowsOptionalFeature -Online -FeatureName "Containers-DisposableClientVM" -ErrorAction SilentlyContinue

    if ($null -eq $sandboxFeature) {
        Write-Host "  ERROR: Could not query Windows features. Run as Administrator." -ForegroundColor Red
        exit 1
    }

    if ($sandboxFeature.State -eq "Enabled") {
        Write-Host "  Windows Sandbox: ENABLED" -ForegroundColor Green
    } else {
        Write-Host "  Windows Sandbox: NOT ENABLED" -ForegroundColor Yellow
        Write-Host ""

        if (-not $isAdmin) {
            Write-Host "  Cannot enable Windows Sandbox without admin privileges." -ForegroundColor Red
            Write-Host "  Please run this script as Administrator."
            exit 1
        }

        $enable = Read-Host "  Enable Windows Sandbox now? (Y/n)"
        if ($enable -eq "" -or $enable -eq "y" -or $enable -eq "Y") {
            Write-Host ""
            Write-Host "  Enabling Windows Sandbox..." -ForegroundColor Yellow
            Enable-WindowsOptionalFeature -Online -FeatureName "Containers-DisposableClientVM" -All -NoRestart

            Write-Host ""
            Write-Host "  Windows Sandbox has been enabled." -ForegroundColor Green
            Write-Host ""
            Write-Host "  IMPORTANT: A REBOOT IS REQUIRED before using Windows Sandbox." -ForegroundColor Yellow
            Write-Host ""

            $reboot = Read-Host "  Reboot now? (Y/n)"
            if ($reboot -eq "" -or $reboot -eq "y" -or $reboot -eq "Y") {
                Write-Host ""
                Write-Host "  Rebooting in 10 seconds... (Ctrl+C to cancel)"
                Write-Host "  After reboot, run this script again to complete installation."
                Start-Sleep -Seconds 10
                Restart-Computer -Force
                exit 0
            } else {
                Write-Host ""
                Write-Host "  Please reboot manually, then run this script again."
                Write-Host "  Use: .\install.ps1 -SkipSandboxCheck"
                exit 0
            }
        } else {
            Write-Host "  Skipping Windows Sandbox enablement."
            Write-Host "  The sandbox features will not work until enabled."
        }
    }
    Write-Host ""
}
#endregion

#region Step 2: Run Existing Setup
Write-Host "Step 2: Setting up sandbox environment..." -ForegroundColor Yellow

$ScriptDir = $PSScriptRoot
$sandboxDir = Join-Path $ScriptDir "sandbox"
$setupScript = Join-Path $sandboxDir "setup.ps1"

if (Test-Path $setupScript) {
    Write-Host "  Running sandbox\setup.ps1..."
    Write-Host ""

    $setupArgs = @("-TransportTestPath", $TransportTestPath)
    if ($Force) { $setupArgs += "-Force" }

    & $setupScript @setupArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  Setup failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "  ERROR: sandbox\setup.ps1 not found at $setupScript" -ForegroundColor Red
    exit 1
}
Write-Host ""
#endregion

#region Step 3: Create MCP Bridge Script
Write-Host "Step 3: Creating MCP bridge script..." -ForegroundColor Yellow

$bridgeScript = Join-Path $TransportTestPath "mcp-bridge.ps1"
$SharedPath = Join-Path $TransportTestPath "Shared"

$bridgeContent = @'
# MCP Sandbox Bridge
# Bridges stdio (Claude Code MCP) to TCP (sandbox server)
# Handles JSON-RPC 2.0: requests (with id) expect responses, notifications (no id) don't
#
# Usage: Called automatically by Claude Code via MCP configuration
# The sandbox must be running with the MCP server listening on TCP

param(
    [int]$Port = 9999
)

$ErrorActionPreference = "SilentlyContinue"

# Read TCP IP from ready signal
$readySignal = "C:\TransportTest\Shared\mcp-ready.signal"
$ServerIP = $null

if (Test-Path $readySignal) {
    try {
        $signal = Get-Content $readySignal -Raw | ConvertFrom-Json
        $ServerIP = $signal.tcp_ip
    } catch {}
}

if ([string]::IsNullOrEmpty($ServerIP)) {
    # Fallback: try common sandbox IP ranges
    $ServerIP = "172.29.16.229"
}

try {
    $tcpClient = New-Object System.Net.Sockets.TcpClient
    $tcpClient.Connect($ServerIP, $Port)
    $tcpClient.ReceiveTimeout = 30000
    $tcpClient.SendTimeout = 30000
    $stream = $tcpClient.GetStream()
    $reader = New-Object System.IO.StreamReader($stream)
    $writer = New-Object System.IO.StreamWriter($stream)
    $writer.AutoFlush = $true

    # Keep connection alive and forward messages bidirectionally
    while ($tcpClient.Connected) {
        $line = [Console]::In.ReadLine()

        if ($null -eq $line) {
            break
        }

        if ($line.Length -gt 0) {
            # Forward to TCP server
            $writer.WriteLine($line)
            $writer.Flush()

            # Check if this is a notification (no "id" field) - notifications don't get responses
            $isNotification = $false
            try {
                $json = $line | ConvertFrom-Json -ErrorAction Stop
                if (-not ($json.PSObject.Properties.Name -contains "id")) {
                    $isNotification = $true
                }
            } catch {}

            # Only wait for response if this was a request (not a notification)
            if (-not $isNotification) {
                $response = $reader.ReadLine()
                if ($null -ne $response) {
                    [Console]::Out.WriteLine($response)
                    [Console]::Out.Flush()
                }
            }
        }
    }
}
catch {
    [Console]::Error.WriteLine("Bridge error: $_")
    exit 1
}
finally {
    if ($reader) { $reader.Dispose() }
    if ($writer) { $writer.Dispose() }
    if ($stream) { $stream.Dispose() }
    if ($tcpClient) { $tcpClient.Close() }
}
'@

Set-Content -Path $bridgeScript -Value $bridgeContent -Encoding UTF8
Write-Host "  Created: $bridgeScript" -ForegroundColor Green
Write-Host ""
#endregion

#region Step 4: Configure Claude Code MCP
if (-not $SkipClaudeConfig) {
    Write-Host "Step 4: Configuring Claude Code MCP..." -ForegroundColor Yellow

    # Find Claude config file
    $claudeConfigPaths = @(
        "$env:USERPROFILE\.claude.json",
        "$env:USERPROFILE\.claude\mcp.json"
    )

    $claudeConfig = $null
    $configPath = $null

    foreach ($path in $claudeConfigPaths) {
        if (Test-Path $path) {
            $configPath = $path
            try {
                $claudeConfig = Get-Content $path -Raw | ConvertFrom-Json
                break
            } catch {
                Write-Host "  Warning: Could not parse $path" -ForegroundColor Yellow
            }
        }
    }

    if ($null -eq $claudeConfig) {
        Write-Host "  Claude Code config not found. Creating new config..." -ForegroundColor Yellow
        $configPath = "$env:USERPROFILE\.claude\mcp.json"

        # Ensure directory exists
        $configDir = Split-Path $configPath -Parent
        if (!(Test-Path $configDir)) {
            New-Item -ItemType Directory -Force -Path $configDir | Out-Null
        }

        $claudeConfig = @{ mcpServers = @{} }
    }

    # Ensure mcpServers exists
    if (-not $claudeConfig.mcpServers) {
        $claudeConfig | Add-Member -NotePropertyName "mcpServers" -NotePropertyValue @{} -Force
    }

    # Add/update winforms-mcp configuration
    $mcpConfig = @{
        type = "stdio"
        command = "powershell.exe"
        args = @("-ExecutionPolicy", "Bypass", "-File", $bridgeScript)
        env = @{}
    }

    # Check if config already exists
    $existingConfig = $claudeConfig.mcpServers."winforms-mcp"
    if ($existingConfig) {
        Write-Host "  Existing winforms-mcp config found."
        $update = Read-Host "  Update to use sandbox bridge? (Y/n)"
        if ($update -eq "" -or $update -eq "y" -or $update -eq "Y") {
            $claudeConfig.mcpServers."winforms-mcp" = $mcpConfig
            Write-Host "  Updated winforms-mcp configuration" -ForegroundColor Green
        } else {
            Write-Host "  Keeping existing configuration"
        }
    } else {
        $claudeConfig.mcpServers | Add-Member -NotePropertyName "winforms-mcp" -NotePropertyValue $mcpConfig -Force
        Write-Host "  Added winforms-mcp configuration" -ForegroundColor Green
    }

    # Save config
    $claudeConfig | ConvertTo-Json -Depth 10 | Set-Content $configPath -Encoding UTF8
    Write-Host "  Saved to: $configPath" -ForegroundColor Green
    Write-Host ""
}
#endregion

#region Summary
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Installation Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Directory structure:"
Write-Host "  $TransportTestPath"
Write-Host "  +-- Server/          (MCP server binaries)"
Write-Host "  +-- App/             (Test app binaries)"
Write-Host "  +-- DotNet/          (.NET runtime)"
Write-Host "  +-- Shared/          (Communication folder)"
Write-Host "  +-- sandbox-dev.wsb  (Sandbox config)"
Write-Host "  +-- mcp-bridge.ps1   (Claude Code bridge)"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  1. Launch the sandbox (run as admin due to coreclr bug):"
Write-Host "     Start-Process powershell -Verb RunAs -ArgumentList '-Command', 'Start-Process ''$TransportTestPath\sandbox-dev.wsb''; exit'"
Write-Host ""
Write-Host "  2. Wait for sandbox to boot (~10-20 seconds)"
Write-Host "     Check: Get-Content $TransportTestPath\Shared\bootstrap.log -Tail 10"
Write-Host ""
Write-Host "  3. Restart Claude Code and run /mcp to reconnect"
Write-Host "     The winforms-mcp tools should show 43+ tools"
Write-Host ""
Write-Host "  4. (Optional) Start hot-reload watchers for development:"
Write-Host "     cd $ScriptDir\sandbox"
Write-Host "     .\watch-all.ps1"
Write-Host ""
Write-Host "For troubleshooting, see: docs\SANDBOX_DEVELOPMENT.md"
Write-Host ""
#endregion
