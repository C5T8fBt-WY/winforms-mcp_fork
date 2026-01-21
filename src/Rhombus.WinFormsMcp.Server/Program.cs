using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Handlers;
using Rhombus.WinFormsMcp.Server.Protocol;
using Rhombus.WinFormsMcp.Server.Sandbox;

namespace Rhombus.WinFormsMcp.Server;

/// <summary>
/// Rhombus.WinFormsMcp - MCP Server for WinForms Automation
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
        var sharedDir = Constants.Paths.SharedDirectory;
        if (Directory.Exists(sharedDir))
        {
            _logFile = Path.Combine(sharedDir, Constants.Paths.CrashLogFile);
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
            int? e2ePort = null;

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
                else if (args[i] == "--e2e-port" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out var port))
                    {
                        e2ePort = port;
                        Log($"  E2E port: {port}");
                    }
                    else
                    {
                        Log($"Invalid E2E port: {args[i + 1]}");
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
                await _server.RunTcpAsync(tcpPort.Value, e2ePort);
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

        if (_eventQueue.Count >= Constants.Queues.MaxEventQueueSize)
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
            ExpiresAt = DateTime.UtcNow.AddSeconds(Constants.Queues.ConfirmationTimeoutSeconds)
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
/// Core MCP server implementation handling JSON-RPC communication.
/// Supports multiple concurrent client connections with serialized UIA operations.
/// </summary>
class AutomationServer
{
    private readonly Dictionary<string, Func<JsonElement, Task<JsonElement>>> _tools;
    private readonly List<IToolHandler> _handlers = new();
    private readonly Script.ScriptRunner _scriptRunner;
    private readonly SessionManager _session = new();
    private readonly WindowManager _windowManager = new();
    private readonly McpProtocol _protocol = new();

    /// <summary>
    /// Semaphore to serialize UIA operations. FlaUI/UIA is not thread-safe,
    /// so we allow multiple clients to connect but serialize actual tool execution.
    /// </summary>
    private readonly SemaphoreSlim _uiaLock = new(1, 1);

    /// <summary>
    /// Track active client tasks for graceful shutdown.
    /// </summary>
    private readonly List<Task> _clientTasks = new();
    private readonly object _clientTasksLock = new();

    public AutomationServer()
    {
        // Register handlers
        RegisterHandler(new ProcessHandlers(_session, _windowManager));
        RegisterHandler(new ElementHandlers(_session, _windowManager));
        RegisterHandler(new InputHandlers(_session, _windowManager));
        RegisterHandler(new TouchPenHandlers(_session, _windowManager));
        RegisterHandler(new ScreenshotHandlers(_session, _windowManager));
        RegisterHandler(new WindowHandlers(_session, _windowManager));
        RegisterHandler(new ValidationHandlers(_session, _windowManager));
        RegisterHandler(new ObservationHandlers(_session, _windowManager));
        RegisterHandler(new SandboxHandlers(_session, _windowManager));
        // AdvancedHandlers needs tool dispatcher for execute_confirmed_action
        RegisterHandler(new AdvancedHandlers(_session, _windowManager, DispatchTool));

        // Initialize ScriptRunner with tool dispatcher
        _scriptRunner = new Script.ScriptRunner(DispatchTool, _windowManager);

        _tools = new Dictionary<string, Func<JsonElement, Task<JsonElement>>>
        {
            // Script Execution
            { "run_script", args => _scriptRunner.RunAsync(args) },
        };

        // Wire up tools from handlers
        foreach (var handler in _handlers)
        {
            foreach (var toolName in handler.SupportedTools)
            {
                _tools[toolName] = args => handler.ExecuteAsync(toolName, args);
            }
        }
    }

    /// <summary>
    /// Dispatch a tool call by name. Used by handlers that need to call other tools.
    /// </summary>
    private Task<JsonElement> DispatchTool(string toolName, JsonElement args)
    {
        if (_tools.TryGetValue(toolName, out var handler))
        {
            return handler(args);
        }
        return Task.FromResult(ToolResponse.Fail($"Unknown tool: {toolName}", _windowManager).ToJsonElement());
    }

    /// <summary>
    /// Register a tool handler.
    /// </summary>
    private void RegisterHandler(IToolHandler handler)
    {
        _handlers.Add(handler);
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
                var request = _protocol.ParseRequest(line);
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
                // For parse errors or other exceptions where we don't have a request ID
                var error = _protocol.FormatInternalError(0, ex.Message);
                await writer.WriteLineAsync(JsonSerializer.Serialize(error));
                await writer.FlushAsync();
            }
        }
    }

    /// <summary>
    /// Run the MCP server in TCP mode, listening on the specified port(s).
    /// Accepts multiple concurrent connections - tool execution is serialized via semaphore.
    /// </summary>
    /// <param name="port">Main TCP port for agent connections</param>
    /// <param name="e2ePort">Optional second port for E2E test connections</param>
    public async Task RunTcpAsync(int port, int? e2ePort = null)
    {
        var mainListener = new TcpListener(IPAddress.Any, port);
        mainListener.Start();

        TcpListener? e2eListener = null;
        if (e2ePort.HasValue)
        {
            e2eListener = new TcpListener(IPAddress.Any, e2ePort.Value);
            e2eListener.Start();
        }

        // Write ready signal to stderr (stdout is reserved for JSON-RPC in stdio mode)
        Console.Error.WriteLine($"MCP Server listening on TCP port {port}");
        if (e2ePort.HasValue)
        {
            Console.Error.WriteLine($"E2E port: {e2ePort.Value}");
        }
        Console.Error.WriteLine("Multiple concurrent connections supported");

        // Start accept loops for both ports
        var mainAcceptTask = AcceptClientsAsync(mainListener, "main");
        if (e2eListener != null)
        {
            var e2eAcceptTask = AcceptClientsAsync(e2eListener, "e2e");
            await Task.WhenAll(mainAcceptTask, e2eAcceptTask);
        }
        else
        {
            await mainAcceptTask;
        }
    }

    /// <summary>
    /// Accept clients on a listener in a loop.
    /// </summary>
    private async Task AcceptClientsAsync(TcpListener listener, string listenerName)
    {
        while (true)
        {
            Console.Error.WriteLine($"[{listenerName}] Waiting for client connection...");
            var client = await listener.AcceptTcpClientAsync();
            var clientId = $"{listenerName}-{Guid.NewGuid().ToString("N")[..6]}";
            Console.Error.WriteLine($"[{clientId}] Client connected from {client.Client.RemoteEndPoint}");

            // Fire-and-forget: handle client concurrently
            var clientTask = HandleTcpClientAsync(client, clientId);

            // Track the task for potential graceful shutdown
            lock (_clientTasksLock)
            {
                _clientTasks.Add(clientTask);
                // Clean up completed tasks
                _clientTasks.RemoveAll(t => t.IsCompleted);
            }
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, string clientId)
    {
        try
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
                    var request = _protocol.ParseRequest(line);

                    // Serialize UIA operations via semaphore (FlaUI is not thread-safe)
                    await _uiaLock.WaitAsync();
                    object response;
                    try
                    {
                        response = await ProcessRequest(request);
                    }
                    finally
                    {
                        _uiaLock.Release();
                    }

                    // Skip writing response for notifications (response is null)
                    if (response != null)
                    {
                        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                    }
                }
                catch (Exception ex)
                {
                    var error = _protocol.FormatInternalError(0, ex.Message);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(error));
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{clientId}] Client error: {ex.Message}");
        }
        finally
        {
            client.Close();
            Console.Error.WriteLine($"[{clientId}] Client disconnected");
        }
    }

    private async Task<object> ProcessRequest(JsonElement request)
    {
        var method = _protocol.GetMethod(request);
        if (method == null)
            throw new InvalidOperationException("Missing method");

        var requestId = _protocol.GetRequestId(request);

        if (method == "initialize")
        {
            return _protocol.FormatSuccess(requestId, new
            {
                protocolVersion = Constants.Protocol.McpProtocolVersion,
                capabilities = new
                {
                    // Indicate tool support with empty object (actual tools returned via tools/list)
                    tools = new { }
                },
                serverInfo = new
                {
                    name = Constants.Protocol.ServerName,
                    version = Constants.Protocol.ServerVersion
                }
            });
        }

        if (method == "tools/list")
        {
            return _protocol.FormatSuccess(requestId, new
            {
                tools = GetToolDefinitions()
            });
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

            return _protocol.FormatSuccess(requestId, new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = result.ToString()
                    }
                }
            });
        }

        // Handle MCP notifications (no response expected)
        if (_protocol.IsNotification(request))
        {
            // Return null to indicate no response should be sent
            return null!;
        }

        throw new InvalidOperationException($"Unknown method: {method}");
    }

    private object GetToolDefinitions() => ToolDefinitions.GetAll();
}
