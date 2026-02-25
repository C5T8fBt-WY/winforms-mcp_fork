namespace Rhombus.WinFormsMcp.Server.Protocol;

/// <summary>
/// MCP tool definitions - Minimal API (8 tools) + Sandbox (3 tools) = 11 total.
/// Consolidated from 52 legacy tools for ~90% token reduction.
/// </summary>
public static class ToolDefinitions
{
    public static object GetAll()
    {
        return new object[]
        {
            // ============================================================
            // SANDBOX TOOLS (kept separate per architecture)
            // ============================================================

            new
            {
                name = "launch_app_sandboxed",
                description = "Launch app in Windows Sandbox (isolated). Requires sandbox running.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Executable path inside sandbox" },
                        arguments = new { type = "string" },
                        idleTimeoutMs = new { type = "integer", description = "Default: 10000" }
                    },
                    required = new[] { "path" }
                }
            },

            new
            {
                name = "close_sandbox",
                description = "Close Windows Sandbox and all apps inside it.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>()
                }
            },

            new
            {
                name = "list_sandbox_apps",
                description = "List processes running in the sandbox.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>()
                }
            },

            // ============================================================
            // MINIMAL API (8 orthogonal primitives)
            // ============================================================

            // app - Application lifecycle
            new
            {
                name = "app",
                description = "Manage application lifecycle: launch, attach, close, or get info",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        action = new { type = "string", description = "launch | attach | close | info" },
                        path = new { type = "string", description = "Executable path (launch)" },
                        args = new { type = "string", description = "Command line args (launch)" },
                        working_directory = new { type = "string", description = "Working directory (launch, defaults to exe directory)" },
                        pid = new { type = "integer", description = "Process ID (attach/close/info)" },
                        title = new { type = "string", description = "Window title (attach)" },
                        wait_ms = new { type = "integer", description = "Max wait for window (launch/attach)" }
                    },
                    required = new[] { "action" }
                }
            },

            // find - Element discovery
            new
            {
                name = "find",
                description = "Find UI elements. Use at:'root' for all windows, at:elementId for subtree",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Element name pattern" },
                        automationId = new { type = "string", description = "Automation ID" },
                        className = new { type = "string", description = "Control class name" },
                        controlType = new { type = "string", description = "Button, Edit, etc." },
                        at = new { type = "string", description = "Element ID or 'root'" },
                        recursive = new { type = "boolean", description = "Return tree structure" },
                        depth = new { type = "integer", description = "Max tree depth" },
                        point = new
                        {
                            type = "object",
                            properties = new { x = new { type = "integer" }, y = new { type = "integer" } },
                            description = "Find at screen coordinates"
                        },
                        near = new
                        {
                            type = "object",
                            properties = new
                            {
                                element = new { type = "string" },
                                direction = new { type = "string", description = "above|below|left|right" }
                            },
                            description = "Find near anchor element"
                        },
                        wait_ms = new { type = "integer", description = "Max wait for element (polling interval 100ms)" },
                        timeout_ms = new { type = "integer", description = "Max time (ms) for the whole find scan. Default: 10000" },
                        window_handle = new { type = "string", description = "Scope search to a specific window HWND (e.g. '0x1A2B3C')" }
                    }
                }
            },

            // snapshot - Compact Playwright-style accessibility tree
            new
            {
                name = "snapshot",
                description = "Compact accessibility snapshot (Playwright-style). Lists interactive elements as YAML. Faster and more readable than find(recursive:true). Use instead of find for initial exploration.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        window_handle = new { type = "string", description = "Scope to specific window HWND (e.g. '0x1A2B3C'). Omit to snapshot all tracked process windows." },
                        depth = new { type = "integer", description = "Max tree depth (default: 6)" },
                        timeout_ms = new { type = "integer", description = "Max scan time ms (default: 10000)" }
                    }
                }
            },

            // click - Unified click/tap
            new
            {
                name = "click",
                description = "Click element or coordinates programmatically (UIA patterns + PostMessage, never moves physical mouse)",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        target = new { type = "string", description = "Element ID to click" },
                        automationId = new { type = "string", description = "AutomationId of element to click directly (no prior find needed)" },
                        name = new { type = "string", description = "Element name to click directly (no prior find needed)" },
                        x = new { type = "integer", description = "Screen X (if no target)" },
                        y = new { type = "integer", description = "Screen Y (if no target)" },
                        window_handle = new { type = "string", description = "Dialog HWND to accept/cancel via WM_COMMAND (works during MessageBox.Show, no UIA needed)" },
                        cancel = new { type = "boolean", description = "When using window_handle: send IDCANCEL instead of IDOK (default: false = accept/OK)" },
                        right = new { type = "boolean", description = "Right-click" },
                        @double = new { type = "boolean", description = "Double-click" },
                        hold_ms = new { type = "integer", description = "Long-press duration" }
                    }
                }
            },

            // type - Text input
            new
            {
                name = "type",
                description = "Type text into element or send keystrokes globally",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        text = new { type = "string", description = "Text or key sequence" },
                        target = new { type = "string", description = "Element ID (omit for global)" },
                        clear = new { type = "boolean", description = "Clear field first" },
                        keys = new { type = "boolean", description = "Interpret as key codes" }
                    },
                    required = new[] { "text" }
                }
            },

            // drag - Unified drag/stroke
            new
            {
                name = "drag",
                description = "Drag, swipe, or pen stroke along a path",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    x = new { type = "integer" },
                                    y = new { type = "integer" },
                                    pressure = new { type = "integer" }
                                }
                            },
                            description = "Points [{x,y}], min 2"
                        },
                        input = new { type = "string", description = "mouse | touch | pen" },
                        button = new { type = "string", description = "left | right | middle (mouse)" },
                        eraser = new { type = "boolean", description = "Pen eraser tip" },
                        right = new { type = "boolean", description = "Right-drag or barrel button (pen)" },
                        duration_ms = new { type = "integer", description = "Total drag duration" }
                    },
                    required = new[] { "path" }
                }
            },

            // gesture - Multi-touch
            new
            {
                name = "gesture",
                description = "Multi-finger gestures: pinch, rotate, or custom",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        type = new { type = "string", description = "pinch | rotate | custom" },
                        center = new
                        {
                            type = "object",
                            properties = new { x = new { type = "integer" }, y = new { type = "integer" } }
                        },
                        start_distance = new { type = "integer", description = "Pinch start distance" },
                        end_distance = new { type = "integer", description = "Pinch end distance" },
                        start_angle = new { type = "number", description = "Rotate start angle" },
                        end_angle = new { type = "number", description = "Rotate end angle" },
                        radius = new { type = "integer", description = "Rotation radius" },
                        fingers = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    path = new
                                    {
                                        type = "array",
                                        items = new
                                        {
                                            type = "object",
                                            properties = new
                                            {
                                                x = new { type = "integer" },
                                                y = new { type = "integer" }
                                            }
                                        }
                                    }
                                }
                            },
                            description = "Custom finger paths"
                        },
                        duration_ms = new { type = "integer" }
                    },
                    required = new[] { "type" }
                }
            },

            // screenshot - Capture
            new
            {
                name = "screenshot",
                description = "Capture screenshot of window or element",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        target = new { type = "string", description = "Element ID or window title" },
                        file = new { type = "string", description = "Save to file path" }
                    }
                }
            },

            // script - Batch operations
            new
            {
                name = "script",
                description = "Execute multiple operations with variable interpolation",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        steps = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    id = new { type = "string", description = "Step ID for references" },
                                    tool = new { type = "string" },
                                    args = new { type = "object", description = "Use $stepId.path refs" }
                                },
                                required = new[] { "tool", "args" }
                            }
                        },
                        stop_on_error = new { type = "boolean", description = "Default: true" }
                    },
                    required = new[] { "steps" }
                }
            }
        };
    }
}
