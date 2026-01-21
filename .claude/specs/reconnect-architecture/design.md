# Design: MCP Bridge Reconnect Architecture

## Overview

This design addresses the reliability issues in `reconnect_sandbox` by implementing a signal-driven connection sequence with proper state management. The key insight is that the current implementation has the connection order wrong: it should read the signal file first, validate server_pid, and only then attempt TCP connection.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              Host Machine                                    │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │                       mcp-sandbox-bridge.ps1                            │ │
│  │  ┌──────────────┐    ┌───────────────────┐    ┌────────────────┐       │ │
│  │  │    State     │    │   Connection      │    │   Signal       │       │ │
│  │  │   Machine    │◄───│   Manager         │◄───│   Reader       │       │ │
│  │  │              │    │   (TCP + Retry)   │    │   (JSON)       │       │ │
│  │  └──────────────┘    └───────────────────┘    └────────────────┘       │ │
│  │        │                      │                       │                 │ │
│  │        │                      │                       │                 │ │
│  │        ▼                      ▼                       ▼                 │ │
│  │  ┌─────────────────────────────────────────────────────────────┐       │ │
│  │  │              C:\WinFormsMcpSandboxWorkspace\Shared           │       │ │
│  │  │  ┌─────────────────┐  ┌─────────────┐  ┌───────────────┐   │       │ │
│  │  │  │ mcp-ready.signal│  │server.trigger│  │shutdown.signal│   │       │ │
│  │  │  │ {tcp_ip, port,  │  │ (trigger)    │  │ (shutdown)    │   │       │ │
│  │  │  │  server_pid}    │  │              │  │               │   │       │ │
│  │  │  └─────────────────┘  └─────────────┘  └───────────────┘   │       │ │
│  │  └─────────────────────────────────────────────────────────────┘       │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────────┘
                                     │ 9P filesystem
                                     ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                           Windows Sandbox                                   │
│  ┌────────────────────────────────────────────────────────────────────────┐│
│  │                         bootstrap.ps1                                  ││
│  │  ┌──────────────────┐                      ┌───────────────────┐      ││
│  │  │  Process Manager │                      │  Signal Writer    │      ││
│  │  │  - Start/Stop    │─────────────────────▶│  - Update PIDs    │      ││
│  │  │  - Hot Reload    │                      │  - TCP endpoint   │      ││
│  │  └──────────────────┘                      └───────────────────┘      ││
│  │           │                                                            ││
│  │           ▼                                                            ││
│  │  ┌────────────────────────────────────────────────────────────────┐   ││
│  │  │           Rhombus.WinFormsMcp.Server (Program.cs)              │   ││
│  │  │  ┌─────────────────┐  ┌─────────────────┐                      │   ││
│  │  │  │ Main Port (9999)│  │ E2E Port (9998) │  (optional)          │   ││
│  │  │  │ Agent connects  │  │ Tests connect   │                      │   ││
│  │  │  └─────────────────┘  └─────────────────┘                      │   ││
│  │  │           │                    │                               │   ││
│  │  │           └────────┬───────────┘                               │   ││
│  │  │                    ▼                                           │   ││
│  │  │           ┌─────────────────┐                                  │   ││
│  │  │           │ UIA Semaphore   │  Serializes FlaUI operations     │   ││
│  │  │           │ (1 permit)      │                                  │   ││
│  │  │           └─────────────────┘                                  │   ││
│  │  └────────────────────────────────────────────────────────────────┘   ││
│  └────────────────────────────────────────────────────────────────────────┘│
└────────────────────────────────────────────────────────────────────────────┘
```

## Connection State Machine

The bridge maintains a finite state machine for connection management:

```
                    ┌─────────────────────────────────────────────────┐
                    │                                                 │
                    ▼                                                 │
              ┌────────────┐                                          │
    startup──▶│Disconnected│◄──────────────────────────────────┐      │
              └─────┬──────┘                                   │      │
                    │                                          │      │
                    │ read_signal()                            │      │
                    │ signal.server_pid != null                │      │
                    ▼                                          │      │
              ┌────────────┐                                   │      │
              │ Connecting │◄──────────────────────────────────┤      │
              └─────┬──────┘                                   │      │
                    │                                          │      │
                    │ tcp_connect() success                    │      │
                    ▼                                          │      │
              ┌────────────┐   server_pid changed     ┌────────┴─────┐
              │ Connected  │─────────────────────────▶│ Reconnecting │
              └─────┬──────┘                          └──────────────┘
                    │                                         │
                    │ tcp_error or                            │ max_retries
                    │ connection_lost                         │ exhausted
                    │                                         │
                    └──────────────────────────────────────────┘
                              return to Disconnected
```

### State Definitions

| State | Description | Allowed Actions |
|-------|-------------|-----------------|
| `Disconnected` | No TCP connection, no active server_pid | `reconnect_sandbox`, `sandbox_status` |
| `Connecting` | Attempting TCP connection after reading valid signal | Wait for connection result |
| `Connected` | Active TCP connection with tracked server_pid | Forward tools, `sandbox_status` |
| `Reconnecting` | Detected server_pid change, reconnecting to new instance | Wait for reconnection result |

### State Transitions

```powershell
# State transition table
$StateTransitions = @{
    "Disconnected" = @{
        "signal_valid" = "Connecting"
        "signal_invalid" = "Disconnected"  # Stay, emit error
    }
    "Connecting" = @{
        "tcp_success" = "Connected"
        "tcp_failure" = "Disconnected"  # After retries exhausted
    }
    "Connected" = @{
        "tcp_error" = "Disconnected"
        "pid_changed" = "Reconnecting"
        "explicit_disconnect" = "Disconnected"
    }
    "Reconnecting" = @{
        "tcp_success" = "Connected"
        "tcp_failure" = "Disconnected"  # After retries exhausted
    }
}
```

## Signal-Driven Connection Sequence

### Signal File Format (mcp-ready.signal)

```json
{
    "timestamp": "2025-01-20T14:30:00.000Z",
    "hostname": "DESKTOP-SANDBOX",
    "server_pid": 1234,
    "app_pid": 5678,
    "server_dir": "C:\\LocalServer",
    "app_dir": "C:\\LocalApp",
    "tcp_enabled": true,
    "tcp_port": 9999,
    "tcp_ip": "172.28.64.1",
    "e2e_port": 9998,
    "coverage_enabled": false,
    "coverage_output": null
}
```

### Connection Sequence

```
┌─────────┐          ┌─────────┐          ┌────────────┐          ┌─────────┐
│  Agent  │          │ Bridge  │          │Signal File │          │  MCP    │
│         │          │         │          │            │          │ Server  │
└────┬────┘          └────┬────┘          └─────┬──────┘          └────┬────┘
     │                    │                     │                      │
     │ reconnect_sandbox  │                     │                      │
     │───────────────────▶│                     │                      │
     │                    │                     │                      │
     │                    │  1. Read signal     │                      │
     │                    │────────────────────▶│                      │
     │                    │                     │                      │
     │                    │  {tcp_ip, port,     │                      │
     │                    │   server_pid}       │                      │
     │                    │◄────────────────────│                      │
     │                    │                     │                      │
     │                    │  2. Validate server_pid != null            │
     │                    │  (if null, create server.trigger           │
     │                    │   and poll for server_pid)                 │
     │                    │                     │                      │
     │                    │  3. TCP connect     │                      │
     │                    │───────────────────────────────────────────▶│
     │                    │                     │                      │
     │                    │  4. Connection ACK  │                      │
     │                    │◄───────────────────────────────────────────│
     │                    │                     │                      │
     │                    │  5. tools/list      │                      │
     │                    │───────────────────────────────────────────▶│
     │                    │                     │                      │
     │                    │  6. Tool definitions│                      │
     │                    │◄───────────────────────────────────────────│
     │                    │                     │                      │
     │  {success: true,   │                     │                      │
     │   tool_count: 45}  │                     │                      │
     │◄───────────────────│                     │                      │
     │                    │                     │                      │
```

## Retry and Backoff Implementation

### Backoff Algorithm

Hardcoded exponential backoff with jitter (no configuration):

```powershell
$BackoffConfig = @{
    InitialDelayMs = 500
    MaxDelayMs = 8000
    MaxAttempts = 5
    JitterFactor = 0.2  # +/- 20% randomization
}

function Get-BackoffDelay {
    param([int]$Attempt)

    # Base delay: 500, 1000, 2000, 4000, 8000
    $baseDelay = [Math]::Min(
        $BackoffConfig.InitialDelayMs * [Math]::Pow(2, $Attempt),
        $BackoffConfig.MaxDelayMs
    )

    # Add jitter: +/- 20%
    $jitter = $baseDelay * $BackoffConfig.JitterFactor
    $minDelay = $baseDelay - $jitter
    $maxDelay = $baseDelay + $jitter

    return [int](Get-Random -Minimum $minDelay -Maximum $maxDelay)
}
```

### Backoff Sequence

| Attempt | Base Delay | With Jitter (example) |
|---------|------------|----------------------|
| 0 | 500ms | 400-600ms |
| 1 | 1000ms | 800-1200ms |
| 2 | 2000ms | 1600-2400ms |
| 3 | 4000ms | 3200-4800ms |
| 4 | 8000ms | 6400-9600ms |

Total worst-case time before giving up: ~18 seconds (fits within default 30s timeout).

### Connection Attempt Flow

```powershell
function Connect-WithRetry {
    param(
        [string]$ServerIP,
        [int]$ServerPid,
        [int]$TimeoutSeconds = 30
    )

    $startTime = Get-Date
    $attempt = 0
    $lastError = $null

    while ($attempt -lt $BackoffConfig.MaxAttempts) {
        # Check timeout
        $elapsed = ((Get-Date) - $startTime).TotalSeconds
        if ($elapsed -ge $TimeoutSeconds) {
            return @{
                Success = $false
                Error = "Timeout after ${elapsed}s"
                Attempts = $attempt
                LastError = $lastError
            }
        }

        # Log attempt
        Write-Log "Connection attempt $($attempt + 1)/$($BackoffConfig.MaxAttempts) to ${ServerIP}:${Port}"

        try {
            if (Connect-Tcp -ServerIP $ServerIP -ServerPid $ServerPid) {
                return @{
                    Success = $true
                    Attempts = $attempt + 1
                    ElapsedSeconds = [Math]::Round($elapsed, 1)
                }
            }
        } catch {
            $lastError = $_.Exception.Message
            Write-Log "Attempt $($attempt + 1) failed: $lastError"
        }

        # Backoff before retry
        $delay = Get-BackoffDelay -Attempt $attempt
        Write-Log "Waiting ${delay}ms before retry..."
        Start-Sleep -Milliseconds $delay

        $attempt++
    }

    return @{
        Success = $false
        Error = "Max retries ($($BackoffConfig.MaxAttempts)) exhausted"
        Attempts = $attempt
        LastError = $lastError
    }
}
```

## Hot-Reload Detection

### Detection Strategy

The bridge detects hot-reload by comparing `server_pid` on each request:

```powershell
function Test-ServerReloadAndReconnect {
    $signal = Get-ReadySignal
    if (-not $signal -or -not $signal.tcp_ip) {
        return @{ Detected = $false; Reason = "no_signal" }
    }

    # Case 1: server_pid is null (server not started)
    if (-not $signal.server_pid) {
        return @{ Detected = $false; Reason = "server_not_started" }
    }

    # Case 2: Connected to different PID (hot-reload occurred)
    if ($global:ConnectedServerPid -and $signal.server_pid -ne $global:ConnectedServerPid) {
        Write-Log "Hot-reload detected: PID $($global:ConnectedServerPid) -> $($signal.server_pid)"

        # Transition to Reconnecting state
        Set-ConnectionState "Reconnecting"

        Disconnect-Tcp
        Start-Sleep -Milliseconds 500  # Wait for new server initialization

        $result = Connect-WithRetry -ServerIP $signal.tcp_ip -ServerPid $signal.server_pid -TimeoutSeconds 10

        if ($result.Success) {
            $global:SandboxTools = Get-SandboxToolList
            Set-ConnectionState "Connected"
            Write-Log "Hot-reload reconnection successful ($($global:SandboxTools.Count) tools)"
            return @{ Detected = $true; Reconnected = $true }
        } else {
            Set-ConnectionState "Disconnected"
            return @{ Detected = $true; Reconnected = $false; Error = $result.Error }
        }
    }

    # Case 3: Not connected but server is running (connection lost)
    if (-not (Test-TcpConnected) -and $signal.server_pid) {
        Write-Log "Connection lost, server still running (PID: $($signal.server_pid))"
        return @{ Detected = $false; Reason = "connection_lost" }
    }

    return @{ Detected = $false; Reason = "no_change" }
}
```

### Hot-Reload Sequence

```
┌─────────────┐     ┌─────────────┐     ┌──────────────┐     ┌─────────────┐
│   Agent     │     │   Bridge    │     │    Host      │     │   Sandbox   │
│             │     │             │     │   (trigger)  │     │             │
└──────┬──────┘     └──────┬──────┘     └──────┬───────┘     └──────┬──────┘
       │                   │                   │                    │
       │                   │                   │  server.trigger    │
       │                   │                   │───────────────────▶│
       │                   │                   │                    │
       │                   │                   │  (bootstrap.ps1    │
       │                   │                   │   restarts server) │
       │                   │                   │                    │
       │                   │                   │  Update signal     │
       │                   │                   │  (new server_pid)  │
       │                   │                   │◄───────────────────│
       │                   │                   │                    │
       │  tools/call       │                   │                    │
       │──────────────────▶│                   │                    │
       │                   │                   │                    │
       │                   │  Read signal      │                    │
       │                   │  (detect PID      │                    │
       │                   │   change)         │                    │
       │                   │                   │                    │
       │                   │  Disconnect old   │                    │
       │                   │─────────────────────────────────────X  │
       │                   │                   │                    │
       │                   │  Wait 500ms       │                    │
       │                   │                   │                    │
       │                   │  Connect to new   │                    │
       │                   │  server_pid       │                    │
       │                   │───────────────────────────────────────▶│
       │                   │                   │                    │
       │                   │  Refresh tools    │                    │
       │                   │───────────────────────────────────────▶│
       │                   │                   │                    │
       │                   │  Forward request  │                    │
       │                   │───────────────────────────────────────▶│
       │                   │                   │                    │
       │  Response         │                   │                    │
       │◄──────────────────│                   │                    │
       │                   │                   │                    │
```

## E2E Port Isolation

### Port Architecture

```
                           Host Machine
    ┌───────────────────────────────────────────────────────────┐
    │                                                           │
    │  Claude Code                           E2E Test Runner    │
    │       │                                      │            │
    │       │ stdio                                │ TCP        │
    │       ▼                                      ▼            │
    │  ┌───────────────┐                    ┌────────────┐     │
    │  │ Bridge        │                    │ Test       │     │
    │  │ (PowerShell)  │                    │ Framework  │     │
    │  └───────┬───────┘                    └─────┬──────┘     │
    │          │                                  │            │
    │          │ TCP 9999                         │ TCP 9998   │
    │          │                                  │            │
    └──────────┼──────────────────────────────────┼────────────┘
               │                                  │
               │ Port Forwarding                  │ Port Forwarding
               │ (if --SetupPortForwarding)       │ (if configured)
               │                                  │
    ┌──────────┼──────────────────────────────────┼────────────┐
    │          ▼                                  ▼            │
    │  ┌───────────────────────────────────────────────────┐   │
    │  │                MCP Server (Program.cs)            │   │
    │  │                                                   │   │
    │  │    Main Port 9999          E2E Port 9998         │   │
    │  │    (Agent)                 (Tests)               │   │
    │  │         │                       │                │   │
    │  │         └───────────┬───────────┘                │   │
    │  │                     ▼                            │   │
    │  │             ┌───────────────┐                    │   │
    │  │             │ UIA Semaphore │ (shared)           │   │
    │  │             │ _uiaLock(1,1) │                    │   │
    │  │             └───────────────┘                    │   │
    │  │                     │                            │   │
    │  │                     ▼                            │   │
    │  │             ┌───────────────┐                    │   │
    │  │             │ FlaUI/UIA2   │                    │   │
    │  │             │ Automation   │                    │   │
    │  │             └───────────────┘                    │   │
    │  └───────────────────────────────────────────────────┘   │
    │                     Windows Sandbox                      │
    └──────────────────────────────────────────────────────────┘
```

### Isolation Guarantees

1. **Separate Connection Tracking**: Each listener (`main`, `e2e`) assigns unique client IDs
2. **Independent Lifecycles**: E2E disconnect does not affect agent connection state
3. **Shared UIA Serialization**: Both ports use the same `_uiaLock` semaphore
4. **No Tool Cache Interference**: Bridge tool cache is per-connection, not shared

### E2E Signal Format Extension

```json
{
    "tcp_port": 9999,
    "tcp_ip": "172.28.64.1",
    "e2e_port": 9998
}
```

## Integration with sandbox_status Tool

### Enhanced sandbox_status Response

```json
{
    "workspace_path": "C:\\WinFormsMcpSandboxWorkspace",
    "workspace_exists": true,
    "sandbox_wsb_exists": true,

    "connection_state": "connected",
    "tcp_connected": true,
    "tcp_port": 9999,
    "connected_server_pid": 1234,
    "last_connection_time": "2025-01-20T14:30:00.000Z",
    "connection_duration_seconds": 125,

    "sandbox_booted": true,
    "sandbox_ip": "172.28.64.1",
    "server_pid": 1234,
    "app_pid": 5678,
    "sandbox_hostname": "DESKTOP-SANDBOX",

    "sandbox_tool_count": 45,
    "pending_request_count": 0,

    "e2e_port": 9998,
    "e2e_connected": false
}
```

### State Field Values

| Field | Type | Description |
|-------|------|-------------|
| `connection_state` | string | One of: `disconnected`, `connecting`, `connected`, `reconnecting` |
| `connected_server_pid` | int? | The server_pid we are currently connected to (null if disconnected) |
| `last_connection_time` | string | ISO 8601 timestamp of last successful connection |
| `connection_duration_seconds` | int? | Seconds since last connection (null if disconnected) |
| `pending_request_count` | int | Number of in-flight requests (always 0 for single-threaded bridge) |

## Implementation Modules

### Module: ConnectionStateMachine.ps1

```powershell
# Connection state machine with logging
$script:ConnectionState = "Disconnected"
$script:LastConnectionTime = $null
$script:ConnectedServerPid = $null

function Get-ConnectionState {
    return $script:ConnectionState
}

function Set-ConnectionState {
    param([string]$NewState)

    $oldState = $script:ConnectionState
    $script:ConnectionState = $NewState

    Write-Log "[STATE] $oldState -> $NewState (server_pid=$($script:ConnectedServerPid))"

    if ($NewState -eq "Connected") {
        $script:LastConnectionTime = Get-Date
    }
}

function Get-ConnectionDuration {
    if ($script:LastConnectionTime) {
        return [int]((Get-Date) - $script:LastConnectionTime).TotalSeconds
    }
    return $null
}
```

### Module: SignalReader.ps1

```powershell
# Signal file reader with validation
function Read-ReadySignal {
    $signalPath = Join-Path $SharedPath "mcp-ready.signal"

    if (-not (Test-Path $signalPath)) {
        return @{ Valid = $false; Reason = "signal_not_found" }
    }

    try {
        $content = Get-Content $signalPath -Raw | ConvertFrom-Json

        # Validate required fields
        if (-not $content.tcp_ip) {
            return @{ Valid = $false; Reason = "missing_tcp_ip"; Content = $content }
        }

        return @{
            Valid = $true
            TcpIP = $content.tcp_ip
            TcpPort = if ($content.tcp_port) { $content.tcp_port } else { 9999 }
            ServerPid = $content.server_pid
            AppPid = $content.app_pid
            E2EPort = $content.e2e_port
            Hostname = $content.hostname
            Timestamp = $content.timestamp
        }
    } catch {
        return @{ Valid = $false; Reason = "parse_error"; Error = $_.Exception.Message }
    }
}
```

### Module: RetryStrategy.ps1

```powershell
# Hardcoded backoff strategy
$script:BackoffDelays = @(500, 1000, 2000, 4000, 8000)
$script:MaxAttempts = 5
$script:JitterFactor = 0.2

function Get-BackoffDelay {
    param([int]$Attempt)

    $baseDelay = if ($Attempt -lt $script:BackoffDelays.Count) {
        $script:BackoffDelays[$Attempt]
    } else {
        $script:BackoffDelays[-1]  # Max delay
    }

    # Add jitter
    $jitter = [int]($baseDelay * $script:JitterFactor)
    $min = $baseDelay - $jitter
    $max = $baseDelay + $jitter

    return Get-Random -Minimum $min -Maximum $max
}

function Test-ShouldRetry {
    param([int]$Attempt, [DateTime]$StartTime, [int]$TimeoutSeconds)

    if ($Attempt -ge $script:MaxAttempts) {
        return $false
    }

    $elapsed = ((Get-Date) - $StartTime).TotalSeconds
    return $elapsed -lt $TimeoutSeconds
}
```

## Error Response Format

### Diagnostic Error Structure

When reconnection fails, return comprehensive diagnostic information:

```json
{
    "success": false,
    "error": "Connection timeout after 30 seconds",
    "diagnostics": {
        "signal_state": {
            "exists": true,
            "tcp_ip": "172.28.64.1",
            "server_pid": null,
            "timestamp": "2025-01-20T14:25:00.000Z"
        },
        "connection_attempts": 5,
        "elapsed_seconds": 30.5,
        "last_error": "Connection refused",
        "connection_state": "disconnected",
        "hints": [
            "server_pid is null - MCP server may not have started",
            "Try creating server.trigger to start the server"
        ]
    }
}
```

## Acceptance Criteria Mapping

| Scenario | Design Component |
|----------|-----------------|
| Clean Reconnect After Server Restart | Hot-reload detection in `Test-ServerReloadAndReconnect` |
| Manual Reconnect After Connection Loss | `Invoke-ReconnectSandbox` with state machine |
| Reconnect With LazyStart Server | Server trigger creation and signal polling |
| E2E Test Isolation | Dual-port architecture with shared UIA semaphore |
| Timeout During Reconnect | Backoff strategy with timeout checking |
| Already Connected | State check at start of `Invoke-ReconnectSandbox` |

## Migration Path

### Phase 1: Add State Machine (Non-Breaking)

1. Add `$script:ConnectionState` tracking
2. Update `sandbox_status` to include new fields
3. Keep existing behavior, just add logging

### Phase 2: Implement Signal-First Connection

1. Refactor `Invoke-ReconnectSandbox` to read signal first
2. Add backoff strategy
3. Add diagnostic error responses

### Phase 3: Hot-Reload Improvements

1. Enhance `Test-ServerReloadAndReconnect` with state transitions
2. Add 500ms wait for server initialization
3. Refresh tool list after reconnection

## Files to Modify

| File | Changes |
|------|---------|
| `mcp-sandbox-bridge.ps1` | State machine, backoff, signal-first connection |
| `sandbox/bootstrap.ps1` | Add `e2e_port` to ready signal |
| `src/.../Program.cs` | No changes (E2E port already supported) |

## Testing Strategy

### Unit Tests

1. `Test-BackoffSequence` - Verify delay progression
2. `Test-StateTransitions` - Verify state machine correctness
3. `Test-SignalValidation` - Verify signal parsing edge cases

### Integration Tests

1. `Test-HotReloadReconnection` - Trigger server restart, verify auto-reconnect
2. `Test-LazyStartConnection` - Start with null server_pid, create trigger, verify connection
3. `Test-E2EIsolation` - Connect on both ports, disconnect E2E, verify agent unaffected

### E2E Tests (in tests/Rhombus.WinFormsMcp.Tests)

1. `E2ETests.ReconnectAfterServerRestart` - Full scenario test
2. `E2ETests.TimeoutBehavior` - Verify timeout and diagnostic output
