# MCP Sandbox Bridge (Hybrid)
# Implements lifecycle tools locally, forwards automation tools to sandbox via TCP
#
# Bridge-handled tools:
#   - start_sandbox: Launch sandbox, wait for ready, start server, connect TCP
#   - stop_sandbox: Signal shutdown, disconnect TCP
#   - sandbox_status: Return current state
#
# All other tools are forwarded to the MCP server running inside the sandbox
#
# Usage: Called automatically by Claude Code via MCP configuration

param(
    [int]$Port = 9999
)

$ErrorActionPreference = "SilentlyContinue"

#region Configuration
$WorkspacePath = if ($env:WINFORMS_MCP_SANDBOX_PATH) { $env:WINFORMS_MCP_SANDBOX_PATH } else { "C:\WinFormsMcpSandboxWorkspace" }
$SharedPath = Join-Path $WorkspacePath "Shared"
$ReadySignalPath = Join-Path $SharedPath "mcp-ready.signal"
$ServerTriggerPath = Join-Path $SharedPath "server.trigger"
$ShutdownSignalPath = Join-Path $SharedPath "shutdown.signal"
$SandboxWsbPath = Join-Path $WorkspacePath "sandbox-dev.wsb"
#endregion

#region Global State
$global:TcpClient = $null
$global:TcpReader = $null
$global:TcpWriter = $null
$global:SandboxTools = @()  # Cached tool list from sandbox
$global:ServerCapabilities = $null
$global:ConnectedServerPid = $null  # Track which server PID we connected to
#endregion

#region Bridge Tool Definitions
$BridgeTools = @(
    @{
        name = "start_sandbox"
        description = "Start Windows Sandbox with the MCP server. Launches the sandbox, waits for it to boot, starts the MCP server, and establishes TCP connection."
        inputSchema = @{
            type = "object"
            properties = @{
                timeout_seconds = @{
                    type = "integer"
                    description = "Maximum time to wait for sandbox to be ready (default: 60)"
                    default = 60
                }
            }
        }
    }
    @{
        name = "stop_sandbox"
        description = "Signal the bootstrap to shut down and disconnect TCP. WARNING: User interaction will be required to start the sandbox again (admin prompt). To restart just the MCP server or app, use trigger files instead (server.trigger or app.trigger). Only use this to completely tear down the sandbox."
        inputSchema = @{
            type = "object"
            properties = @{
                confirm = @{
                    type = "boolean"
                    description = "Must be set to true to confirm shutdown. User interaction will be required to start sandbox again."
                }
            }
            required = @("confirm")
        }
    }
    @{
        name = "sandbox_status"
        description = "Get current sandbox and MCP server status."
        inputSchema = @{
            type = "object"
            properties = @{}
        }
    }
)
#endregion

#region Helper Functions
function Write-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format "HH:mm:ss"
    [Console]::Error.WriteLine("[$timestamp] $Message")
}

function Send-McpResponse {
    param($Id, $Result = $null, $Error = $null)

    $response = @{
        jsonrpc = "2.0"
        id = $Id
    }

    if ($null -ne $Error) {
        $response.error = $Error
    } else {
        $response.result = $Result
    }

    $json = $response | ConvertTo-Json -Depth 20 -Compress
    [Console]::Out.WriteLine($json)
    [Console]::Out.Flush()
}

function Send-McpError {
    param($Id, [int]$Code, [string]$Message)
    Send-McpResponse -Id $Id -Error @{ code = $Code; message = $Message }
}

function Get-ReadySignal {
    if (Test-Path $ReadySignalPath) {
        try {
            return Get-Content $ReadySignalPath -Raw | ConvertFrom-Json
        } catch {}
    }
    return $null
}

function Test-TcpConnected {
    return ($null -ne $global:TcpClient) -and $global:TcpClient.Connected
}

function Connect-Tcp {
    param([string]$ServerIP, [int]$ServerPid = 0)

    if (Test-TcpConnected) {
        return $true
    }

    try {
        $global:TcpClient = New-Object System.Net.Sockets.TcpClient
        $global:TcpClient.Connect($ServerIP, $Port)
        $global:TcpClient.ReceiveTimeout = 30000
        $global:TcpClient.SendTimeout = 30000
        $stream = $global:TcpClient.GetStream()
        $global:TcpReader = New-Object System.IO.StreamReader($stream)
        $global:TcpWriter = New-Object System.IO.StreamWriter($stream)
        $global:TcpWriter.AutoFlush = $true
        $global:ConnectedServerPid = $ServerPid

        Write-Log "TCP connected to ${ServerIP}:${Port} (server PID: $ServerPid)"
        return $true
    } catch {
        Write-Log "TCP connection failed: $_"
        Disconnect-Tcp
        return $false
    }
}

function Disconnect-Tcp {
    if ($global:TcpReader) { $global:TcpReader.Dispose(); $global:TcpReader = $null }
    if ($global:TcpWriter) { $global:TcpWriter.Dispose(); $global:TcpWriter = $null }
    if ($global:TcpClient) { $global:TcpClient.Close(); $global:TcpClient = $null }
    $global:SandboxTools = @()
    $global:ConnectedServerPid = $null
    Write-Log "TCP disconnected"
}

# Check if server was hot-reloaded and reconnect if needed
function Test-ServerReloadAndReconnect {
    $signal = Get-ReadySignal
    if (-not $signal -or -not $signal.tcp_ip -or -not $signal.server_pid) {
        return $false
    }

    # Case 1: Connected to different PID - server was hot-reloaded
    if ($global:ConnectedServerPid -and $signal.server_pid -ne $global:ConnectedServerPid) {
        Write-Log "Server hot-reloaded: PID changed from $($global:ConnectedServerPid) to $($signal.server_pid)"
        Disconnect-Tcp
        Start-Sleep -Milliseconds 500  # Give new server time to initialize
        if (Connect-Tcp $signal.tcp_ip $signal.server_pid) {
            $global:SandboxTools = Get-SandboxToolList
            Write-Log "Reconnected to hot-reloaded server ($($global:SandboxTools.Count) tools)"
            return $true
        }
        return $false
    }

    # Case 2: Not connected but server is running - reconnect
    if (-not (Test-TcpConnected) -and $signal.server_pid) {
        Write-Log "Not connected but server running (PID: $($signal.server_pid)), reconnecting..."
        if (Connect-Tcp $signal.tcp_ip $signal.server_pid) {
            $global:SandboxTools = Get-SandboxToolList
            Write-Log "Reconnected to server ($($global:SandboxTools.Count) tools)"
            return $true
        }
    }

    return $false
}

function Forward-ToSandbox {
    param([string]$JsonLine)

    # Check if server was hot-reloaded and reconnect if needed
    Test-ServerReloadAndReconnect | Out-Null

    if (-not (Test-TcpConnected)) {
        return $null
    }

    try {
        $global:TcpWriter.WriteLine($JsonLine)
        $global:TcpWriter.Flush()

        # Check if notification (no id)
        $request = $JsonLine | ConvertFrom-Json -ErrorAction Stop
        if (-not ($request.PSObject.Properties.Name -contains "id")) {
            return $null  # Notification, no response expected
        }

        # Wait for response
        $response = $global:TcpReader.ReadLine()
        return $response
    } catch {
        Write-Log "Forward error: $_"
        # Connection might be dead - check for hot-reload on next call
        Disconnect-Tcp
        return $null
    }
}

function Get-SandboxToolList {
    if (-not (Test-TcpConnected)) {
        return @()
    }

    # Send tools/list request to sandbox
    # Note: id must be numeric because server uses GetInt32()
    $request = @{
        jsonrpc = "2.0"
        id = 999999
        method = "tools/list"
        params = @{}
    } | ConvertTo-Json -Compress

    $response = Forward-ToSandbox $request
    if ($response) {
        try {
            $parsed = $response | ConvertFrom-Json
            if ($parsed.error) {
                Write-Log "Sandbox tools/list error: $($parsed.error.message)"
                return @()
            }
            if ($parsed.result -and $parsed.result.tools) {
                Write-Log "Got $($parsed.result.tools.Count) tools from sandbox"
                return $parsed.result.tools
            }
            Write-Log "Unexpected tools/list response format"
        } catch {
            Write-Log "Failed to parse tools/list response: $_"
        }
    } else {
        Write-Log "No response from sandbox for tools/list"
    }
    return @()
}
#endregion

#region Bridge Tool Handlers
function Invoke-StartSandbox {
    param($Arguments)

    $timeout = if ($Arguments.timeout_seconds) { $Arguments.timeout_seconds } else { 60 }
    $startTime = Get-Date

    # Check if already connected
    if (Test-TcpConnected) {
        return @{
            success = $true
            message = "Already connected to sandbox MCP server"
            status = (Invoke-SandboxStatus @{})
        }
    }

    # Check if sandbox WSB exists
    if (-not (Test-Path $SandboxWsbPath)) {
        return @{
            success = $false
            error = "Sandbox configuration not found at $SandboxWsbPath. Run install.ps1 first."
        }
    }

    # Check if sandbox might already be running (ready signal exists)
    # Do this BEFORE cleaning up signals!
    $signal = Get-ReadySignal
    if ($signal -and $signal.tcp_ip) {
        Write-Log "Found existing ready signal, checking connection..."
        if (Connect-Tcp $signal.tcp_ip $signal.server_pid) {
            # Try to get tool list to verify connection works
            $global:SandboxTools = Get-SandboxToolList
            if ($global:SandboxTools.Count -gt 0) {
                return @{
                    success = $true
                    message = "Connected to existing sandbox MCP server"
                    tcp_endpoint = "$($signal.tcp_ip):$Port"
                    tool_count = $global:SandboxTools.Count
                }
            }
            Disconnect-Tcp
        }
        # Stale signal, will be cleaned up below
    }

    # Clean up old signals before launching new sandbox
    Remove-Item $ReadySignalPath -Force -ErrorAction SilentlyContinue
    Remove-Item $ShutdownSignalPath -Force -ErrorAction SilentlyContinue

    # Launch sandbox (requires elevation due to coreclr bug)
    # Can't use -Verb RunAs directly on .wsb files, so launch elevated PowerShell
    Write-Log "Launching sandbox with elevation: $SandboxWsbPath"
    try {
        $escapedPath = $SandboxWsbPath -replace "'", "''"
        Start-Process powershell -Verb RunAs -ArgumentList "-Command", "Start-Process '$escapedPath'; exit"
    } catch {
        return @{
            success = $false
            error = "Failed to launch sandbox: $_. You may need to approve the admin prompt."
        }
    }

    # Wait for ready signal (sandbox booted)
    Write-Log "Waiting for sandbox to boot..."
    $signal = $null
    while (((Get-Date) - $startTime).TotalSeconds -lt $timeout) {
        Start-Sleep -Milliseconds 500
        $signal = Get-ReadySignal
        if ($signal -and $signal.tcp_ip) {
            Write-Log "Sandbox ready signal received"
            break
        }
    }

    if (-not $signal -or -not $signal.tcp_ip) {
        return @{
            success = $false
            error = "Timeout waiting for sandbox to boot (${timeout}s)"
        }
    }

    # Check if server is running (LazyStart mode means it might not be)
    # Create ONE trigger, then wait for server to start
    if (-not $signal.server_pid) {
        Write-Log "Server not started (LazyStart mode), creating trigger..."
        $serverTimeout = 30
        $serverStart = Get-Date

        # Create trigger once
        "start" | Out-File -FilePath $ServerTriggerPath -Encoding UTF8
        Write-Log "Created server.trigger, waiting for server to start..."

        # Wait for server to start (check every 500ms)
        while (((Get-Date) - $serverStart).TotalSeconds -lt $serverTimeout) {
            Start-Sleep -Milliseconds 500
            $signal = Get-ReadySignal
            if ($signal -and $signal.server_pid) {
                Write-Log "Server started (PID: $($signal.server_pid))"
                break
            }

            # Only recreate trigger if it's been more than 5 seconds and trigger file is gone
            $elapsed = ((Get-Date) - $serverStart).TotalSeconds
            if ($elapsed -gt 5 -and -not (Test-Path $ServerTriggerPath)) {
                Write-Log "Recreating trigger (elapsed: ${elapsed}s)..."
                "start" | Out-File -FilePath $ServerTriggerPath -Encoding UTF8
            }
        }

        if (-not $signal -or -not $signal.server_pid) {
            return @{
                success = $false
                error = "Timeout waiting for MCP server to start"
                sandbox_ip = $signal.tcp_ip
            }
        }
    }

    # Give server a moment to initialize TCP listener
    Start-Sleep -Seconds 1

    # Connect TCP
    Write-Log "Connecting to MCP server at $($signal.tcp_ip):$Port..."
    if (-not (Connect-Tcp $signal.tcp_ip $signal.server_pid)) {
        return @{
            success = $false
            error = "Failed to connect to MCP server TCP"
            sandbox_ip = $signal.tcp_ip
            server_pid = $signal.server_pid
        }
    }

    # Get tool list from sandbox
    $global:SandboxTools = Get-SandboxToolList

    $elapsed = [math]::Round(((Get-Date) - $startTime).TotalSeconds, 1)
    return @{
        success = $true
        message = "Sandbox started and connected"
        tcp_endpoint = "$($signal.tcp_ip):$Port"
        server_pid = $signal.server_pid
        tool_count = $global:SandboxTools.Count
        elapsed_seconds = $elapsed
    }
}

function Invoke-StopSandbox {
    param($Arguments)

    # Check confirmation
    if (-not $Arguments.confirm) {
        return @{
            success = $false
            error = "Confirmation required. Set confirm=true to proceed. WARNING: User interaction will be required to start sandbox again (admin prompt). To restart just the MCP server or app, use trigger files instead."
        }
    }

    # Disconnect TCP first
    $wasConnected = Test-TcpConnected
    Disconnect-Tcp

    # Signal shutdown
    if (Test-Path $SharedPath) {
        "shutdown" | Out-File -FilePath $ShutdownSignalPath -Encoding UTF8
        Write-Log "Shutdown signal sent"
    }

    return @{
        success = $true
        message = if ($wasConnected) { "Disconnected and shutdown signal sent" } else { "Shutdown signal sent (was not connected)" }
    }
}

function Invoke-SandboxStatus {
    param($Arguments)

    $signal = Get-ReadySignal
    $tcpConnected = Test-TcpConnected

    $status = @{
        workspace_path = $WorkspacePath
        workspace_exists = (Test-Path $WorkspacePath)
        sandbox_wsb_exists = (Test-Path $SandboxWsbPath)
        tcp_connected = $tcpConnected
        tcp_port = $Port
    }

    if ($signal) {
        $status.sandbox_booted = $true
        $status.sandbox_ip = $signal.tcp_ip
        $status.server_pid = $signal.server_pid
        $status.app_pid = $signal.app_pid
        $status.sandbox_hostname = $signal.hostname
    } else {
        $status.sandbox_booted = $false
    }

    if ($tcpConnected) {
        $status.sandbox_tool_count = $global:SandboxTools.Count
    }

    return $status
}

$BridgeToolHandlers = @{
    "start_sandbox" = { param($args) Invoke-StartSandbox $args }
    "stop_sandbox" = { param($args) Invoke-StopSandbox $args }
    "sandbox_status" = { param($args) Invoke-SandboxStatus $args }
}
#endregion

#region MCP Protocol Handlers
function Handle-Initialize {
    param($Request)

    $result = @{
        protocolVersion = "2024-11-05"
        capabilities = @{
            tools = @{}
        }
        serverInfo = @{
            name = "winforms-mcp-bridge"
            version = "1.0.0"
        }
    }

    Send-McpResponse -Id $Request.id -Result $result
}

function Handle-ToolsList {
    param($Request)

    # Combine bridge tools with sandbox tools
    $allTools = @()
    $allTools += $BridgeTools

    if (Test-TcpConnected) {
        # Refresh sandbox tools
        $global:SandboxTools = Get-SandboxToolList
        $allTools += $global:SandboxTools
    }

    Send-McpResponse -Id $Request.id -Result @{ tools = $allTools }
}

function Handle-ToolsCall {
    param($Request)

    $toolName = $Request.params.name
    $arguments = $Request.params.arguments
    if ($null -eq $arguments) { $arguments = @{} }

    # Check if this is a bridge tool
    if ($BridgeToolHandlers.ContainsKey($toolName)) {
        Write-Log "Handling bridge tool: $toolName"
        try {
            $result = & $BridgeToolHandlers[$toolName] $arguments
            Send-McpResponse -Id $Request.id -Result @{
                content = @(@{
                    type = "text"
                    text = ($result | ConvertTo-Json -Depth 10)
                })
            }
        } catch {
            Send-McpError -Id $Request.id -Code -32603 -Message "Bridge tool error: $_"
        }
        return
    }

    # Forward to sandbox
    if (-not (Test-TcpConnected)) {
        Send-McpError -Id $Request.id -Code -32603 -Message "Not connected to sandbox. Call 'start_sandbox' first."
        return
    }

    Write-Log "Forwarding to sandbox: $toolName"
    $requestJson = $Request | ConvertTo-Json -Depth 20 -Compress
    $responseJson = Forward-ToSandbox $requestJson

    if ($null -eq $responseJson) {
        Send-McpError -Id $Request.id -Code -32603 -Message "No response from sandbox server"
        return
    }

    # Forward response directly
    [Console]::Out.WriteLine($responseJson)
    [Console]::Out.Flush()
}
#endregion

#region Main Loop
Write-Log "MCP Sandbox Bridge starting..."
Write-Log "Workspace: $WorkspacePath"

# Try to connect to existing sandbox
$signal = Get-ReadySignal
if ($signal -and $signal.tcp_ip -and $signal.server_pid) {
    Write-Log "Found existing ready signal, attempting connection..."
    if (Connect-Tcp $signal.tcp_ip $signal.server_pid) {
        $global:SandboxTools = Get-SandboxToolList
        Write-Log "Connected to existing sandbox ($($global:SandboxTools.Count) tools)"
    }
}

try {
    while ($true) {
        $line = [Console]::In.ReadLine()

        if ($null -eq $line) {
            Write-Log "EOF received, exiting"
            break
        }

        if ($line.Length -eq 0) {
            continue
        }

        try {
            $request = $line | ConvertFrom-Json -ErrorAction Stop
        } catch {
            Write-Log "Failed to parse JSON: $line"
            continue
        }

        # Check if notification (no id)
        $isNotification = -not ($request.PSObject.Properties.Name -contains "id")

        switch ($request.method) {
            "initialize" {
                Handle-Initialize $request
            }
            "initialized" {
                # Notification, no response needed
                Write-Log "Client initialized"
            }
            "tools/list" {
                Handle-ToolsList $request
            }
            "tools/call" {
                Handle-ToolsCall $request
            }
            default {
                if ($isNotification) {
                    # Forward notification to sandbox if connected
                    if (Test-TcpConnected) {
                        Forward-ToSandbox $line | Out-Null
                    }
                } else {
                    # Unknown method
                    Send-McpError -Id $request.id -Code -32601 -Message "Method not found: $($request.method)"
                }
            }
        }
    }
} finally {
    Disconnect-Tcp
    Write-Log "Bridge shutdown"
}
#endregion
