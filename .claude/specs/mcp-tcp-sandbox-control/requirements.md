# MCP Control for WinForms Applications

## 1. Introduction

Enable Claude Code agents to control WinForms applications using MCP (Model Context Protocol). Two operating modes are supported:

1. **Sandbox Mode** (default) - Control applications running inside Windows Sandbox via TCP
2. **Direct Mode** - Control applications on the host machine, including attaching to existing processes

**Current State:** The MCP server supports both stdio and TCP transport (`--tcp <port>`). The sandbox bootstrap supports lazy startup via trigger files.

**Goal:** Define clear protocols and workflows for agents to discover, connect to, and control WinForms applications in both modes.

### Mode Selection

The agent infers which mode to use from context:

| User Request | Mode | Reason |
|--------------|------|--------|
| "Test this WinForms app" | Sandbox | Default - safe, isolated |
| "Run the app and click the button" | Sandbox | Default for new app launch |
| "Attach to Notepad" | Direct | Explicit attach request |
| "Connect to the running calculator" | Direct | Explicit connect to existing |
| "Automate the open Excel window" | Direct | Targeting existing process |

**Rule:** Default to Sandbox Mode unless the user explicitly asks to attach/connect to a running application.

## 2. User Stories

### 2.0 Sandbox Mode: Agent ensures sandbox is running
**As a** Claude Code agent
**I want to** ensure a sandbox is available before connecting
**So that** I can automate the full workflow without manual intervention

**Acceptance Criteria:**
- 2.0.1 Agent checks if `mcp-ready.signal` exists
- 2.0.2 If signal exists, agent attempts TCP connection to `tcp_ip:tcp_port`
- 2.0.3 If connection succeeds, agent reuses existing sandbox
- 2.0.4 If signal missing or connection fails, agent launches sandbox via `WindowsSandbox.exe <wsb-path>`
- 2.0.5 Agent waits for signal file to appear (timeout: 90 seconds for sandbox boot)
- 2.0.6 Sandbox `.wsb` config path is read from environment or project config
- 2.0.7 Agent never shuts down sandbox - it persists for faster subsequent tests

**Note:** User manually closes sandbox when done with dev session, or it persists until system reboot.

### 2.1 Sandbox Mode: Agent discovers sandbox endpoint
**As a** Claude Code agent
**I want to** discover the TCP endpoint of a running sandbox MCP server
**So that** I can establish a connection without hardcoded IPs

**Acceptance Criteria:**
- 2.1.1 Agent reads `mcp-ready.signal` from shared folder to get `tcp_ip` and `tcp_port`
- 2.1.2 Signal file contains valid JSON with connection details
- 2.1.3 Agent detects when sandbox is not ready (`server_pid: null`)
- 2.1.4 Agent can trigger server start via `server.trigger` file

### 2.2 Sandbox Mode: Agent establishes TCP connection
**As a** Claude Code agent
**I want to** connect to the sandbox MCP server via TCP
**So that** I can send commands to control the sandboxed application

**Acceptance Criteria:**
- 2.2.1 Agent opens TCP socket to `tcp_ip:tcp_port`
- 2.2.2 Agent sends MCP `initialize` request and receives valid response
- 2.2.3 Agent sends `notifications/initialized` notification (no response expected)
- 2.2.4 Connection remains open for subsequent commands

### 2.3 Direct Mode: Agent connects via stdio
**As a** Claude Code agent
**I want to** connect to the MCP server running on the host
**So that** I can control applications without sandbox overhead

**Acceptance Criteria:**
- 2.3.1 Agent launches MCP server process with stdio transport
- 2.3.2 Agent communicates via stdin/stdout JSON-RPC
- 2.3.3 MCP handshake completes (`initialize` + `notifications/initialized`)

### 2.4 Direct Mode: Agent attaches to existing process
**As a** Claude Code agent
**I want to** attach to an already-running Windows application
**So that** I can automate existing applications without launching them

**Acceptance Criteria:**
- 2.4.1 Agent calls `attach_to_process` with process ID or window title
- 2.4.2 Server locates running process and returns success with PID
- 2.4.3 Agent can then use UI automation tools on the attached process
- 2.4.4 Server returns clear error if process not found

### 2.5 Both Modes: Agent launches new application
**As a** Claude Code agent
**I want to** launch a WinForms application
**So that** I can test it from a known starting state

**Acceptance Criteria:**
- 2.5.1 Agent calls `launch_app` with executable path
- 2.5.2 Server returns `pid` and `processName` on success
- 2.5.3 In sandbox mode, path is relative to sandbox (`C:\App\...`)
- 2.5.4 In direct mode, path is on host filesystem

### 2.6 Both Modes: Agent discovers UI elements
**As a** Claude Code agent
**I want to** list and find UI elements in the running application
**So that** I can interact with specific controls

**Acceptance Criteria:**
- 2.6.1 Agent calls `list_elements` with window title to enumerate controls
- 2.6.2 Agent calls `find_element` with `automationId`, `name`, or `controlType`
- 2.6.3 Agent calls `get_window_bounds` to get window position/size
- 2.6.4 Agent receives element references for subsequent actions

### 2.7 Both Modes: Agent interacts with UI elements
**As a** Claude Code agent
**I want to** click, type, and manipulate UI elements
**So that** I can exercise application functionality

**Acceptance Criteria:**
- 2.7.1 Agent calls `click_element` to click buttons/controls
- 2.7.2 Agent calls `type_text` to enter text in fields
- 2.7.3 Agent calls `mouse_drag` with window title and window-relative coordinates
- 2.7.4 Agent calls `take_screenshot` to capture visual state
- 2.7.5 All coordinate-based tools accept window-relative coordinates; MCP server translates to screen coordinates

### 2.8 Sandbox Mode: Agent triggers server hot reload
**As a** Claude Code agent
**I want to** restart the MCP server without restarting sandbox
**So that** I can test server code changes quickly

**Acceptance Criteria:**
- 2.8.1 Agent creates `server.trigger` file to restart MCP server
- 2.8.2 Agent monitors `mcp-ready.signal` for updated `server_pid`
- 2.8.3 Agent reconnects TCP after server restart
- 2.8.4 Agent re-launches app via `launch_app` MCP tool after reconnecting

**Note:** App lifecycle is managed via MCP tools (`launch_app`, `close_app`), not trigger files. The `app.trigger` mechanism is deprecated.

### 2.9 Direct Mode: Agent closes application
**As a** Claude Code agent
**I want to** close the application under test
**So that** I can clean up after testing

**Acceptance Criteria:**
- 2.9.1 Agent calls `close_app` with PID to terminate gracefully
- 2.9.2 Agent can use `force: true` for immediate termination
- 2.9.3 Works for both launched and attached processes

## 3. Non-Functional Requirements

### 3.1 Performance
- TCP latency <50ms for local connections
- Stdio latency <20ms (no network overhead)
- Command round-trip <200ms for typical operations
- Hot reload completes within 5 seconds

### 3.2 Reliability
- Connection failures provide clear error messages
- Server handles malformed JSON gracefully
- Notifications don't block protocol flow
- Stale element references return actionable errors

### 3.3 Security
- TCP server binds to specific IP (not 0.0.0.0 in production)
- Sandbox mode: server only accessible from host
- Direct mode: localhost only by default
- Firewall rules auto-created in sandbox

## 4. Edge Cases

### 4.1 Sandbox not ready
- **When** agent reads signal with `server_pid: null`
- **Then** agent triggers server start and waits for updated signal

### 4.2 TCP connection refused
- **When** TCP connection fails
- **Then** agent checks ready signal, retries with backoff

### 4.3 Process not found (attach)
- **When** `attach_to_process` can't find target
- **Then** server returns error with available process hints

### 4.4 Server restart during operation
- **When** server restarts mid-operation (hot reload)
- **Then** agent detects disconnect, re-reads signal, reconnects

### 4.5 Stale element reference
- **When** agent uses old element ID after app restart
- **Then** server returns error, agent re-discovers elements

### 4.6 Application crash
- **When** target application crashes
- **Then** subsequent MCP commands targeting that window/process fail with clear error
- **Recovery:** agent calls `launch_app` again to restart

### 4.7 Window not found
- **When** `list_elements` or `get_window_bounds` can't find window
- **Then** server returns error with partial matches if available

## 5. Out of Scope

- **Multi-client connections** - Single agent per server instance
- **Authentication/encryption** - Not needed for local/sandbox use
- **Remote machine access** - Only local or sandbox connections
- **Bidirectional notifications** - Server doesn't push events
- **Session persistence** - Each connection starts fresh
- **Cross-process element references** - Elements scoped to one process
- **Sandbox auto-shutdown** - Agent never shuts down sandbox; user closes manually
- **Build/deploy synchronization** - Agent manages build externally; no signal needed before `launch_app`
- **App trigger files** - Deprecated; use `launch_app` MCP tool instead

### Future Enhancements

- **Window summaries in response** - Include element counts or top-level children in `windows` array to help agent choose which window to target. For now, agent calls `list_elements` if it needs more detail about a specific window.

## 6. Implementation Notes

### 6.1 Window-Relative Coordinates (BREAKING CHANGE)

The following tools currently use screen coordinates but should use window-relative coordinates:

| Tool | Current | Required Change |
|------|---------|-----------------|
| `mouse_click` | Screen coords | Add `windowTitle`, use window-relative |
| `mouse_drag` | Screen coords | Add `windowTitle`, use window-relative |
| `mouse_drag_path` | Screen coords | Add `windowTitle`, use window-relative |
| `touch_tap` | Screen coords | Add `windowTitle`, use window-relative |
| `touch_drag` | Screen coords | Add `windowTitle`, use window-relative |
| `pinch_zoom` | Screen coords | Add `windowTitle`, use window-relative |
| `pen_tap` | Screen coords | Add `windowTitle`, use window-relative |
| `pen_stroke` | Screen coords | Add `windowTitle`, use window-relative |

**Implementation:** MCP server calls `get_window_bounds` internally and adds window position to coordinates before injecting input.

### 6.2 Window Context in Every Response

Every tool response should include current window state so the agent always knows what to target:

```json
{
  "success": true,
  "result": { ... },
  "windows": [
    {
      "handle": "0x12345",
      "title": "My App - Document1.txt",
      "automationId": "MainWindow",
      "bounds": { "x": 100, "y": 100, "width": 800, "height": 600 },
      "isActive": true
    },
    {
      "handle": "0x12346",
      "title": "Save As",
      "automationId": "SaveDialog",
      "bounds": { "x": 300, "y": 200, "width": 400, "height": 300 },
      "isActive": false
    }
  ]
}
```

**Window Identification:**
- `handle` - Native HWND as hex string (stable within session)
- `title` - Current window title (may change dynamically)
- `automationId` - Static ID if set by developer (may be empty)
- `isActive` - Whether window has focus

**Window Targeting:**
- Tools accept either `windowHandle` (exact) or `windowTitle` (substring match)
- If `windowTitle` matches multiple windows, return error with list of matches
- Agent uses `handle` from response for precise targeting

### 6.3 Deprecated Features

- `app.trigger` file - Remove from bootstrap.ps1 polling loop
- `app_pid` in signal file - No longer updated (server only tracks `server_pid`)
