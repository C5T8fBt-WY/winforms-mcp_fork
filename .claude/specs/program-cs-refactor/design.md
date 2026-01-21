# Design: Program.cs Refactoring

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         Program.cs                               │
│  - Main entry point                                              │
│  - CLI argument parsing (--tcp, --port)                          │
│  - Exception handlers                                            │
│  - Creates AutomationServer                                      │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                      AutomationServer                            │
│  - Orchestrates protocol + handlers                              │
│  - Owns SessionManager, WindowManager                            │
│  - Manages TCP/stdio communication                               │
│  - Routes tool calls to handlers                                 │
└─────────────────────────────────────────────────────────────────┘
           │                    │                    │
           ▼                    ▼                    ▼
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────────┐
│   McpProtocol    │ │   ScriptRunner   │ │   Tool Handlers      │
│                  │ │                  │ │                      │
│ - ParseRequest() │ │ - RunScript()    │ │ ProcessHandlers      │
│ - FormatResponse │ │ - Interpolate()  │ │ ElementHandlers      │
│ - FormatError()  │ │ - ExecuteStep()  │ │ InputHandlers        │
│ - ToolDefs()     │ │                  │ │ ScreenshotHandlers   │
└──────────────────┘ └──────────────────┘ │ WindowHandlers       │
                                          │ ValidationHandlers   │
                                          │ TouchPenHandlers     │
                                          │ SandboxHandlers      │
                                          │ ObservationHandlers  │
                                          └──────────────────────┘
```

## File Structure

```
src/Rhombus.WinFormsMcp.Server/
├── Program.cs                    # Entry point only (~150 LOC)
├── AutomationServer.cs           # Orchestration (~400 LOC)
├── Constants.cs                  # Magic numbers/strings (~100 LOC)
├── Protocol/
│   ├── McpProtocol.cs           # JSON-RPC handling (~300 LOC)
│   └── ToolDefinitions.cs       # GetToolDefinitions (~800 LOC)
├── Handlers/
│   ├── IToolHandler.cs          # Common interface
│   ├── ProcessHandlers.cs       # launch_app, attach_to_process, close_app, get_process_info
│   ├── ElementHandlers.cs       # find_element, click_element, type_text, set_value, get_property
│   ├── InputHandlers.cs         # send_keys, drag_drop, mouse_* tools
│   ├── TouchPenHandlers.cs      # touch_*, pen_*, pinch_zoom, rotate
│   ├── ScreenshotHandlers.cs    # take_screenshot
│   ├── WindowHandlers.cs        # get_window_bounds, focus_window
│   ├── ValidationHandlers.cs    # element_exists, wait_for_element, check_element_state
│   ├── ObservationHandlers.cs   # get_ui_tree, expand_collapse, scroll, capture/compare snapshots
│   ├── SandboxHandlers.cs       # launch_app_sandboxed, close_sandbox, list_sandbox_apps
│   └── AdvancedHandlers.cs      # find_near_anchor, relocate, cache, confirm, capabilities
├── Script/
│   └── ScriptRunner.cs          # run_script implementation (~250 LOC)
├── Automation/                   # Existing (unchanged)
│   ├── AutomationHelper.cs
│   ├── InputInjection.cs
│   ├── SessionManager.cs
│   └── WindowManager.cs
├── Models/                       # Existing (unchanged)
└── Sandbox/                      # Existing (unchanged)
```

## Component Details

### Constants.cs

```csharp
namespace Rhombus.WinFormsMcp.Server;

public static class Constants
{
    // JSON-RPC Error Codes
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;

    // Timeouts (milliseconds)
    public const int DefaultTimeout = 30000;
    public const int DefaultWaitTimeout = 5000;
    public const int ScriptTimeout = 120000;
    public const int ScreenshotDelay = 100;

    // Protocol
    public const string JsonRpcVersion = "2.0";
    public const string ProtocolVersion = "2024-11-05";
    public const string ServerName = "winforms-mcp";
    public const string ServerVersion = "1.0.0";

    // Paths
    public const string SharedDirectory = @"C:\Shared";
    public const string CrashLogFile = "server-crash.log";
}
```

### IToolHandler Interface

```csharp
namespace Rhombus.WinFormsMcp.Server.Handlers;

public interface IToolHandler
{
    /// <summary>
    /// Returns tool names this handler supports.
    /// </summary>
    IEnumerable<string> SupportedTools { get; }

    /// <summary>
    /// Execute a tool by name with given arguments.
    /// </summary>
    Task<JsonElement> ExecuteAsync(string toolName, JsonElement args);
}
```

### Handler Base Class

```csharp
namespace Rhombus.WinFormsMcp.Server.Handlers;

public abstract class HandlerBase : IToolHandler
{
    protected readonly SessionManager Session;
    protected readonly WindowManager Windows;

    protected HandlerBase(SessionManager session, WindowManager windows)
    {
        Session = session;
        Windows = windows;
    }

    public abstract IEnumerable<string> SupportedTools { get; }
    public abstract Task<JsonElement> ExecuteAsync(string toolName, JsonElement args);

    // Shared helper methods
    protected JsonElement Success(object result) => ...;
    protected JsonElement Error(string message) => ...;
    protected JsonElement WithWindows(object result) => ...;
}
```

### ScriptRunner

```csharp
namespace Rhombus.WinFormsMcp.Server.Script;

public class ScriptRunner
{
    private readonly Func<string, JsonElement, Task<JsonElement>> _toolExecutor;

    public ScriptRunner(Func<string, JsonElement, Task<JsonElement>> toolExecutor)
    {
        _toolExecutor = toolExecutor;
    }

    public async Task<JsonElement> RunAsync(JsonElement scriptArgs, CancellationToken ct)
    {
        // Parse steps, options
        // Execute each step via _toolExecutor
        // Handle variable interpolation
        // Track timing, errors
        // Return aggregate result
    }

    private JsonElement InterpolateArgs(JsonElement args, Dictionary<string, JsonElement> results) => ...;
}
```

### AutomationServer (Simplified)

```csharp
public class AutomationServer
{
    private readonly Dictionary<string, IToolHandler> _handlersByTool;
    private readonly McpProtocol _protocol;
    private readonly ScriptRunner _scriptRunner;
    private readonly SemaphoreSlim _uiaLock = new(1, 1);

    public AutomationServer()
    {
        var session = new SessionManager();
        var windows = new WindowManager();

        // Register all handlers
        var handlers = new IToolHandler[]
        {
            new ProcessHandlers(session, windows),
            new ElementHandlers(session, windows),
            new InputHandlers(session, windows),
            // ... etc
        };

        _handlersByTool = handlers
            .SelectMany(h => h.SupportedTools.Select(t => (tool: t, handler: h)))
            .ToDictionary(x => x.tool, x => x.handler);

        _protocol = new McpProtocol(_handlersByTool.Keys);
        _scriptRunner = new ScriptRunner(ExecuteToolAsync);
    }

    private async Task<JsonElement> ExecuteToolAsync(string tool, JsonElement args)
    {
        await _uiaLock.WaitAsync();
        try
        {
            return await _handlersByTool[tool].ExecuteAsync(tool, args);
        }
        finally
        {
            _uiaLock.Release();
        }
    }
}
```

## Migration Strategy

1. **Phase A: Extract Constants** - No behavior change, just move literals
2. **Phase B: Extract McpProtocol** - Move parsing/formatting, keep handlers in place
3. **Phase C: Create Handler Infrastructure** - Add interface, base class
4. **Phase D: Extract Handlers One-by-One** - Move each group, verify tests pass
5. **Phase E: Extract ScriptRunner** - Final extraction
6. **Phase F: Cleanup AutomationServer** - Remove dead code, verify <500 LOC

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Breaking changes during migration | Run E2E tests after each phase |
| Handler dependencies on private state | Pass SessionManager/WindowManager via constructor |
| Circular dependencies | Protocol layer has no handler dependencies |
| Test coverage gaps | Add unit tests for each handler as extracted |

## Decision Records

**DR-1: Keep ToolDefinitions separate from McpProtocol**
- Rationale: Tool definitions are ~800 LOC of JSON schema, deserves own file
- Trade-off: Slightly more files vs. cleaner separation

**DR-2: Use composition over inheritance for handlers**
- Rationale: Handlers share dependencies but not behavior
- Trade-off: Some code duplication in helper methods vs. rigid hierarchy

**DR-3: ScriptRunner receives tool executor function, not handler references**
- Rationale: Preserves UIA lock semantics without ScriptRunner knowing about it
- Trade-off: Indirect invocation vs. tight coupling
