# Bootstrap script for Windows Sandbox MCP Server
# This script runs inside Windows Sandbox at logon.
#
# It:
# 1. Optionally installs Virtual Display Driver (for background automation)
# 2. Setup local execution directory (to allow host-side updates)
# 3. Starts the MCP server
# 4. Monitors for updates (update.trigger) and shutdown.signal

param(
    [string]$SharedFolder = "C:\Shared"
)

$ScriptPath = $PSScriptRoot
$LogPath = Join-Path $SharedFolder "bootstrap.log"
$ReadySignal = Join-Path $SharedFolder "mcp-ready.signal"
$ShutdownSignal = Join-Path $SharedFolder "shutdown.signal"
$UpdateTrigger = Join-Path $SharedFolder "update.trigger"
$LocalMcpDir = "C:\LocalMcp"

# Start logging
Start-Transcript -Path $LogPath -Append
Write-Output "=== Bootstrap starting at $(Get-Date) ==="
Write-Output "Script path: $ScriptPath"
Write-Output "Shared folder: $SharedFolder"
Write-Output "Local execution dir: $LocalMcpDir"

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

# Function to copy files and start the server
function Start-ServerInstance {
    Write-Output "Preparing to start server instance..."
    
    # 1. Create/Clean Local Directory
    if (!(Test-Path $LocalMcpDir)) {
        New-Item -ItemType Directory -Force -Path $LocalMcpDir | Out-Null
    }
    
    # 2. Copy files from ScriptPath (Source) to LocalDir
    # We exclude bootstrap.log and other runtime artifacts if they are in source
    Write-Output "Copying files from $ScriptPath to $LocalMcpDir..."
    Copy-Item "$ScriptPath\*" $LocalMcpDir -Recurse -Force -ErrorAction SilentlyContinue
    
    # 3. Determine Executable
    $McpExe = Join-Path $LocalMcpDir "C5T8fBtWY.WinFormsMcp.Server.exe"
    $TestClient = Join-Path $LocalMcpDir "SharedFolderClient.exe"
    
    $proc = $null
    
    if (Test-Path $McpExe) {
        Write-Output "Starting MCP Server: $McpExe"
        $proc = Start-Process $McpExe -ArgumentList $SharedFolder -PassThru
    } elseif (Test-Path $TestClient) {
        Write-Output "Starting Test Client: $TestClient"
        $proc = Start-Process $TestClient -ArgumentList $SharedFolder -PassThru
    } else {
        Write-Error "No executable found in $LocalMcpDir"
        return $null
    }
    
    # 4. Signal Ready
    Start-Sleep -Seconds 2
    $ReadyContent = @{
        timestamp = (Get-Date).ToString("o")
        hostname = $env:COMPUTERNAME
        server_pid = $proc.Id
        script_path = $ScriptPath
        local_path = $LocalMcpDir
    } | ConvertTo-Json
    
    $ReadyContent | Out-File -FilePath $ReadySignal -Encoding UTF8
    Write-Output "Server started (PID: $($proc.Id)) and ready signal written."
    
    return $proc
}

# Initial Start
$ServerProcess = Start-ServerInstance

if ($null -eq $ServerProcess) {
    Write-Error "Failed to start server."
    Stop-Transcript
    exit 1
}

# Monitor Loop
Write-Output "Entering monitor loop..."
Write-Output "  Shutdown: $ShutdownSignal"
Write-Output "  Update:   $UpdateTrigger"

while ($true) {
    # 1. Check for Shutdown
    if (Test-Path $ShutdownSignal) {
        Write-Output "Shutdown signal received."
        break
    }
    
    # 2. Check for Update
    if (Test-Path $UpdateTrigger) {
        Write-Output "Update trigger detected!"
        
        # Stop current process
        if (!$ServerProcess.HasExited) {
            Write-Output "Stopping current server (PID: $($ServerProcess.Id))..."
            Stop-Process -Id $ServerProcess.Id -Force -ErrorAction SilentlyContinue
            $ServerProcess.WaitForExit(2000)
        }
        
        # Remove trigger
        Remove-Item $UpdateTrigger -Force -ErrorAction SilentlyContinue
        
        # Restart
        Write-Output "Updating and restarting..."
        $ServerProcess = Start-ServerInstance
        
        if ($null -eq $ServerProcess) {
            Write-Error "Failed to restart server after update!"
        }
    }
    
    # 3. Check if process crashed (and not updating)
    if ($ServerProcess -ne $null -and $ServerProcess.HasExited) {
        # Silent wait for recovery via update
    }

    Start-Sleep -Milliseconds 500
}

# Cleanup on exit
if ($ServerProcess -ne $null -and !$ServerProcess.HasExited) {
    Stop-Process -Id $ServerProcess.Id -Force -ErrorAction SilentlyContinue
}
Remove-Item $ReadySignal -Force -ErrorAction SilentlyContinue
Remove-Item $ShutdownSignal -Force -ErrorAction SilentlyContinue

Write-Output "=== Bootstrap finished ==="
Stop-Transcript