# Requirements: MCP Bridge Reconnect Architecture

## Overview

This specification addresses connection reliability issues in the WinForms MCP sandbox architecture. The bridge (`mcp-sandbox-bridge.ps1`) acts as a proxy between Claude Code and the MCP server running inside Windows Sandbox, forwarding JSON-RPC requests over TCP.

**Problem Statement:** The `reconnect_sandbox` tool does not work reliably on retries. Connection order is incorrect (should read signals before connecting). User reports describe the backup function as "garbage" and timeout reconnects causing issues.

## Stakeholders

- **Agent Operators**: Need reliable reconnection after transient failures
- **E2E Test Framework**: Needs dedicated port to avoid interfering with agent connections
- **Hot-Reload Workflow**: Needs seamless reconnection after server restarts

## Functional Requirements

### FR-1: Signal-Driven Connection Sequence

**FR-1.1** (EARS: Ubiquitous)
The bridge SHALL read the ready signal file before attempting any TCP connection.

**FR-1.2** (EARS: Ubiquitous)
The ready signal file SHALL contain `tcp_ip`, `tcp_port`, and `server_pid` fields in JSON format.

**FR-1.3** (EARS: Ubiquitous)
The bridge SHALL NOT attempt TCP connection if `server_pid` is null or missing from the signal.

**FR-1.4** (EARS: State-driven)
WHEN the signal file does not exist, the bridge SHALL wait with exponential backoff up to a configurable timeout.

**FR-1.5** (EARS: State-driven)
WHEN the server_pid in the signal changes, the bridge SHALL detect this as a server restart and initiate reconnection.

### FR-2: Reconnect Tool Behavior

**FR-2.1** (EARS: Ubiquitous)
The `reconnect_sandbox` tool SHALL first check if already connected with matching server_pid.

**FR-2.2** (EARS: State-driven)
WHEN already connected to the current server_pid, reconnect_sandbox SHALL return success immediately without reconnecting.

**FR-2.3** (EARS: Event-driven)
WHEN connection is lost, `reconnect_sandbox` SHALL:
1. Disconnect existing TCP resources
2. Re-read the ready signal file
3. Validate server_pid is present
4. Attempt new TCP connection to signaled endpoint
5. Refresh tool list from sandbox

**FR-2.4** (EARS: State-driven)
WHEN server_pid is null (LazyStart mode), reconnect_sandbox SHALL:
1. Create server.trigger file
2. Poll signal file for server_pid to appear
3. Connect once server_pid is present

**FR-2.5** (EARS: Ubiquitous)
The reconnect_sandbox tool SHALL support a `timeout_seconds` parameter (default: 30).

### FR-3: Retry and Backoff Strategy

**FR-3.1** (EARS: Ubiquitous)
Connection attempts SHALL use exponential backoff with jitter.

**FR-3.2** (EARS: Ubiquitous)
The backoff sequence SHALL be: 500ms, 1s, 2s, 4s, 8s (max 5 attempts or timeout).

**FR-3.3** (EARS: Event-driven)
WHEN a TCP connection attempt fails, the bridge SHALL log the failure reason before retrying.

**FR-3.4** (EARS: State-driven)
WHEN all retry attempts are exhausted, the bridge SHALL return a structured error with diagnostic information including:
- Last signal file contents
- Connection attempt count
- Elapsed time
- Last error message

### FR-4: Hot-Reload Server Restart Detection

**FR-4.1** (EARS: Event-driven)
WHEN forwarding a request, the bridge SHALL check if server_pid has changed since last connection.

**FR-4.2** (EARS: Event-driven)
WHEN server_pid change is detected, the bridge SHALL:
1. Disconnect cleanly from old server
2. Wait 500ms for new server initialization
3. Connect to new server
4. Refresh cached tool list

**FR-4.3** (EARS: State-driven)
WHEN hot-reload reconnection fails, the bridge SHALL return error to agent rather than silently failing.

### FR-5: E2E Test Connection Isolation

**FR-5.1** (EARS: Optional feature)
IF the `--e2e-port` parameter is provided, the MCP server SHALL listen on a separate port for E2E test connections.

**FR-5.2** (EARS: Ubiquitous)
E2E test connections on the dedicated port SHALL NOT interfere with agent connections on the main port.

**FR-5.3** (EARS: Ubiquitous)
Both connection types SHALL serialize UIA operations through the same semaphore to prevent FlaUI race conditions.

**FR-5.4** (EARS: State-driven)
WHEN an E2E client disconnects, it SHALL NOT affect the agent connection state or tool cache.

### FR-6: Connection State Management

**FR-6.1** (EARS: Ubiquitous)
The bridge SHALL track connection state: `disconnected`, `connecting`, `connected`, `reconnecting`.

**FR-6.2** (EARS: Event-driven)
WHEN `sandbox_status` is called, it SHALL return current state including:
- Connection state enum
- Connected server_pid (if connected)
- Time since last successful connection
- Pending request count (if any)

**FR-6.3** (EARS: Ubiquitous)
The bridge SHALL maintain a single TCP connection per endpoint (no connection pooling).

### FR-7: Graceful Disconnection

**FR-7.1** (EARS: Event-driven)
WHEN the bridge process receives EOF on stdin, it SHALL:
1. Stop accepting new requests
2. Wait up to 5 seconds for pending requests to complete
3. Close TCP connection cleanly
4. Exit with code 0

**FR-7.2** (EARS: Event-driven)
WHEN TCP connection is lost unexpectedly during a request, the bridge SHALL return an error to the pending request before attempting reconnection.

## Non-Functional Requirements

### NFR-1: Reliability

**NFR-1.1** Reconnection SHALL succeed within 10 seconds in 99% of cases where sandbox is running and healthy.

**NFR-1.2** The bridge SHALL handle at least 100 consecutive reconnection cycles without resource leaks.

### NFR-2: Observability

**NFR-2.1** All connection state transitions SHALL be logged to stderr with timestamps.

**NFR-2.2** Log messages SHALL include: event type, old state, new state, server_pid, elapsed time.

### NFR-3: Performance

**NFR-3.1** Reconnection overhead SHALL be less than 2 seconds for hot-reload scenarios.

**NFR-3.2** Signal file polling SHALL not exceed 10% CPU utilization.

## Acceptance Scenarios

### Scenario 1: Clean Reconnect After Server Restart

**Given** the bridge is connected to MCP server (PID 1234)
**When** the server is restarted via hot-reload (new PID 5678)
**Then** the bridge detects PID change on next request
**And** automatically reconnects to PID 5678
**And** refreshes the tool list
**And** the request succeeds

### Scenario 2: Manual Reconnect After Connection Loss

**Given** the TCP connection is lost (server crash)
**When** the agent calls `reconnect_sandbox`
**Then** the bridge reads the current ready signal
**And** connects to the signaled endpoint
**And** returns success with tool count

### Scenario 3: Reconnect With LazyStart Server

**Given** the sandbox is running but server not started (server_pid is null)
**When** the agent calls `reconnect_sandbox`
**Then** the bridge creates server.trigger
**And** waits for server_pid to appear in signal
**And** connects once server is ready

### Scenario 4: E2E Test Isolation

**Given** the MCP server is running with --e2e-port 9998
**And** an agent is connected on port 9999
**When** an E2E test connects on port 9998
**Then** both connections work independently
**And** UIA operations are serialized
**And** disconnecting E2E does not affect agent

### Scenario 5: Timeout During Reconnect

**Given** the sandbox is not running
**When** the agent calls `reconnect_sandbox` with timeout_seconds=10
**Then** the bridge attempts connection with backoff
**And** after 10 seconds returns a timeout error
**And** error includes diagnostic information

### Scenario 6: Already Connected

**Given** the bridge is connected to server PID 1234
**When** the agent calls `reconnect_sandbox`
**And** the signal still shows PID 1234
**Then** reconnect returns immediately with success
**And** no actual reconnection is performed

## Out of Scope

- Connection pooling for multiple concurrent agent sessions
- Automatic sandbox restart (requires elevation)
- Persistent connection across Claude Code restarts
- WebSocket protocol support

## Dependencies

- Windows Sandbox network stack (9P filesystem, NAT networking)
- FlaUI UIA2 backend (not thread-safe, requires serialization)
- PowerShell 5.1+ on host, PowerShell Core in sandbox

## Operator Decisions

1. **Configurable backoff via env vars?** → No. Use hardcoded defaults.
2. **Health check endpoint?** → Existing `sandbox_status` tool is sufficient. No new endpoint needed.
3. **E2E timeout behavior?** → Use agreed-upon max timeout that both client and server follow. Server can kill requests exceeding the timeout.
4. **Connection pooling?** → Stretch goal. Not needed yet since multi-agent scenarios haven't been required.
