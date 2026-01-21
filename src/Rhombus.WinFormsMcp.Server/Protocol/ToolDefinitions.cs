namespace Rhombus.WinFormsMcp.Server.Protocol;

/// <summary>
/// Contains MCP tool definitions (JSON schemas) for all available tools.
/// </summary>
public static class ToolDefinitions
{
    /// <summary>
    /// Get all tool definitions for the MCP tools/list response.
    /// </summary>
    public static object GetAll()
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
                description = "Take a screenshot. Specify windowHandle or windowTitle to capture a specific window (recommended). Response includes window bounds for coordinate reference. Without window params, captures full desktop. Use returnBase64=true to get base64-encoded image data instead of saving to file.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        outputPath = new { type = "string", description = "Path to save the screenshot (required unless returnBase64=true)" },
                        windowHandle = new { type = "string", description = "Window handle (hex) to screenshot. Takes priority over windowTitle." },
                        windowTitle = new { type = "string", description = "Window title to screenshot (substring match). Use this or windowHandle for window-specific capture." },
                        elementPath = new { type = "string", description = "Specific element to screenshot (optional, uses cached element)" },
                        returnBase64 = new { type = "boolean", description = "If true, return base64-encoded image data instead of saving to file. outputPath is ignored when this is true." }
                    }
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
                        holdMs = new { type = "integer", description = "Milliseconds to hold before release (default 0)" },
                        useLegacy = new { type = "boolean", description = "Use legacy InjectTouchInput API instead of synthetic pointer. May route through system touch device with proper coordinate mapping." }
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
            new
            {
                name = "get_element_at_point",
                description = "Get the UI element at a specific screen coordinate. Used for native grounding - verifying what's actually under a visual coordinate before performing actions. The Brain uses this to verify that a coordinate from Vision matches the expected element.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        x = new { type = "integer", description = "X screen coordinate" },
                        y = new { type = "integer", description = "Y screen coordinate" }
                    },
                    required = new[] { "x", "y" }
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
            new
            {
                name = "list_sandbox_apps",
                description = "List available applications in the sandbox App folder. Returns executables that can be launched with launch_app.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        appFolder = new { type = "string", description = "App folder to scan (default: C:\\App)" }
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
            },
            // Script Execution
            new
            {
                name = "run_script",
                description = "Execute multiple commands in sequence without round-trip overhead. Steps can reference results from previous steps via variable binding (e.g., '$step1.result.elementId'). Use for multi-step workflows like launching an app, finding elements, and interacting with them in one call.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        script = new
                        {
                            type = "object",
                            description = "Script containing steps and options",
                            properties = new
                            {
                                steps = new
                                {
                                    type = "array",
                                    description = "Array of steps to execute in sequence",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            id = new { type = "string", description = "Optional step identifier for referencing results (e.g., 'launch', 'findBtn'). Auto-generated as 'step_N' if omitted." },
                                            tool = new { type = "string", description = "Name of the MCP tool to execute (e.g., 'launch_app', 'find_element', 'click_element')" },
                                            args = new { type = "object", description = "Arguments to pass to the tool. Use '$stepId.result.path' syntax to reference previous step results (e.g., '$step1.result.elementId'). Use '$last.result.path' for previous step." },
                                            delay_after_ms = new { type = "integer", description = "Milliseconds to wait after this step completes (overrides default_delay_ms)" }
                                        },
                                        required = new[] { "tool" }
                                    }
                                },
                                options = new
                                {
                                    type = "object",
                                    description = "Script execution options",
                                    properties = new
                                    {
                                        stop_on_error = new { type = "boolean", description = "Stop execution on first error (default: true)" },
                                        default_delay_ms = new { type = "integer", description = "Default delay in milliseconds between steps (default: 0)" },
                                        timeout_ms = new { type = "integer", description = "Maximum total execution time in milliseconds (default: 60000)" }
                                    }
                                }
                            },
                            required = new[] { "steps" }
                        }
                    },
                    required = new[] { "script" }
                }
            }
        };
    }
}
