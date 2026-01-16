# Bootstrap script for Windows Sandbox MCP Server
# This script runs inside Windows Sandbox at logon.
#
# Features:
# - Windows Job Object with KillOnJobClose (auto-cleanup of child processes)
# - FileSystemWatcher for instant trigger detection + 20s fallback poll
# - PID tracking for precise hot-reload control
# - Crash logging with exit codes
#
# It:
# 1. Creates Job Object for subprocess management
# 2. Copies and starts the MCP server and test app
# 3. Monitors for updates (server.trigger, app.trigger) and shutdown.signal
# 4. Hot-reloads processes when triggers are detected

param(
    [string]$SharedFolder = "C:\Shared",
    [switch]$EnableTcp,
    [int]$TcpPort = 9999,
    [switch]$LazyStart  # Skip auto-starting server/app - use triggers instead
)

$ErrorActionPreference = "Stop"

#region WDAC Bypass via Certificate Trust
# Windows Sandbox enforces WDAC (Windows Defender Application Control)
# Import our self-signed code signing certificate to trusted root
# This allows our signed binaries to run

$CertPath = "C:\Server\SandboxTrust.cer"
if (Test-Path $CertPath) {
    try {
        Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\LocalMachine\Root -ErrorAction Stop | Out-Null
        Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\LocalMachine\TrustedPublisher -ErrorAction Stop | Out-Null
        Write-Host "Certificate imported to trusted stores" -ForegroundColor Green
    } catch {
        Write-Host "WARNING: Could not import certificate: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "WARNING: Certificate not found at $CertPath" -ForegroundColor Yellow
}

# Bypass execution policy for this process
Set-ExecutionPolicy Bypass -Scope Process -Force
#endregion

# Path configuration
$ServerSource = "C:\Server"           # Read-only mapped folder with server binaries
$AppSource = "C:\App"                 # Read-only mapped folder with app binaries
$LocalServerDir = "C:\LocalServer"    # Writable execution directory for server
$LocalAppDir = "C:\LocalApp"          # Writable execution directory for app

$LogPath = Join-Path $SharedFolder "bootstrap.log"
$ReadySignal = Join-Path $SharedFolder "mcp-ready.signal"
$ShutdownSignal = Join-Path $SharedFolder "shutdown.signal"
$ServerTrigger = Join-Path $SharedFolder "server.trigger"
$AppTrigger = Join-Path $SharedFolder "app.trigger"

# PID tracking
$global:ServerPid = $null
$global:AppPid = $null
$global:ServerProcess = $null
$global:AppProcess = $null
$global:CurrentAppName = $null  # Track which app is running for hot-reload

# Version for tracking deployments
$BootstrapVersion = "v7.0-smart-app-reload"

# Start logging
Start-Transcript -Path $LogPath -Append
Write-Output "=== Bootstrap $BootstrapVersion starting at $(Get-Date) ==="
Write-Output "Server source: $ServerSource"
Write-Output "App source: $AppSource"
Write-Output "Local server dir: $LocalServerDir"
Write-Output "Local app dir: $LocalAppDir"
Write-Output "Shared folder: $SharedFolder"

#region Job Object P/Invoke
# Windows Job Object for automatic subprocess cleanup
# When bootstrap exits, all child processes are automatically killed

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public class JobObject : IDisposable
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

    [DllImport("kernel32.dll")]
    static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll")]
    static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    const int JobObjectExtendedLimitInformation = 9;
    const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    private IntPtr _handle;
    private bool _disposed;

    public JobObject()
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero)
            throw new Exception("Failed to create job object");

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ptr, (uint)size))
                throw new Exception("Failed to set job object information");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public bool AddProcess(IntPtr processHandle)
    {
        return AssignProcessToJobObject(_handle, processHandle);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
            _disposed = true;
        }
    }
}
"@

# Create Job Object at startup
$global:JobObject = New-Object JobObject
Write-Output "Job Object created (KillOnJobClose enabled)"
#endregion

#region Process Management Functions
function Start-ServerProcess {
    Write-Output "Starting server process..."

    # Create/clean local directory
    if (!(Test-Path $LocalServerDir)) {
        New-Item -ItemType Directory -Force -Path $LocalServerDir | Out-Null
    }

    # Copy files from mapped (read-only) source to local writable folder
    Write-Output "Copying files from $ServerSource to $LocalServerDir..."
    Copy-Item "$ServerSource\*" $LocalServerDir -Recurse -Force -ErrorAction SilentlyContinue

    # Unblock all files to remove Zone.Identifier ADS (prevents WDAC blocking)
    # This MUST be done inside the sandbox on the copied files
    Write-Output "Unblocking files in $LocalServerDir..."
    Get-ChildItem -Path $LocalServerDir -Recurse | Unblock-File -ErrorAction SilentlyContinue

    # Find the DLL to run (use dotnet.exe to bypass WDAC on unsigned exe)
    # WDAC trusts Microsoft-signed dotnet.exe, which can then load our unsigned DLL
    $McpDll = Join-Path $LocalServerDir "Rhombus.WinFormsMcp.Server.dll"
    $McpExe = Join-Path $LocalServerDir "Rhombus.WinFormsMcp.Server.exe"

    if (!(Test-Path $McpDll)) {
        Write-Warning "Server DLL not found at $McpDll"
        return $null
    }

    # Build arguments for dotnet.exe
    $mcpArgs = @($McpDll)
    if ($EnableTcp) {
        $mcpArgs += "--tcp"
        $mcpArgs += $TcpPort.ToString()
        Write-Output "Starting in TCP mode on port $TcpPort"

        # Add firewall rule to allow TCP port (avoids Windows Firewall prompt)
        # Use netsh instead of New-NetFirewallRule because CIM isn't available in sandbox
        $ruleName = "MCP Server TCP $TcpPort"
        Write-Output "Creating firewall rule for port $TcpPort..."
        netsh advfirewall firewall add rule name="$ruleName" dir=in action=allow protocol=tcp localport=$TcpPort 2>$null | Out-Null
    }

    # Use dotnet.exe (trusted by WDAC) to run our DLL instead of exe
    $dotnetExe = "C:\DotNet\dotnet.exe"
    Write-Output "Starting via dotnet: $dotnetExe $($mcpArgs -join ' ')"
    $proc = Start-Process $dotnetExe -ArgumentList $mcpArgs -PassThru

    # Add to Job Object
    if ($global:JobObject.AddProcess($proc.Handle)) {
        Write-Output "Server process (PID: $($proc.Id)) added to Job Object"
    } else {
        Write-Warning "Failed to add server to Job Object"
    }

    $global:ServerPid = $proc.Id
    $global:ServerProcess = $proc

    return $proc
}

function Start-AppProcess {
    param(
        [string]$AppName = ""  # Optional: specific app to start (e.g., "MagpieCad")
    )

    Write-Output "Starting app process..."

    # Check if app source exists
    if (!(Test-Path $AppSource)) {
        Write-Output "App source not found at $AppSource, skipping app start"
        return $null
    }

    # Create/clean local directory
    if (!(Test-Path $LocalAppDir)) {
        New-Item -ItemType Directory -Force -Path $LocalAppDir | Out-Null
    }

    # Copy files from source
    Write-Output "Copying files from $AppSource to $LocalAppDir..."
    Copy-Item "$AppSource\*" $LocalAppDir -Recurse -Force -ErrorAction SilentlyContinue

    # Unblock all files to remove Zone.Identifier ADS (prevents WDAC blocking)
    Write-Output "Unblocking files in $LocalAppDir..."
    Get-ChildItem -Path $LocalAppDir -Recurse | Unblock-File -ErrorAction SilentlyContinue

    # Find the DLL to run
    $AppDll = $null

    if ($AppName) {
        # Look for specific app DLL
        Write-Output "Looking for app: $AppName"

        # Try common locations: root, publish folder, bin folders
        $searchPaths = @(
            (Join-Path $LocalAppDir "$AppName.dll"),
            (Join-Path $LocalAppDir "publish\$AppName.dll"),
            (Join-Path $LocalAppDir "$AppName\bin\Release\net8.0-windows\win-x64\$AppName.dll"),
            (Join-Path $LocalAppDir "$AppName\bin\Debug\net8.0-windows\$AppName.dll")
        )

        foreach ($path in $searchPaths) {
            if (Test-Path $path) {
                $AppDll = $path
                break
            }
        }

        if (!$AppDll) {
            # Search recursively as last resort
            $found = Get-ChildItem -Path $LocalAppDir -Filter "$AppName.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($found) {
                $AppDll = $found.FullName
            }
        }
    }

    # Fall back to test app if no specific app found or requested
    if (!$AppDll) {
        $AppDll = Join-Path $LocalAppDir "Rhombus.WinFormsMcp.TestApp.dll"
        if ($AppName) {
            Write-Warning "Could not find $AppName.dll, falling back to TestApp"
        }
    }

    if (!(Test-Path $AppDll)) {
        Write-Warning "App DLL not found at $AppDll"
        return $null
    }

    # Track which app we're running for hot-reload
    $global:CurrentAppName = [System.IO.Path]::GetFileNameWithoutExtension($AppDll)

    # Use dotnet.exe (trusted by WDAC) to run our DLL instead of exe
    $dotnetExe = "C:\DotNet\dotnet.exe"
    Write-Output "Starting via dotnet: $dotnetExe $AppDll"
    $proc = Start-Process $dotnetExe -ArgumentList $AppDll -PassThru

    # Add to Job Object
    if ($global:JobObject.AddProcess($proc.Handle)) {
        Write-Output "App process (PID: $($proc.Id)) added to Job Object"
    } else {
        Write-Warning "Failed to add app to Job Object"
    }

    $global:AppPid = $proc.Id
    $global:AppProcess = $proc

    return $proc
}

function Stop-ServerProcess {
    if ($global:ServerProcess -and !$global:ServerProcess.HasExited) {
        Write-Output "Stopping server (PID: $global:ServerPid)..."
        Stop-Process -Id $global:ServerPid -Force -ErrorAction SilentlyContinue
        $global:ServerProcess.WaitForExit(2000)
    }
    $global:ServerPid = $null
    $global:ServerProcess = $null
}

function Stop-AppProcess {
    if ($global:AppProcess -and !$global:AppProcess.HasExited) {
        Write-Output "Stopping app (PID: $global:AppPid)..."
        Stop-Process -Id $global:AppPid -Force -ErrorAction SilentlyContinue
        $global:AppProcess.WaitForExit(2000)
    }
    $global:AppPid = $null
    $global:AppProcess = $null
}

function Update-ReadySignal {
    # Get sandbox IP address for TCP connections
    $sandboxIp = $null
    if ($EnableTcp) {
        try {
            # Try Get-NetIPAddress first
            $sandboxIp = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object { $_.IPAddress -notlike "127.*" } | Select-Object -First 1).IPAddress
        } catch {
            Write-Output "Get-NetIPAddress failed, trying fallback method..."
            try {
                # Fallback: use ipconfig parsing
                $ipconfig = ipconfig | Select-String "IPv4 Address" | Select-Object -First 1
                if ($ipconfig) {
                    $sandboxIp = ($ipconfig -split ": ")[1].Trim()
                }
            } catch {
                Write-Output "ipconfig fallback also failed"
            }
        }

        if (-not $sandboxIp) {
            # Last resort: Windows Sandbox typically uses 172.x addresses
            Write-Output "Could not detect IP, using hostname for TCP"
            $sandboxIp = $env:COMPUTERNAME
        }
    }

    # Update the ready signal with current PIDs and TCP info
    $ReadyContent = @{
        timestamp = (Get-Date).ToString("o")
        hostname = $env:COMPUTERNAME
        server_pid = $global:ServerPid
        app_pid = $global:AppPid
        server_dir = $LocalServerDir
        app_dir = $LocalAppDir
        tcp_enabled = $EnableTcp.IsPresent
        tcp_port = if ($EnableTcp) { $TcpPort } else { $null }
        tcp_ip = $sandboxIp
    } | ConvertTo-Json

    $ReadyContent | Out-File -FilePath $ReadySignal -Encoding UTF8
    Write-Output "Ready signal updated (Server: $global:ServerPid, App: $global:AppPid)"
    if ($EnableTcp) {
        Write-Output "TCP endpoint: ${sandboxIp}:${TcpPort}"
    }
}

function Handle-Trigger {
    param([string]$TriggerPath)

    $triggerName = [System.IO.Path]::GetFileName($TriggerPath)
    Write-Output "[$(Get-Date -Format 'HH:mm:ss')] Handling trigger: $triggerName"

    # Read trigger content before deleting (may contain app name or other params)
    $triggerContent = ""
    if (Test-Path $TriggerPath) {
        $triggerContent = (Get-Content $TriggerPath -Raw -ErrorAction SilentlyContinue).Trim()
    }

    # Delete trigger file
    Remove-Item $TriggerPath -Force -ErrorAction SilentlyContinue

    switch -Regex ($triggerName) {
        "server\.trigger" {
            $wasRunning = $null -ne $global:ServerProcess
            Stop-ServerProcess
            Start-Sleep -Milliseconds 500
            $proc = Start-ServerProcess
            if ($proc) {
                $action = if ($wasRunning) { "restarted" } else { "started" }
                Write-Output "[$(Get-Date -Format 'HH:mm:ss')] Server $action (PID: $($proc.Id))"
                Update-ReadySignal
            } else {
                Write-Warning "[$(Get-Date -Format 'HH:mm:ss')] Server start FAILED"
            }
        }
        "app\.trigger" {
            $wasRunning = $null -ne $global:AppProcess
            Stop-AppProcess
            Start-Sleep -Milliseconds 500

            # Determine which app to start:
            # 1. If trigger content specifies an app name (not "restart" or timestamp), use it
            # 2. Read from MCP server's state file (written by launch_app tool)
            # 3. If restarting (wasRunning), use the previously running app
            # 4. Otherwise, fall back to default (TestApp)
            $appToStart = ""
            $stateFile = Join-Path $SharedFolder "current_app.state"

            if ($triggerContent -and $triggerContent -notmatch '^\d{4}-\d{2}-\d{2}' -and $triggerContent -ne "restart" -and $triggerContent -ne "start") {
                $appToStart = $triggerContent
                Write-Output "Trigger specified app: $appToStart"
            } elseif (Test-Path $stateFile) {
                $appToStart = (Get-Content $stateFile -Raw -ErrorAction SilentlyContinue).Trim()
                Write-Output "Using app from MCP state file: $appToStart"
            } elseif ($wasRunning -and $global:CurrentAppName) {
                $appToStart = $global:CurrentAppName
                Write-Output "Hot-reloading previously running app: $appToStart"
            }

            $proc = Start-AppProcess -AppName $appToStart
            if ($proc) {
                $action = if ($wasRunning) { "restarted" } else { "started" }
                Write-Output "[$(Get-Date -Format 'HH:mm:ss')] App $action (PID: $($proc.Id))"
                Update-ReadySignal
            } else {
                Write-Warning "[$(Get-Date -Format 'HH:mm:ss')] App start FAILED"
            }
        }
    }
}

function Check-ProcessCrash {
    # Check server
    if ($global:ServerProcess -and $global:ServerProcess.HasExited) {
        $exitCode = $global:ServerProcess.ExitCode
        Write-Warning "[$(Get-Date -Format 'HH:mm:ss')] Server crashed! Exit code: $exitCode (PID was: $global:ServerPid)"
        $global:ServerPid = $null
        $global:ServerProcess = $null
    }

    # Check app
    if ($global:AppProcess -and $global:AppProcess.HasExited) {
        $exitCode = $global:AppProcess.ExitCode
        Write-Warning "[$(Get-Date -Format 'HH:mm:ss')] App crashed! Exit code: $exitCode (PID was: $global:AppPid)"
        $global:AppPid = $null
        $global:AppProcess = $null
    }
}
#endregion

#region Initial Startup
if ($LazyStart) {
    Write-Output "LazyStart mode - skipping automatic process startup"
    Write-Output "Use server.trigger and app.trigger to start processes"
} else {
    # Start server
    $ServerProcess = Start-ServerProcess

    if ($null -eq $ServerProcess) {
        Write-Error "Failed to start server"
        Stop-Transcript
        exit 1
    }

    # Start app (if available)
    $AppProcess = Start-AppProcess

    # Wait for processes to initialize
    Start-Sleep -Seconds 2
}

# Signal ready (even with null PIDs in LazyStart mode)
Update-ReadySignal
Write-Output "  Server PID: $global:ServerPid"
Write-Output "  App PID: $global:AppPid"
if ($EnableTcp) {
    Write-Output "  TCP Mode: Enabled on port $TcpPort"
}
Write-Output "Bootstrap ready - monitoring for triggers..."
#endregion

#region FileSystemWatcher + Fallback Poll
Write-Output "Setting up trigger monitoring..."
Write-Output "  Watching: $SharedFolder"
Write-Output "  Triggers: server.trigger, app.trigger"
Write-Output "  Shutdown: shutdown.signal"

# FileSystemWatcher for instant trigger detection
# Note: FSW over mapped folders (9P protocol) is unreliable in Windows Sandbox
# These settings maximize our chances of catching events
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $SharedFolder
$watcher.Filter = "*.trigger"
$watcher.InternalBufferSize = 51200  # 50KB - recommended max for network/mapped paths
$watcher.NotifyFilter = [System.IO.NotifyFilters]::FileName -bor `
                        [System.IO.NotifyFilters]::LastWrite -bor `
                        [System.IO.NotifyFilters]::CreationTime
$watcher.EnableRaisingEvents = $true

$triggerAction = {
    $path = $Event.SourceEventArgs.FullPath
    Write-Output "FSW: Trigger detected - $path"
    Handle-Trigger -TriggerPath $path
}

Register-ObjectEvent $watcher "Created" -Action $triggerAction | Out-Null
Register-ObjectEvent $watcher "Changed" -Action $triggerAction | Out-Null
Register-ObjectEvent $watcher "Renamed" -Action $triggerAction | Out-Null  # For atomic rename pattern

Write-Output "FileSystemWatcher registered (Created, Changed, Renamed events)"

# Main loop with polling
# FSW doesn't work over 9P mapped folders in Windows Sandbox, so we poll at 500ms
$lastPollTime = Get-Date
$pollIntervalMs = 500

Write-Output "Entering monitor loop (500ms polling)..."

while ($true) {
    # 1. Check for shutdown signal
    if (Test-Path $ShutdownSignal) {
        Write-Output "Shutdown signal received"
        break
    }

    # 2. Check for crashed processes
    Check-ProcessCrash

    # 3. Poll for triggers
    $now = Get-Date
    if (($now - $lastPollTime).TotalMilliseconds -ge $pollIntervalMs) {
        # Check for triggers
        if (Test-Path $ServerTrigger) {
            Write-Output "Poll: Server trigger found"
            Handle-Trigger -TriggerPath $ServerTrigger
        }
        if (Test-Path $AppTrigger) {
            Write-Output "Poll: App trigger found"
            Handle-Trigger -TriggerPath $AppTrigger
        }
        $lastPollTime = $now
    }

    Start-Sleep -Milliseconds 500
}
#endregion

#region Cleanup
Write-Output "Cleaning up..."

# Stop processes (Job Object will also kill them, but explicit stop is cleaner)
Stop-ServerProcess
Stop-AppProcess

# Dispose Job Object (this will also kill any remaining child processes)
if ($global:JobObject) {
    $global:JobObject.Dispose()
    Write-Output "Job Object disposed"
}

# Clean up signal files
Remove-Item $ReadySignal -Force -ErrorAction SilentlyContinue
Remove-Item $ShutdownSignal -Force -ErrorAction SilentlyContinue

# Unregister events
Get-EventSubscriber | Unregister-Event -ErrorAction SilentlyContinue

Write-Output "=== Bootstrap finished at $(Get-Date) ==="
Stop-Transcript
#endregion
