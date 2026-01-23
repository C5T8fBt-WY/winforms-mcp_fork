# CLAUDE.md

## Project Overview

**Rhombus.WinFormsMcp** - MCP server for headless WinForms automation using FlaUI (UIA2 backend).

## Commands

```bash
dotnet build Rhombus.WinFormsMcp.sln                    # Build
dotnet test Rhombus.WinFormsMcp.sln                     # Test
dotnet run --project src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj  # Run server
dotnet run --project src/Rhombus.WinFormsMcp.TestApp/Rhombus.WinFormsMcp.TestApp.csproj # Run test app
dotnet publish src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj -c Release -o publish
```

## Architecture

| Component | Path | Purpose |
|-----------|------|---------|
| Server | `src/Rhombus.WinFormsMcp.Server/` | MCP server with JSON-RPC 2.0 over stdio/TCP |
| Handlers | `src/Rhombus.WinFormsMcp.Server/Handlers/` | Tool implementations (11 tools) |
| Protocol | `src/Rhombus.WinFormsMcp.Server/Protocol/` | JSON-RPC parsing, tool definitions |
| Automation | `src/Rhombus.WinFormsMcp.Server/Automation/` | FlaUI wrappers, window management |
| Script | `src/Rhombus.WinFormsMcp.Server/Script/` | Batch script execution with variable interpolation |
| Services | `src/Rhombus.WinFormsMcp.Server/Services/` | Extracted testable services (DI-ready) |
| Utilities | `src/Rhombus.WinFormsMcp.Server/Utilities/` | Static helpers (ArgHelpers, CoordinateMath, etc.) |
| Input | `src/Rhombus.WinFormsMcp.Server/Input/` | Touch, pen, mouse input injection wrappers |
| Abstractions | `src/Rhombus.WinFormsMcp.Server/Abstractions/` | Interfaces for testability (ITimeProvider, etc.) |
| Interop | `src/Rhombus.WinFormsMcp.Server/Interop/` | Win32 P/Invoke declarations |
| TestApp | `src/Rhombus.WinFormsMcp.TestApp/` | Sample WinForms app for testing |
| Tests | `tests/Rhombus.WinFormsMcp.Tests/` | NUnit test suite |

**Stack**: .NET 8.0-windows, FlaUI 4.0.0 (UIA2), NUnit 3.14.0

## Minimal API (11 Tools)

Consolidated from 52 legacy tools for ~90% token reduction.

### Sandbox Tools (3)

| Tool | Description |
|------|-------------|
| `launch_app_sandboxed` | Launch app in Windows Sandbox |
| `close_sandbox` | Close Windows Sandbox |
| `list_sandbox_apps` | List processes in sandbox |

### Core Tools (8)

| Tool | Description | Replaces |
|------|-------------|----------|
| `app` | Application lifecycle: launch, attach, close, info | launch_app, attach_to_process, close_app, get_process_info |
| `find` | Element discovery with tree traversal | find_element, get_ui_tree, list_elements, element_exists, wait_for_element, get_element_at_point, etc. |
| `click` | Unified click/tap with mouse/touch/pen | click_element, mouse_click, touch_tap, pen_tap |
| `type` | Text input and keyboard operations | type_text, set_value, send_keys |
| `drag` | Drag/stroke with path support | drag_drop, mouse_drag, touch_drag, pen_stroke |
| `gesture` | Multi-touch: pinch, rotate, custom | pinch_zoom, rotate_gesture, multi_touch_gesture |
| `screenshot` | Capture window or element | take_screenshot |
| `script` | Batch operations with variable interpolation | run_script |

### Tool Usage Examples

```json
// Launch application
{"tool": "app", "args": {"action": "launch", "path": "C:\\app.exe"}}

// Find element
{"tool": "find", "args": {"automationId": "btnSubmit"}}

// Find UI tree
{"tool": "find", "args": {"at": "root", "recursive": true, "depth": 3}}

// Click element
{"tool": "click", "args": {"target": "elem_1"}}

// Click coordinates with touch
{"tool": "click", "args": {"x": 100, "y": 200, "input": "touch"}}

// Type text
{"tool": "type", "args": {"text": "Hello", "target": "elem_2", "clear": true}}

// Drag path
{"tool": "drag", "args": {"path": [{"x": 100, "y": 100}, {"x": 200, "y": 200}], "input": "pen"}}

// Pinch gesture
{"tool": "gesture", "args": {"type": "pinch", "center": {"x": 400, "y": 300}, "start_distance": 200, "end_distance": 50}}

// Screenshot
{"tool": "screenshot", "args": {"target": "elem_1", "file": "C:\\capture.png"}}

// Batch script
{"tool": "script", "args": {"steps": [
  {"id": "btn", "tool": "find", "args": {"name": "OK"}},
  {"tool": "click", "args": {"target": "$btn.id"}}
]}}
```

### Services Architecture

SessionManager is a facade delegating to extracted services for testability:

| Service | Interface | Purpose |
|---------|-----------|---------|
| ElementCache | IElementCache | Cache AutomationElements with staleness detection |
| ProcessContext | IProcessContext | Track launched apps by executable path |
| ProcessTracker | IProcessTracker | Track PIDs for window scoping |
| SnapshotCache | ISnapshotCache | LRU cache for UI tree snapshots |
| EventService | IEventService | Queue UI events for async retrieval |
| ConfirmationService | IConfirmationService | Pending confirmations for destructive actions |
| TreeExpansionService | ITreeExpansionService | Mark elements for tree expansion |

All services are thread-safe and injectable for testing.

### Handler Architecture

| Handler | Tools |
|---------|-------|
| SandboxHandlers | `launch_app_sandboxed`, `close_sandbox`, `list_sandbox_apps` |
| AppHandler | `app` |
| FindHandler | `find` |
| ClickHandler | `click` |
| TypeHandler | `type` |
| DragHandler | `drag` |
| GestureHandler | `gesture` |
| ScreenshotHandler | `screenshot` |
| ScriptHandler | `script` |

### Window Scoping

Tool responses include a `windows` array with visible windows scoped to tracked processes.

## Windows Sandbox Deployment

### Workspace Structure

```
C:\WinFormsMcpSandboxWorkspace\
├── Server/           # MCP server binaries (read-only in sandbox)
├── App/              # Test app binaries (read-only in sandbox)
├── DotNet/           # .NET runtime (read-only in sandbox)
├── Shared/           # Communication folder (read-write)
│   ├── server.trigger    # Touch to hot-reload server
│   ├── app.trigger       # Touch to hot-reload app
│   ├── mcp-ready.signal  # Written by bootstrap when ready
│   └── *.png             # Screenshots saved here
└── sandbox-dev.wsb   # Sandbox configuration
```

### Launching the Sandbox

**From Windows PowerShell** (not WSL):
```powershell
# The bridge handles everything: launch, wait, connect, port forwarding
C:\WinFormsMcpSandboxWorkspace\mcp-sandbox-bridge.ps1 -SetupPortForwarding
```

Or copy the bridge from the repo first:
```powershell
Copy-Item '\\wsl.localhost\Ubuntu\home\jhedin\workspace\magpie-craft\winforms-mcp\mcp-sandbox-bridge.ps1' 'C:\WinFormsMcpSandboxWorkspace\' -Force
```

### Deploying Server Updates

```powershell
# 1. Build and publish from Windows (via UNC path to WSL)
cd '\\wsl.localhost\Ubuntu\home\jhedin\workspace\magpie-craft\winforms-mcp'
dotnet publish src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj -c Release -o 'C:\WinFormsMcpSandboxWorkspace\Server'

# 2. Trigger hot-reload (if sandbox is running)
New-Item -Path 'C:\WinFormsMcpSandboxWorkspace\Shared\server.trigger' -ItemType File -Force
```

### How Bootstrap Works

The `sandbox/bootstrap.ps1` script inside the sandbox:
1. Copies files from mapped folders (`C:\Server`) to local folders (`C:\LocalServer`)
2. Runs `Unblock-File` on local copies (removes Mark of the Web)
3. Runs server via `dotnet.exe` (trusted by WDAC) loading the DLL
4. Monitors for trigger files and hot-reloads on change

### Key Files

| File | Purpose |
|------|---------|
| `mcp-sandbox-bridge.ps1` | Bridge script - launches sandbox, connects TCP, forwards MCP |
| `sandbox/sandbox-dev.wsb` | Sandbox configuration with mapped folders |
| `sandbox/bootstrap.ps1` | Runs inside sandbox - manages server/app lifecycle |

## CI/CD

- Version in `VERSION` file, auto-bumped on master commits
- Publishes to NuGet (`Rhombus.WinFormsMcp`) and NPM (`@rhom6us/winforms-mcp`)
