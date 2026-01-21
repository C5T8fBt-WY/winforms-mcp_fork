# Design: Windows Array Scoping

## Overview

This design introduces process-scoped window filtering to reduce token overhead in MCP tool responses. Currently, every response includes ALL visible desktop windows (200-800 tokens). This change scopes the `windows` array to only relevant windows based on context:

1. **Process scoping**: Windows belonging to tracked/active processes only
2. **Error expansion**: Full window list on errors for debugging context
3. **Explicit override**: `includeAllWindows` parameter for full desktop view

**Expected token reduction**: 60-80% for typical single-app workflows.

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Tool Request                                                                 │
│   └── windowTitle: "MyApp"                                                   │
│   └── includeAllWindows: false (default)                                     │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ HandlerBase.GetScopedWindows(context)                                        │
│                                                                              │
│   1. Determine context PIDs:                                                 │
│      ├── From windowTitle/windowHandle → GetWindowThreadProcessId            │
│      ├── From cached elementId → element's owning process                    │
│      └── From SessionManager.TrackedProcesses                                │
│                                                                              │
│   2. Filter windows by PID set                                               │
│      └── WindowManager.GetWindowsByProcessIds(HashSet<int> pids)             │
│                                                                              │
│   3. Return scoped list (or full list on error/explicit request)             │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ Tool Response                                                                │
│   └── success: true                                                          │
│   └── result: { ... }                                                        │
│   └── windows: [ only MyApp windows ]  // Scoped!                            │
│   └── windowScope: "process"           // Indicates scoping applied          │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Components and Interfaces

### 1. SessionManager Extensions

**File:** `src/Rhombus.WinFormsMcp.Server/Program.cs` (SessionManager class)

Add tracked process set and management methods.

```csharp
class SessionManager
{
    // Existing fields...

    /// <summary>
    /// Set of process IDs actively being tracked for window scoping.
    /// Populated by launch_app and attach_to_process, cleared by close_app.
    /// </summary>
    private readonly HashSet<int> _trackedProcessIds = new();

    /// <summary>
    /// Add a process to the tracked set. Called by launch_app and attach_to_process.
    /// </summary>
    public void TrackProcess(int pid)
    {
        _trackedProcessIds.Add(pid);
    }

    /// <summary>
    /// Remove a process from the tracked set. Called by close_app.
    /// Also cleans up any stale PIDs (processes that have exited).
    /// </summary>
    public void UntrackProcess(int pid)
    {
        _trackedProcessIds.Remove(pid);
        CleanupStaleProcesses();
    }

    /// <summary>
    /// Get all currently tracked process IDs (excluding stale ones).
    /// </summary>
    public IReadOnlySet<int> GetTrackedProcessIds()
    {
        CleanupStaleProcesses();
        return _trackedProcessIds;
    }

    /// <summary>
    /// Check if any processes are being tracked.
    /// </summary>
    public bool HasTrackedProcesses => _trackedProcessIds.Count > 0;

    /// <summary>
    /// Remove PIDs for processes that have exited.
    /// </summary>
    private void CleanupStaleProcesses()
    {
        var stale = _trackedProcessIds
            .Where(pid => !IsProcessRunning(pid))
            .ToList();
        foreach (var pid in stale)
        {
            _trackedProcessIds.Remove(pid);
        }
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // Process doesn't exist
        }
    }
}
```

### 2. WindowManager Extensions

**File:** `src/Rhombus.WinFormsMcp.Server/Automation/WindowManager.cs`

Add process ID lookup and filtered enumeration.

```csharp
public class WindowManager
{
    // Existing methods...

    /// <summary>
    /// Get the process ID that owns a window handle.
    /// </summary>
    public int? GetWindowProcessId(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;

        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid > 0 ? (int)pid : null;
    }

    /// <summary>
    /// Get the process ID for a window found by handle or title.
    /// Returns null if window not found.
    /// </summary>
    public int? GetProcessIdForWindow(string? windowHandle, string? windowTitle)
    {
        var window = FindWindow(windowHandle, windowTitle);
        if (window == null)
            return null;

        return GetWindowProcessId(window.HandlePtr);
    }

    /// <summary>
    /// Get all visible windows belonging to the specified process IDs.
    /// </summary>
    public List<WindowInfo> GetWindowsByProcessIds(IReadOnlySet<int> processIds)
    {
        if (processIds.Count == 0)
            return new List<WindowInfo>();

        var windows = new List<WindowInfo>();
        var foregroundHwnd = GetForegroundWindow();

        EnumWindows((hwnd, lParam) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            // Check if window belongs to one of our processes
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!processIds.Contains((int)pid))
                return true;

            // ... rest of existing GetAllWindows logic ...
            var titleLength = GetWindowTextLength(hwnd);
            if (titleLength == 0)
                return true;

            var sb = new StringBuilder(titleLength + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (string.IsNullOrWhiteSpace(title))
                return true;

            if (!GetWindowRect(hwnd, out RECT rect))
                return true;

            var width = rect.right - rect.left;
            var height = rect.bottom - rect.top;
            if (width <= 0 || height <= 0)
                return true;

            windows.Add(new WindowInfo
            {
                HandlePtr = hwnd,
                Title = title,
                AutomationId = "",
                Bounds = new WindowBounds
                {
                    X = rect.left,
                    Y = rect.top,
                    Width = width,
                    Height = height
                },
                IsActive = hwnd == foregroundHwnd
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
```

### 3. WindowScope Context

**File:** `src/Rhombus.WinFormsMcp.Server/Models/WindowScopeContext.cs` (New)

Encapsulates window scoping decisions.

```csharp
namespace Rhombus.WinFormsMcp.Server.Models;

/// <summary>
/// Context for determining window scoping behavior.
/// </summary>
public class WindowScopeContext
{
    /// <summary>
    /// Process IDs to include in scoped response. Empty means use tracked processes.
    /// </summary>
    public HashSet<int> ProcessIds { get; } = new();

    /// <summary>
    /// If true, return all windows regardless of process IDs.
    /// </summary>
    public bool IncludeAllWindows { get; set; }

    /// <summary>
    /// If true, an error occurred and full context should be returned.
    /// </summary>
    public bool IsErrorContext { get; set; }

    /// <summary>
    /// Window handle from the request (if any).
    /// </summary>
    public string? WindowHandle { get; set; }

    /// <summary>
    /// Window title from the request (if any).
    /// </summary>
    public string? WindowTitle { get; set; }

    /// <summary>
    /// Cached element ID from the request (if any).
    /// </summary>
    public string? ElementId { get; set; }

    /// <summary>
    /// Describes how windows were scoped (for response metadata).
    /// </summary>
    public string ScopeDescription => IncludeAllWindows || IsErrorContext
        ? "all"
        : ProcessIds.Count > 0
            ? "process"
            : "tracked";
}
```

### 4. ToolResponse Extensions

**File:** `src/Rhombus.WinFormsMcp.Server/ToolResponse.cs`

Add scoped window support.

```csharp
public class ToolResponse
{
    // Existing properties...

    /// <summary>
    /// Describes how windows were scoped: "all", "process", or "tracked".
    /// </summary>
    [JsonPropertyName("windowScope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WindowScope { get; set; }

    /// <summary>
    /// Create a successful response with scoped windows.
    /// </summary>
    public static ToolResponse OkScoped(
        object? result,
        WindowManager windowManager,
        SessionManager session,
        WindowScopeContext scope)
    {
        var windows = ResolveWindows(windowManager, session, scope);
        return new ToolResponse
        {
            Success = true,
            Result = result,
            Windows = windows,
            WindowScope = scope.ScopeDescription
        };
    }

    /// <summary>
    /// Create a failure response with full window context (errors always expand).
    /// </summary>
    public static ToolResponse FailWithContext(
        string error,
        WindowManager windowManager)
    {
        return new ToolResponse
        {
            Success = false,
            Error = error,
            Windows = windowManager.GetAllWindows(),
            WindowScope = "all" // Errors always show all windows
        };
    }

    private static List<WindowInfo> ResolveWindows(
        WindowManager windowManager,
        SessionManager session,
        WindowScopeContext scope)
    {
        // Error or explicit all-windows request
        if (scope.IsErrorContext || scope.IncludeAllWindows)
            return windowManager.GetAllWindows();

        // Build process ID set
        var pids = new HashSet<int>(scope.ProcessIds);

        // Add process from window handle/title if specified
        if (!string.IsNullOrEmpty(scope.WindowHandle) || !string.IsNullOrEmpty(scope.WindowTitle))
        {
            var pid = windowManager.GetProcessIdForWindow(scope.WindowHandle, scope.WindowTitle);
            if (pid.HasValue)
                pids.Add(pid.Value);
        }

        // Add tracked processes if no explicit scope
        if (pids.Count == 0)
        {
            foreach (var pid in session.GetTrackedProcessIds())
                pids.Add(pid);
        }

        // If still no PIDs, return all (discovery mode)
        if (pids.Count == 0)
            return windowManager.GetAllWindows();

        return windowManager.GetWindowsByProcessIds(pids);
    }
}
```

### 5. HandlerBase Extensions

**File:** `src/Rhombus.WinFormsMcp.Server/Handlers/HandlerBase.cs`

Add scoped response helpers.

```csharp
internal abstract class HandlerBase : IToolHandler
{
    // Existing members...

    /// <summary>
    /// Create a successful response with scoped windows based on request context.
    /// </summary>
    protected Task<JsonElement> SuccessScoped(
        WindowScopeContext scope,
        params (string key, object? value)[] properties)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in properties)
            result[key] = value;

        return Task.FromResult(
            ToolResponse.OkScoped(result, Windows, Session, scope).ToJsonElement());
    }

    /// <summary>
    /// Create an error response with full window context.
    /// </summary>
    protected Task<JsonElement> ErrorWithContext(string message)
    {
        return Task.FromResult(
            ToolResponse.FailWithContext(message, Windows).ToJsonElement());
    }

    /// <summary>
    /// Extract window scope context from tool arguments.
    /// </summary>
    protected WindowScopeContext GetScopeContext(JsonElement args)
    {
        return new WindowScopeContext
        {
            IncludeAllWindows = GetBoolArg(args, "includeAllWindows", false),
            WindowHandle = GetStringArg(args, "windowHandle"),
            WindowTitle = GetStringArg(args, "windowTitle"),
            ElementId = GetStringArg(args, "elementId") ?? GetStringArg(args, "elementPath")
        };
    }
}
```

## Data Flow

### Tool Execution with Scoping

```
1. Tool Request arrives
   └── Parse args including windowTitle, windowHandle, includeAllWindows

2. Handler extracts scope context
   └── GetScopeContext(args) → WindowScopeContext

3. Tool executes business logic
   ├── Success path: Use SuccessScoped(scope, properties)
   └── Error path: Use ErrorWithContext(message)

4. ToolResponse resolves windows
   ├── Error? → Return all windows
   ├── includeAllWindows? → Return all windows
   ├── windowTitle/Handle? → Resolve PID → Filter by PID
   ├── Tracked processes? → Filter by tracked PIDs
   └── No context? → Return all windows (discovery)

5. Response serialized with windowScope indicator
```

### Process Tracking Flow

```
1. Agent calls launch_app(path: "MyApp.exe")
   └── ProcessHandlers.LaunchApp:
       ├── Launch process → PID 1234
       ├── Session.TrackProcess(1234)      // NEW
       └── Return response with scoped windows

2. Agent calls click_element(elementId: "elem_1")
   └── ElementHandlers.ClickElement:
       ├── Get element's window handle
       ├── Get window's process ID → 1234
       ├── Scope context includes PID 1234
       └── Return response with MyApp windows only

3. Agent calls close_app(pid: 1234)
   └── ProcessHandlers.CloseApp:
       ├── Close process
       ├── Session.UntrackProcess(1234)    // NEW
       └── Return response (empty windows since no tracked processes)
```

## API Changes

### New Parameter: includeAllWindows

Added to all tools that return windows in their response.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `includeAllWindows` | boolean | false | When true, return all desktop windows regardless of scoping |

**Tools affected** (partial list):
- `mouse_click`, `mouse_drag`, `touch_tap`, `touch_drag`
- `click_element`, `type_text`, `find_element`
- `take_screenshot`, `get_ui_tree`
- `launch_app`, `close_app`, `attach_to_process`

### New Response Field: windowScope

```json
{
    "success": true,
    "result": { ... },
    "windows": [ ... ],
    "windowScope": "process"  // NEW: "all" | "process" | "tracked"
}
```

Values:
- `"all"`: Full desktop window list (error, explicit request, or discovery)
- `"process"`: Scoped to process from windowTitle/Handle
- `"tracked"`: Scoped to all processes tracked by session

### Tools with Special Behavior

| Tool | Behavior |
|------|----------|
| `launch_app` | Adds launched PID to tracked set; scopes to new app |
| `attach_to_process` | Adds PID to tracked set; scopes to attached app |
| `close_app` | Removes PID from tracked set |
| `list_sandbox_apps` | Always returns all windows (discovery tool) |
| `get_capabilities` | Always returns all windows (discovery tool) |
| Any error | Always returns all windows (debugging context) |

## Error Handling

### Error Expansion Behavior

All error conditions automatically expand to full window context:

```csharp
// In handler
catch (Exception ex)
{
    return ErrorWithContext(ex.Message);  // Full windows for debugging
}
```

**Rationale**: When errors occur, agents need maximum context to recover. A "window not found" error is useless without knowing what windows ARE available.

### Window Resolution Errors

| Error | Windows Returned | Additional Context |
|-------|-----------------|-------------------|
| Window not found | All | `partialMatches` if similar titles exist |
| Multiple matches | All | `matches` array with all matching windows |
| Element not found | All | (standard error response) |
| Process exited | Tracked (minus stale) | Warning about stale PID |

### Stale Process Handling

When a tracked process exits unexpectedly:

1. `GetTrackedProcessIds()` calls `CleanupStaleProcesses()`
2. Stale PIDs removed from tracked set
3. Next response reflects actual running processes
4. No explicit error (graceful degradation)

## Testing Strategy

### Unit Tests

**SessionManager tracking tests:**
- `TrackProcess_AddsToSet`
- `UntrackProcess_RemovesFromSet`
- `GetTrackedProcessIds_ExcludesStaleProcesses`
- `CleanupStaleProcesses_RemovesExitedProcesses`

**WindowManager filtering tests:**
- `GetWindowsByProcessIds_FiltersCorrectly`
- `GetWindowsByProcessIds_EmptySetReturnsEmpty`
- `GetProcessIdForWindow_ReturnsCorrectPid`
- `GetProcessIdForWindow_NotFoundReturnsNull`

**ToolResponse scoping tests:**
- `OkScoped_WithProcessIds_FiltersWindows`
- `OkScoped_WithNoContext_ReturnsAllWindows`
- `FailWithContext_AlwaysReturnsAllWindows`
- `WindowScope_SetCorrectly`

### Integration Tests

**Single-app workflow:**
1. Launch TestApp
2. Verify windows array contains only TestApp window
3. Click button
4. Verify response still scoped to TestApp
5. Close TestApp
6. Verify windows array empty (or all if no tracking)

**Multi-app workflow:**
1. Launch TestApp (PID 1234)
2. Verify scoped to 1234
3. Launch Calculator (PID 5678)
4. Verify scoped to both 1234 and 5678
5. Close Calculator
6. Verify scoped to only 1234

**Error expansion:**
1. Launch TestApp
2. Request non-existent window
3. Verify error response has ALL windows (not scoped)

**Explicit override:**
1. Launch TestApp
2. Call `mouse_click` with `includeAllWindows: true`
3. Verify response has ALL windows despite TestApp focus

### Performance Tests

- Measure response size (bytes) before/after scoping
- Measure token count reduction
- Verify filtering adds <5ms latency
- Verify no memory leaks with process tracking

## Migration Path

### Backwards Compatibility

1. **No breaking changes**: `windows` array location unchanged
2. **New field only**: `windowScope` is additive
3. **Default safe**: Without `includeAllWindows`, behavior is "smart" but safe
4. **Error behavior**: Errors always return full context (most conservative)

### Rollout

Phase 1: Add infrastructure (WindowScopeContext, SessionManager extensions)
Phase 2: Update ToolResponse with scoped methods
Phase 3: Update handlers one category at a time
Phase 4: Add `includeAllWindows` parameter to tool definitions
Phase 5: Document new behavior in MCP_TOOLS.md

### Opt-Out

Agents that depend on full window lists can:
1. Pass `includeAllWindows: true` on each request
2. Or use discovery tools (`get_capabilities`) for full enumeration
