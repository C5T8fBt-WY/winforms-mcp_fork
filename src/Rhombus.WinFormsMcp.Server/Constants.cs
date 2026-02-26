namespace C5T8fBtWY.WinFormsMcp.Server;

/// <summary>
/// Centralized constants for the WinForms MCP server.
/// Eliminates magic numbers and strings throughout the codebase.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Standard JSON-RPC 2.0 error codes.
    /// See: https://www.jsonrpc.org/specification#error_object
    /// </summary>
    public static class JsonRpcErrors
    {
        /// <summary>Invalid JSON was received by the server.</summary>
        public const int ParseError = -32700;

        /// <summary>The JSON sent is not a valid Request object.</summary>
        public const int InvalidRequest = -32600;

        /// <summary>The method does not exist or is not available.</summary>
        public const int MethodNotFound = -32601;

        /// <summary>Invalid method parameter(s).</summary>
        public const int InvalidParams = -32602;

        /// <summary>Internal JSON-RPC error.</summary>
        public const int InternalError = -32603;
    }

    /// <summary>
    /// Timeout values in milliseconds.
    /// </summary>
    public static class Timeouts
    {
        /// <summary>Default timeout for element wait operations.</summary>
        public const int DefaultWait = 10000;

        /// <summary>Default poll interval for element search.</summary>
        public const int DefaultPollInterval = 100;

        /// <summary>Default timeout for app close operations.</summary>
        public const int CloseApp = 5000;

        /// <summary>Default timeout for app idle detection.</summary>
        public const int AppIdle = 5000;

        /// <summary>Default timeout for script execution.</summary>
        public const int ScriptExecution = 120000;

        /// <summary>Delay after UI actions for state to settle.</summary>
        public const int UiUpdateDelay = 100;

        /// <summary>Delay after drag setup before starting drag.</summary>
        public const int DragSetupDelay = 100;

        /// <summary>Delay after clearing text before typing.</summary>
        public const int ClearDelay = 100;

        /// <summary>Brief delay for window to come to front.</summary>
        public const int WindowFocusDelay = 100;

        /// <summary>Timeout for sandbox app shutdown.</summary>
        public const int SandboxShutdown = 10000;

        /// <summary>Wait for process to exit during hot reload.</summary>
        public const int ProcessExitWait = 1000;
    }

    /// <summary>
    /// Limits and boundaries.
    /// </summary>
    public static class Limits
    {
        /// <summary>Maximum waypoints in a drag path.</summary>
        public const int MaxDragPathWaypoints = 1000;

        /// <summary>Default token budget for UI tree output.</summary>
        public const int DefaultTokenBudget = 5000;
    }

    /// <summary>
    /// MCP protocol constants.
    /// </summary>
    public static class Protocol
    {
        /// <summary>JSON-RPC version.</summary>
        public const string JsonRpcVersion = "2.0";

        /// <summary>MCP protocol version.</summary>
        public const string McpProtocolVersion = "2024-11-05";

        /// <summary>Server name for MCP identification.</summary>
        public const string ServerName = "winforms-mcp";

        /// <summary>Server version (from assembly, set by VERSION file at build time).</summary>
        public static readonly string ServerVersion =
            typeof(Protocol).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>
    /// File system paths.
    /// </summary>
    public static class Paths
    {
        /// <summary>Shared folder path (maps to C:\Shared in sandbox).</summary>
        public const string SharedDirectory = @"C:\Shared";

        /// <summary>Crash log file name.</summary>
        public const string CrashLogFile = "server-crash.log";
    }

    /// <summary>
    /// Win32 API constants.
    /// </summary>
    public static class Win32
    {
        /// <summary>ShowWindow command constants.</summary>
        public static class ShowWindow
        {
            public const int Minimized = 2;
            public const int Maximized = 3;
            public const int Restore = 9;
        }

        /// <summary>GetSystemMetrics/GetDeviceCaps constants.</summary>
        public static class SystemMetrics
        {
            public const int LogPixelsX = 88;
            public const int LogPixelsY = 90;
            public const int XVirtualScreen = 76;
            public const int YVirtualScreen = 77;
        }
    }

    /// <summary>
    /// Display and coordinate constants.
    /// </summary>
    public static class Display
    {
        /// <summary>Standard DPI (100% scaling).</summary>
        public const int StandardDpi = 96;

        /// <summary>HIMETRIC units per inch (for touch/pen coordinates).</summary>
        public const double HimetricPerInch = 2540.0;

        /// <summary>Maximum coordinate value for tablet digitizers.</summary>
        public const int TabletCoordMax = 32767;

        /// <summary>Approximate tokens per UI element (for budget estimation).</summary>
        public const int TokensPerElement = 25;
    }

    /// <summary>
    /// Event and cache limits.
    /// </summary>
    public static class Queues
    {
        /// <summary>Maximum events in the event queue before dropping oldest.</summary>
        public const int MaxEventQueueSize = 10;

        /// <summary>Timeout for confirmation tokens in seconds.</summary>
        public const int ConfirmationTimeoutSeconds = 60;

        /// <summary>Maximum depth for state change detection.</summary>
        public const int StateChangeMaxDepth = 5;
    }

    /// <summary>
    /// Input injection defaults for touch and pen.
    /// </summary>
    public static class Input
    {
        /// <summary>Default contact size for touch injection (pixels).</summary>
        public const int TouchContactSize = 2;

        /// <summary>Default touch pressure value (0-65535 range).</summary>
        public const uint TouchPressureDefault = 32000;

        /// <summary>Default pen pressure value (0-1024 range, midpoint).</summary>
        public const uint PenPressureDefault = 512;

        /// <summary>Perpendicular orientation value (degrees).</summary>
        public const uint OrientationPerpendicular = 90;

        /// <summary>Default number of steps for drag interpolation.</summary>
        public const int DefaultDragSteps = 10;

        /// <summary>Default delay between drag steps (ms).</summary>
        public const int DefaultDragDelayMs = 5;

        /// <summary>Default delay between touch events (ms).</summary>
        public const int DefaultTouchDelayMs = 1;

        /// <summary>Default number of steps for pen stroke interpolation.</summary>
        public const int DefaultPenStrokeSteps = 20;

        /// <summary>Default delay between pen stroke steps (ms).</summary>
        public const int DefaultPenStrokeDelayMs = 2;

        /// <summary>Maximum touch contacts for multi-touch.</summary>
        public const uint MaxTouchContacts = 10;

        /// <summary>Default number of steps for pinch/rotate gestures.</summary>
        public const int DefaultGestureSteps = 20;
    }

    /// <summary>
    /// Pointer flag constants for input injection.
    /// Mirrors Win32 POINTER_FLAG_* values for quick reference.
    /// Full definitions are in Interop/Win32Constants.cs.
    /// </summary>
    public static class Pointer
    {
        /// <summary>No flags.</summary>
        public const uint FlagNone = 0x00000000;

        /// <summary>New pointer (first contact).</summary>
        public const uint FlagNew = 0x00000001;

        /// <summary>Pointer is in range.</summary>
        public const uint FlagInRange = 0x00000002;

        /// <summary>Pointer is in contact.</summary>
        public const uint FlagInContact = 0x00000004;

        /// <summary>First button pressed.</summary>
        public const uint FlagFirstButton = 0x00000010;

        /// <summary>This is the primary pointer.</summary>
        public const uint FlagPrimary = 0x00002000;

        /// <summary>Input is confident (not accidental).</summary>
        public const uint FlagConfidence = 0x00004000;

        /// <summary>Pointer down event.</summary>
        public const uint FlagDown = 0x00010000;

        /// <summary>Pointer update event.</summary>
        public const uint FlagUpdate = 0x00020000;

        /// <summary>Pointer up event.</summary>
        public const uint FlagUp = 0x00040000;

        /// <summary>Pointer type: Touch.</summary>
        public const uint TypeTouch = 0x00000002;

        /// <summary>Pointer type: Pen.</summary>
        public const uint TypePen = 0x00000003;
    }
}
