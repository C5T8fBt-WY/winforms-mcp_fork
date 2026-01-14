# Bootstrap script for Windows Sandbox MCP Server
# This script runs inside Windows Sandbox at logon.
#
# It:
# 1. Optionally installs Virtual Display Driver (for background automation)
# 2. Starts the MCP server
# 3. Signals readiness via mcp-ready.signal file
# 4. Monitors for shutdown.signal to gracefully exit

param(
    [string]$SharedFolder = "C:\Shared"
)

$ScriptPath = $PSScriptRoot
$LogPath = Join-Path $SharedFolder "bootstrap.log"
$ReadySignal = Join-Path $SharedFolder "mcp-ready.signal"
$ShutdownSignal = Join-Path $SharedFolder "shutdown.signal"

# Start logging
Start-Transcript -Path $LogPath -Append
Write-Output "=== Bootstrap starting at $(Get-Date) ==="
Write-Output "Script path: $ScriptPath"
Write-Output "Shared folder: $SharedFolder"

# 1. Install Virtual Display Driver (if available)
$VddPath = Join-Path $ScriptPath "vdd\vdd_driver.inf"
if (Test-Path $VddPath) {
    Write-Output "Installing Virtual Display Driver from $VddPath"
    try {
        $proc = Start-Process pnputil.exe -ArgumentList "/add-driver `"$VddPath`" /install" -Wait -PassThru
        Write-Output "Driver install exit code: $($proc.ExitCode)"
    } catch {
        Write-Error "Failed to install driver: $_"
    }
} else {
    Write-Output "VDD driver not found at $VddPath. Skipping."
}

# 2. Find and start MCP Server (or shared folder client for testing)
$McpExe = Join-Path $ScriptPath "Rhombus.WinFormsMcp.Server.exe"
$TestClient = Join-Path $ScriptPath "SharedFolderClient.exe"

$ServerProcess = $null

if (Test-Path $McpExe) {
    Write-Output "Starting MCP Server: $McpExe"
    try {
        # Start MCP server in background, passing shared folder path
        $ServerProcess = Start-Process $McpExe -ArgumentList $SharedFolder -PassThru
        Write-Output "MCP Server started with PID: $($ServerProcess.Id)"
    } catch {
        Write-Error "Failed to start MCP Server: $_"
    }
} elseif (Test-Path $TestClient) {
    Write-Output "MCP Server not found. Starting test client: $TestClient"
    try {
        $ServerProcess = Start-Process $TestClient -ArgumentList $SharedFolder -PassThru
        Write-Output "Test client started with PID: $($ServerProcess.Id)"
    } catch {
        Write-Error "Failed to start test client: $_"
    }
} else {
    Write-Error "No MCP Server or test client found in $ScriptPath"
    Write-Output "Expected: $McpExe or $TestClient"
    Stop-Transcript
    exit 1
}

# 3. Signal readiness to host
# Small delay to let server initialize
Start-Sleep -Seconds 2

$ReadyContent = @{
    timestamp = (Get-Date).ToString("o")
    hostname = $env:COMPUTERNAME
    server_pid = $ServerProcess.Id
    script_path = $ScriptPath
} | ConvertTo-Json

Write-Output "Writing ready signal to $ReadySignal"
$ReadyContent | Out-File -FilePath $ReadySignal -Encoding UTF8
Write-Output "Ready signal written."

# 4. Wait for shutdown signal or server exit
Write-Output "Monitoring for shutdown signal at $ShutdownSignal"

while ($true) {
    # Check if server exited
    if ($ServerProcess.HasExited) {
        Write-Output "Server process exited with code: $($ServerProcess.ExitCode)"
        break
    }

    # Check for shutdown signal from host
    if (Test-Path $ShutdownSignal) {
        Write-Output "Shutdown signal received. Stopping server..."
        try {
            $ServerProcess | Stop-Process -Force
            Write-Output "Server stopped."
        } catch {
            Write-Warning "Failed to stop server: $_"
        }
        Remove-Item $ShutdownSignal -Force -ErrorAction SilentlyContinue
        break
    }

    Start-Sleep -Milliseconds 500
}

# 5. Cleanup
Write-Output "=== Bootstrap finished at $(Get-Date) ==="
Stop-Transcript
