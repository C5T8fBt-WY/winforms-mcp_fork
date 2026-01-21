# MCP Sandbox Bridge (Hybrid)
# Auto-launches sandbox on startup, forwards automation tools via TCP
#
# Bridge-handled tools:
#   - start_sandbox: Launch sandbox, wait for boot, connect (called at startup)
#   - reconnect_sandbox: Reconnect if connection dropped (sandbox must be running)
#   - sandbox_status: Return current connection state (read-only)
#
# All other tools are forwarded to the MCP server running inside the sandbox
#
# Sandbox lifecycle:
#   - AUTO-LAUNCH: Bridge calls start_sandbox on startup (requires UAC prompt)
#   - NO STOP: Agent cannot stop the sandbox - only user can close it manually
#   - This prevents agents from accidentally terminating the sandbox environment
#
# Usage: Called automatically by Claude Code via MCP configuration

param(
    [int]$Port = 9999,
    [int]$E2EPort = 0,  # Optional E2E port for test framework connections
    [switch]$SetupPortForwarding  # Set up netsh port forwarding for external access
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
$global:ConnectionState = "Disconnected"  # States: Disconnected, Connecting, Connected, Reconnecting
$global:LastConnectionTime = $null  # Timestamp of successful connection
$global:ConnectionAttempts = 0  # Track retry attempts for backoff
#endregion

#region Connection State Machine
$script:BackoffDelays = @(500, 1000, 2000, 4000, 8000)  # Milliseconds

function Get-BackoffDelay {
    param([int]$Attempt)
    $index = [Math]::Min($Attempt, $script:BackoffDelays.Length - 1)
    $baseDelay = $script:BackoffDelays[$index]
    # Add 20% jitter
    $jitter = Get-Random -Minimum (-$baseDelay * 0.2) -Maximum ($baseDelay * 0.2)
    return [int]($baseDelay + $jitter)
}

function Set-ConnectionState {
    param([string]$State)
    $oldState = $global:ConnectionState
    $global:ConnectionState = $State
    if ($State -eq "Connected") {
        $global:LastConnectionTime = Get-Date
        $global:ConnectionAttempts = 0
    }
    Write-Log "Connection state: $oldState -> $State"
}
#endregion

#region Bridge Tool Definitions
# Note: Bridge auto-launches sandbox on startup via start_sandbox (internal, not exposed to agent).
# Agent cannot stop sandbox (user must close manually).
$BridgeTools = @(
    @{
        name = "reconnect_sandbox"
        description = "Reconnect to the sandbox MCP server if connection was lost. Only works if sandbox is already running."
        inputSchema = @{
            type = "object"
            properties = @{
                timeout_seconds = @{
                    type = "integer"
                    description = "Maximum time to wait for connection (default: 30)"
                    default = 30
                }
            }
        }
    }
    @{
        name = "sandbox_status"
        description = "Get current sandbox and MCP server connection status (read-only)."
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

    Set-ConnectionState "Connecting"
    $global:ConnectionAttempts++

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

        Set-ConnectionState "Connected"
        Write-Log "TCP connected to ${ServerIP}:${Port} (server PID: $ServerPid)"
        return $true
    } catch {
        Write-Log "TCP connection failed (attempt $($global:ConnectionAttempts)): $_"
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
    Set-ConnectionState "Disconnected"
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
        Set-ConnectionState "Reconnecting"
        Disconnect-Tcp
        $delay = Get-BackoffDelay $global:ConnectionAttempts
        Start-Sleep -Milliseconds $delay  # Backoff with jitter
        if (Connect-Tcp $signal.tcp_ip $signal.server_pid) {
            $global:SandboxTools = Get-SandboxToolList
            Write-Log "Reconnected to hot-reloaded server ($($global:SandboxTools.Count) tools)"
            return $true
        }
        return $false
    }

    # Case 2: Not connected but server is running - reconnect
    if (-not (Test-TcpConnected) -and $signal.server_pid) {
        Set-ConnectionState "Reconnecting"
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

function Setup-PortForwarding {
    param(
        [string]$SandboxIP,
        [int]$MainPort,
        [int]$E2EPort = 0
    )

    # Requires elevation to run netsh portproxy commands
    Write-Log "Setting up port forwarding: localhost:$MainPort -> ${SandboxIP}:$MainPort"

    try {
        # Set up main port forwarding
        $null = netsh interface portproxy add v4tov4 listenport=$MainPort listenaddress=0.0.0.0 connectport=$MainPort connectaddress=$SandboxIP 2>&1

        # Set up E2E port forwarding if specified
        if ($E2EPort -gt 0) {
            Write-Log "Setting up E2E port forwarding: localhost:$E2EPort -> ${SandboxIP}:$E2EPort"
            $null = netsh interface portproxy add v4tov4 listenport=$E2EPort listenaddress=0.0.0.0 connectport=$E2EPort connectaddress=$SandboxIP 2>&1
        }

        Write-Log "Port forwarding configured successfully"
        return $true
    } catch {
        Write-Log "Failed to set up port forwarding: $_"
        return $false
    }
}

function Remove-PortForwarding {
    param(
        [int]$MainPort,
        [int]$E2EPort = 0
    )

    Write-Log "Removing port forwarding for port $MainPort"
    try {
        $null = netsh interface portproxy delete v4tov4 listenport=$MainPort listenaddress=0.0.0.0 2>&1

        if ($E2EPort -gt 0) {
            Write-Log "Removing port forwarding for E2E port $E2EPort"
            $null = netsh interface portproxy delete v4tov4 listenport=$E2EPort listenaddress=0.0.0.0 2>&1
        }
    } catch {
        Write-Log "Failed to remove port forwarding: $_"
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
    $signal = Get-ReadySignal
    if ($signal -and $signal.tcp_ip) {
        Write-Log "Found existing ready signal, checking connection..."
        if (Connect-Tcp $signal.tcp_ip $signal.server_pid) {
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
        # Stale signal, clean up
        Remove-Item $ReadySignalPath -Force -ErrorAction SilentlyContinue
    }

    # Clean up old signals before launching
    Remove-Item $ReadySignalPath -Force -ErrorAction SilentlyContinue
    Remove-Item $ShutdownSignalPath -Force -ErrorAction SilentlyContinue

    # Launch sandbox (requires elevation)
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
    if (-not $signal.server_pid) {
        Write-Log "Server not started (LazyStart mode), creating trigger..."
        "start" | Out-File -FilePath $ServerTriggerPath -Encoding UTF8

        $serverTimeout = 30
        $serverStart = Get-Date
        while (((Get-Date) - $serverStart).TotalSeconds -lt $serverTimeout) {
            Start-Sleep -Milliseconds 500
            $signal = Get-ReadySignal
            if ($signal -and $signal.server_pid) {
                Write-Log "Server started (PID: $($signal.server_pid))"
                break
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

function Invoke-ReconnectSandbox {
    param($Arguments)

    $timeout = if ($Arguments.timeout_seconds) { $Arguments.timeout_seconds } else { 30 }
    $startTime = Get-Date

    # Check if already connected
    if (Test-TcpConnected) {
        return @{
            success = $true
            message = "Already connected to sandbox MCP server"
            status = (Invoke-SandboxStatus @{})
        }
    }

    # Check for ready signal (sandbox must already be running)
    $signal = Get-ReadySignal
    if (-not $signal -or -not $signal.tcp_ip) {
        return @{
            success = $false
            error = "Sandbox is not running. Please start the sandbox manually first."
            hint = "Run the sandbox WSB file at: $SandboxWsbPath"
        }
    }

    Write-Log "Found ready signal, attempting connection..."

    # If server is not running (LazyStart mode), create trigger to start it
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
                error = "Timeout waiting for MCP server to start inside sandbox"
                sandbox_ip = $signal.tcp_ip
            }
        }
    }

    # Give server a moment to initialize TCP listener if it just started
    Start-Sleep -Milliseconds 500

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

    # Set up port forwarding if requested (for external access from WSL/remote)
    $portForwardingSet = $false
    if ($SetupPortForwarding) {
        $portForwardingSet = Setup-PortForwarding -SandboxIP $signal.tcp_ip -MainPort $Port -E2EPort $E2EPort
    }

    $elapsed = [math]::Round(((Get-Date) - $startTime).TotalSeconds, 1)
    $result = @{
        success = $true
        message = "Connected to sandbox MCP server"
        tcp_endpoint = "$($signal.tcp_ip):$Port"
        server_pid = $signal.server_pid
        tool_count = $global:SandboxTools.Count
        elapsed_seconds = $elapsed
    }

    # Include E2E port info if configured
    if ($E2EPort -gt 0) {
        $result.e2e_endpoint = "$($signal.tcp_ip):$E2EPort"
    }

    # Include port forwarding status
    if ($SetupPortForwarding) {
        $result.port_forwarding = $portForwardingSet
        if ($portForwardingSet) {
            $result.localhost_endpoint = "localhost:$Port"
            if ($E2EPort -gt 0) {
                $result.localhost_e2e_endpoint = "localhost:$E2EPort"
            }
        }
    }

    return $result
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
        connection_state = $global:ConnectionState
        connected_server_pid = $global:ConnectedServerPid
    }

    # Include last connection time if available
    if ($global:LastConnectionTime) {
        $status.last_connection_time = $global:LastConnectionTime.ToString("yyyy-MM-ddTHH:mm:ss")
    }

    # Include E2E port if configured
    if ($E2EPort -gt 0) {
        $status.e2e_port = $E2EPort
    }

    if ($signal) {
        $status.sandbox_booted = $true
        $status.sandbox_ip = $signal.tcp_ip
        $status.server_pid = $signal.server_pid
        $status.app_pid = $signal.app_pid
        $status.sandbox_hostname = $signal.hostname

        # Include e2e_port from ready signal if available
        if ($signal.e2e_port) {
            $status.sandbox_e2e_port = $signal.e2e_port
        }
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
    "reconnect_sandbox" = { param($args) Invoke-ReconnectSandbox $args }
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
        Send-McpError -Id $Request.id -Code -32603 -Message "Not connected to sandbox. Restart Claude Code to auto-launch sandbox, or call 'reconnect_sandbox' if sandbox is already running."
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

# Auto-launch sandbox at startup (or connect to existing)
$result = Invoke-StartSandbox @{ timeout_seconds = 60 }
if ($result.success) {
    Write-Log "Startup: $($result.message) ($($result.tool_count) tools)"
} else {
    Write-Log "Startup: $($result.error)"
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
