# Tasks: MCP Bridge Reconnect Architecture

> **⚠️ ARCHIVED**: This spec has been superseded by `unified-refactor/`. All tasks from this spec were consolidated and completed in the unified refactoring plan.

## Overview

Implementation checklist for the reconnect architecture improvements. Tasks are organized in three phases as outlined in the design:
1. Add state machine (non-breaking)
2. Implement signal-first connection with backoff
3. Hot-reload improvements

Excludes unit tests (PowerShell/sandbox integration testing is manual).

---

## Phase 1: Add State Machine (Non-Breaking)

### 1.1 Add Connection State Variables
- [ ] **File:** `mcp-sandbox-bridge.ps1`
- [ ] Add `$script:ConnectionState` variable (enum: `Disconnected`, `Connecting`, `Connected`, `Reconnecting`)
- [ ] Add `$script:LastConnectionTime` variable (DateTime or null)
- [ ] Keep existing `$global:ConnectedServerPid` (already exists)

### 1.2 Implement State Management Functions
- [ ] **File:** `mcp-sandbox-bridge.ps1`
- [ ] Create `Get-ConnectionState` function returning current state
- [ ] Create `Set-ConnectionState` function with logging (logs old -> new state with server_pid)
- [ ] Create `Get-ConnectionDuration` function returning seconds since last connection (or null)

### 1.3 Update Existing Functions to Track State
- [ ] **File:** `mcp-sandbox-bridge.ps1`
- [ ] Update `Connect-Tcp` to call `Set-ConnectionState "Connected"` on success
- [ ] Update `Disconnect-Tcp` to call `Set-ConnectionState "Disconnected"`
- [ ] Update `Test-ServerReloadAndReconnect` to set state to `Reconnecting` before reconnect attempt

### 1.4 Enhance sandbox_status Response
- [ ] **File:** `mcp-sandbox-bridge.ps1`
- [ ] Update `Invoke-SandboxStatus` to include `connection_state` field
- [ ] Add `connected_server_pid` field (current `$global:ConnectedServerPid`)
- [ ] Add `last_connection_time` field (ISO 8601 timestamp or null)
- [ ] Add `connection_duration_seconds` field (int or null)
- [ ] Add `pending_request_count` field (always 0 for single-threaded bridge)

---

## Phase 2: Signal-First Connection with Backoff

### 2.1 Add Backoff Strategy Functions
- [ ] **File:** `mcp-sandbox-bridge.ps1`
- [ ] Add `$script:BackoffDelays` array: `@(500, 1000, 2000, 4000, 8000)`
- [ ] Add `$script:MaxAttempts = 5`
- [ ] Add `$script:JitterFactor = 0.2`
- [ ] Create `Get-BackoffDelay` function with jitter calculation

### 2.2 Create Connect-WithRetry Function
- [ ] **File:** `mcp-sandbox-bridge.ps1`
- [ ] Implement `Connect-WithRetry` function with parameters: `ServerIP`, `ServerPid`, `TimeoutSeconds`
- [ ] Loop up to `$MaxAttempts` with backoff delays
- [ ] Check timeout between attempts
- [ ] Log each attempt with attempt number
- [ ] Return result object with: `Success`, `Attempts`, `ElapsedSeconds`, `Error`, `LastError`

### 2.3 Create Signal Reader with Validation
- [ ] **File:** `mcp-sandbox-bridge.ps1`
- [ ] Create `Read-ReadySignal` function (replaces or wraps `Get-ReadySignal`)
- [ ] Return structured result with `Valid` boolean
- [ ] Handle missing signal file: return `@{ Valid = $false; Reason = "signal_not_found" }`
- [ ] **Handle malformed JSON gracefully:** return `@{ Valid = $false; Reason = "parse_error"; Error = $_.Exception.Message }`
- [ ] Validate required fields (`tcp_ip`): return `@{ Valid = $false; Reason = "missing_tcp_ip" }` if absent
- [ ] On success return: `TcpIP`, `TcpPort` (default 9999), `ServerPid`, `AppPid`, `E2EPort`, `Hostname`, `Timestamp`

### 2.4 Refactor Invoke-ReconnectSandbox
- [ ] **File:** `mcp-sandbox-bridge.ps1`
- [ ] Replace direct `Get-ReadySignal` call with `Read-ReadySignal`
- [ ] Set state to `Connecting` before TCP attempt
- [ ] Use `Connect-WithRetry` instead of single `Connect-Tcp` call
- [ ] On success: set state to `Connected`, refresh tools
- [ ] **Handle tool list refresh failure:** If `Get-SandboxToolList` returns empty after successful TCP, log warning but still return success with `tool_count = 0` and `tools_warning = "Tool refresh failed"`
- [ ] On failure: set state to `Disconnected`, return diagnostic error

### 2.5 Implement Diagnostic Error Response
- [ ] **File:** `mcp-sandbox-bridge.ps1`
- [ ] Update reconnect failure returns to include `diagnostics` object
- [ ] Include in diagnostics:
  - `signal_state` (exists, tcp_ip, server_pid, timestamp from signal)
  - `connection_attempts` (count)
  - `elapsed_seconds`
  - `last_error` (message from final attempt)
  - `connection_state` (current state)
  - `hints` (array of actionable suggestions based on failure mode)

---

## Phase 3: Hot-Reload Improvements

### 3.1 Enhance Test-ServerReloadAndReconnect
- [ ] **File:** `mcp-sandbox-bridge.ps1`
- [ ] Add state transition to `Reconnecting` at start of hot-reload detection
- [ ] Use `Connect-WithRetry` with shorter timeout (10s) for reconnection
- [ ] On success: set state to `Connected`, log tool count
- [ ] **On failure:** set state to `Disconnected`, return structured error (not silent failure)
- [ ] Return result object with `Detected`, `Reconnected`, `Error` fields

### 3.2 Document Concurrent Request Behavior During Hot-Reload
- [ ] **File:** `mcp-sandbox-bridge.ps1` (header comment)
- [ ] Add comment block documenting that bridge is single-threaded
- [ ] Document that hot-reload reconnection blocks the current request
- [ ] Document that subsequent requests will see the new connection state
- [ ] Note: No race conditions possible due to single-threaded nature

### 3.3 Update Forward-ToSandbox Error Handling
- [ ] **File:** `mcp-sandbox-bridge.ps1`
- [ ] After failed forward, check if hot-reload occurred (PID change)
- [ ] If hot-reload detected during request, attempt reconnect and retry forward once
- [ ] Return clear error to agent if reconnect fails (not null)

---

## Phase 4: Bootstrap Signal Enhancement

### 4.1 Add E2E Port to Ready Signal
- [ ] **File:** `sandbox/bootstrap.ps1`
- [ ] Add `e2e_port` field to `mcp-ready.signal` JSON output
- [ ] Value should come from `--e2e-port` parameter passed to MCP server (or null if not set)
- [ ] Verify signal is written after server successfully starts TCP listener

---

## Integration Testing (Manual)

After implementation, verify these scenarios manually:

### Scenario 1: Clean Reconnect After Server Restart
- [ ] Connect to sandbox
- [ ] Trigger server hot-reload (create `server.trigger`)
- [ ] Execute a tool call
- [ ] Verify: bridge detects PID change, reconnects, request succeeds

### Scenario 2: Reconnect With Malformed Signal
- [ ] Connect to sandbox
- [ ] Corrupt `mcp-ready.signal` with invalid JSON
- [ ] Call `reconnect_sandbox`
- [ ] Verify: returns `parse_error` reason, not a crash

### Scenario 3: Reconnect Timeout
- [ ] Stop sandbox
- [ ] Call `reconnect_sandbox` with `timeout_seconds=5`
- [ ] Verify: returns diagnostic error after ~5s with attempt count

### Scenario 4: Already Connected
- [ ] Connect to sandbox
- [ ] Call `reconnect_sandbox`
- [ ] Verify: returns immediately with "Already connected" message

### Scenario 5: LazyStart Server
- [ ] Start sandbox without MCP server (server_pid null)
- [ ] Call `reconnect_sandbox`
- [ ] Verify: creates `server.trigger`, waits for server, connects

---

## Task Dependencies

```
Phase 1.1 ─┬─> Phase 1.2 ─> Phase 1.3 ─> Phase 1.4
           │
Phase 2.1 ─┤
           │
Phase 2.3 ─┴─> Phase 2.2 ─> Phase 2.4 ─> Phase 2.5
                              │
Phase 3.1 <───────────────────┘
    │
    └─> Phase 3.2 ─> Phase 3.3

Phase 4.1 (independent, can be done anytime)
```

**Key Dependencies:**
- State management (1.2) must exist before tracking calls (1.3)
- Backoff (2.1) and signal reader (2.3) must exist before Connect-WithRetry (2.2)
- Connect-WithRetry (2.2) must exist before refactoring reconnect (2.4)
- Hot-reload improvements (3.1) depend on Connect-WithRetry (2.2)

---

## Estimated Effort

| Phase | Tasks | Estimated Time |
|-------|-------|----------------|
| Phase 1 | 4 | 1-2 hours |
| Phase 2 | 5 | 2-3 hours |
| Phase 3 | 3 | 1-2 hours |
| Phase 4 | 1 | 30 minutes |
| Testing | 5 scenarios | 1 hour |

**Total:** ~6-8 hours

---

## Notes

- All changes are in PowerShell (`mcp-sandbox-bridge.ps1`, `bootstrap.ps1`)
- No changes needed to C# MCP server (`Program.cs`) - E2E port already supported
- Bridge is single-threaded, no concurrency concerns
- Backoff values are hardcoded per operator decision (no env var config)
