using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Sandbox;

namespace Rhombus.WinFormsMcp.Server;

/// <summary>
/// fnWindowsMCP - MCP Server for WinForms Automation
///
/// This server provides tools for automating WinForms applications in a headless manner.
/// It communicates via JSON-RPC over stdio (compatible with Claude Code).
/// </summary>
class Program
{
    private static AutomationServer? _server;

    static async Task Main(string[] args)
    {
        try
        {
            _server = new AutomationServer();
            await _server.RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }
}

/// <summary>
/// Session manager for tracking automation contexts and element references
/// </summary>
class SessionManager
{
    private readonly Dictionary<string, AutomationElement> _elementCache = new();
    private readonly Dictionary<int, object> _processContext = new();
    private readonly Dictionary<string, TreeSnapshot> _snapshotCache = new();
    private int _nextElementId = 1;
    private AutomationHelper? _automation;
    private SandboxManager? _sandboxManager;
    private StateChangeDetector? _stateChangeDetector;

    public AutomationHelper GetAutomation()
    {
        return _automation ??= new AutomationHelper();
    }

    public SandboxManager GetSandboxManager()
    {
        return _sandboxManager ??= new SandboxManager();
    }

    public StateChangeDetector GetStateChangeDetector()
    {
        return _stateChangeDetector ??= new StateChangeDetector();
    }

    public string CacheElement(AutomationElement element)
    {
        var id = $"elem_{_nextElementId++}";
        _elementCache[id] = element;
        return id;
    }

    public AutomationElement? GetElement(string elementId)
    {
        return _elementCache.TryGetValue(elementId, out var elem) ? elem : null;
    }

    public void ClearElement(string elementId)
    {
        _elementCache.Remove(elementId);
    }

    public void CacheProcess(int pid, object context)
    {
        _processContext[pid] = context;
    }

    public void CacheSnapshot(string snapshotId, TreeSnapshot snapshot)
    {
        _snapshotCache[snapshotId] = snapshot;
    }

    public TreeSnapshot? GetSnapshot(string snapshotId)
    {
        return _snapshotCache.TryGetValue(snapshotId, out var snapshot) ? snapshot : null;
    }

    public void ClearSnapshot(string snapshotId)
    {
        _snapshotCache.Remove(snapshotId);
    }

    public void Dispose()
    {
        _automation?.Dispose();
        _sandboxManager?.Dispose();
    }
}

/// <summary>
/// Core MCP server implementation handling JSON-RPC communication
/// </summary>
class AutomationServer
{
    private readonly Dictionary<string, Func<JsonElement, Task<JsonElement>>> _tools;
    private int _nextId = 1;
    private readonly SessionManager _session = new();

    public AutomationServer()
    {
        _tools = new Dictionary<string, Func<JsonElement, Task<JsonElement>>>
        {
            // Element Tools
            { "find_element", FindElement },
            { "click_element", ClickElement },
            { "type_text", TypeText },
            { "set_value", SetValue },
            { "get_property", GetProperty },

            // Process Tools
            { "launch_app", LaunchApp },
            { "attach_to_process", AttachToProcess },
            { "close_app", CloseApp },

            // Validation Tools
            { "take_screenshot", TakeScreenshot },
            { "element_exists", ElementExists },
            { "wait_for_element", WaitForElement },

            // Interaction Tools
            { "drag_drop", DragDrop },
            { "send_keys", SendKeys },

            // Event Tools
            { "raise_event", RaiseEvent },
            { "listen_for_event", ListenForEvent },

            // Touch/Pen Injection Tools
            { "touch_tap", TouchTap },
            { "touch_drag", TouchDrag },
            { "pinch_zoom", PinchZoom },
            { "pen_stroke", PenStroke },
            { "pen_tap", PenTap },

            // Mouse Input Tools
            { "mouse_drag", MouseDrag },
            { "mouse_drag_path", MouseDragPath },
            { "mouse_click", MouseClick },

            // Window Targeting Tools
            { "get_window_bounds", GetWindowBounds },
            { "focus_window", FocusWindow },

            // Enhanced WPF Tools
            { "click_by_automation_id", ClickByAutomationId },
            { "list_elements", ListElements },

            // Phase 2: Core Observation Tools
            { "get_ui_tree", GetUiTree },
            { "expand_collapse", ExpandCollapse },
            { "scroll", Scroll },
            { "check_element_state", CheckElementState },

            // Phase 3: State Change Detection
            { "capture_ui_snapshot", CaptureUiSnapshot },
            { "compare_ui_snapshots", CompareUiSnapshots },

            // Sandbox Tools
            { "launch_app_sandboxed", LaunchAppSandboxed },
            { "close_sandbox", CloseSandbox },
        };
    }

    public async Task RunAsync()
    {
        var reader = Console.In;
        var writer = Console.Out;

        // MCP protocol: server must wait for client to send initialize request first
        // Do NOT send anything until we receive a message

        // Process incoming messages
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line))
                break;

            try
            {
                var request = JsonDocument.Parse(line).RootElement;
                var response = await ProcessRequest(request);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                await writer.FlushAsync();
            }
            catch (Exception ex)
            {
                var error = new
                {
                    jsonrpc = "2.0",
                    error = new
                    {
                        code = -32603,
                        message = "Internal error",
                        data = new { details = ex.Message }
                    }
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(error));
                await writer.FlushAsync();
            }
        }
    }

    private async Task<object> ProcessRequest(JsonElement request)
    {
        if (!request.TryGetProperty("method", out var methodElement))
            throw new InvalidOperationException("Missing method");

        var method = methodElement.GetString();
        if (method == "initialize")
        {
            return new
            {
                jsonrpc = "2.0",
                id = request.TryGetProperty("id", out var id) ? id.GetInt32() : _nextId++,
                result = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new
                    {
                        // Indicate tool support with empty object (actual tools returned via tools/list)
                        tools = new { }
                    },
                    serverInfo = new
                    {
                        name = "fnWindowsMCP",
                        version = "1.0.0"
                    }
                }
            };
        }

        if (method == "tools/list")
        {
            return new
            {
                jsonrpc = "2.0",
                id = request.TryGetProperty("id", out var id) ? id.GetInt32() : _nextId++,
                result = new
                {
                    tools = GetToolDefinitions()
                }
            };
        }

        if (method == "tools/call")
        {
            if (!request.TryGetProperty("params", out var paramsElement))
                throw new InvalidOperationException("Missing params");

            if (!paramsElement.TryGetProperty("name", out var nameElement))
                throw new InvalidOperationException("Missing tool name");

            var toolName = nameElement.GetString() ?? throw new InvalidOperationException("Tool name is empty");
            var toolArgs = paramsElement.TryGetProperty("arguments", out var args) ? args : default;

            if (!_tools.ContainsKey(toolName))
                throw new InvalidOperationException($"Unknown tool: {toolName}");

            var result = await _tools[toolName](toolArgs);

            return new
            {
                jsonrpc = "2.0",
                id = request.TryGetProperty("id", out var id) ? id.GetInt32() : _nextId++,
                result = new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = result.ToString()
                        }
                    }
                }
            };
        }

        throw new InvalidOperationException($"Unknown method: {method}");
    }

    private object GetToolDefinitions()
    {
        return new object[]
        {
            new
            {
                name = "find_element",
                description = "Find a UI element by AutomationId, Name, ClassName, or ControlType",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        automationId = new { type = "string", description = "AutomationId of the element" },
                        name = new { type = "string", description = "Name of the element" },
                        className = new { type = "string", description = "ClassName of the element" },
                        controlType = new { type = "string", description = "ControlType of the element" },
                        parent = new { type = "string", description = "Parent element path (optional)" }
                    }
                }
            },
            new
            {
                name = "click_element",
                description = "Click on a UI element",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementPath = new { type = "string", description = "Path or identifier of the element" },
                        doubleClick = new { type = "boolean", description = "Double-click if true" }
                    },
                    required = new[] { "elementPath" }
                }
            },
            new
            {
                name = "type_text",
                description = "Type text into a text field",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementPath = new { type = "string", description = "Path or identifier of the element" },
                        text = new { type = "string", description = "Text to type" },
                        clearFirst = new { type = "boolean", description = "Clear field before typing" }
                    },
                    required = new[] { "elementPath", "text" }
                }
            },
            new
            {
                name = "launch_app",
                description = "Launch a WinForms application",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Path to the executable" },
                        arguments = new { type = "string", description = "Command-line arguments (optional)" },
                        workingDirectory = new { type = "string", description = "Working directory (optional)" }
                    },
                    required = new[] { "path" }
                }
            },
            new
            {
                name = "take_screenshot",
                description = "Take a screenshot of the application or element",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        outputPath = new { type = "string", description = "Path to save the screenshot" },
                        elementPath = new { type = "string", description = "Specific element to screenshot (optional)" }
                    },
                    required = new[] { "outputPath" }
                }
            },
            // Touch/Pen Injection Tools
            new
            {
                name = "touch_tap",
                description = "Simulate a touch tap at screen coordinates. No delays by default - add holdMs for long-press gestures.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        x = new { type = "integer", description = "Screen X coordinate" },
                        y = new { type = "integer", description = "Screen Y coordinate" },
                        holdMs = new { type = "integer", description = "Milliseconds to hold before release (default 0)" }
                    },
                    required = new[] { "x", "y" }
                }
            },
            new
            {
                name = "touch_drag",
                description = "Simulate a touch drag from one point to another. No delays by default (instant). Add delayMs if slower gesture speed is needed.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        x1 = new { type = "integer", description = "Start X coordinate" },
                        y1 = new { type = "integer", description = "Start Y coordinate" },
                        x2 = new { type = "integer", description = "End X coordinate" },
                        y2 = new { type = "integer", description = "End Y coordinate" },
                        steps = new { type = "integer", description = "Number of intermediate points (default 10)" },
                        delayMs = new { type = "integer", description = "Delay in milliseconds between steps (default 0)" }
                    },
                    required = new[] { "x1", "y1", "x2", "y2" }
                }
            },
            new
            {
                name = "pinch_zoom",
                description = "Simulate a pinch-to-zoom gesture with two fingers. No delays by default (instant). Add delayMs if slower animation is needed.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        centerX = new { type = "integer", description = "Center X coordinate of the pinch gesture" },
                        centerY = new { type = "integer", description = "Center Y coordinate of the pinch gesture" },
                        startDistance = new { type = "integer", description = "Initial distance between fingers in pixels" },
                        endDistance = new { type = "integer", description = "Final distance between fingers in pixels (larger = zoom in, smaller = zoom out)" },
                        steps = new { type = "integer", description = "Number of animation steps (default 20)" },
                        delayMs = new { type = "integer", description = "Delay in milliseconds between steps (default 0)" }
                    },
                    required = new[] { "centerX", "centerY", "startDistance", "endDistance" }
                }
            },
            new
            {
                name = "pen_stroke",
                description = "Simulate a pen stroke from one point to another with pressure. No delays by default (instant). Add delayMs if slower stroke speed is needed.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        x1 = new { type = "integer", description = "Start X coordinate" },
                        y1 = new { type = "integer", description = "Start Y coordinate" },
                        x2 = new { type = "integer", description = "End X coordinate" },
                        y2 = new { type = "integer", description = "End Y coordinate" },
                        steps = new { type = "integer", description = "Number of intermediate points (default 20)" },
                        pressure = new { type = "integer", description = "Pen pressure 0-1024 (default 512)" },
                        eraser = new { type = "boolean", description = "Use eraser end of pen (default false)" },
                        delayMs = new { type = "integer", description = "Delay in milliseconds between steps (default 0)" }
                    },
                    required = new[] { "x1", "y1", "x2", "y2" }
                }
            },
            new
            {
                name = "pen_tap",
                description = "Simulate a pen tap at screen coordinates. No delays by default - add holdMs for long-press gestures.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        x = new { type = "integer", description = "Screen X coordinate" },
                        y = new { type = "integer", description = "Screen Y coordinate" },
                        pressure = new { type = "integer", description = "Pen pressure 0-1024 (default 512)" },
                        holdMs = new { type = "integer", description = "Milliseconds to hold before release (default 0)" }
                    },
                    required = new[] { "x", "y" }
                }
            },
            // Mouse Input Tools
            new
            {
                name = "mouse_drag",
                description = "Simulate a mouse drag from one point to another (for drawing on InkCanvas). No delays by default (instant). Add delayMs if slower drag speed is needed.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        x1 = new { type = "integer", description = "Start X coordinate" },
                        y1 = new { type = "integer", description = "Start Y coordinate" },
                        x2 = new { type = "integer", description = "End X coordinate" },
                        y2 = new { type = "integer", description = "End Y coordinate" },
                        steps = new { type = "integer", description = "Number of intermediate points (default 10)" },
                        delayMs = new { type = "integer", description = "Delay in milliseconds between steps (default 0)" }
                    },
                    required = new[] { "x1", "y1", "x2", "y2" }
                }
            },
            new
            {
                name = "mouse_drag_path",
                description = "Drag the mouse through multiple waypoints in sequence. Useful for drawing shapes, curves, and complex gestures without lifting the mouse button. No delays by default (instant). Add delayMs if slower drag speed is needed.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        points = new
                        {
                            type = "array",
                            description = "Array of {x, y} waypoints to drag through (minimum 2, maximum 1000)",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    x = new { type = "integer", description = "X coordinate" },
                                    y = new { type = "integer", description = "Y coordinate" }
                                },
                                required = new[] { "x", "y" }
                            }
                        },
                        stepsPerSegment = new { type = "integer", description = "Interpolation steps between each waypoint (default 1 - waypoints already define the path)" },
                        delayMs = new { type = "integer", description = "Delay in milliseconds between steps (default 0)" }
                    },
                    required = new[] { "points" }
                }
            },
            new
            {
                name = "mouse_click",
                description = "Simulate a mouse click at screen coordinates. No delays by default (instant). Add delayMs if a pause before clicking is needed.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        x = new { type = "integer", description = "Screen X coordinate" },
                        y = new { type = "integer", description = "Screen Y coordinate" },
                        doubleClick = new { type = "boolean", description = "Double-click if true (default false)" },
                        delayMs = new { type = "integer", description = "Delay in milliseconds before click (default 0)" }
                    },
                    required = new[] { "x", "y" }
                }
            },
            // Window Targeting Tools
            new
            {
                name = "get_window_bounds",
                description = "Get the screen bounds of a window by its title (supports partial match)",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowTitle = new { type = "string", description = "Window title to search for (partial match supported)" }
                    },
                    required = new[] { "windowTitle" }
                }
            },
            new
            {
                name = "focus_window",
                description = "Bring a window to foreground by its title",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowTitle = new { type = "string", description = "Window title to focus (partial match supported)" }
                    },
                    required = new[] { "windowTitle" }
                }
            },
            // Enhanced WPF Tools
            new
            {
                name = "click_by_automation_id",
                description = "Find and click a UI element by its AutomationId in one operation. Works with WPF apps that have AutomationProperties.AutomationId set.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        automationId = new { type = "string", description = "AutomationId of the element to click" },
                        windowTitle = new { type = "string", description = "Window title to search in (optional, searches desktop if not provided)" },
                        doubleClick = new { type = "boolean", description = "Double-click if true (default false)" }
                    },
                    required = new[] { "automationId" }
                }
            },
            new
            {
                name = "list_elements",
                description = "List all UI elements in a window for debugging. Returns AutomationId, Name, ClassName, ControlType, and bounds for each element.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowTitle = new { type = "string", description = "Window title to list elements from" },
                        maxDepth = new { type = "integer", description = "Maximum depth to traverse (default 3)" }
                    },
                    required = new[] { "windowTitle" }
                }
            },
            // Phase 2: Core Observation Tools
            new
            {
                name = "get_ui_tree",
                description = "Get a hierarchical XML representation of the UI tree. Includes heuristic pruning (skips invisible elements, PART_* internal elements, disabled containers) and token budget enforcement.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowTitle = new { type = "string", description = "Window title to get tree from (optional - uses desktop if not specified)" },
                        maxDepth = new { type = "integer", description = "Maximum tree depth (default 3)" },
                        maxTokenBudget = new { type = "integer", description = "Maximum token budget for output (default 5000)" },
                        includeInvisible = new { type = "boolean", description = "Include invisible/offscreen elements (default false)" },
                        skipInternalParts = new { type = "boolean", description = "Skip PART_* internal WPF elements (default true)" }
                    }
                }
            },
            new
            {
                name = "expand_collapse",
                description = "Expand or collapse a tree node, menu item, or other expandable element using the ExpandCollapse UI Automation pattern.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID (from find_element)" },
                        automationId = new { type = "string", description = "AutomationId to find the element (alternative to elementId)" },
                        windowTitle = new { type = "string", description = "Window to search in (required if using automationId)" },
                        expand = new { type = "boolean", description = "True to expand, false to collapse" }
                    },
                    required = new[] { "expand" }
                }
            },
            new
            {
                name = "scroll",
                description = "Scroll a scrollable container (ListBox, DataGrid, ScrollViewer, etc.) using the Scroll UI Automation pattern.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID (from find_element)" },
                        automationId = new { type = "string", description = "AutomationId to find the element (alternative to elementId)" },
                        windowTitle = new { type = "string", description = "Window to search in (required if using automationId)" },
                        direction = new { type = "string", description = "Scroll direction: Up, Down, Left, Right" },
                        amount = new { type = "string", description = "Scroll amount: SmallDecrement or LargeDecrement (default SmallDecrement)" }
                    },
                    required = new[] { "direction" }
                }
            },
            new
            {
                name = "check_element_state",
                description = "Get detailed state information for a UI element including enabled/visible state, value, toggle state, selection state, and range values.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID (from find_element)" },
                        automationId = new { type = "string", description = "AutomationId to find the element (alternative to elementId)" },
                        windowTitle = new { type = "string", description = "Window to search in (required if using automationId)" }
                    }
                }
            },
            // Phase 3: State Change Detection
            new
            {
                name = "capture_ui_snapshot",
                description = "Capture a snapshot of the current UI tree state for later comparison. Use this before performing an action, then call compare_ui_snapshots after to detect what changed.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowTitle = new { type = "string", description = "Window title to snapshot (optional - uses desktop if not specified)" },
                        snapshotId = new { type = "string", description = "Identifier for this snapshot (e.g., 'before_click'). Used to reference it later in compare_ui_snapshots." }
                    },
                    required = new[] { "snapshotId" }
                }
            },
            new
            {
                name = "compare_ui_snapshots",
                description = "Compare two previously captured UI snapshots to detect changes. Returns a diff summary showing elements added, removed, or modified.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        beforeSnapshotId = new { type = "string", description = "ID of the 'before' snapshot (from capture_ui_snapshot)" },
                        afterSnapshotId = new { type = "string", description = "ID of the 'after' snapshot. If omitted, captures current state automatically." },
                        windowTitle = new { type = "string", description = "Window title for auto-capture of 'after' snapshot (if afterSnapshotId omitted)" }
                    },
                    required = new[] { "beforeSnapshotId" }
                }
            },
            // Sandbox Tools
            new
            {
                name = "launch_app_sandboxed",
                description = "Launch an application inside Windows Sandbox for isolated, safe automation. Creates a secure sandbox environment with the target app and MCP server. Requires Windows Pro/Enterprise/Education.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        appPath = new { type = "string", description = "Path to the application directory to test (will be mapped read-only to C:\\App in sandbox)" },
                        appExe = new { type = "string", description = "Name of the executable to run inside sandbox (e.g., 'MyApp.exe')" },
                        mcpServerPath = new { type = "string", description = "Path to the MCP server binaries (will be mapped read-only to C:\\MCP in sandbox)" },
                        sharedFolderPath = new { type = "string", description = "Path to shared folder for host-sandbox communication (will be mapped read-write to C:\\Shared in sandbox)" },
                        outputFolderPath = new { type = "string", description = "Optional path for sandbox output like screenshots (will be mapped read-write to C:\\Output)" },
                        bootTimeoutMs = new { type = "integer", description = "Timeout in ms to wait for sandbox boot and MCP server ready (default 60000)" }
                    },
                    required = new[] { "appPath", "appExe", "mcpServerPath", "sharedFolderPath" }
                }
            },
            new
            {
                name = "close_sandbox",
                description = "Close the currently running Windows Sandbox and clean up resources.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        timeoutMs = new { type = "integer", description = "Timeout in ms to wait for graceful shutdown before force kill (default 10000)" }
                    }
                }
            }
        };
    }

    // Tool implementations
    private Task<JsonElement> FindElement(JsonElement args)
    {
        try
        {
            var automation = _session.GetAutomation();
            var pid = GetIntArg(args, "pid");
            var automationId = GetStringArg(args, "automationId");
            var name = GetStringArg(args, "name");
            var className = GetStringArg(args, "className");

            AutomationElement? element = null;

            if (!string.IsNullOrEmpty(automationId))
            {
                element = automation.FindByAutomationId(automationId);
            }
            else if (!string.IsNullOrEmpty(name))
            {
                element = automation.FindByName(name);
            }
            else if (!string.IsNullOrEmpty(className))
            {
                element = automation.FindByClassName(className);
            }

            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found\"}").RootElement);

            var elementId = _session.CacheElement(element);
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"elementId\": \"{elementId}\", \"name\": \"{element.Name ?? ""}\", \"automationId\": \"{element.AutomationId ?? ""}\", \"controlType\": \"{element.ControlType}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ClickElement(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var doubleClick = GetBoolArg(args, "doubleClick", false);

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            automation.Click(element, doubleClick);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Element clicked\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> TypeText(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var text = GetStringArg(args, "text") ?? "";
            var clearFirst = GetBoolArg(args, "clearFirst", false);

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            automation.TypeText(element, text, clearFirst);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Text typed\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> SetValue(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var value = GetStringArg(args, "value") ?? "";

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            automation.SetValue(element, value);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Value set\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> GetProperty(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var propertyName = GetStringArg(args, "propertyName") ?? "";

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            var value = automation.GetProperty(element, propertyName);

            var valueJson = value == null ? "null" : $"\"{EscapeJson(value.ToString())}\"";
            var json = $"{{\"success\": true, \"propertyName\": \"{propertyName}\", \"value\": {valueJson}}}";
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> LaunchApp(JsonElement args)
    {
        try
        {
            var path = GetStringArg(args, "path") ?? throw new ArgumentException("path is required");
            var arguments = GetStringArg(args, "arguments");
            var workingDirectory = GetStringArg(args, "workingDirectory");

            var automation = _session.GetAutomation();
            var process = automation.LaunchApp(path, arguments, workingDirectory);

            _session.CacheProcess(process.Id, process);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"pid\": {process.Id}, \"processName\": \"{process.ProcessName}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> AttachToProcess(JsonElement args)
    {
        try
        {
            var pid = GetIntArg(args, "pid");
            var processName = GetStringArg(args, "processName");

            var automation = _session.GetAutomation();
            var process = !string.IsNullOrEmpty(processName)
                ? automation.AttachToProcessByName(processName)
                : automation.AttachToProcess(pid);

            _session.CacheProcess(process.Id, process);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"pid\": {process.Id}, \"processName\": \"{process.ProcessName}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> CloseApp(JsonElement args)
    {
        try
        {
            var pid = GetIntArg(args, "pid");
            var force = GetBoolArg(args, "force", false);

            var automation = _session.GetAutomation();
            automation.CloseApp(pid, force);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Application closed\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> TakeScreenshot(JsonElement args)
    {
        try
        {
            var outputPath = GetStringArg(args, "outputPath") ?? throw new ArgumentException("outputPath is required");
            var elementId = GetStringArg(args, "elementId");

            var automation = _session.GetAutomation();
            AutomationElement? element = null;

            if (!string.IsNullOrEmpty(elementId))
                element = _session.GetElement(elementId!);

            automation.TakeScreenshot(outputPath, element);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"message\": \"Screenshot saved to {EscapeJson(outputPath)}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ElementExists(JsonElement args)
    {
        try
        {
            var automationId = GetStringArg(args, "automationId") ?? throw new ArgumentException("automationId is required");

            var automation = _session.GetAutomation();
            var exists = automation.ElementExists(automationId);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"exists\": {(exists ? "true" : "false")}}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private async Task<JsonElement> WaitForElement(JsonElement args)
    {
        try
        {
            var automationId = GetStringArg(args, "automationId") ?? throw new ArgumentException("automationId is required");
            var timeoutMs = GetIntArg(args, "timeoutMs", 10000);

            var automation = _session.GetAutomation();
            var found = await automation.WaitForElementAsync(automationId, null, timeoutMs);

            return JsonDocument.Parse($"{{\"success\": true, \"found\": {(found ? "true" : "false")}}}").RootElement;
        }
        catch (Exception ex)
        {
            return JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement;
        }
    }

    private Task<JsonElement> DragDrop(JsonElement args)
    {
        try
        {
            var sourceElementId = GetStringArg(args, "sourceElementId") ?? throw new ArgumentException("sourceElementId is required");
            var targetElementId = GetStringArg(args, "targetElementId") ?? throw new ArgumentException("targetElementId is required");

            var sourceElement = _session.GetElement(sourceElementId!);
            var targetElement = _session.GetElement(targetElementId!);

            if (sourceElement == null || targetElement == null)
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Source or target element not found in session\"}").RootElement);

            var automation = _session.GetAutomation();
            automation.DragDrop(sourceElement, targetElement);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Drag and drop completed\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> SendKeys(JsonElement args)
    {
        try
        {
            var keys = GetStringArg(args, "keys") ?? throw new ArgumentException("keys is required");

            var automation = _session.GetAutomation();
            automation.SendKeys(keys);

            return Task.FromResult(JsonDocument.Parse("{\"success\": true, \"message\": \"Keys sent\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> RaiseEvent(JsonElement args)
    {
        // Event raising is handled by FlaUI patterns in future enhancement
        return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Event raising not yet implemented\"}").RootElement);
    }

    private Task<JsonElement> ListenForEvent(JsonElement args)
    {
        // Event listening is handled by FlaUI event handlers in future enhancement
        return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Event listening not yet implemented\"}").RootElement);
    }

    // Touch/Pen Injection Tools
    private Task<JsonElement> TouchTap(JsonElement args)
    {
        try
        {
            var x = GetIntArg(args, "x");
            var y = GetIntArg(args, "y");
            var holdMs = GetIntArg(args, "holdMs", 0);

            var success = InputInjection.TouchTap(x, y, holdMs);

            if (success)
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"message\": \"Touch tap at ({x}, {y})\"}}").RootElement);
            else
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Touch injection failed. Requires Windows 8+ and touch injection permissions.\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> TouchDrag(JsonElement args)
    {
        try
        {
            var x1 = GetIntArg(args, "x1");
            var y1 = GetIntArg(args, "y1");
            var x2 = GetIntArg(args, "x2");
            var y2 = GetIntArg(args, "y2");
            var steps = GetIntArg(args, "steps", 10);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var success = InputInjection.TouchDrag(x1, y1, x2, y2, steps, delayMs);

            if (success)
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"message\": \"Touch drag from ({x1}, {y1}) to ({x2}, {y2})\"}}").RootElement);
            else
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Touch injection failed. Requires Windows 8+ and touch injection permissions.\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> PinchZoom(JsonElement args)
    {
        try
        {
            var centerX = GetIntArg(args, "centerX");
            var centerY = GetIntArg(args, "centerY");
            var startDistance = GetIntArg(args, "startDistance");
            var endDistance = GetIntArg(args, "endDistance");
            var steps = GetIntArg(args, "steps", 20);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var success = InputInjection.PinchZoom(centerX, centerY, startDistance, endDistance, steps, delayMs);

            if (success)
            {
                var zoomType = endDistance > startDistance ? "zoom in" : "zoom out";
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"message\": \"Pinch {zoomType} at ({centerX}, {centerY}) from {startDistance}px to {endDistance}px\"}}").RootElement);
            }
            else
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Touch injection failed. Requires Windows 8+ and touch injection permissions.\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> PenStroke(JsonElement args)
    {
        try
        {
            var x1 = GetIntArg(args, "x1");
            var y1 = GetIntArg(args, "y1");
            var x2 = GetIntArg(args, "x2");
            var y2 = GetIntArg(args, "y2");
            var steps = GetIntArg(args, "steps", 20);
            var pressure = (uint)GetIntArg(args, "pressure", 512);
            var eraser = GetBoolArg(args, "eraser", false);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var success = InputInjection.PenStroke(x1, y1, x2, y2, steps, pressure, eraser, delayMs);

            if (success)
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"message\": \"Pen stroke from ({x1}, {y1}) to ({x2}, {y2}) with pressure {pressure}\"}}").RootElement);
            else
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Pen injection failed. Requires Windows 8+ and pen injection permissions.\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> PenTap(JsonElement args)
    {
        try
        {
            var x = GetIntArg(args, "x");
            var y = GetIntArg(args, "y");
            var pressure = (uint)GetIntArg(args, "pressure", 512);
            var holdMs = GetIntArg(args, "holdMs", 0);

            var success = InputInjection.PenTap(x, y, pressure, holdMs);

            if (success)
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"message\": \"Pen tap at ({x}, {y}) with pressure {pressure}\"}}").RootElement);
            else
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Pen injection failed. Requires Windows 8+ and pen injection permissions.\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    // Mouse Input Tools
    private Task<JsonElement> MouseDrag(JsonElement args)
    {
        try
        {
            var x1 = GetIntArg(args, "x1");
            var y1 = GetIntArg(args, "y1");
            var x2 = GetIntArg(args, "x2");
            var y2 = GetIntArg(args, "y2");
            var steps = GetIntArg(args, "steps", 10);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var success = InputInjection.MouseDrag(x1, y1, x2, y2, steps, delayMs);

            if (success)
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"message\": \"Mouse drag from ({x1}, {y1}) to ({x2}, {y2})\"}}").RootElement);
            else
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Mouse drag failed.\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> MouseDragPath(JsonElement args)
    {
        try
        {
            // Parse points array
            if (!args.TryGetProperty("points", out var pointsElement) || pointsElement.ValueKind != JsonValueKind.Array)
            {
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"points array is required\"}").RootElement);
            }

            var pointsList = new List<(int x, int y)>();
            int index = 0;
            foreach (var point in pointsElement.EnumerateArray())
            {
                if (!point.TryGetProperty("x", out var xProp) || !point.TryGetProperty("y", out var yProp))
                {
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Point at index {index} missing required x or y coordinate\"}}").RootElement);
                }

                var x = xProp.GetInt32();
                var y = yProp.GetInt32();

                if (x < 0 || y < 0)
                {
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Point at index {index} has invalid coordinate (must be >= 0)\"}}").RootElement);
                }

                pointsList.Add((x, y));
                index++;
            }

            if (pointsList.Count < 2)
            {
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Path requires at least 2 points\"}").RootElement);
            }

            if (pointsList.Count > 1000)
            {
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Path exceeds maximum of 1000 waypoints\"}").RootElement);
            }

            var stepsPerSegment = GetIntArg(args, "stepsPerSegment", 1);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var (success, pointsProcessed, totalSteps) = InputInjection.MouseDragPath(
                pointsList.ToArray(),
                stepsPerSegment,
                delayMs);

            if (success)
            {
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"message\": \"Completed drag path through {pointsProcessed} waypoints\", \"pointsProcessed\": {pointsProcessed}, \"totalSteps\": {totalSteps}}}").RootElement);
            }
            else
            {
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Mouse drag path failed\"}").RootElement);
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> MouseClick(JsonElement args)
    {
        try
        {
            var x = GetIntArg(args, "x");
            var y = GetIntArg(args, "y");
            var doubleClick = GetBoolArg(args, "doubleClick", false);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var success = InputInjection.MouseClick(x, y, doubleClick, delayMs);

            if (success)
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"message\": \"{(doubleClick ? "Double-click" : "Click")} at ({x}, {y})\"}}").RootElement);
            else
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Mouse click failed.\"}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    // Window Targeting Tools
    private Task<JsonElement> GetWindowBounds(JsonElement args)
    {
        try
        {
            var windowTitle = GetStringArg(args, "windowTitle") ?? throw new ArgumentException("windowTitle is required");

            var bounds = InputInjection.GetWindowBounds(windowTitle);

            if (bounds != null)
            {
                var (x, y, width, height) = bounds.Value;
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"x\": {x}, \"y\": {y}, \"width\": {width}, \"height\": {height}, \"windowTitle\": \"{EscapeJson(windowTitle)}\"}}").RootElement);
            }
            else
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Window not found: {EscapeJson(windowTitle)}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> FocusWindow(JsonElement args)
    {
        try
        {
            var windowTitle = GetStringArg(args, "windowTitle") ?? throw new ArgumentException("windowTitle is required");

            var success = InputInjection.FocusWindow(windowTitle);

            if (success)
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"message\": \"Focused window: {EscapeJson(windowTitle)}\"}}").RootElement);
            else
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Could not focus window: {EscapeJson(windowTitle)}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    // Enhanced WPF Tools
    private Task<JsonElement> ClickByAutomationId(JsonElement args)
    {
        try
        {
            var automationId = GetStringArg(args, "automationId") ?? throw new ArgumentException("automationId is required");
            var windowTitle = GetStringArg(args, "windowTitle");
            var doubleClick = GetBoolArg(args, "doubleClick", false);

            var automation = _session.GetAutomation();

            // If window title provided, search within that window
            FlaUI.Core.AutomationElements.AutomationElement? parent = null;
            if (!string.IsNullOrEmpty(windowTitle))
            {
                parent = automation.GetWindowByTitle(windowTitle);
                if (parent == null)
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Window not found: {EscapeJson(windowTitle)}\"}}").RootElement);
            }

            var element = automation.FindByAutomationId(automationId, parent);
            if (element == null)
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Element not found: {EscapeJson(automationId)}\"}}").RootElement);

            automation.Click(element, doubleClick);

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"message\": \"Clicked element: {EscapeJson(automationId)}\"}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ListElements(JsonElement args)
    {
        try
        {
            var windowTitle = GetStringArg(args, "windowTitle") ?? throw new ArgumentException("windowTitle is required");
            var maxDepth = GetIntArg(args, "maxDepth", 3);

            var automation = _session.GetAutomation();
            var window = automation.GetWindowByTitle(windowTitle);

            if (window == null)
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Window not found: {EscapeJson(windowTitle)}\"}}").RootElement);

            var elements = automation.GetElementTree(window, maxDepth);

            // Build JSON array of elements
            var elementsJson = new System.Text.StringBuilder();
            elementsJson.Append("[");
            for (int i = 0; i < elements.Count; i++)
            {
                if (i > 0) elementsJson.Append(",");
                elementsJson.Append("{");
                var first = true;
                foreach (var kvp in elements[i])
                {
                    if (!first) elementsJson.Append(",");
                    first = false;
                    elementsJson.Append($"\"{kvp.Key}\":\"{EscapeJson(kvp.Value)}\"");
                }
                elementsJson.Append("}");
            }
            elementsJson.Append("]");

            return Task.FromResult(JsonDocument.Parse($"{{\"success\": true, \"elementCount\": {elements.Count}, \"elements\": {elementsJson}}}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    // Phase 2: Core Observation Tools
    private Task<JsonElement> GetUiTree(JsonElement args)
    {
        try
        {
            var windowTitle = GetStringArg(args, "windowTitle");
            var maxDepth = GetIntArg(args, "maxDepth", 3);
            var maxTokenBudget = GetIntArg(args, "maxTokenBudget", 5000);
            var includeInvisible = GetBoolArg(args, "includeInvisible", false);
            var skipInternalParts = GetBoolArg(args, "skipInternalParts", true);

            var automation = _session.GetAutomation();

            // Get root element (window or desktop)
            AutomationElement? root = null;
            if (!string.IsNullOrEmpty(windowTitle))
            {
                root = automation.GetWindowByTitle(windowTitle);
                if (root == null)
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Window not found: {EscapeJson(windowTitle)}\"}}").RootElement);
            }

            // Build tree with options
            var options = new TreeBuilderOptions
            {
                MaxDepth = maxDepth,
                MaxTokenBudget = maxTokenBudget,
                IncludeInvisible = includeInvisible,
                SkipInternalParts = skipInternalParts
            };

            var result = automation.BuildUiTree(root, options);

            // Return XML tree with metadata
            var xmlEscaped = EscapeJson(result.Xml);
            return Task.FromResult(JsonDocument.Parse($@"{{
                ""success"": true,
                ""xml"": ""{xmlEscaped}"",
                ""tokenCount"": {result.TokenCount},
                ""elementCount"": {result.ElementCount},
                ""dpiScaleFactor"": {result.DpiScaleFactor:F2},
                ""timestamp"": ""{result.Timestamp}"",
                ""truncated"": {result.Truncated.ToString().ToLower()}
            }}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> ExpandCollapse(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId");
            var automationId = GetStringArg(args, "automationId");
            var windowTitle = GetStringArg(args, "windowTitle");
            var expand = GetBoolArg(args, "expand", true);

            var automation = _session.GetAutomation();
            AutomationElement? element = null;

            // Get element by ID or find by automationId
            if (!string.IsNullOrEmpty(elementId))
            {
                element = _session.GetElement(elementId);
                if (element == null)
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Element not found in cache: {EscapeJson(elementId)}\"}}").RootElement);
            }
            else if (!string.IsNullOrEmpty(automationId))
            {
                if (string.IsNullOrEmpty(windowTitle))
                    return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"windowTitle is required when using automationId\"}").RootElement);

                var window = automation.GetWindowByTitle(windowTitle);
                if (window == null)
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Window not found: {EscapeJson(windowTitle)}\"}}").RootElement);

                element = automation.FindByAutomationId(automationId, window);
                if (element == null)
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Element not found by AutomationId: {EscapeJson(automationId)}\"}}").RootElement);
            }
            else
            {
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Either elementId or automationId is required\"}").RootElement);
            }

            var result = automation.ExpandCollapse(element, expand);

            if (result.Success)
            {
                return Task.FromResult(JsonDocument.Parse($@"{{
                    ""success"": true,
                    ""previousState"": ""{result.PreviousState}"",
                    ""currentState"": ""{result.CurrentState}""
                }}").RootElement);
            }
            else
            {
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(result.ErrorMessage ?? "Unknown error")}\"}}").RootElement);
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> Scroll(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId");
            var automationId = GetStringArg(args, "automationId");
            var windowTitle = GetStringArg(args, "windowTitle");
            var directionStr = GetStringArg(args, "direction") ?? throw new ArgumentException("direction is required");
            var amountStr = GetStringArg(args, "amount") ?? "SmallDecrement";

            // Parse direction
            if (!Enum.TryParse<ScrollDirection>(directionStr, true, out var direction))
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Invalid direction: {EscapeJson(directionStr)}. Valid values: Up, Down, Left, Right\"}}").RootElement);

            // Parse amount
            if (!Enum.TryParse<ScrollAmount>(amountStr, true, out var amount))
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Invalid amount: {EscapeJson(amountStr)}. Valid values: SmallDecrement, LargeDecrement\"}}").RootElement);

            var automation = _session.GetAutomation();
            AutomationElement? element = null;

            // Get element by ID or find by automationId
            if (!string.IsNullOrEmpty(elementId))
            {
                element = _session.GetElement(elementId);
                if (element == null)
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Element not found in cache: {EscapeJson(elementId)}\"}}").RootElement);
            }
            else if (!string.IsNullOrEmpty(automationId))
            {
                if (string.IsNullOrEmpty(windowTitle))
                    return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"windowTitle is required when using automationId\"}").RootElement);

                var window = automation.GetWindowByTitle(windowTitle);
                if (window == null)
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Window not found: {EscapeJson(windowTitle)}\"}}").RootElement);

                element = automation.FindByAutomationId(automationId, window);
                if (element == null)
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Element not found by AutomationId: {EscapeJson(automationId)}\"}}").RootElement);
            }
            else
            {
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Either elementId or automationId is required\"}").RootElement);
            }

            var result = automation.Scroll(element, direction, amount);

            if (result.Success)
            {
                return Task.FromResult(JsonDocument.Parse($@"{{
                    ""success"": true,
                    ""horizontalScrollPercent"": {result.HorizontalScrollPercent:F2},
                    ""verticalScrollPercent"": {result.VerticalScrollPercent:F2},
                    ""horizontalChanged"": {result.HorizontalChanged.ToString().ToLower()},
                    ""verticalChanged"": {result.VerticalChanged.ToString().ToLower()}
                }}").RootElement);
            }
            else
            {
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(result.ErrorMessage ?? "Unknown error")}\"}}").RootElement);
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> CheckElementState(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId");
            var automationId = GetStringArg(args, "automationId");
            var windowTitle = GetStringArg(args, "windowTitle");

            var automation = _session.GetAutomation();
            AutomationElement? element = null;

            // Get element by ID or find by automationId
            if (!string.IsNullOrEmpty(elementId))
            {
                element = _session.GetElement(elementId);
                if (element == null)
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Element not found in cache: {EscapeJson(elementId)}\"}}").RootElement);
            }
            else if (!string.IsNullOrEmpty(automationId))
            {
                if (string.IsNullOrEmpty(windowTitle))
                    return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"windowTitle is required when using automationId\"}").RootElement);

                var window = automation.GetWindowByTitle(windowTitle);
                if (window == null)
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Window not found: {EscapeJson(windowTitle)}\"}}").RootElement);

                element = automation.FindByAutomationId(automationId, window);
                if (element == null)
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Element not found by AutomationId: {EscapeJson(automationId)}\"}}").RootElement);
            }
            else
            {
                return Task.FromResult(JsonDocument.Parse("{\"success\": false, \"error\": \"Either elementId or automationId is required\"}").RootElement);
            }

            var result = automation.GetElementState(element);

            if (result.Success)
            {
                // Build JSON response with optional properties
                var json = new System.Text.StringBuilder();
                json.Append("{\"success\": true");
                json.Append($", \"automationId\": \"{EscapeJson(result.AutomationId ?? "")}\"");
                json.Append($", \"name\": \"{EscapeJson(result.Name ?? "")}\"");
                json.Append($", \"className\": \"{EscapeJson(result.ClassName ?? "")}\"");
                json.Append($", \"controlType\": \"{EscapeJson(result.ControlType ?? "")}\"");
                json.Append($", \"isEnabled\": {result.IsEnabled.ToString().ToLower()}");
                json.Append($", \"isOffscreen\": {result.IsOffscreen.ToString().ToLower()}");
                json.Append($", \"isKeyboardFocusable\": {result.IsKeyboardFocusable.ToString().ToLower()}");
                json.Append($", \"hasKeyboardFocus\": {result.HasKeyboardFocus.ToString().ToLower()}");
                json.Append($", \"dpiScaleFactor\": {result.DpiScaleFactor:F2}");

                if (result.BoundingRect != null)
                {
                    var rect = result.BoundingRect;
                    json.Append($", \"boundingRect\": {{\"x\": {rect.X}, \"y\": {rect.Y}, \"width\": {rect.Width}, \"height\": {rect.Height}}}");
                }

                if (result.Value != null)
                    json.Append($", \"value\": \"{EscapeJson(result.Value)}\"");

                if (result.IsReadOnly.HasValue)
                    json.Append($", \"isReadOnly\": {result.IsReadOnly.Value.ToString().ToLower()}");

                if (result.ToggleState != null)
                    json.Append($", \"toggleState\": \"{result.ToggleState}\"");

                if (result.IsSelected.HasValue)
                    json.Append($", \"isSelected\": {result.IsSelected.Value.ToString().ToLower()}");

                if (result.RangeValue.HasValue)
                {
                    json.Append($", \"rangeValue\": {result.RangeValue.Value:F2}");
                    if (result.RangeMinimum.HasValue)
                        json.Append($", \"rangeMinimum\": {result.RangeMinimum.Value:F2}");
                    if (result.RangeMaximum.HasValue)
                        json.Append($", \"rangeMaximum\": {result.RangeMaximum.Value:F2}");
                }

                json.Append("}");
                return Task.FromResult(JsonDocument.Parse(json.ToString()).RootElement);
            }
            else
            {
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(result.ErrorMessage ?? "Unknown error")}\"}}").RootElement);
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    // Phase 3: State Change Detection
    private Task<JsonElement> CaptureUiSnapshot(JsonElement args)
    {
        try
        {
            var windowTitle = GetStringArg(args, "windowTitle");
            var snapshotId = GetStringArg(args, "snapshotId") ?? throw new ArgumentException("snapshotId is required");

            var automation = _session.GetAutomation();
            var detector = _session.GetStateChangeDetector();

            // Get root element (window or desktop)
            AutomationElement? root = null;
            if (!string.IsNullOrEmpty(windowTitle))
            {
                root = automation.GetWindowByTitle(windowTitle);
                if (root == null)
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Window not found: {EscapeJson(windowTitle)}\"}}").RootElement);
            }
            else
            {
                root = automation.GetDesktop();
            }

            // Capture snapshot
            var snapshot = detector.CaptureSnapshot(root);
            _session.CacheSnapshot(snapshotId, snapshot);

            return Task.FromResult(JsonDocument.Parse($@"{{
                ""success"": true,
                ""snapshotId"": ""{EscapeJson(snapshotId)}"",
                ""hash"": ""{snapshot.Hash}"",
                ""elementCount"": {snapshot.Elements.Count},
                ""capturedAt"": ""{snapshot.CapturedAt:o}""
            }}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    private Task<JsonElement> CompareUiSnapshots(JsonElement args)
    {
        try
        {
            var beforeSnapshotId = GetStringArg(args, "beforeSnapshotId") ?? throw new ArgumentException("beforeSnapshotId is required");
            var afterSnapshotId = GetStringArg(args, "afterSnapshotId");
            var windowTitle = GetStringArg(args, "windowTitle");

            var automation = _session.GetAutomation();
            var detector = _session.GetStateChangeDetector();

            // Get before snapshot
            var beforeSnapshot = _session.GetSnapshot(beforeSnapshotId);
            if (beforeSnapshot == null)
                return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Snapshot not found: {EscapeJson(beforeSnapshotId)}\"}}").RootElement);

            // Get or capture after snapshot
            TreeSnapshot afterSnapshot;
            if (!string.IsNullOrEmpty(afterSnapshotId))
            {
                var cached = _session.GetSnapshot(afterSnapshotId);
                if (cached == null)
                    return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Snapshot not found: {EscapeJson(afterSnapshotId)}\"}}").RootElement);
                afterSnapshot = cached;
            }
            else
            {
                // Auto-capture current state
                AutomationElement? root = null;
                if (!string.IsNullOrEmpty(windowTitle))
                {
                    root = automation.GetWindowByTitle(windowTitle);
                    if (root == null)
                        return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"Window not found: {EscapeJson(windowTitle)}\"}}").RootElement);
                }
                else
                {
                    root = automation.GetDesktop();
                }
                afterSnapshot = detector.CaptureSnapshot(root);
            }

            // Compare snapshots
            var result = detector.CompareSnapshots(beforeSnapshot, afterSnapshot);

            return Task.FromResult(JsonDocument.Parse($@"{{
                ""success"": true,
                ""stateChanged"": {result.StateChanged.ToString().ToLower()},
                ""addedCount"": {result.AddedCount},
                ""removedCount"": {result.RemovedCount},
                ""modifiedCount"": {result.ModifiedCount},
                ""diffSummary"": ""{EscapeJson(result.DiffSummary)}""
            }}").RootElement);
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement);
        }
    }

    // Sandbox Tools
    private async Task<JsonElement> LaunchAppSandboxed(JsonElement args)
    {
        try
        {
            var appPath = GetStringArg(args, "appPath") ?? throw new ArgumentException("appPath is required");
            var appExe = GetStringArg(args, "appExe") ?? throw new ArgumentException("appExe is required");
            var mcpServerPath = GetStringArg(args, "mcpServerPath") ?? throw new ArgumentException("mcpServerPath is required");
            var sharedFolderPath = GetStringArg(args, "sharedFolderPath") ?? throw new ArgumentException("sharedFolderPath is required");
            var outputFolderPath = GetStringArg(args, "outputFolderPath");
            var bootTimeoutMs = GetIntArg(args, "bootTimeoutMs", 60000);

            var sandboxManager = _session.GetSandboxManager();

            // Check if sandbox is already running
            if (sandboxManager.IsRunning)
            {
                return JsonDocument.Parse("{\"success\": false, \"error\": \"Sandbox is already running. Call close_sandbox first.\"}").RootElement;
            }

            // Ensure shared folder exists
            if (!Directory.Exists(sharedFolderPath))
            {
                Directory.CreateDirectory(sharedFolderPath);
            }

            // Build the .wsb configuration
            var builder = SandboxConfigurations.CreateMcpSandbox(
                appPath,
                mcpServerPath,
                sharedFolderPath,
                outputFolderPath);

            // Generate a temp .wsb file
            var wsbPath = Path.Combine(Path.GetTempPath(), $"mcp-sandbox-{Guid.NewGuid():N}.wsb");
            builder.BuildAndSave(wsbPath);

            // Set boot timeout
            sandboxManager.BootTimeoutMs = bootTimeoutMs;

            // Launch the sandbox
            var result = await sandboxManager.LaunchSandboxAsync(wsbPath, sharedFolderPath);

            // Clean up the .wsb file (sandbox has already read it)
            try { File.Delete(wsbPath); } catch { }

            if (result.Success)
            {
                return JsonDocument.Parse($"{{\"success\": true, \"message\": \"Sandbox launched and MCP server ready\", \"processId\": {result.ProcessId}, \"sharedFolderPath\": \"{EscapeJson(result.SharedFolderPath ?? "")}\"}}").RootElement;
            }
            else
            {
                var sandboxAvailable = result.SandboxAvailable ? "true" : "false";
                return JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(result.Error ?? "Unknown error")}\", \"sandboxAvailable\": {sandboxAvailable}}}").RootElement;
            }
        }
        catch (ArgumentException ex)
        {
            // Security validation errors from WsbConfigBuilder
            return JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement;
        }
        catch (Exception ex)
        {
            return JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement;
        }
    }

    private async Task<JsonElement> CloseSandbox(JsonElement args)
    {
        try
        {
            var timeoutMs = GetIntArg(args, "timeoutMs", 10000);

            var sandboxManager = _session.GetSandboxManager();

            if (!sandboxManager.IsRunning)
            {
                return JsonDocument.Parse("{\"success\": true, \"message\": \"No sandbox was running\"}").RootElement;
            }

            await sandboxManager.CloseSandboxAsync(timeoutMs);

            return JsonDocument.Parse("{\"success\": true, \"message\": \"Sandbox closed successfully\"}").RootElement;
        }
        catch (Exception ex)
        {
            return JsonDocument.Parse($"{{\"success\": false, \"error\": \"{EscapeJson(ex.Message)}\"}}").RootElement;
        }
    }

    // Helper methods
    private string? GetStringArg(JsonElement args, string key)
    {
        if (args.ValueKind == JsonValueKind.Null)
            return null;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind != JsonValueKind.Null
            ? prop.GetString()
            : null;
    }

    private int GetIntArg(JsonElement args, string key, int defaultValue = 0)
    {
        if (args.ValueKind == JsonValueKind.Null)
            return defaultValue;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : defaultValue;
    }

    private bool GetBoolArg(JsonElement args, string key, bool defaultValue = false)
    {
        if (args.ValueKind == JsonValueKind.Null)
            return defaultValue;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.True
            ? true
            : args.TryGetProperty(key, out var prop2) && prop2.ValueKind == JsonValueKind.False
                ? false
                : defaultValue;
    }

    private string EscapeJson(string? value)
    {
        if (value == null)
            return "";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
