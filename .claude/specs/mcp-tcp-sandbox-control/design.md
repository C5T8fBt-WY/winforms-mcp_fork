# Design: MCP TCP Sandbox Control

## Overview

This design adds window-relative coordinate support and window context responses to the existing MCP server, plus updates to bootstrap.ps1 for LazyStart mode. The goal is to enable Claude Code agents to control WinForms applications running in Windows Sandbox via TCP, with a smooth hot-reload development loop.

**Key changes:**
1. All coordinate-based tools accept `windowHandle` or `windowTitle` and translate to screen coordinates
2. Every tool response includes a `windows` array with current window state
3. Bootstrap.ps1 supports `-LazyStart` mode for minimal sandbox startup
4. App lifecycle managed via MCP tools (`launch_app`/`close_app`), not trigger files

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ Host (WSL/Windows)                                              │
│                                                                 │
│  ┌─────────────┐     ┌──────────────────────────────────────┐  │
│  │ Claude Code │     │ C:\TransportTest\                    │  │
│  │   Agent     │     │   ├── Server\  (MCP binaries)        │  │
│  │             │     │   ├── App\     (WinForms app)        │  │
│  │ TCP Client  │────▶│   ├── Shared\  (signals, screenshots)│  │
│  └─────────────┘     │   └── DotNet\  (.NET runtime)        │  │
│         │            └──────────────────────────────────────┘  │
│         │                           │                          │
│         │                    Mapped Folders                    │
│         │                           │                          │
└─────────│───────────────────────────│──────────────────────────┘
          │                           │
          │ TCP :9999                 ▼
┌─────────▼───────────────────────────────────────────────────────┐
│ Windows Sandbox                                                 │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ bootstrap.ps1                                            │   │
│  │   - LazyStart mode: just watches for triggers            │   │
│  │   - Starts MCP server when server.trigger appears        │   │
│  │   - Writes mcp-ready.signal with tcp_ip, tcp_port        │   │
│  └─────────────────────────────────────────────────────────┘   │
│                          │                                      │
│                          ▼                                      │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ MCP Server (TCP mode)                                    │   │
│  │   - Listens on 0.0.0.0:9999                              │   │
│  │   - JSON-RPC 2.0 over TCP                                │   │
│  │   - Manages app lifecycle via launch_app/close_app       │   │
│  └─────────────────────────────────────────────────────────┘   │
│                          │                                      │
│                          ▼                                      │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ WinForms Application                                     │   │
│  │   - Launched via launch_app tool                         │   │
│  │   - Controlled via FlaUI automation                      │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## Components and Interfaces

### 1. Window Manager (New Component)

**File:** `src/Rhombus.WinFormsMcp.Server/Automation/WindowManager.cs`

Centralizes window enumeration and coordinate translation.

```csharp
public class WindowManager
{
    // Get all visible windows for the current session
    public List<WindowInfo> GetAllWindows();

    // Find window by handle or title
    public WindowInfo? FindWindow(string? windowHandle, string? windowTitle);

    // Translate window-relative coords to screen coords
    public (int screenX, int screenY) TranslateCoordinates(
        WindowInfo window, int windowX, int windowY);
}

public class WindowInfo
{
    public string Handle { get; set; }      // "0x1A2B3C"
    public string Title { get; set; }       // "Calculator"
    public string AutomationId { get; set; } // "MainWindow"
    public Rectangle Bounds { get; set; }   // Screen position/size
    public bool IsActive { get; set; }      // Has focus
}
```

### 2. Tool Response Wrapper (New)

**File:** `src/Rhombus.WinFormsMcp.Server/ToolResponse.cs`

Standardizes response format with window context.

```csharp
public class ToolResponse
{
    public bool Success { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
    public List<WindowInfo> Windows { get; set; }

    public static ToolResponse Ok(object? result, WindowManager wm)
        => new() { Success = true, Result = result, Windows = wm.GetAllWindows() };

    public static ToolResponse Fail(string error, WindowManager wm)
        => new() { Success = false, Error = error, Windows = wm.GetAllWindows() };
}
```

### 3. Updated Tool Handlers

**File:** `src/Rhombus.WinFormsMcp.Server/Program.cs`

Each coordinate-based tool handler updated to:
1. Accept `windowHandle` or `windowTitle` parameter
2. Resolve to `WindowInfo` via `WindowManager`
3. Translate coordinates before passing to `InputInjection`
4. Return `ToolResponse` with `windows` array

**Example - mouse_click handler:**

```csharp
case "mouse_click":
{
    var windowHandle = args.GetValueOrDefault("windowHandle")?.ToString();
    var windowTitle = args.GetValueOrDefault("windowTitle")?.ToString();
    var x = Convert.ToInt32(args["x"]);
    var y = Convert.ToInt32(args["y"]);

    var window = _windowManager.FindWindow(windowHandle, windowTitle);
    if (window == null)
        return ToolResponse.Fail($"Window not found", _windowManager);

    var (screenX, screenY) = _windowManager.TranslateCoordinates(window, x, y);
    InputInjection.MouseClick(screenX, screenY, doubleClick);

    return ToolResponse.Ok(null, _windowManager);
}
```

### 4. Bootstrap.ps1 Updates

**File:** `src/Rhombus.WinFormsMcp.Server/bootstrap.ps1`

Add `-LazyStart` parameter:

```powershell
param(
    [string]$SharedFolder = "C:\Shared",
    [switch]$EnableTcp,
    [int]$TcpPort = 9999,
    [switch]$LazyStart  # NEW: Skip auto-starting server
)

# Only auto-start if not in lazy mode
if (-not $LazyStart) {
    $ServerProcess = Start-ServerProcess
    Start-Sleep -Seconds 2
}

# Always write signal (even with null PIDs in lazy mode)
Update-ReadySignal

# Always enter trigger watch loop
while ($true) {
    Handle-Triggers
    Start-Sleep -Seconds 1
}
```

### 5. Signal File Format

**File:** `C:\Shared\mcp-ready.signal`

```json
{
    "tcp_ip": "172.23.144.1",
    "tcp_port": 9999,
    "server_pid": 1234,
    "ready": true
}
```

- `server_pid: null` indicates server not yet started (lazy mode)
- Agent creates `server.trigger` to start server
- Agent monitors signal for `server_pid` change after trigger

## Data Models

### WindowInfo Schema

```json
{
    "handle": "0x1A2B3C",
    "title": "Calculator - Result",
    "automationId": "MainWindow",
    "bounds": {
        "x": 100,
        "y": 100,
        "width": 320,
        "height": 480
    },
    "isActive": true
}
```

### Tool Response Schema

```json
{
    "success": true,
    "result": { /* tool-specific */ },
    "error": null,
    "windows": [ /* WindowInfo[] */ ]
}
```

### Coordinate System

- **Window-relative:** (0, 0) is top-left of window client area
- **Screen:** (0, 0) is top-left of primary monitor
- **Translation:** `screenX = window.bounds.x + windowX`

## Error Handling

### Window Resolution Errors

| Error | Condition | Response |
|-------|-----------|----------|
| `Window not found` | No match for handle/title | Include `partialMatches` if available |
| `Multiple windows match` | Title matches >1 window | Include `matches` array with handles |
| `Window closed` | Handle valid but window gone | Clear error, include current windows |

### Coordinate Errors

| Error | Condition | Response |
|-------|-----------|----------|
| `Coordinates outside window` | x/y exceed bounds | Warning but proceed (user may want edge) |
| `Window minimized` | Can't get valid bounds | Error, suggest `focus_window` first |

### Connection Errors

| Error | Condition | Agent Action |
|-------|-----------|--------------|
| TCP refused | Server not running | Check signal, create trigger, retry |
| TCP timeout | Server hung | Restart via trigger |
| Malformed JSON | Protocol error | Log and retry request |

## Testing Strategy

### Unit Tests

**WindowManager tests:**
- `FindWindow_ByHandle_ReturnsCorrectWindow`
- `FindWindow_ByTitle_SubstringMatch`
- `FindWindow_MultipleMatches_ReturnsError`
- `TranslateCoordinates_AddsWindowOffset`
- `GetAllWindows_ReturnsOnlyVisible`

**ToolResponse tests:**
- `Ok_IncludesWindowList`
- `Fail_IncludesWindowListAndError`
- `Serialization_MatchesExpectedSchema`

### Integration Tests

**Coordinate translation:**
- Launch test app at known position
- Call `mouse_click` with window-relative coords
- Verify click hit expected screen position

**Window enumeration:**
- Launch test app
- Verify it appears in `windows` response
- Close app
- Verify it disappears from `windows` response

**Multi-window:**
- Open dialog from main window
- Verify both windows in response
- Verify `isActive` is correct

### E2E Tests

**Full workflow:**
1. Start sandbox (or verify running)
2. Connect TCP
3. Launch app
4. List elements
5. Click button
6. Verify result
7. Take screenshot
8. Close app

**Hot reload:**
1. Launch app
2. Detect bug (expected value != actual)
3. Close app
4. (Simulate code fix)
5. Re-launch app
6. Verify fix

### Manual Testing

- Bootstrap.ps1 with `-LazyStart` flag
- Trigger file server start
- Signal file updates correctly
- Screenshot saved to shared folder and accessible from host
