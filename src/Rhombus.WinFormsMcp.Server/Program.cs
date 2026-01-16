using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
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
/// It communicates via JSON-RPC over stdio (default) or TCP (with --tcp flag).
///
/// Usage:
///   Rhombus.WinFormsMcp.Server.exe              # stdio mode (default)
///   Rhombus.WinFormsMcp.Server.exe --tcp 9999   # TCP mode on port 9999
/// </summary>
class Program
{
    private static AutomationServer? _server;
    private static string? _logFile;

    private static void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var line = $"[{timestamp}] {message}";
        Console.Error.WriteLine(line);

        // Also log to file if available
        if (_logFile != null)
        {
            try { File.AppendAllText(_logFile, line + Environment.NewLine); }
            catch { /* ignore file write errors */ }
        }
    }

    static async Task Main(string[] args)
    {
        // Setup crash log file (C:\Shared exists in sandbox)
        var sharedDir = @"C:\Shared";
        if (Directory.Exists(sharedDir))
        {
            _logFile = Path.Combine(sharedDir, "server-crash.log");
            try { File.WriteAllText(_logFile, $"=== Server starting at {DateTime.Now} ==={Environment.NewLine}"); }
            catch { _logFile = null; }
        }

        // Global unhandled exception handlers
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Log($"UNHANDLED EXCEPTION (IsTerminating={e.IsTerminating}):");
            Log($"  Type: {ex?.GetType().FullName}");
            Log($"  Message: {ex?.Message}");
            Log($"  StackTrace: {ex?.StackTrace}");
            if (ex?.InnerException != null)
            {
                Log($"  InnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                Log($"  InnerStackTrace: {ex.InnerException.StackTrace}");
            }
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            Log($"UNOBSERVED TASK EXCEPTION:");
            Log($"  Type: {e.Exception?.GetType().FullName}");
            Log($"  Message: {e.Exception?.Message}");
            e.SetObserved();
        };

        // Assembly load logging
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            Log($"ASSEMBLY RESOLVING: {name.FullName}");
            return null; // Let default resolution continue
        };

        AppDomain.CurrentDomain.AssemblyLoad += (sender, e) =>
        {
            Log($"ASSEMBLY LOADED: {e.LoadedAssembly.FullName} from {e.LoadedAssembly.Location}");
        };

        try
        {
            Log("Server startup begin");
            Log($"  Working directory: {Environment.CurrentDirectory}");
            Log($"  Assembly location: {Assembly.GetExecutingAssembly().Location}");
            Log($"  .NET version: {Environment.Version}");
            Log($"  OS: {Environment.OSVersion}");
            Log($"  Args: {string.Join(" ", args)}");

            int? tcpPort = null;

            // Parse arguments
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--tcp" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out var port))
                    {
                        tcpPort = port;
                        Log($"  TCP port: {port}");
                    }
                    else
                    {
                        Log($"Invalid port: {args[i + 1]}");
                        Environment.Exit(1);
                    }
                }
            }

            Log("Creating AutomationServer...");
            _server = new AutomationServer();
            Log("AutomationServer created successfully");

            if (tcpPort.HasValue)
            {
                Log($"Starting TCP server on port {tcpPort.Value}...");
                await _server.RunTcpAsync(tcpPort.Value);
            }
            else
            {
                Log("Starting stdio server...");
                await _server.RunAsync();
            }
        }
        catch (Exception ex)
        {
            Log($"FATAL ERROR in Main:");
            Log($"  Type: {ex.GetType().FullName}");
            Log($"  Message: {ex.Message}");
            Log($"  StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Log($"  InnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
            }
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }
}

/// <summary>
/// Represents a UI event captured by the event monitoring system.
/// </summary>
public class UiEvent
{
    public string Type { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string? WindowTitle { get; set; }
    public int? ProcessId { get; set; }
    public string? Details { get; set; }
}

/// <summary>
/// Represents a pending action awaiting confirmation.
/// </summary>
public class PendingConfirmation
{
    public string Token { get; set; } = "";
    public string Action { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Target { get; set; }
    public JsonElement? Parameters { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Session manager for tracking automation contexts and element references
/// </summary>
class SessionManager
{
    private readonly Dictionary<string, AutomationElement> _elementCache = new();
    private readonly Dictionary<int, object> _processContext = new();
    private readonly Dictionary<string, TreeSnapshot> _snapshotCache = new();
    private readonly Dictionary<string, int> _launchedAppsByPath = new(); // Track last launched PID by executable path
    private readonly Queue<UiEvent> _eventQueue = new();
    private readonly HashSet<string> _subscribedEventTypes = new();
    private readonly Dictionary<string, PendingConfirmation> _pendingConfirmations = new();
    private readonly HashSet<string> _expandedElements = new();
    private readonly TreeCache _treeCache = new();
    private const int MaxEventQueueSize = 10;
    private const int ConfirmationTimeoutSeconds = 60;
    private int _eventsDropped = 0;
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

    public TreeCache GetTreeCache() => _treeCache;

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

    /// <summary>
    /// Track a launched app by its executable path. Returns the previous PID if one was tracked.
    /// </summary>
    public int? TrackLaunchedApp(string exePath, int pid)
    {
        var normalizedPath = Path.GetFullPath(exePath).ToLowerInvariant();
        int? previousPid = null;
        if (_launchedAppsByPath.TryGetValue(normalizedPath, out var oldPid))
        {
            previousPid = oldPid;
        }
        _launchedAppsByPath[normalizedPath] = pid;
        return previousPid;
    }

    /// <summary>
    /// Get the previously launched PID for an executable path (if any).
    /// </summary>
    public int? GetPreviousLaunchedPid(string exePath)
    {
        var normalizedPath = Path.GetFullPath(exePath).ToLowerInvariant();
        return _launchedAppsByPath.TryGetValue(normalizedPath, out var pid) ? pid : null;
    }

    /// <summary>
    /// Remove tracking for a launched app.
    /// </summary>
    public void UntrackLaunchedApp(string exePath)
    {
        var normalizedPath = Path.GetFullPath(exePath).ToLowerInvariant();
        _launchedAppsByPath.Remove(normalizedPath);
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

    /// <summary>
    /// Subscribe to specific event types. Events will be queued for later retrieval.
    /// </summary>
    public void SubscribeToEvents(IEnumerable<string> eventTypes)
    {
        foreach (var eventType in eventTypes)
        {
            _subscribedEventTypes.Add(eventType.ToLowerInvariant());
        }
    }

    /// <summary>
    /// Get the list of currently subscribed event types.
    /// </summary>
    public IReadOnlyCollection<string> GetSubscribedEventTypes() => _subscribedEventTypes;

    /// <summary>
    /// Add an event to the queue. If queue is full, oldest event is dropped.
    /// </summary>
    public void EnqueueEvent(UiEvent evt)
    {
        if (!_subscribedEventTypes.Contains(evt.Type.ToLowerInvariant()))
            return;

        if (_eventQueue.Count >= MaxEventQueueSize)
        {
            _eventQueue.Dequeue();
            _eventsDropped++;
        }
        _eventQueue.Enqueue(evt);
    }

    /// <summary>
    /// Get all queued events and clear the queue. Returns events dropped count.
    /// </summary>
    public (List<UiEvent> events, int droppedCount) DrainEventQueue()
    {
        var events = _eventQueue.ToList();
        _eventQueue.Clear();
        var dropped = _eventsDropped;
        _eventsDropped = 0;
        return (events, dropped);
    }

    /// <summary>
    /// Check if any events are subscribed.
    /// </summary>
    public bool HasSubscriptions => _subscribedEventTypes.Count > 0;

    /// <summary>
    /// Create a pending confirmation for a destructive action.
    /// </summary>
    public PendingConfirmation CreateConfirmation(string action, string description, string? target, JsonElement? parameters)
    {
        // Clean up expired confirmations first
        CleanupExpiredConfirmations();

        var confirmation = new PendingConfirmation
        {
            Token = Guid.NewGuid().ToString("N"),
            Action = action,
            Description = description,
            Target = target,
            Parameters = parameters,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddSeconds(ConfirmationTimeoutSeconds)
        };

        _pendingConfirmations[confirmation.Token] = confirmation;
        return confirmation;
    }

    /// <summary>
    /// Get and remove a pending confirmation by token. Returns null if not found or expired.
    /// </summary>
    public PendingConfirmation? ConsumeConfirmation(string token)
    {
        CleanupExpiredConfirmations();

        if (_pendingConfirmations.TryGetValue(token, out var confirmation))
        {
            _pendingConfirmations.Remove(token);

            if (confirmation.ExpiresAt < DateTime.UtcNow)
            {
                return null; // Expired
            }

            return confirmation;
        }

        return null;
    }

    private void CleanupExpiredConfirmations()
    {
        var expired = _pendingConfirmations
            .Where(kvp => kvp.Value.ExpiresAt < DateTime.UtcNow)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var token in expired)
        {
            _pendingConfirmations.Remove(token);
        }
    }

    /// <summary>
    /// Mark an element for expansion. The tree builder will expand its children
    /// on the next get_ui_tree call regardless of depth limit.
    /// </summary>
    /// <param name="elementKey">AutomationId or Name of the element</param>
    public void MarkForExpansion(string elementKey)
    {
        _expandedElements.Add(elementKey);
    }

    /// <summary>
    /// Check if an element is marked for expansion.
    /// </summary>
    public bool IsMarkedForExpansion(string elementKey)
    {
        return _expandedElements.Contains(elementKey);
    }

    /// <summary>
    /// Get all elements marked for expansion.
    /// </summary>
    public IReadOnlyCollection<string> GetExpandedElements() => _expandedElements;

    /// <summary>
    /// Clear expansion marks for an element.
    /// </summary>
    public void ClearExpansionMark(string elementKey)
    {
        _expandedElements.Remove(elementKey);
    }

    /// <summary>
    /// Clear all expansion marks.
    /// </summary>
    public void ClearAllExpansionMarks()
    {
        _expandedElements.Clear();
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
    private readonly WindowManager _windowManager = new();

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

            // Touch/Pen Injection Tools
            { "touch_tap", TouchTap },
            { "touch_drag", TouchDrag },
            { "pinch_zoom", PinchZoom },
            { "rotate", RotateGesture },
            { "multi_touch_gesture", MultiTouchGesture },
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

            // Capability Detection
            { "get_capabilities", GetCapabilities },
            { "get_dpi_info", GetDpiInfo },

            // Process Information
            { "get_process_info", GetProcessInfo },

            // Event Subscription
            { "subscribe_to_events", SubscribeToEvents },
            { "get_pending_events", GetPendingEvents },

            // Advanced Selectors
            { "find_element_near_anchor", FindElementNearAnchor },

            // Progressive Disclosure
            { "mark_for_expansion", MarkForExpansion },
            { "clear_expansion_marks", ClearExpansionMarks },

            // Self-Healing
            { "relocate_element", RelocateElement },
            { "check_element_stale", CheckElementStale },

            // Performance/Caching
            { "get_cache_stats", GetCacheStats },
            { "invalidate_cache", InvalidateCache },

            // Confirmation Flow
            { "confirm_action", ConfirmAction },
            { "execute_confirmed_action", ExecuteConfirmedAction },
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
                // Skip writing response for notifications (response is null)
                if (response != null)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                    await writer.FlushAsync();
                }
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

    /// <summary>
    /// Run the MCP server in TCP mode, listening on the specified port.
    /// Accepts connections and processes JSON-RPC messages over the TCP stream.
    /// </summary>
    public async Task RunTcpAsync(int port)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        // Write ready signal to stderr (stdout is reserved for JSON-RPC in stdio mode)
        Console.Error.WriteLine($"MCP Server listening on TCP port {port}");
        Console.Error.WriteLine($"Connect with: telnet localhost {port}");

        while (true)
        {
            Console.Error.WriteLine("Waiting for client connection...");
            var client = await listener.AcceptTcpClientAsync();
            Console.Error.WriteLine($"Client connected from {client.Client.RemoteEndPoint}");

            try
            {
                await HandleTcpClientAsync(client);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Client error: {ex.Message}");
            }
            finally
            {
                client.Close();
                Console.Error.WriteLine("Client disconnected");
            }
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client)
    {
        var stream = client.GetStream();
        var reader = new StreamReader(stream);
        var writer = new StreamWriter(stream) { AutoFlush = true };

        // Process incoming messages
        while (client.Connected)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line))
                break;

            try
            {
                var request = JsonDocument.Parse(line).RootElement;
                var response = await ProcessRequest(request);
                // Skip writing response for notifications (response is null)
                if (response != null)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                }
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

        // Handle MCP notifications (no response expected)
        // notifications/initialized is sent by client after initialize response
        if (method?.StartsWith("notifications/") == true)
        {
            // Return null to indicate no response should be sent
            return null!;
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
                description = "Find a UI element by AutomationId, Name, ClassName, or ControlType. Supports regex patterns for dynamic IDs.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        automationId = new { type = "string", description = "Exact AutomationId of the element" },
                        automationIdPattern = new { type = "string", description = "Regex pattern for AutomationId (e.g., 'btn_Submit_\\d+' matches btn_Submit_123)" },
                        name = new { type = "string", description = "Name of the element" },
                        namePattern = new { type = "string", description = "Regex pattern for Name" },
                        className = new { type = "string", description = "ClassName of the element" },
                        controlType = new { type = "string", description = "ControlType of the element" },
                        parent = new { type = "string", description = "Parent element path (optional)" },
                        pollIntervalMs = new { type = "integer", description = "Interval between search attempts in milliseconds (default: 100)" }
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
                        clearFirst = new { type = "boolean", description = "Clear field before typing" },
                        clearDelayMs = new { type = "integer", description = "Delay in milliseconds after select-all before typing (default: 100)" }
                    },
                    required = new[] { "elementPath", "text" }
                }
            },
            new
            {
                name = "set_value",
                description = "Set the value of an input element by selecting all and typing the new value",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementPath = new { type = "string", description = "Path or identifier of the element" },
                        value = new { type = "string", description = "Value to set" },
                        selectAllDelayMs = new { type = "integer", description = "Delay in milliseconds after select-all before typing (default: 50)" }
                    },
                    required = new[] { "elementPath", "value" }
                }
            },
            new
            {
                name = "drag_drop",
                description = "Drag an element and drop it onto another element",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        sourceElementPath = new { type = "string", description = "Path or identifier of the source element to drag" },
                        targetElementPath = new { type = "string", description = "Path or identifier of the target element to drop onto" },
                        dragSetupDelayMs = new { type = "integer", description = "Delay in milliseconds after positioning cursor before starting drag (default: 100)" },
                        dropDelayMs = new { type = "integer", description = "Delay in milliseconds before releasing mouse button after drag (default: 200)" }
                    },
                    required = new[] { "sourceElementPath", "targetElementPath" }
                }
            },
            new
            {
                name = "close_app",
                description = "Close a running application by process ID",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer", description = "Process ID of the application to close" },
                        force = new { type = "boolean", description = "If true, forcefully kills the process; if false, requests graceful close first (default: false)" },
                        closeTimeoutMs = new { type = "integer", description = "Timeout in milliseconds to wait for graceful close before force killing (default: 5000)" }
                    },
                    required = new[] { "pid" }
                }
            },
            new
            {
                name = "wait_for_element",
                description = "Wait for an element to appear in the UI",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        automationId = new { type = "string", description = "AutomationId of the element to wait for" },
                        parent = new { type = "string", description = "Parent element path (optional)" },
                        timeoutMs = new { type = "integer", description = "Timeout in milliseconds to wait for the element (default: 10000)" },
                        pollIntervalMs = new { type = "integer", description = "Interval between search attempts in milliseconds (default: 100)" }
                    },
                    required = new[] { "automationId" }
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
                        workingDirectory = new { type = "string", description = "Working directory (optional)" },
                        idleTimeoutMs = new { type = "integer", description = "Timeout in milliseconds to wait for app to become idle (default: 5000)" }
                    },
                    required = new[] { "path" }
                }
            },
            new
            {
                name = "take_screenshot",
                description = "Take a screenshot. Specify windowHandle or windowTitle to capture a specific window (recommended). Response includes window bounds for coordinate reference. Without window params, captures full desktop.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        outputPath = new { type = "string", description = "Path to save the screenshot" },
                        windowHandle = new { type = "string", description = "Window handle (hex) to screenshot. Takes priority over windowTitle." },
                        windowTitle = new { type = "string", description = "Window title to screenshot (substring match). Use this or windowHandle for window-specific capture." },
                        elementPath = new { type = "string", description = "Specific element to screenshot (optional, uses cached element)" }
                    },
                    required = new[] { "outputPath" }
                }
            },
            // Touch/Pen Injection Tools
            new
            {
                name = "touch_tap",
                description = "Simulate a touch tap. Use windowHandle or windowTitle for window-relative coordinates, or omit for screen coordinates. No delays by default - add holdMs for long-press gestures.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string", description = "Window handle (hex, e.g. '0x1A2B3C') for window-relative coordinates" },
                        windowTitle = new { type = "string", description = "Window title (substring match) for window-relative coordinates" },
                        x = new { type = "integer", description = "X coordinate (window-relative if window specified, otherwise screen)" },
                        y = new { type = "integer", description = "Y coordinate (window-relative if window specified, otherwise screen)" },
                        holdMs = new { type = "integer", description = "Milliseconds to hold before release (default 0)" }
                    },
                    required = new[] { "x", "y" }
                }
            },
            new
            {
                name = "touch_drag",
                description = "Simulate a touch drag from one point to another. Use windowHandle or windowTitle for window-relative coordinates, or omit for screen coordinates. No delays by default (instant). Add delayMs if slower gesture speed is needed.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string", description = "Window handle (hex, e.g. '0x1A2B3C') for window-relative coordinates" },
                        windowTitle = new { type = "string", description = "Window title (substring match) for window-relative coordinates" },
                        x1 = new { type = "integer", description = "Start X coordinate (window-relative if window specified, otherwise screen)" },
                        y1 = new { type = "integer", description = "Start Y coordinate (window-relative if window specified, otherwise screen)" },
                        x2 = new { type = "integer", description = "End X coordinate (window-relative if window specified, otherwise screen)" },
                        y2 = new { type = "integer", description = "End Y coordinate (window-relative if window specified, otherwise screen)" },
                        steps = new { type = "integer", description = "Number of intermediate points (default 10)" },
                        delayMs = new { type = "integer", description = "Delay in milliseconds between steps (default 0)" }
                    },
                    required = new[] { "x1", "y1", "x2", "y2" }
                }
            },
            new
            {
                name = "pinch_zoom",
                description = "Simulate a pinch-to-zoom gesture with two fingers. Use windowHandle or windowTitle for window-relative coordinates, or omit for screen coordinates. No delays by default (instant). Add delayMs if slower animation is needed.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string", description = "Window handle (hex, e.g. '0x1A2B3C') for window-relative coordinates" },
                        windowTitle = new { type = "string", description = "Window title (substring match) for window-relative coordinates" },
                        centerX = new { type = "integer", description = "Center X coordinate of the pinch gesture (window-relative if window specified, otherwise screen)" },
                        centerY = new { type = "integer", description = "Center Y coordinate of the pinch gesture (window-relative if window specified, otherwise screen)" },
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
                description = "Simulate a pen stroke from one point to another with pressure. Use windowHandle or windowTitle for window-relative coordinates, or omit for screen coordinates. No delays by default (instant). Add delayMs if slower stroke speed is needed.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string", description = "Window handle (hex, e.g. '0x1A2B3C') for window-relative coordinates" },
                        windowTitle = new { type = "string", description = "Window title (substring match) for window-relative coordinates" },
                        x1 = new { type = "integer", description = "Start X coordinate (window-relative if window specified, otherwise screen)" },
                        y1 = new { type = "integer", description = "Start Y coordinate (window-relative if window specified, otherwise screen)" },
                        x2 = new { type = "integer", description = "End X coordinate (window-relative if window specified, otherwise screen)" },
                        y2 = new { type = "integer", description = "End Y coordinate (window-relative if window specified, otherwise screen)" },
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
                description = "Simulate a pen tap. Use windowHandle or windowTitle for window-relative coordinates, or omit for screen coordinates. No delays by default - add holdMs for long-press gestures.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string", description = "Window handle (hex, e.g. '0x1A2B3C') for window-relative coordinates" },
                        windowTitle = new { type = "string", description = "Window title (substring match) for window-relative coordinates" },
                        x = new { type = "integer", description = "X coordinate (window-relative if window specified, otherwise screen)" },
                        y = new { type = "integer", description = "Y coordinate (window-relative if window specified, otherwise screen)" },
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
                description = "Simulate a mouse drag. If windowHandle or windowTitle is provided, coordinates are window-relative. Response includes 'windows' array.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string", description = "Window handle (hex, e.g., '0x1A2B3C') for precise targeting" },
                        windowTitle = new { type = "string", description = "Window title (substring match) for targeting" },
                        x1 = new { type = "integer", description = "Start X coordinate (window-relative if window specified)" },
                        y1 = new { type = "integer", description = "Start Y coordinate (window-relative if window specified)" },
                        x2 = new { type = "integer", description = "End X coordinate (window-relative if window specified)" },
                        y2 = new { type = "integer", description = "End Y coordinate (window-relative if window specified)" },
                        steps = new { type = "integer", description = "Number of intermediate points (default 10)" },
                        delayMs = new { type = "integer", description = "Delay in milliseconds between steps (default 0)" }
                    },
                    required = new[] { "x1", "y1", "x2", "y2" }
                }
            },
            new
            {
                name = "mouse_drag_path",
                description = "Drag mouse through waypoints. If windowHandle or windowTitle is provided, coordinates are window-relative. Response includes 'windows' array.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string", description = "Window handle (hex, e.g., '0x1A2B3C') for precise targeting" },
                        windowTitle = new { type = "string", description = "Window title (substring match) for targeting" },
                        points = new
                        {
                            type = "array",
                            description = "Waypoints (min 2, max 1000). Coordinates are window-relative if window specified",
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
                        stepsPerSegment = new { type = "integer", description = "Interpolation steps between each waypoint (default 1)" },
                        delayMs = new { type = "integer", description = "Delay in milliseconds between steps (default 0)" }
                    },
                    required = new[] { "points" }
                }
            },
            new
            {
                name = "mouse_click",
                description = "Simulate a mouse click. If windowHandle or windowTitle is provided, coordinates are window-relative and translated to screen coordinates. Response includes 'windows' array with all visible windows.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string", description = "Window handle (hex, e.g., '0x1A2B3C') for precise targeting" },
                        windowTitle = new { type = "string", description = "Window title (substring match) for targeting" },
                        x = new { type = "integer", description = "X coordinate (window-relative if window specified, otherwise screen)" },
                        y = new { type = "integer", description = "Y coordinate (window-relative if window specified, otherwise screen)" },
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
                        expand = new { type = "boolean", description = "True to expand, false to collapse" },
                        uiUpdateDelayMs = new { type = "integer", description = "Delay in milliseconds to wait for UI to update after action (default: 100)" }
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
                        amount = new { type = "string", description = "Scroll amount: SmallDecrement or LargeDecrement (default SmallDecrement)" },
                        uiUpdateDelayMs = new { type = "integer", description = "Delay in milliseconds to wait for UI to update after scrolling (default: 100)" }
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
            },
            // Capability Detection
            new
            {
                name = "get_capabilities",
                description = "Detect MCP server capabilities including sandbox availability, OS version, and supported features. Use before sandbox operations to check if sandboxing is available.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new
            {
                name = "get_dpi_info",
                description = "Get DPI scaling information for coordinate normalization. Returns system DPI, scale factor, and per-monitor awareness status. Use to ensure click coordinates land correctly on high-DPI displays.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowTitle = new { type = "string", description = "Optional: Get DPI info for a specific window (may differ in per-monitor setups)" }
                    }
                }
            },
            // Process Information
            new
            {
                name = "get_process_info",
                description = "Get process metadata for window targeting and continuity. Returns PID, process name, responding status, and window state from a window handle.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string", description = "HWND in hex format (e.g., '0x0004A3B8') or decimal" },
                        windowTitle = new { type = "string", description = "Window title (partial match supported) - alternative to windowHandle" }
                    }
                }
            },
            // Event Subscription
            new
            {
                name = "subscribe_to_events",
                description = "Subscribe to UI events. Events are queued (max 10) and can be retrieved via get_pending_events or injected into get_ui_tree responses. Supported event types: window_opened, dialog_shown, structure_changed, property_changed.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        event_types = new
                        {
                            type = "array",
                            description = "Event types to subscribe to: window_opened, dialog_shown, structure_changed, property_changed",
                            items = new { type = "string" }
                        }
                    },
                    required = new[] { "event_types" }
                }
            },
            new
            {
                name = "get_pending_events",
                description = "Retrieve all pending events from the queue and clear it. Returns events that occurred since last retrieval.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            // Advanced Selectors
            new
            {
                name = "find_element_near_anchor",
                description = "Find an element relative to a known anchor element. Useful for self-healing selectors when primary selectors fail. Searches siblings and nearby elements.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        anchorElementId = new { type = "string", description = "Cached element ID of the anchor element" },
                        anchorAutomationId = new { type = "string", description = "AutomationId to find the anchor (if anchorElementId not provided)" },
                        anchorName = new { type = "string", description = "Name to find the anchor (if anchorElementId not provided)" },
                        targetControlType = new { type = "string", description = "ControlType of target element (e.g., 'Button', 'Edit', 'Text')" },
                        targetNamePattern = new { type = "string", description = "Regex pattern for target element name" },
                        targetAutomationIdPattern = new { type = "string", description = "Regex pattern for target element AutomationId" },
                        searchDirection = new { type = "string", description = "Direction: 'siblings' (default), 'children', 'parent_children' (siblings of parent)" },
                        maxDistance = new { type = "integer", description = "Max elements to search (default 10)" }
                    }
                }
            },
            // Progressive Disclosure
            new
            {
                name = "mark_for_expansion",
                description = "Mark an element for progressive disclosure expansion. On next get_ui_tree call, marked elements will have their children included regardless of depth limit. Use to drill down into specific areas of complex UIs.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementKey = new { type = "string", description = "AutomationId or Name of the element to mark for expansion" },
                        elementId = new { type = "string", description = "Cached element ID to mark (alternative to elementKey)" }
                    }
                }
            },
            new
            {
                name = "clear_expansion_marks",
                description = "Clear expansion marks for elements. Use after drilling down to clean up state.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementKey = new { type = "string", description = "Specific element to clear (if omitted, clears all marks)" }
                    }
                }
            },
            // Self-Healing
            new
            {
                name = "relocate_element",
                description = "Attempt to relocate a stale element using its original search criteria. Use when an action fails with 'stale element reference'. Will re-find the element and update the cache.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Original cached element ID that is now stale" },
                        automationId = new { type = "string", description = "AutomationId to search for (alternative to elementId)" },
                        name = new { type = "string", description = "Name to search for (alternative to elementId)" },
                        className = new { type = "string", description = "ClassName to search for (alternative to elementId)" },
                        controlType = new { type = "string", description = "ControlType for heuristic matching" }
                    }
                }
            },
            new
            {
                name = "check_element_stale",
                description = "Check if a cached element reference is still valid. Returns true if element is stale (no longer accessible).",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID to check" }
                    },
                    required = new[] { "elementId" }
                }
            },
            // Performance/Caching
            new
            {
                name = "get_cache_stats",
                description = "Get tree cache statistics including hit rate and cache age. Use for monitoring and debugging performance.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new
            {
                name = "invalidate_cache",
                description = "Invalidate the UI tree cache. Use after actions that may have changed the UI structure.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        reset_stats = new { type = "boolean", description = "Also reset cache statistics (default: false)" }
                    }
                }
            },
            // Confirmation Flow
            new
            {
                name = "confirm_action",
                description = "Request confirmation for a destructive action. Returns a confirmation token that must be passed to execute_confirmed_action within 60 seconds. Use for close_app, force_close, or any potentially destructive operation.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        action = new { type = "string", description = "Action to confirm: 'close_app', 'force_close', 'send_keys_dangerous', 'custom'" },
                        description = new { type = "string", description = "Human-readable description of what will happen" },
                        target = new { type = "string", description = "Target of the action (e.g., process name, window title)" },
                        parameters = new { type = "object", description = "Parameters to pass to the action. For 'send_keys_dangerous': {keys: string, windowHandle?: string, windowTitle?: string}. IMPORTANT: Always include windowHandle or windowTitle to ensure keys go to the correct window." }
                    },
                    required = new[] { "action", "description" }
                }
            },
            new
            {
                name = "execute_confirmed_action",
                description = "Execute a previously confirmed action using its confirmation token. Token must be used within 60 seconds of creation.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        confirmationToken = new { type = "string", description = "Token from confirm_action response" }
                    },
                    required = new[] { "confirmationToken" }
                }
            }
        };
    }

    // Tool implementations
    private Task<JsonElement> FindElement(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var automation = _session.GetAutomation();
            var pid = GetIntArg(args, "pid");
            var automationId = GetStringArg(args, "automationId");
            var automationIdPattern = GetStringArg(args, "automationIdPattern");
            var name = GetStringArg(args, "name");
            var namePattern = GetStringArg(args, "namePattern");
            var className = GetStringArg(args, "className");

            AutomationElement? element = null;
            string? matchedBy = null;

            // Exact match first (faster)
            if (!string.IsNullOrEmpty(automationId))
            {
                element = automation.FindByAutomationId(automationId);
                matchedBy = "automationId";
            }
            else if (!string.IsNullOrEmpty(name))
            {
                element = automation.FindByName(name);
                matchedBy = "name";
            }
            else if (!string.IsNullOrEmpty(className))
            {
                element = automation.FindByClassName(className);
                matchedBy = "className";
            }
            // Pattern matching (slower - searches all elements)
            else if (!string.IsNullOrEmpty(automationIdPattern))
            {
                try
                {
                    var regex = new System.Text.RegularExpressions.Regex(automationIdPattern,
                        System.Text.RegularExpressions.RegexOptions.Compiled,
                        TimeSpan.FromSeconds(1));
                    element = FindElementByPattern(automation, e =>
                        !string.IsNullOrEmpty(e.AutomationId) && regex.IsMatch(e.AutomationId));
                    matchedBy = "automationIdPattern";
                }
                catch (System.Text.RegularExpressions.RegexParseException ex)
                {
                    stopwatch.Stop();
                    return Task.FromResult(ToolResponse.Fail($"Invalid regex pattern: {ex.Message}", _windowManager,
                        ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
                }
            }
            else if (!string.IsNullOrEmpty(namePattern))
            {
                try
                {
                    var regex = new System.Text.RegularExpressions.Regex(namePattern,
                        System.Text.RegularExpressions.RegexOptions.Compiled,
                        TimeSpan.FromSeconds(1));
                    element = FindElementByPattern(automation, e =>
                        !string.IsNullOrEmpty(e.Name) && regex.IsMatch(e.Name));
                    matchedBy = "namePattern";
                }
                catch (System.Text.RegularExpressions.RegexParseException ex)
                {
                    stopwatch.Stop();
                    return Task.FromResult(ToolResponse.Fail($"Invalid regex pattern: {ex.Message}", _windowManager,
                        ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
                }
            }

            stopwatch.Stop();

            if (element == null)
                return Task.FromResult(ToolResponse.Fail("Element not found", _windowManager,
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());

            var elementId = _session.CacheElement(element);
            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("elementId", elementId),
                ("name", element.Name ?? ""),
                ("automationId", element.AutomationId ?? ""),
                ("controlType", element.ControlType.ToString()),
                ("matched_by", matchedBy),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    /// <summary>
    /// Find element by predicate - searches desktop tree (limited depth for performance)
    /// </summary>
    private AutomationElement? FindElementByPattern(AutomationHelper automation, Func<AutomationElement, bool> predicate, int maxDepth = 5)
    {
        var desktop = automation.GetDesktop();
        if (desktop == null) return null;

        return SearchTreeForElement(desktop, predicate, 0, maxDepth);
    }

    private AutomationElement? SearchTreeForElement(AutomationElement parent, Func<AutomationElement, bool> predicate, int depth, int maxDepth)
    {
        if (depth > maxDepth) return null;

        try
        {
            // Check children
            var children = parent.FindAllChildren();
            foreach (var child in children)
            {
                // Check if this element matches
                if (predicate(child))
                    return child;

                // Recursively search children
                var found = SearchTreeForElement(child, predicate, depth + 1, maxDepth);
                if (found != null)
                    return found;
            }
        }
        catch
        {
            // Element may have become invalid during search
        }

        return null;
    }

    private Task<JsonElement> FindElementNearAnchor(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var automation = _session.GetAutomation();

            // Find anchor element
            AutomationElement? anchor = null;
            var anchorElementId = GetStringArg(args, "anchorElementId");
            var anchorAutomationId = GetStringArg(args, "anchorAutomationId");
            var anchorName = GetStringArg(args, "anchorName");

            if (!string.IsNullOrEmpty(anchorElementId))
            {
                anchor = _session.GetElement(anchorElementId);
            }
            else if (!string.IsNullOrEmpty(anchorAutomationId))
            {
                anchor = automation.FindByAutomationId(anchorAutomationId);
            }
            else if (!string.IsNullOrEmpty(anchorName))
            {
                anchor = automation.FindByName(anchorName);
            }

            if (anchor == null)
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Fail("Anchor element not found", _windowManager,
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }

            // Get search parameters
            var targetControlType = GetStringArg(args, "targetControlType");
            var targetNamePattern = GetStringArg(args, "targetNamePattern");
            var targetAutomationIdPattern = GetStringArg(args, "targetAutomationIdPattern");
            var searchDirection = GetStringArg(args, "searchDirection") ?? "siblings";
            var maxDistance = GetIntArg(args, "maxDistance", 10);

            // Build predicate for matching
            System.Text.RegularExpressions.Regex? nameRegex = null;
            System.Text.RegularExpressions.Regex? automationIdRegex = null;

            if (!string.IsNullOrEmpty(targetNamePattern))
            {
                try
                {
                    nameRegex = new System.Text.RegularExpressions.Regex(targetNamePattern,
                        System.Text.RegularExpressions.RegexOptions.Compiled, TimeSpan.FromSeconds(1));
                }
                catch (System.Text.RegularExpressions.RegexParseException ex)
                {
                    stopwatch.Stop();
                    return Task.FromResult(ToolResponse.Fail($"Invalid name pattern: {ex.Message}", _windowManager,
                        ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
                }
            }

            if (!string.IsNullOrEmpty(targetAutomationIdPattern))
            {
                try
                {
                    automationIdRegex = new System.Text.RegularExpressions.Regex(targetAutomationIdPattern,
                        System.Text.RegularExpressions.RegexOptions.Compiled, TimeSpan.FromSeconds(1));
                }
                catch (System.Text.RegularExpressions.RegexParseException ex)
                {
                    stopwatch.Stop();
                    return Task.FromResult(ToolResponse.Fail($"Invalid automationId pattern: {ex.Message}", _windowManager,
                        ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
                }
            }

            Func<AutomationElement, bool> predicate = e =>
            {
                if (!string.IsNullOrEmpty(targetControlType) &&
                    !e.ControlType.ToString().Equals(targetControlType, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (nameRegex != null && (string.IsNullOrEmpty(e.Name) || !nameRegex.IsMatch(e.Name)))
                    return false;

                if (automationIdRegex != null && (string.IsNullOrEmpty(e.AutomationId) || !automationIdRegex.IsMatch(e.AutomationId)))
                    return false;

                return true;
            };

            // Search for element
            AutomationElement? found = null;
            int searchedCount = 0;
            var candidates = new List<AutomationElement>();

            try
            {
                switch (searchDirection.ToLowerInvariant())
                {
                    case "children":
                        candidates.AddRange(anchor.FindAllChildren().Take(maxDistance));
                        break;

                    case "parent_children":
                        var parent = anchor.Parent;
                        if (parent != null)
                        {
                            candidates.AddRange(parent.FindAllChildren().Take(maxDistance));
                        }
                        break;

                    case "siblings":
                    default:
                        // Get parent's children (siblings)
                        var siblingParent = anchor.Parent;
                        if (siblingParent != null)
                        {
                            foreach (var sibling in siblingParent.FindAllChildren().Take(maxDistance))
                            {
                                // Skip the anchor itself
                                if (sibling.Equals(anchor)) continue;
                                candidates.Add(sibling);
                            }
                        }
                        break;
                }

                // Find matching element
                foreach (var candidate in candidates)
                {
                    searchedCount++;
                    if (predicate(candidate))
                    {
                        found = candidate;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Fail($"Search failed: {ex.Message}", _windowManager,
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }

            stopwatch.Stop();

            if (found == null)
            {
                return Task.FromResult(ToolResponse.Fail("No matching element found near anchor", _windowManager,
                    ("searched_count", searchedCount),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }

            var elementId = _session.CacheElement(found);
            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("elementId", elementId),
                ("name", found.Name ?? ""),
                ("automationId", found.AutomationId ?? ""),
                ("controlType", found.ControlType.ToString()),
                ("searched_count", searchedCount),
                ("search_direction", searchDirection),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> ClickElement(JsonElement args)
    {
        try
        {
            // Accept either elementId or elementPath (LLMs use both)
            var elementId = GetStringArg(args, "elementId") ?? GetStringArg(args, "elementPath")
                ?? throw new ArgumentException("elementId or elementPath is required");
            var doubleClick = GetBoolArg(args, "doubleClick", false);

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(ToolResponse.Fail("Element not found in session", _windowManager).ToJsonElement());

            var automation = _session.GetAutomation();
            automation.Click(element, doubleClick);

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("message", "Element clicked")).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> TypeText(JsonElement args)
    {
        try
        {
            // Accept either elementId or elementPath (LLMs use both)
            var elementId = GetStringArg(args, "elementId") ?? GetStringArg(args, "elementPath")
                ?? throw new ArgumentException("elementId or elementPath is required");
            var text = GetStringArg(args, "text") ?? "";
            var clearFirst = GetBoolArg(args, "clearFirst", false);

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(ToolResponse.Fail("Element not found in session", _windowManager).ToJsonElement());

            var automation = _session.GetAutomation();
            automation.TypeText(element, text, clearFirst);

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("message", "Text typed")).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> SetValue(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var value = GetStringArg(args, "value") ?? "";
            var selectAllDelayMs = GetIntArg(args, "selectAllDelayMs", 50);

            var element = _session.GetElement(elementId);
            if (element == null)
                return Task.FromResult(ToolResponse.Fail("Element not found in session", _windowManager).ToJsonElement());

            var automation = _session.GetAutomation();
            automation.SetValue(element, value, selectAllDelayMs);

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("message", "Value set")).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
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
                return Task.FromResult(ToolResponse.Fail("Element not found in session", _windowManager).ToJsonElement());

            var automation = _session.GetAutomation();
            var value = automation.GetProperty(element, propertyName);

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("propertyName", propertyName),
                ("value", value?.ToString())).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> LaunchApp(JsonElement args)
    {
        try
        {
            var path = GetStringArg(args, "path") ?? throw new ArgumentException("path is required");
            var arguments = GetStringArg(args, "arguments");
            var workingDirectory = GetStringArg(args, "workingDirectory");
            var idleTimeoutMs = GetIntArg(args, "idleTimeoutMs", 5000);

            var automation = _session.GetAutomation();

            // Check if there's a previous instance of this app running and close it
            int? previousPid = null;
            bool previousClosed = false;
            var prevPid = _session.GetPreviousLaunchedPid(path);
            if (prevPid.HasValue)
            {
                previousPid = prevPid.Value;
                try
                {
                    var prevProcess = System.Diagnostics.Process.GetProcessById(prevPid.Value);
                    if (!prevProcess.HasExited)
                    {
                        prevProcess.CloseMainWindow();
                        if (!prevProcess.WaitForExit(2000))
                        {
                            prevProcess.Kill();
                            prevProcess.WaitForExit(1000);
                        }
                        previousClosed = true;
                    }
                }
                catch (ArgumentException)
                {
                    // Process already exited, that's fine
                }
                catch (Exception)
                {
                    // Ignore errors closing previous instance
                }
            }

            var process = automation.LaunchApp(path, arguments, workingDirectory, idleTimeoutMs);

            _session.CacheProcess(process.Id, process);
            _session.TrackLaunchedApp(path, process.Id);

            var props = new List<(string, object?)>
            {
                ("pid", process.Id),
                ("processName", process.ProcessName)
            };

            if (previousPid.HasValue)
            {
                props.Add(("previousPid", previousPid.Value));
                props.Add(("previousClosed", previousClosed));
            }

            return Task.FromResult(ToolResponse.Ok(_windowManager, props.ToArray()).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
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

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("pid", process.Id),
                ("processName", process.ProcessName)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> CloseApp(JsonElement args)
    {
        try
        {
            var pid = GetIntArg(args, "pid");
            var force = GetBoolArg(args, "force", false);
            var closeTimeoutMs = GetIntArg(args, "closeTimeoutMs", 5000);

            var automation = _session.GetAutomation();
            automation.CloseApp(pid, force, closeTimeoutMs);

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("message", "Application closed")).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> TakeScreenshot(JsonElement args)
    {
        try
        {
            var outputPath = GetStringArg(args, "outputPath") ?? throw new ArgumentException("outputPath is required");
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var elementId = GetStringArg(args, "elementId") ?? GetStringArg(args, "elementPath");

            var automation = _session.GetAutomation();

            // Priority: element > window > desktop
            if (!string.IsNullOrEmpty(elementId))
            {
                // Element screenshot
                var element = _session.GetElement(elementId!);
                if (element == null)
                    return Task.FromResult(ToolResponse.Fail($"Element '{elementId}' not found in session", _windowManager).ToJsonElement());

                automation.TakeScreenshot(outputPath, element);
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("message", $"Screenshot of element saved to {outputPath}")).ToJsonElement());
            }
            else if (!string.IsNullOrEmpty(windowHandle) || !string.IsNullOrEmpty(windowTitle))
            {
                // Window screenshot
                var window = _windowManager.FindWindow(windowHandle, windowTitle);
                if (window == null)
                {
                    // Check for multiple matches
                    if (!string.IsNullOrEmpty(windowTitle))
                    {
                        var matches = _windowManager.FindWindowsByTitle(windowTitle);
                        if (matches.Count > 1)
                            return Task.FromResult(ToolResponse.Fail($"Multiple windows match '{windowTitle}': {string.Join(", ", matches.ConvertAll(w => w.Title))}", _windowManager).ToJsonElement());
                    }
                    return Task.FromResult(ToolResponse.Fail($"Window not found: {windowHandle ?? windowTitle}", _windowManager).ToJsonElement());
                }

                // Focus the window to bring it to front (saves previous for restore)
                var previousForeground = _windowManager.GetCurrentForegroundHandle();
                _windowManager.FocusWindowByHandle(window.Handle);
                System.Threading.Thread.Sleep(100); // Brief delay for window to come to front

                // Get client area bounds (excludes title bar and borders)
                var clientBounds = _windowManager.GetClientAreaBounds(window.Handle);
                if (clientBounds == null)
                {
                    return Task.FromResult(ToolResponse.Fail($"Could not get client area bounds for window", _windowManager).ToJsonElement());
                }

                // Capture only the client area (no title bar)
                automation.TakeRegionScreenshot(outputPath,
                    clientBounds.X, clientBounds.Y,
                    clientBounds.Width, clientBounds.Height);

                // Restore previous foreground window
                if (!string.IsNullOrEmpty(previousForeground) && previousForeground != window.Handle)
                {
                    _windowManager.FocusWindowByHandle(previousForeground);
                }

                // Return with client area bounds - coordinates in screenshot match client coordinates directly
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("message", $"Screenshot of window '{window.Title}' (client area) saved to {outputPath}"),
                    ("window", new {
                        handle = window.Handle,
                        title = window.Title,
                        clientBounds = new {
                            x = clientBounds.X,
                            y = clientBounds.Y,
                            width = clientBounds.Width,
                            height = clientBounds.Height
                        },
                        note = "Screenshot shows client area only (no title bar). Coordinates in screenshot match client coordinates. Add clientBounds.x/y to get screen coordinates for clicking."
                    })).ToJsonElement());
            }
            else
            {
                // Full desktop screenshot
                automation.TakeScreenshot(outputPath, null);
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("message", $"Desktop screenshot saved to {outputPath}")).ToJsonElement());
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> ElementExists(JsonElement args)
    {
        try
        {
            var automationId = GetStringArg(args, "automationId") ?? throw new ArgumentException("automationId is required");

            var automation = _session.GetAutomation();
            var exists = automation.ElementExists(automationId);

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("exists", exists)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private async Task<JsonElement> WaitForElement(JsonElement args)
    {
        try
        {
            var automationId = GetStringArg(args, "automationId") ?? throw new ArgumentException("automationId is required");
            var timeoutMs = GetIntArg(args, "timeoutMs", 10000);
            var pollIntervalMs = GetIntArg(args, "pollIntervalMs", 100);

            var automation = _session.GetAutomation();
            var found = await automation.WaitForElementAsync(automationId, null, timeoutMs, pollIntervalMs);

            return ToolResponse.Ok(_windowManager,
                ("found", found)).ToJsonElement();
        }
        catch (Exception ex)
        {
            return ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement();
        }
    }

    private Task<JsonElement> DragDrop(JsonElement args)
    {
        try
        {
            var sourceElementId = GetStringArg(args, "sourceElementId") ?? throw new ArgumentException("sourceElementId is required");
            var targetElementId = GetStringArg(args, "targetElementId") ?? throw new ArgumentException("targetElementId is required");
            var dragSetupDelayMs = GetIntArg(args, "dragSetupDelayMs", 100);
            var dropDelayMs = GetIntArg(args, "dropDelayMs", 200);

            var sourceElement = _session.GetElement(sourceElementId!);
            var targetElement = _session.GetElement(targetElementId!);

            if (sourceElement == null || targetElement == null)
                return Task.FromResult(ToolResponse.Fail("Source or target element not found in session", _windowManager).ToJsonElement());

            var automation = _session.GetAutomation();
            automation.DragDrop(sourceElement, targetElement, dragSetupDelayMs, dropDelayMs);

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("message", "Drag and drop completed")).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> SendKeys(JsonElement args)
    {
        try
        {
            var keys = GetStringArg(args, "keys") ?? throw new ArgumentException("keys is required");
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");

            // If window targeting specified, focus that window first
            if (!string.IsNullOrEmpty(windowHandle) || !string.IsNullOrEmpty(windowTitle))
            {
                var window = _windowManager.FindWindow(windowHandle, windowTitle);
                if (window == null)
                {
                    if (!string.IsNullOrEmpty(windowTitle))
                    {
                        var matches = _windowManager.FindWindowsByTitle(windowTitle);
                        if (matches.Count > 1)
                        {
                            return Task.FromResult(ToolResponse.FailWithMultipleMatches(
                                $"Multiple windows match title '{windowTitle}'",
                                matches,
                                _windowManager).ToJsonElement());
                        }
                    }
                    return Task.FromResult(ToolResponse.Fail(
                        $"Window not found: {windowHandle ?? windowTitle}",
                        _windowManager).ToJsonElement());
                }

                if (_windowManager.IsWindowMinimized(windowHandle, windowTitle))
                {
                    return Task.FromResult(ToolResponse.Fail(
                        "Window is minimized. Cannot send keys to minimized window.",
                        _windowManager).ToJsonElement());
                }

                // Focus the window before sending keys
                _windowManager.FocusWindowByHandle(window.Handle);
                System.Threading.Thread.Sleep(50); // Brief delay for window to come to front
            }

            var automation = _session.GetAutomation();
            automation.SendKeys(keys);

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("message", $"Keys sent{(string.IsNullOrEmpty(windowTitle) ? "" : $" to '{windowTitle}'")}")).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> RaiseEvent(JsonElement args)
    {
        // Event raising is handled by FlaUI patterns in future enhancement
        return Task.FromResult(ToolResponse.Fail("Event raising not yet implemented", _windowManager).ToJsonElement());
    }

    private Task<JsonElement> ListenForEvent(JsonElement args)
    {
        // Event listening is handled by FlaUI event handlers in future enhancement
        return Task.FromResult(ToolResponse.Fail("Event listening not yet implemented", _windowManager).ToJsonElement());
    }

    // Touch/Pen Injection Tools
    private Task<JsonElement> TouchTap(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var x = GetIntArg(args, "x");
            var y = GetIntArg(args, "y");
            var holdMs = GetIntArg(args, "holdMs", 0);

            var (resolved, screenX, screenY, errorResponse) = ResolveWindowCoordinates(windowHandle, windowTitle, x, y);
            if (!resolved)
                return Task.FromResult(errorResponse!.Value);

            var success = InputInjection.TouchTap(screenX, screenY, holdMs);

            if (success)
                return Task.FromResult(ToolResponse.Ok(_windowManager, ("message", $"Touch tap at ({screenX}, {screenY})")).ToJsonElement());
            else
                return Task.FromResult(ToolResponse.Fail("Touch injection failed. Requires Windows 10 1809+ with Synthetic Pointer API support.", _windowManager).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> TouchDrag(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var x1 = GetIntArg(args, "x1");
            var y1 = GetIntArg(args, "y1");
            var x2 = GetIntArg(args, "x2");
            var y2 = GetIntArg(args, "y2");
            var steps = GetIntArg(args, "steps", 10);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var (resolved1, screenX1, screenY1, err1) = ResolveWindowCoordinates(windowHandle, windowTitle, x1, y1);
            if (!resolved1)
                return Task.FromResult(err1!.Value);

            var (resolved2, screenX2, screenY2, err2) = ResolveWindowCoordinates(windowHandle, windowTitle, x2, y2);
            if (!resolved2)
                return Task.FromResult(err2!.Value);

            var success = InputInjection.TouchDrag(screenX1, screenY1, screenX2, screenY2, steps, delayMs);

            if (success)
                return Task.FromResult(ToolResponse.Ok(_windowManager, ("message", $"Touch drag from ({screenX1}, {screenY1}) to ({screenX2}, {screenY2})")).ToJsonElement());
            else
                return Task.FromResult(ToolResponse.Fail("Touch injection failed. Requires Windows 10 1809+ with Synthetic Pointer API support.", _windowManager).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> PinchZoom(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var centerX = GetIntArg(args, "centerX");
            var centerY = GetIntArg(args, "centerY");
            var startDistance = GetIntArg(args, "startDistance");
            var endDistance = GetIntArg(args, "endDistance");
            var steps = GetIntArg(args, "steps", 20);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var (resolved, screenX, screenY, err) = ResolveWindowCoordinates(windowHandle, windowTitle, centerX, centerY);
            if (!resolved)
                return Task.FromResult(err!.Value);

            var success = InputInjection.PinchZoom(screenX, screenY, startDistance, endDistance, steps, delayMs);

            if (success)
            {
                var zoomType = endDistance > startDistance ? "zoom in" : "zoom out";
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("message", $"Pinch {zoomType} at ({screenX}, {screenY}) from {startDistance}px to {endDistance}px")).ToJsonElement());
            }
            else
                return Task.FromResult(ToolResponse.Fail("Touch injection failed. Requires Windows 10 1809+ with Synthetic Pointer API support.", _windowManager).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> RotateGesture(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var centerX = GetIntArg(args, "centerX");
            var centerY = GetIntArg(args, "centerY");
            var radius = GetIntArg(args, "radius", 50);
            var startAngle = GetDoubleArg(args, "startAngle", 0);
            var endAngle = GetDoubleArg(args, "endAngle", 90);
            var steps = GetIntArg(args, "steps", 20);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var (resolved, screenX, screenY, err) = ResolveWindowCoordinates(windowHandle, windowTitle, centerX, centerY);
            if (!resolved)
                return Task.FromResult(err!.Value);

            var success = InputInjection.Rotate(screenX, screenY, radius, startAngle, endAngle, steps, delayMs);

            if (success)
            {
                var direction = endAngle > startAngle ? "clockwise" : "counter-clockwise";
                var degrees = Math.Abs(endAngle - startAngle);
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("message", $"Rotated {degrees}° {direction} at ({screenX}, {screenY}) with {radius}px radius")).ToJsonElement());
            }
            else
                return Task.FromResult(ToolResponse.Fail("Touch injection failed. Requires Windows 10 1809+ with Synthetic Pointer API support.", _windowManager).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> MultiTouchGesture(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");

            // Parse fingers array - each finger is an array of waypoints [x, y, timeMs]
            if (!args.TryGetProperty("fingers", out var fingersElement) || fingersElement.ValueKind != JsonValueKind.Array)
                return Task.FromResult(ToolResponse.Fail("fingers is required and must be an array of finger paths", _windowManager).ToJsonElement());

            var fingersList = new List<(int x, int y, int timeMs)[]>();

            foreach (var fingerElement in fingersElement.EnumerateArray())
            {
                if (fingerElement.ValueKind != JsonValueKind.Array)
                    return Task.FromResult(ToolResponse.Fail("Each finger must be an array of waypoints", _windowManager).ToJsonElement());

                var waypoints = new List<(int x, int y, int timeMs)>();
                foreach (var waypointElement in fingerElement.EnumerateArray())
                {
                    if (waypointElement.ValueKind != JsonValueKind.Array)
                        return Task.FromResult(ToolResponse.Fail("Each waypoint must be an array [x, y, timeMs]", _windowManager).ToJsonElement());

                    var wpArray = waypointElement.EnumerateArray().ToArray();
                    if (wpArray.Length < 3)
                        return Task.FromResult(ToolResponse.Fail("Each waypoint must have at least 3 elements [x, y, timeMs]", _windowManager).ToJsonElement());

                    int wpX = wpArray[0].GetInt32();
                    int wpY = wpArray[1].GetInt32();
                    int wpTime = wpArray[2].GetInt32();

                    // Convert to screen coordinates if window targeting is specified
                    if (!string.IsNullOrEmpty(windowHandle) || !string.IsNullOrEmpty(windowTitle))
                    {
                        var (resolved, screenX, screenY, err) = ResolveWindowCoordinates(windowHandle, windowTitle, wpX, wpY);
                        if (!resolved)
                            return Task.FromResult(err!.Value);
                        wpX = screenX;
                        wpY = screenY;
                    }

                    waypoints.Add((wpX, wpY, wpTime));
                }

                if (waypoints.Count < 2)
                    return Task.FromResult(ToolResponse.Fail("Each finger must have at least 2 waypoints", _windowManager).ToJsonElement());

                fingersList.Add(waypoints.ToArray());
            }

            if (fingersList.Count == 0)
                return Task.FromResult(ToolResponse.Fail("At least one finger path is required", _windowManager).ToJsonElement());

            var (success, fingersProcessed, totalSteps) = InputInjection.MultiTouchGesture(fingersList.ToArray());

            if (success)
            {
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("message", $"Multi-touch gesture completed with {fingersProcessed} fingers over {totalSteps} steps"),
                    ("fingersProcessed", fingersProcessed),
                    ("totalSteps", totalSteps)).ToJsonElement());
            }
            else
                return Task.FromResult(ToolResponse.Fail("Touch injection failed. Requires Windows 10 1809+ with Synthetic Pointer API support.", _windowManager).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> PenStroke(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var x1 = GetIntArg(args, "x1");
            var y1 = GetIntArg(args, "y1");
            var x2 = GetIntArg(args, "x2");
            var y2 = GetIntArg(args, "y2");
            var steps = GetIntArg(args, "steps", 20);
            var pressure = (uint)GetIntArg(args, "pressure", 512);
            var eraser = GetBoolArg(args, "eraser", false);
            var delayMs = GetIntArg(args, "delayMs", 0);

            // Use the handle-returning version to get hwnd for pen targeting
            var (resolved1, screenX1, screenY1, hwnd, err1) = ResolveWindowCoordinatesWithHandle(windowHandle, windowTitle, x1, y1);
            if (!resolved1)
                return Task.FromResult(err1!.Value);

            var (resolved2, screenX2, screenY2, _, err2) = ResolveWindowCoordinatesWithHandle(windowHandle, windowTitle, x2, y2, focusWindow: false);
            if (!resolved2)
                return Task.FromResult(err2!.Value);

            var success = InputInjection.PenStroke(screenX1, screenY1, screenX2, screenY2, steps, pressure, eraser, delayMs, hwnd);

            if (success)
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("message", $"Pen stroke from ({screenX1}, {screenY1}) to ({screenX2}, {screenY2}) with pressure {pressure}")).ToJsonElement());
            else
                return Task.FromResult(ToolResponse.Fail("Pen injection failed. Requires Windows 8+ and pen injection permissions.", _windowManager).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> PenTap(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var x = GetIntArg(args, "x");
            var y = GetIntArg(args, "y");
            var pressure = (uint)GetIntArg(args, "pressure", 512);
            var holdMs = GetIntArg(args, "holdMs", 0);

            // Use the handle-returning version to get hwnd for pen targeting
            var (resolved, screenX, screenY, hwnd, errorResponse) = ResolveWindowCoordinatesWithHandle(windowHandle, windowTitle, x, y);
            if (!resolved)
                return Task.FromResult(errorResponse!.Value);

            var success = InputInjection.PenTap(screenX, screenY, pressure, holdMs, hwnd);

            if (success)
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("message", $"Pen tap at ({screenX}, {screenY}) with pressure {pressure}")).ToJsonElement());
            else
                return Task.FromResult(ToolResponse.Fail("Pen injection failed. Requires Windows 8+ and pen injection permissions.", _windowManager).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    // Mouse Input Tools
    private Task<JsonElement> MouseDrag(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var x1 = GetIntArg(args, "x1");
            var y1 = GetIntArg(args, "y1");
            var x2 = GetIntArg(args, "x2");
            var y2 = GetIntArg(args, "y2");
            var steps = GetIntArg(args, "steps", 10);
            var delayMs = GetIntArg(args, "delayMs", 0);

            // Resolve first point (this will focus the window)
            var (resolved1, screenX1, screenY1, err1) = ResolveWindowCoordinates(windowHandle, windowTitle, x1, y1);
            if (!resolved1)
                return Task.FromResult(err1!.Value);

            // Resolve second point (skip focus since already done)
            var (resolved2, screenX2, screenY2, err2) = ResolveWindowCoordinates(windowHandle, windowTitle, x2, y2, focusWindow: false);
            if (!resolved2)
                return Task.FromResult(err2!.Value);

            var success = InputInjection.MouseDrag(screenX1, screenY1, screenX2, screenY2, steps, delayMs);

            if (success)
            {
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("message", $"Mouse drag from ({screenX1}, {screenY1}) to ({screenX2}, {screenY2})")).ToJsonElement());
            }
            else
            {
                return Task.FromResult(ToolResponse.Fail("Mouse drag failed.", _windowManager).ToJsonElement());
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> MouseDragPath(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");

            // Parse points array
            if (!args.TryGetProperty("points", out var pointsElement) || pointsElement.ValueKind != JsonValueKind.Array)
            {
                return Task.FromResult(ToolResponse.Fail("points array is required", _windowManager).ToJsonElement());
            }

            var pointsList = new List<(int x, int y)>();
            int index = 0;
            foreach (var point in pointsElement.EnumerateArray())
            {
                if (!point.TryGetProperty("x", out var xProp) || !point.TryGetProperty("y", out var yProp))
                {
                    return Task.FromResult(ToolResponse.Fail($"Point at index {index} missing required x or y coordinate", _windowManager).ToJsonElement());
                }

                var x = xProp.GetInt32();
                var y = yProp.GetInt32();

                if (x < 0 || y < 0)
                {
                    return Task.FromResult(ToolResponse.Fail($"Point at index {index} has invalid coordinate (must be >= 0)", _windowManager).ToJsonElement());
                }

                pointsList.Add((x, y));
                index++;
            }

            if (pointsList.Count < 2)
            {
                return Task.FromResult(ToolResponse.Fail("Path requires at least 2 points", _windowManager).ToJsonElement());
            }

            if (pointsList.Count > 1000)
            {
                return Task.FromResult(ToolResponse.Fail("Path exceeds maximum of 1000 waypoints", _windowManager).ToJsonElement());
            }

            // If window targeting is specified, translate all points to screen coordinates
            if (!string.IsNullOrEmpty(windowHandle) || !string.IsNullOrEmpty(windowTitle))
            {
                // Use ResolveWindowCoordinates for first point to handle validation and focus
                var (firstPt, firstY) = pointsList[0];
                var (resolved, _, _, err) = ResolveWindowCoordinates(windowHandle, windowTitle, firstPt, firstY);
                if (!resolved)
                    return Task.FromResult(err!.Value);

                // Now get window for translating remaining points (already focused)
                var window = _windowManager.FindWindow(windowHandle, windowTitle);
                if (window == null)
                {
                    return Task.FromResult(ToolResponse.Fail(
                        $"Window not found after focus: {windowHandle ?? windowTitle}",
                        _windowManager).ToJsonElement());
                }

                // Translate all points using client coordinates
                var translatedPoints = new List<(int x, int y)>();
                foreach (var (x, y) in pointsList)
                {
                    var translated = _windowManager.TranslateClientToScreen(window.Handle, x, y);
                    if (translated == null)
                    {
                        return Task.FromResult(ToolResponse.Fail(
                            "Could not translate coordinates to screen coordinates.",
                            _windowManager).ToJsonElement());
                    }
                    translatedPoints.Add((translated.Value.screenX, translated.Value.screenY));
                }
                pointsList = translatedPoints;
            }

            var stepsPerSegment = GetIntArg(args, "stepsPerSegment", 1);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var (success, pointsProcessed, totalSteps) = InputInjection.MouseDragPath(
                pointsList.ToArray(),
                stepsPerSegment,
                delayMs);

            if (success)
            {
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("message", $"Completed drag path through {pointsProcessed} waypoints"),
                    ("pointsProcessed", pointsProcessed),
                    ("totalSteps", totalSteps)).ToJsonElement());
            }
            else
            {
                return Task.FromResult(ToolResponse.Fail("Mouse drag path failed", _windowManager).ToJsonElement());
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> MouseClick(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var x = GetIntArg(args, "x");
            var y = GetIntArg(args, "y");
            var doubleClick = GetBoolArg(args, "doubleClick", false);
            var delayMs = GetIntArg(args, "delayMs", 0);

            int screenX = x, screenY = y;

            // If window targeting is specified, translate to screen coordinates
            if (!string.IsNullOrEmpty(windowHandle) || !string.IsNullOrEmpty(windowTitle))
            {
                var window = _windowManager.FindWindow(windowHandle, windowTitle);
                if (window == null)
                {
                    // Check for multiple matches
                    if (!string.IsNullOrEmpty(windowTitle))
                    {
                        var matches = _windowManager.FindWindowsByTitle(windowTitle);
                        if (matches.Count > 1)
                        {
                            return Task.FromResult(ToolResponse.FailWithMultipleMatches(
                                $"Multiple windows match title '{windowTitle}'",
                                matches,
                                _windowManager).ToJsonElement());
                        }
                    }
                    return Task.FromResult(ToolResponse.Fail(
                        $"Window not found: {windowHandle ?? windowTitle}",
                        _windowManager).ToJsonElement());
                }

                // Check if minimized
                if (_windowManager.IsWindowMinimized(windowHandle, windowTitle))
                {
                    return Task.FromResult(ToolResponse.Fail(
                        "Window is minimized. Use focus_window first.",
                        _windowManager).ToJsonElement());
                }

                // Translate client coordinates to screen coordinates
                var translated = _windowManager.TranslateClientToScreen(window.Handle, x, y);
                if (translated == null)
                {
                    return Task.FromResult(ToolResponse.Fail(
                        "Could not translate coordinates to screen coordinates.",
                        _windowManager).ToJsonElement());
                }
                (screenX, screenY) = translated.Value;
            }

            var success = InputInjection.MouseClick(screenX, screenY, doubleClick, delayMs);

            if (success)
            {
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("message", $"{(doubleClick ? "Double-click" : "Click")} at ({screenX}, {screenY})"),
                    ("clientX", x),
                    ("clientY", y),
                    ("screenX", screenX),
                    ("screenY", screenY)).ToJsonElement());
            }
            else
            {
                return Task.FromResult(ToolResponse.Fail("Mouse click failed.", _windowManager).ToJsonElement());
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
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
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("x", x), ("y", y), ("width", width), ("height", height), ("windowTitle", windowTitle)).ToJsonElement());
            }
            else
                return Task.FromResult(ToolResponse.Fail($"Window not found: {windowTitle}", _windowManager).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> FocusWindow(JsonElement args)
    {
        try
        {
            var windowTitle = GetStringArg(args, "windowTitle") ?? throw new ArgumentException("windowTitle is required");

            var success = InputInjection.FocusWindow(windowTitle);

            if (success)
                return Task.FromResult(ToolResponse.Ok(_windowManager, ("message", $"Focused window: {windowTitle}")).ToJsonElement());
            else
                return Task.FromResult(ToolResponse.Fail($"Could not focus window: {windowTitle}", _windowManager).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
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
                    return Task.FromResult(ToolResponse.Fail($"Window not found: {windowTitle}", _windowManager).ToJsonElement());
            }

            var element = automation.FindByAutomationId(automationId, parent);
            if (element == null)
                return Task.FromResult(ToolResponse.Fail($"Element not found: {automationId}", _windowManager).ToJsonElement());

            automation.Click(element, doubleClick);

            return Task.FromResult(ToolResponse.Ok(_windowManager, ("message", $"Clicked element: {automationId}")).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
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
                return Task.FromResult(ToolResponse.Fail($"Window not found: {windowTitle}", _windowManager).ToJsonElement());

            var elements = automation.GetElementTree(window, maxDepth);

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("elementCount", elements.Count), ("elements", elements)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
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
                    return Task.FromResult(ToolResponse.Fail($"Window not found: {windowTitle}", _windowManager).ToJsonElement());
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

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("xml", result.Xml),
                ("tokenCount", result.TokenCount),
                ("elementCount", result.ElementCount),
                ("dpiScaleFactor", result.DpiScaleFactor),
                ("timestamp", result.Timestamp),
                ("truncated", result.Truncated)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
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
            var uiUpdateDelayMs = GetIntArg(args, "uiUpdateDelayMs", 100);

            var automation = _session.GetAutomation();
            AutomationElement? element = null;

            // Get element by ID or find by automationId
            if (!string.IsNullOrEmpty(elementId))
            {
                element = _session.GetElement(elementId);
                if (element == null)
                    return Task.FromResult(ToolResponse.Fail($"Element not found in cache: {elementId}", _windowManager).ToJsonElement());
            }
            else if (!string.IsNullOrEmpty(automationId))
            {
                if (string.IsNullOrEmpty(windowTitle))
                    return Task.FromResult(ToolResponse.Fail("windowTitle is required when using automationId", _windowManager).ToJsonElement());

                var window = automation.GetWindowByTitle(windowTitle);
                if (window == null)
                    return Task.FromResult(ToolResponse.Fail($"Window not found: {windowTitle}", _windowManager).ToJsonElement());

                element = automation.FindByAutomationId(automationId, window);
                if (element == null)
                    return Task.FromResult(ToolResponse.Fail($"Element not found by AutomationId: {automationId}", _windowManager).ToJsonElement());
            }
            else
            {
                return Task.FromResult(ToolResponse.Fail("Either elementId or automationId is required", _windowManager).ToJsonElement());
            }

            var result = automation.ExpandCollapse(element, expand, uiUpdateDelayMs);

            if (result.Success)
            {
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("previousState", result.PreviousState),
                    ("currentState", result.CurrentState)).ToJsonElement());
            }
            else
            {
                return Task.FromResult(ToolResponse.Fail(result.ErrorMessage ?? "Unknown error", _windowManager).ToJsonElement());
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
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
            var uiUpdateDelayMs = GetIntArg(args, "uiUpdateDelayMs", 100);

            // Parse direction
            if (!Enum.TryParse<ScrollDirection>(directionStr, true, out var direction))
                return Task.FromResult(ToolResponse.Fail($"Invalid direction: {directionStr}. Valid values: Up, Down, Left, Right", _windowManager).ToJsonElement());

            // Parse amount
            if (!Enum.TryParse<ScrollAmount>(amountStr, true, out var amount))
                return Task.FromResult(ToolResponse.Fail($"Invalid amount: {amountStr}. Valid values: SmallDecrement, LargeDecrement", _windowManager).ToJsonElement());

            var automation = _session.GetAutomation();
            AutomationElement? element = null;

            // Get element by ID or find by automationId
            if (!string.IsNullOrEmpty(elementId))
            {
                element = _session.GetElement(elementId);
                if (element == null)
                    return Task.FromResult(ToolResponse.Fail($"Element not found in cache: {elementId}", _windowManager).ToJsonElement());
            }
            else if (!string.IsNullOrEmpty(automationId))
            {
                if (string.IsNullOrEmpty(windowTitle))
                    return Task.FromResult(ToolResponse.Fail("windowTitle is required when using automationId", _windowManager).ToJsonElement());

                var window = automation.GetWindowByTitle(windowTitle);
                if (window == null)
                    return Task.FromResult(ToolResponse.Fail($"Window not found: {windowTitle}", _windowManager).ToJsonElement());

                element = automation.FindByAutomationId(automationId, window);
                if (element == null)
                    return Task.FromResult(ToolResponse.Fail($"Element not found by AutomationId: {automationId}", _windowManager).ToJsonElement());
            }
            else
            {
                return Task.FromResult(ToolResponse.Fail("Either elementId or automationId is required", _windowManager).ToJsonElement());
            }

            var result = automation.Scroll(element, direction, amount, uiUpdateDelayMs);

            if (result.Success)
            {
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("horizontalScrollPercent", result.HorizontalScrollPercent),
                    ("verticalScrollPercent", result.VerticalScrollPercent),
                    ("horizontalChanged", result.HorizontalChanged),
                    ("verticalChanged", result.VerticalChanged)).ToJsonElement());
            }
            else
            {
                return Task.FromResult(ToolResponse.Fail(result.ErrorMessage ?? "Unknown error", _windowManager).ToJsonElement());
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
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
                    return Task.FromResult(ToolResponse.Fail($"Element not found in cache: {elementId}", _windowManager).ToJsonElement());
            }
            else if (!string.IsNullOrEmpty(automationId))
            {
                if (string.IsNullOrEmpty(windowTitle))
                    return Task.FromResult(ToolResponse.Fail("windowTitle is required when using automationId", _windowManager).ToJsonElement());

                var window = automation.GetWindowByTitle(windowTitle);
                if (window == null)
                    return Task.FromResult(ToolResponse.Fail($"Window not found: {windowTitle}", _windowManager).ToJsonElement());

                element = automation.FindByAutomationId(automationId, window);
                if (element == null)
                    return Task.FromResult(ToolResponse.Fail($"Element not found by AutomationId: {automationId}", _windowManager).ToJsonElement());
            }
            else
            {
                return Task.FromResult(ToolResponse.Fail("Either elementId or automationId is required", _windowManager).ToJsonElement());
            }

            var result = automation.GetElementState(element);

            if (result.Success)
            {
                // Build properties list for ToolResponse
                var props = new List<(string, object?)>
                {
                    ("automationId", result.AutomationId ?? ""),
                    ("name", result.Name ?? ""),
                    ("className", result.ClassName ?? ""),
                    ("controlType", result.ControlType ?? ""),
                    ("isEnabled", result.IsEnabled),
                    ("isOffscreen", result.IsOffscreen),
                    ("isKeyboardFocusable", result.IsKeyboardFocusable),
                    ("hasKeyboardFocus", result.HasKeyboardFocus),
                    ("dpiScaleFactor", result.DpiScaleFactor)
                };

                if (result.BoundingRect != null)
                {
                    props.Add(("boundingRect", new { x = result.BoundingRect.X, y = result.BoundingRect.Y, width = result.BoundingRect.Width, height = result.BoundingRect.Height }));
                }

                if (result.Value != null) props.Add(("value", result.Value));
                if (result.IsReadOnly.HasValue) props.Add(("isReadOnly", result.IsReadOnly.Value));
                if (result.ToggleState != null) props.Add(("toggleState", result.ToggleState));
                if (result.IsSelected.HasValue) props.Add(("isSelected", result.IsSelected.Value));
                if (result.RangeValue.HasValue)
                {
                    props.Add(("rangeValue", result.RangeValue.Value));
                    if (result.RangeMinimum.HasValue) props.Add(("rangeMinimum", result.RangeMinimum.Value));
                    if (result.RangeMaximum.HasValue) props.Add(("rangeMaximum", result.RangeMaximum.Value));
                }

                return Task.FromResult(ToolResponse.Ok(_windowManager, props.ToArray()).ToJsonElement());
            }
            else
            {
                return Task.FromResult(ToolResponse.Fail(result.ErrorMessage ?? "Unknown error", _windowManager).ToJsonElement());
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
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
                    return Task.FromResult(ToolResponse.Fail($"Window not found: {windowTitle}", _windowManager).ToJsonElement());
            }
            else
            {
                root = automation.GetDesktop();
            }

            // Capture snapshot
            var snapshot = detector.CaptureSnapshot(root);
            _session.CacheSnapshot(snapshotId, snapshot);

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("snapshotId", snapshotId),
                ("hash", snapshot.Hash),
                ("elementCount", snapshot.Elements.Count),
                ("capturedAt", snapshot.CapturedAt.ToString("o"))).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
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
                return Task.FromResult(ToolResponse.Fail($"Snapshot not found: {beforeSnapshotId}", _windowManager).ToJsonElement());

            // Get or capture after snapshot
            TreeSnapshot afterSnapshot;
            if (!string.IsNullOrEmpty(afterSnapshotId))
            {
                var cached = _session.GetSnapshot(afterSnapshotId);
                if (cached == null)
                    return Task.FromResult(ToolResponse.Fail($"Snapshot not found: {afterSnapshotId}", _windowManager).ToJsonElement());
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
                        return Task.FromResult(ToolResponse.Fail($"Window not found: {windowTitle}", _windowManager).ToJsonElement());
                }
                else
                {
                    root = automation.GetDesktop();
                }
                afterSnapshot = detector.CaptureSnapshot(root);
            }

            // Compare snapshots
            var result = detector.CompareSnapshots(beforeSnapshot, afterSnapshot);

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("stateChanged", result.StateChanged),
                ("addedCount", result.AddedCount),
                ("removedCount", result.RemovedCount),
                ("modifiedCount", result.ModifiedCount),
                ("diffSummary", result.DiffSummary)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
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
                return ToolResponse.Fail("Sandbox is already running. Call close_sandbox first.", _windowManager).ToJsonElement();
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
                return ToolResponse.Ok(_windowManager,
                    ("message", "Sandbox launched and MCP server ready"),
                    ("processId", result.ProcessId),
                    ("sharedFolderPath", result.SharedFolderPath ?? "")).ToJsonElement();
            }
            else
            {
                return ToolResponse.Fail(result.Error ?? "Unknown error", _windowManager,
                    ("sandboxAvailable", result.SandboxAvailable)).ToJsonElement();
            }
        }
        catch (ArgumentException ex)
        {
            // Security validation errors from WsbConfigBuilder
            return ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement();
        }
        catch (Exception ex)
        {
            return ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement();
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
                return ToolResponse.Ok(_windowManager, ("message", "No sandbox was running")).ToJsonElement();
            }

            await sandboxManager.CloseSandboxAsync(timeoutMs);

            return ToolResponse.Ok(_windowManager, ("message", "Sandbox closed successfully")).ToJsonElement();
        }
        catch (Exception ex)
        {
            return ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement();
        }
    }

    private Task<JsonElement> GetCapabilities(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Check if Windows Sandbox is available
            bool sandboxAvailable = false;
            try
            {
                // Check for Windows Sandbox feature via registry
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\Packages");
                if (key != null)
                {
                    var packageNames = key.GetSubKeyNames();
                    sandboxAvailable = packageNames.Any(p =>
                        p.Contains("Containers-DisposableClientVM", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch
            {
                // If we can't check registry, try running the sandbox manager's check
                try
                {
                    var sandboxManager = _session.GetSandboxManager();
                    sandboxAvailable = sandboxManager.IsSandboxAvailable();
                }
                catch { /* sandbox not available */ }
            }

            // Get OS version
            var osVersion = Environment.OSVersion.ToString();
            var osVersionFriendly = $"Windows {Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor} Build {Environment.OSVersion.Version.Build}";

            // Get FlaUI version from assembly
            var flauiVersion = typeof(FlaUI.Core.AutomationBase).Assembly.GetName().Version?.ToString() ?? "Unknown";

            // List all available features (tools)
            var features = _tools.Keys.ToArray();

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("sandbox_available", sandboxAvailable),
                ("os_version", osVersionFriendly),
                ("os_full", osVersion),
                ("flaui_version", flauiVersion),
                ("uia_backend", "UIA2"),
                ("max_depth_supported", 10),
                ("token_budget", 5000),
                ("features", features),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> GetDpiInfo(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var windowTitle = GetStringArg(args, "windowTitle");

            IntPtr windowHandle = IntPtr.Zero;

            // If window title provided, get handle for that window
            if (!string.IsNullOrEmpty(windowTitle))
            {
                var automation = _session.GetAutomation();
                var window = automation.GetWindowByTitle(windowTitle);
                if (window != null)
                {
                    try
                    {
                        windowHandle = window.Properties.NativeWindowHandle.ValueOrDefault;
                    }
                    catch
                    {
                        // Fall back to no window handle if property not available
                    }
                }
            }

            var dpiInfo = DpiHelper.GetDpiInfo(windowHandle != IntPtr.Zero ? windowHandle : null);

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("system_dpi", dpiInfo.SystemDpi),
                ("system_scale_factor", dpiInfo.SystemScaleFactor),
                ("window_dpi", dpiInfo.WindowDpi),
                ("window_scale_factor", dpiInfo.WindowScaleFactor),
                ("is_per_monitor_aware", dpiInfo.IsPerMonitorAware),
                ("standard_dpi", dpiInfo.StandardDpi),
                ("window_specified", !string.IsNullOrEmpty(windowTitle)),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public System.Drawing.Point ptMinPosition;
        public System.Drawing.Point ptMaxPosition;
        public System.Drawing.Rectangle rcNormalPosition;
    }

    private const int SW_NORMAL = 1;
    private const int SW_MINIMIZED = 2;
    private const int SW_MAXIMIZED = 3;

    private Task<JsonElement> GetProcessInfo(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            IntPtr hwnd = IntPtr.Zero;
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");

            if (!string.IsNullOrEmpty(windowHandle))
            {
                // Parse hex or decimal HWND
                if (windowHandle.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    hwnd = new IntPtr(Convert.ToInt64(windowHandle, 16));
                }
                else
                {
                    hwnd = new IntPtr(long.Parse(windowHandle));
                }
            }
            else if (!string.IsNullOrEmpty(windowTitle))
            {
                // Find window by title using InputInjection helper
                var bounds = InputInjection.GetWindowBounds(windowTitle);
                if (bounds == null)
                {
                    stopwatch.Stop();
                    return Task.FromResult(ToolResponse.Fail($"Window not found: {windowTitle}", _windowManager).ToJsonElement());
                }

                // We need to find the HWND - use FindWindow
                hwnd = FindWindowByTitle(windowTitle);
                if (hwnd == IntPtr.Zero)
                {
                    stopwatch.Stop();
                    return Task.FromResult(ToolResponse.Fail($"Could not get window handle for: {windowTitle}", _windowManager).ToJsonElement());
                }
            }
            else
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Fail("Either windowHandle or windowTitle is required", _windowManager).ToJsonElement());
            }

            // Validate window handle
            if (!IsWindow(hwnd))
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Fail("Invalid window handle", _windowManager).ToJsonElement());
            }

            // Get process ID
            GetWindowThreadProcessId(hwnd, out uint pid);

            // Get process info
            string processName = "Unknown";
            bool isResponding = false;
            try
            {
                var process = System.Diagnostics.Process.GetProcessById((int)pid);
                processName = process.ProcessName + ".exe";
                isResponding = process.Responding;
            }
            catch { /* Process may have exited */ }

            // Get window state
            var placement = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
            GetWindowPlacement(hwnd, ref placement);

            string windowState = placement.showCmd switch
            {
                SW_MINIMIZED => "minimized",
                SW_MAXIMIZED => "maximized",
                _ => "normal"
            };

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("pid", (int)pid),
                ("process_name", processName),
                ("is_responding", isResponding),
                ("window_state", windowState),
                ("main_window_handle", $"0x{hwnd.ToInt64():X8}"),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsDelegate lpEnumFunc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    private delegate bool EnumWindowsDelegate(IntPtr hwnd, IntPtr lParam);

    private static IntPtr FindWindowByTitle(string partialTitle)
    {
        IntPtr foundHwnd = IntPtr.Zero;

        EnumWindows((hwnd, lParam) =>
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (!string.IsNullOrEmpty(title) && title.Contains(partialTitle, StringComparison.OrdinalIgnoreCase))
            {
                foundHwnd = hwnd;
                return false; // Stop enumeration
            }
            return true; // Continue enumeration
        }, IntPtr.Zero);

        return foundHwnd;
    }

    private Task<JsonElement> SubscribeToEvents(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Parse event types array
            var eventTypes = new List<string>();
            if (args.TryGetProperty("event_types", out var typesElement) && typesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in typesElement.EnumerateArray())
                {
                    var eventType = item.GetString();
                    if (!string.IsNullOrEmpty(eventType))
                    {
                        eventTypes.Add(eventType);
                    }
                }
            }

            if (eventTypes.Count == 0)
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Fail("No event types specified", _windowManager,
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }

            // Validate event types
            var validTypes = new HashSet<string> { "window_opened", "dialog_shown", "structure_changed", "property_changed" };
            var invalidTypes = eventTypes.Where(t => !validTypes.Contains(t.ToLowerInvariant())).ToList();
            if (invalidTypes.Count > 0)
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Fail($"Invalid event types: {string.Join(", ", invalidTypes)}", _windowManager,
                    ("valid_types", new[] { "window_opened", "dialog_shown", "structure_changed", "property_changed" }),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }

            // Subscribe
            _session.SubscribeToEvents(eventTypes);

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("subscribed_to", eventTypes),
                ("queue_max_size", 10),
                ("message", "Events will be queued. Use get_pending_events to retrieve them."),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> GetPendingEvents(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var (events, droppedCount) = _session.DrainEventQueue();

            stopwatch.Stop();

            var eventList = events.Select(e => new
            {
                type = e.Type,
                timestamp = e.Timestamp.ToString("o"),
                window_title = e.WindowTitle,
                process_id = e.ProcessId,
                details = e.Details
            }).ToArray();

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("events", eventList),
                ("events_count", events.Count),
                ("events_dropped", droppedCount),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> ConfirmAction(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var action = GetStringArg(args, "action") ?? throw new ArgumentException("action is required");
            var description = GetStringArg(args, "description") ?? throw new ArgumentException("description is required");
            var target = GetStringArg(args, "target");

            JsonElement? parameters = null;
            if (args.TryGetProperty("parameters", out var paramsElement) && paramsElement.ValueKind != JsonValueKind.Null)
            {
                parameters = paramsElement;
            }

            // Validate action type
            var validActions = new HashSet<string> { "close_app", "force_close", "send_keys_dangerous", "custom" };
            if (!validActions.Contains(action.ToLowerInvariant()))
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Fail(
                    $"Invalid action type: {action}",
                    _windowManager,
                    ("valid_actions", new[] { "close_app", "force_close", "send_keys_dangerous", "custom" }),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)
                ).ToJsonElement());
            }

            var confirmation = _session.CreateConfirmation(action, description, target, parameters);

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("status", "pending_confirmation"),
                ("confirmation_token", confirmation.Token),
                ("action", confirmation.Action),
                ("description", confirmation.Description),
                ("target", confirmation.Target),
                ("expires_at", confirmation.ExpiresAt.ToString("o")),
                ("expires_in_seconds", 60),
                ("message", "Call execute_confirmed_action with this token to proceed"),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)
            ).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private async Task<JsonElement> ExecuteConfirmedAction(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var token = GetStringArg(args, "confirmationToken") ?? throw new ArgumentException("confirmationToken is required");

            var confirmation = _session.ConsumeConfirmation(token);
            if (confirmation == null)
            {
                stopwatch.Stop();
                return ToolResponse.Fail("Invalid or expired confirmation token", _windowManager,
                    ("error_code", "INVALID_TOKEN"),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement();
            }

            // Execute the confirmed action
            JsonElement actionResult;
            switch (confirmation.Action.ToLowerInvariant())
            {
                case "close_app":
                    actionResult = await CloseApp(confirmation.Parameters ?? JsonDocument.Parse("{}").RootElement);
                    break;

                case "force_close":
                    // Extract PID from parameters and force kill
                    if (confirmation.Parameters?.TryGetProperty("pid", out var pidElement) == true)
                    {
                        try
                        {
                            var pid = pidElement.GetInt32();
                            var process = System.Diagnostics.Process.GetProcessById(pid);
                            process.Kill(entireProcessTree: true);
                            actionResult = ToolResponse.Ok(_windowManager, ("message", $"Process {pid} force killed")).ToJsonElement();
                        }
                        catch (Exception ex)
                        {
                            actionResult = ToolResponse.Fail($"Force kill failed: {ex.Message}", _windowManager).ToJsonElement();
                        }
                    }
                    else
                    {
                        actionResult = ToolResponse.Fail("Missing pid parameter for force_close", _windowManager).ToJsonElement();
                    }
                    break;

                case "send_keys_dangerous":
                    actionResult = await SendKeys(confirmation.Parameters ?? JsonDocument.Parse("{}").RootElement);
                    break;

                case "custom":
                    // Custom actions just return success - caller handles the action
                    // Parse the parameters to include them properly
                    object? actionParams = null;
                    if (confirmation.Parameters.HasValue)
                    {
                        actionParams = JsonSerializer.Deserialize<object>(confirmation.Parameters.Value.GetRawText());
                    }
                    actionResult = ToolResponse.Ok(_windowManager,
                        ("message", "Custom action confirmed"),
                        ("action_parameters", actionParams)
                    ).ToJsonElement();
                    break;

                default:
                    actionResult = ToolResponse.Fail($"Unknown action type: {confirmation.Action}", _windowManager).ToJsonElement();
                    break;
            }

            stopwatch.Stop();

            // Merge action result with execution metadata
            var resultDict = new Dictionary<string, object?>
            {
                ["confirmed_action"] = confirmation.Action,
                ["confirmed_target"] = confirmation.Target,
                ["confirmation_used"] = true,
                ["execution_time_ms"] = stopwatch.ElapsedMilliseconds
            };

            // Add all properties from actionResult (which already has windows)
            foreach (var prop in actionResult.EnumerateObject())
            {
                resultDict[prop.Name] = prop.Value.Clone();
            }

            // Ensure windows are included in case actionResult doesn't have them
            if (!resultDict.ContainsKey("windows"))
            {
                resultDict["windows"] = _windowManager.GetAllWindows();
            }

            return JsonDocument.Parse(JsonSerializer.Serialize(resultDict)).RootElement;
        }
        catch (Exception ex)
        {
            return ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement();
        }
    }

    private Task<JsonElement> MarkForExpansion(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var elementKey = GetStringArg(args, "elementKey");
            var elementId = GetStringArg(args, "elementId");

            // Resolve element key from cached element if provided
            if (!string.IsNullOrEmpty(elementId))
            {
                var element = _session.GetElement(elementId);
                if (element != null)
                {
                    // Use AutomationId if available, otherwise Name
                    elementKey = element.AutomationId;
                    if (string.IsNullOrEmpty(elementKey))
                        elementKey = element.Name;
                }
            }

            if (string.IsNullOrEmpty(elementKey))
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Fail("Either elementKey or valid elementId required", _windowManager,
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }

            _session.MarkForExpansion(elementKey);

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("element_key", elementKey),
                ("total_marked", _session.GetExpandedElements().Count),
                ("message", "Element marked for expansion. Next get_ui_tree call will expand its children regardless of depth limit."),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> ClearExpansionMarks(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var elementKey = GetStringArg(args, "elementKey");
            var clearedCount = 0;

            if (string.IsNullOrEmpty(elementKey))
            {
                // Clear all
                clearedCount = _session.GetExpandedElements().Count;
                _session.ClearAllExpansionMarks();
            }
            else
            {
                // Clear specific element
                if (_session.IsMarkedForExpansion(elementKey))
                {
                    _session.ClearExpansionMark(elementKey);
                    clearedCount = 1;
                }
            }

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("cleared_count", clearedCount),
                ("remaining_marked", _session.GetExpandedElements().Count),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> RelocateElement(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var automation = _session.GetAutomation();

            var elementId = GetStringArg(args, "elementId");
            var automationId = GetStringArg(args, "automationId");
            var name = GetStringArg(args, "name");
            var className = GetStringArg(args, "className");
            var controlType = GetStringArg(args, "controlType");

            // Build search criteria
            var criteria = new ElementSearchCriteria
            {
                AutomationId = automationId,
                Name = name,
                ClassName = className,
                ControlType = controlType,
                OriginalElementId = elementId
            };

            // If we have an element ID, try to extract criteria from cached element
            if (!string.IsNullOrEmpty(elementId))
            {
                var cachedElement = _session.GetElement(elementId);
                if (cachedElement != null)
                {
                    // Get criteria from existing element
                    try
                    {
                        criteria = new ElementSearchCriteria
                        {
                            AutomationId = string.IsNullOrEmpty(automationId) ? cachedElement.AutomationId : automationId,
                            Name = string.IsNullOrEmpty(name) ? cachedElement.Name : name,
                            ClassName = string.IsNullOrEmpty(className) ? cachedElement.ClassName : className,
                            ControlType = string.IsNullOrEmpty(controlType) ? cachedElement.ControlType.ToString() : controlType,
                            OriginalElementId = elementId,
                            LastKnownBounds = new System.Drawing.Rectangle(
                                (int)cachedElement.BoundingRectangle.X,
                                (int)cachedElement.BoundingRectangle.Y,
                                (int)cachedElement.BoundingRectangle.Width,
                                (int)cachedElement.BoundingRectangle.Height
                            )
                        };
                    }
                    catch
                    {
                        // Element is stale, use whatever criteria we have
                    }
                }
            }

            // Attempt relocation
            var relocateResult = automation.RelocateElement(criteria);

            stopwatch.Stop();

            if (relocateResult.Success && relocateResult.RelocatedElement != null)
            {
                // Update the cache with the new element reference
                var newElementId = _session.CacheElement(relocateResult.RelocatedElement);

                // If we had an old element ID, remove it
                if (!string.IsNullOrEmpty(elementId))
                {
                    _session.ClearElement(elementId);
                }

                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("relocated", true),
                    ("new_element_id", newElementId),
                    ("old_element_id", elementId),
                    ("matched_by", relocateResult.MatchedBy),
                    ("message", relocateResult.Message),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }
            else
            {
                return Task.FromResult(ToolResponse.Fail(relocateResult.Message ?? "Element not found", _windowManager,
                    ("relocated", false),
                    ("suggestions", relocateResult.Suggestions),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> CheckElementStale(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var automation = _session.GetAutomation();

            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");

            var element = _session.GetElement(elementId);
            if (element == null)
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Ok(_windowManager,
                    ("is_stale", true),
                    ("reason", "Element ID not found in cache"),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }

            var isStale = automation.IsElementStale(element);

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("is_stale", isStale),
                ("element_id", elementId),
                ("reason", isStale ? "Element reference is no longer valid" : "Element is accessible"),
                ("recommendation", isStale ? "Use relocate_element to re-find this element" : null),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> GetCacheStats(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var treeCache = _session.GetTreeCache();
            var stats = treeCache.GetStats();

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("cache_hits", stats.CacheHits),
                ("cache_misses", stats.CacheMisses),
                ("hit_rate", stats.HitRate),
                ("hit_rate_percent", $"{stats.HitRate * 100:F1}%"),
                ("is_dirty", stats.IsDirty),
                ("has_cached_data", stats.HasCachedData),
                ("cache_age_ms", stats.CacheAgeMs),
                ("max_cache_age_ms", stats.MaxCacheAgeMs),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
        }
    }

    private Task<JsonElement> InvalidateCache(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var treeCache = _session.GetTreeCache();
            var resetStats = GetBoolArg(args, "reset_stats", false);

            treeCache.Clear();

            if (resetStats)
            {
                treeCache.ResetStats();
            }

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(_windowManager,
                ("cache_cleared", true),
                ("stats_reset", resetStats),
                ("message", "Tree cache invalidated. Next get_ui_tree will rebuild from scratch."),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, _windowManager).ToJsonElement());
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

    private double GetDoubleArg(JsonElement args, string key, double defaultValue = 0)
    {
        if (args.ValueKind == JsonValueKind.Null)
            return defaultValue;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetDouble()
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

    /// <summary>
    /// Resolve window and translate coordinates. Returns error response if window not found.
    /// </summary>
    private (bool success, int screenX, int screenY, JsonElement? errorResponse) ResolveWindowCoordinates(
        string? windowHandle, string? windowTitle, int x, int y, bool focusWindow = true)
    {
        if (string.IsNullOrEmpty(windowHandle) && string.IsNullOrEmpty(windowTitle))
        {
            return (true, x, y, null); // No window targeting, use screen coords
        }

        var window = _windowManager.FindWindow(windowHandle, windowTitle);
        if (window == null)
        {
            if (!string.IsNullOrEmpty(windowTitle))
            {
                var matches = _windowManager.FindWindowsByTitle(windowTitle);
                if (matches.Count > 1)
                {
                    return (false, 0, 0, ToolResponse.FailWithMultipleMatches(
                        $"Multiple windows match title '{windowTitle}'",
                        matches,
                        _windowManager).ToJsonElement());
                }
            }
            return (false, 0, 0, ToolResponse.Fail(
                $"Window not found: {windowHandle ?? windowTitle}",
                _windowManager).ToJsonElement());
        }

        if (_windowManager.IsWindowMinimized(windowHandle, windowTitle))
        {
            return (false, 0, 0, ToolResponse.Fail(
                "Window is minimized. Use focus_window first.",
                _windowManager).ToJsonElement());
        }

        // Focus the window to ensure it's in front for input
        if (focusWindow)
        {
            _windowManager.FocusWindowByHandle(window.Handle);
            System.Threading.Thread.Sleep(50); // Brief delay for window to come to front
        }

        // Translate client coordinates to screen coordinates
        var translated = _windowManager.TranslateClientToScreen(window.Handle, x, y);
        if (translated == null)
        {
            return (false, 0, 0, ToolResponse.Fail(
                "Could not translate coordinates to screen coordinates.",
                _windowManager).ToJsonElement());
        }

        return (true, translated.Value.screenX, translated.Value.screenY, null);
    }

    /// <summary>
    /// Resolve window and translate coordinates, also returning the window handle for input targeting.
    /// </summary>
    private (bool success, int screenX, int screenY, IntPtr hwnd, JsonElement? errorResponse) ResolveWindowCoordinatesWithHandle(
        string? windowHandle, string? windowTitle, int x, int y, bool focusWindow = true)
    {
        if (string.IsNullOrEmpty(windowHandle) && string.IsNullOrEmpty(windowTitle))
        {
            return (true, x, y, IntPtr.Zero, null); // No window targeting, use screen coords
        }

        var window = _windowManager.FindWindow(windowHandle, windowTitle);
        if (window == null)
        {
            if (!string.IsNullOrEmpty(windowTitle))
            {
                var matches = _windowManager.FindWindowsByTitle(windowTitle);
                if (matches.Count > 1)
                {
                    return (false, 0, 0, IntPtr.Zero, ToolResponse.FailWithMultipleMatches(
                        $"Multiple windows match title '{windowTitle}'",
                        matches,
                        _windowManager).ToJsonElement());
                }
            }
            return (false, 0, 0, IntPtr.Zero, ToolResponse.Fail(
                $"Window not found: {windowHandle ?? windowTitle}",
                _windowManager).ToJsonElement());
        }

        if (_windowManager.IsWindowMinimized(windowHandle, windowTitle))
        {
            return (false, 0, 0, IntPtr.Zero, ToolResponse.Fail(
                "Window is minimized. Use focus_window first.",
                _windowManager).ToJsonElement());
        }

        // Focus the window to ensure it's in front for input
        if (focusWindow)
        {
            _windowManager.FocusWindowByHandle(window.Handle);
            System.Threading.Thread.Sleep(50); // Brief delay for window to come to front
        }

        // Translate client coordinates to screen coordinates
        var translated = _windowManager.TranslateClientToScreen(window.Handle, x, y);
        if (translated == null)
        {
            return (false, 0, 0, IntPtr.Zero, ToolResponse.Fail(
                "Could not translate coordinates to screen coordinates.",
                _windowManager).ToJsonElement());
        }

        // Convert string handle to IntPtr
        IntPtr hwnd = IntPtr.Zero;
        if (!string.IsNullOrEmpty(window.Handle))
        {
            try
            {
                hwnd = new IntPtr(Convert.ToInt64(window.Handle, 16));
            }
            catch { }
        }
        return (true, translated.Value.screenX, translated.Value.screenY, hwnd, null);
    }
}
