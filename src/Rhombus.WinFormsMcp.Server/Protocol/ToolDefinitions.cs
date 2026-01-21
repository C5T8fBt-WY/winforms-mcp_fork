namespace Rhombus.WinFormsMcp.Server.Protocol;

/// <summary>
/// MCP tool definitions (JSON schemas).
/// </summary>
public static class ToolDefinitions
{
    public static object GetAll()
    {
        return new object[]
        {
            // Element Tools
            new
            {
                name = "find_element",
                description = "Find UI element by AutomationId, Name, ClassName, or ControlType. Supports regex patterns.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        automationId = new { type = "string", description = "Exact AutomationId" },
                        automationIdPattern = new { type = "string", description = "Regex pattern for AutomationId" },
                        name = new { type = "string", description = "Element name" },
                        namePattern = new { type = "string", description = "Regex pattern for name" },
                        className = new { type = "string" },
                        controlType = new { type = "string" },
                        parent = new { type = "string", description = "Parent element path" },
                        pollIntervalMs = new { type = "integer", description = "Search interval (default: 100)" }
                    }
                }
            },
            new
            {
                name = "click_element",
                description = "Click a UI element",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementPath = new { type = "string", description = "Element path/ID" },
                        doubleClick = new { type = "boolean" }
                    },
                    required = new[] { "elementPath" }
                }
            },
            new
            {
                name = "type_text",
                description = "Type text into a field",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementPath = new { type = "string" },
                        text = new { type = "string" },
                        clearFirst = new { type = "boolean" },
                        clearDelayMs = new { type = "integer", description = "Delay after select-all (default: 100)" }
                    },
                    required = new[] { "elementPath", "text" }
                }
            },
            new
            {
                name = "set_value",
                description = "Set input value (select-all + type)",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementPath = new { type = "string" },
                        value = new { type = "string" },
                        selectAllDelayMs = new { type = "integer", description = "Default: 50" }
                    },
                    required = new[] { "elementPath", "value" }
                }
            },
            new
            {
                name = "drag_drop",
                description = "Drag element to target",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        sourceElementPath = new { type = "string" },
                        targetElementPath = new { type = "string" },
                        dragSetupDelayMs = new { type = "integer", description = "Default: 100" },
                        dropDelayMs = new { type = "integer", description = "Default: 200" }
                    },
                    required = new[] { "sourceElementPath", "targetElementPath" }
                }
            },
            new
            {
                name = "click_by_automation_id",
                description = "Find and click by AutomationId",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        automationId = new { type = "string" },
                        windowTitle = new { type = "string" },
                        doubleClick = new { type = "boolean" }
                    },
                    required = new[] { "automationId" }
                }
            },
            new
            {
                name = "list_elements",
                description = "List UI elements in window (debug)",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowTitle = new { type = "string" },
                        maxDepth = new { type = "integer", description = "Default: 3" }
                    },
                    required = new[] { "windowTitle" }
                }
            },
            new
            {
                name = "get_property",
                description = "Get element property value",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementPath = new { type = "string" },
                        propertyName = new { type = "string" }
                    },
                    required = new[] { "elementPath", "propertyName" }
                }
            },
            // Process Tools
            new
            {
                name = "launch_app",
                description = "Launch application. Tracks PID for window scoping.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Executable path" },
                        arguments = new { type = "string" },
                        workingDirectory = new { type = "string" },
                        idleTimeoutMs = new { type = "integer", description = "Default: 5000" }
                    },
                    required = new[] { "path" }
                }
            },
            new
            {
                name = "attach_to_process",
                description = "Attach to running process by PID or name",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer" },
                        processName = new { type = "string" }
                    }
                }
            },
            new
            {
                name = "close_app",
                description = "Close application by PID",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pid = new { type = "integer" },
                        force = new { type = "boolean", description = "Force kill (default: false)" },
                        closeTimeoutMs = new { type = "integer", description = "Default: 5000" }
                    },
                    required = new[] { "pid" }
                }
            },
            new
            {
                name = "get_process_info",
                description = "Get process info from window handle/title",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string", description = "HWND (hex or decimal)" },
                        windowTitle = new { type = "string" }
                    }
                }
            },
            // Validation Tools
            new
            {
                name = "element_exists",
                description = "Check if element exists",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        automationId = new { type = "string" },
                        name = new { type = "string" },
                        windowTitle = new { type = "string" }
                    }
                }
            },
            new
            {
                name = "wait_for_element",
                description = "Wait for element to appear",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        automationId = new { type = "string" },
                        parent = new { type = "string" },
                        timeoutMs = new { type = "integer", description = "Default: 10000" },
                        pollIntervalMs = new { type = "integer", description = "Default: 100" }
                    },
                    required = new[] { "automationId" }
                }
            },
            new
            {
                name = "check_element_state",
                description = "Get element state (enabled, visible, value, toggle, selection)",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string", description = "Cached element ID" },
                        automationId = new { type = "string" },
                        windowTitle = new { type = "string" }
                    }
                }
            },
            // Screenshot
            new
            {
                name = "take_screenshot",
                description = "Capture screenshot. Specify window for targeted capture.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        outputPath = new { type = "string", description = "File path (ignored if returnBase64)" },
                        windowHandle = new { type = "string" },
                        windowTitle = new { type = "string" },
                        elementPath = new { type = "string" },
                        returnBase64 = new { type = "boolean", description = "Return base64 instead of file" }
                    }
                }
            },
            // Input Tools
            new
            {
                name = "send_keys",
                description = "Send keyboard input",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        keys = new { type = "string", description = "Keys to send" },
                        windowHandle = new { type = "string" },
                        windowTitle = new { type = "string" }
                    },
                    required = new[] { "keys" }
                }
            },
            new
            {
                name = "mouse_click",
                description = "Mouse click at coordinates",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string", description = "For window-relative coords" },
                        windowTitle = new { type = "string" },
                        x = new { type = "integer" },
                        y = new { type = "integer" },
                        doubleClick = new { type = "boolean" },
                        delayMs = new { type = "integer" }
                    },
                    required = new[] { "x", "y" }
                }
            },
            new
            {
                name = "mouse_drag",
                description = "Mouse drag between points",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string" },
                        windowTitle = new { type = "string" },
                        x1 = new { type = "integer" },
                        y1 = new { type = "integer" },
                        x2 = new { type = "integer" },
                        y2 = new { type = "integer" },
                        steps = new { type = "integer", description = "Default: 10" },
                        delayMs = new { type = "integer" }
                    },
                    required = new[] { "x1", "y1", "x2", "y2" }
                }
            },
            new
            {
                name = "mouse_drag_path",
                description = "Drag through waypoints",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string" },
                        windowTitle = new { type = "string" },
                        points = new
                        {
                            type = "array",
                            description = "Waypoints [{x,y}] (2-1000)",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    x = new { type = "integer" },
                                    y = new { type = "integer" }
                                },
                                required = new[] { "x", "y" }
                            }
                        },
                        stepsPerSegment = new { type = "integer", description = "Default: 1" },
                        delayMs = new { type = "integer" }
                    },
                    required = new[] { "points" }
                }
            },
            // Touch/Pen Tools
            new
            {
                name = "touch_tap",
                description = "Touch tap. Window params enable relative coords.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string" },
                        windowTitle = new { type = "string" },
                        x = new { type = "integer" },
                        y = new { type = "integer" },
                        holdMs = new { type = "integer", description = "Hold duration" },
                        useLegacy = new { type = "boolean", description = "Use InjectTouchInput API" }
                    },
                    required = new[] { "x", "y" }
                }
            },
            new
            {
                name = "touch_drag",
                description = "Touch drag",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string" },
                        windowTitle = new { type = "string" },
                        x1 = new { type = "integer" },
                        y1 = new { type = "integer" },
                        x2 = new { type = "integer" },
                        y2 = new { type = "integer" },
                        steps = new { type = "integer", description = "Default: 10" },
                        delayMs = new { type = "integer" }
                    },
                    required = new[] { "x1", "y1", "x2", "y2" }
                }
            },
            new
            {
                name = "pinch_zoom",
                description = "Two-finger pinch gesture",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string" },
                        windowTitle = new { type = "string" },
                        centerX = new { type = "integer" },
                        centerY = new { type = "integer" },
                        startDistance = new { type = "integer", description = "Initial finger distance (px)" },
                        endDistance = new { type = "integer", description = "Final distance (larger=zoom in)" },
                        steps = new { type = "integer", description = "Default: 20" },
                        delayMs = new { type = "integer" }
                    },
                    required = new[] { "centerX", "centerY", "startDistance", "endDistance" }
                }
            },
            new
            {
                name = "rotate",
                description = "Two-finger rotation gesture",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string" },
                        windowTitle = new { type = "string" },
                        centerX = new { type = "integer" },
                        centerY = new { type = "integer" },
                        radius = new { type = "integer", description = "Default: 50" },
                        startAngle = new { type = "number", description = "Degrees, default: 0" },
                        endAngle = new { type = "number", description = "Degrees, default: 90" },
                        steps = new { type = "integer", description = "Default: 20" },
                        delayMs = new { type = "integer" }
                    },
                    required = new[] { "centerX", "centerY" }
                }
            },
            new
            {
                name = "multi_touch_gesture",
                description = "Custom multi-finger gesture",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string" },
                        windowTitle = new { type = "string" },
                        fingers = new
                        {
                            type = "array",
                            description = "Array of finger paths, each [[x,y,timeMs],...]",
                            items = new { type = "array" }
                        }
                    },
                    required = new[] { "fingers" }
                }
            },
            new
            {
                name = "pen_stroke",
                description = "Pen stroke with pressure",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string" },
                        windowTitle = new { type = "string" },
                        x1 = new { type = "integer" },
                        y1 = new { type = "integer" },
                        x2 = new { type = "integer" },
                        y2 = new { type = "integer" },
                        steps = new { type = "integer", description = "Default: 20" },
                        pressure = new { type = "integer", description = "0-1024, default: 512" },
                        eraser = new { type = "boolean" },
                        delayMs = new { type = "integer" }
                    },
                    required = new[] { "x1", "y1", "x2", "y2" }
                }
            },
            new
            {
                name = "pen_tap",
                description = "Pen tap with pressure",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowHandle = new { type = "string" },
                        windowTitle = new { type = "string" },
                        x = new { type = "integer" },
                        y = new { type = "integer" },
                        pressure = new { type = "integer", description = "0-1024, default: 512" },
                        holdMs = new { type = "integer" }
                    },
                    required = new[] { "x", "y" }
                }
            },
            // Window Tools
            new
            {
                name = "get_window_bounds",
                description = "Get window screen bounds",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowTitle = new { type = "string", description = "Partial match" }
                    },
                    required = new[] { "windowTitle" }
                }
            },
            new
            {
                name = "focus_window",
                description = "Bring window to foreground",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowTitle = new { type = "string" }
                    },
                    required = new[] { "windowTitle" }
                }
            },
            // Observation Tools
            new
            {
                name = "get_ui_tree",
                description = "Get UI tree (XML). Prunes invisible/internal elements, enforces token budget.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowTitle = new { type = "string" },
                        maxDepth = new { type = "integer", description = "Default: 3" },
                        maxTokenBudget = new { type = "integer", description = "Default: 5000" },
                        includeInvisible = new { type = "boolean" },
                        skipInternalParts = new { type = "boolean", description = "Skip PART_* (default: true)" }
                    }
                }
            },
            new
            {
                name = "expand_collapse",
                description = "Expand/collapse tree node or menu",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string" },
                        automationId = new { type = "string" },
                        windowTitle = new { type = "string" },
                        expand = new { type = "boolean", description = "true=expand, false=collapse" },
                        uiUpdateDelayMs = new { type = "integer", description = "Default: 100" }
                    },
                    required = new[] { "expand" }
                }
            },
            new
            {
                name = "scroll",
                description = "Scroll a container",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string" },
                        automationId = new { type = "string" },
                        windowTitle = new { type = "string" },
                        direction = new { type = "string", description = "Up, Down, Left, Right" },
                        amount = new { type = "string", description = "SmallDecrement or LargeDecrement" },
                        uiUpdateDelayMs = new { type = "integer", description = "Default: 100" }
                    },
                    required = new[] { "direction" }
                }
            },
            new
            {
                name = "get_element_at_point",
                description = "Get element at screen coordinate",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        x = new { type = "integer" },
                        y = new { type = "integer" }
                    },
                    required = new[] { "x", "y" }
                }
            },
            // Snapshot Tools
            new
            {
                name = "capture_ui_snapshot",
                description = "Capture UI state for diff comparison",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowTitle = new { type = "string" },
                        snapshotId = new { type = "string", description = "ID for later reference" }
                    },
                    required = new[] { "snapshotId" }
                }
            },
            new
            {
                name = "compare_ui_snapshots",
                description = "Diff two snapshots (added/removed/modified)",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        beforeSnapshotId = new { type = "string" },
                        afterSnapshotId = new { type = "string", description = "Auto-captures if omitted" },
                        windowTitle = new { type = "string" }
                    },
                    required = new[] { "beforeSnapshotId" }
                }
            },
            // Sandbox Tools
            new
            {
                name = "launch_app_sandboxed",
                description = "Launch app in Windows Sandbox",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        appPath = new { type = "string", description = "App directory (maps to C:\\App)" },
                        appExe = new { type = "string", description = "Executable name" },
                        mcpServerPath = new { type = "string", description = "MCP server path (maps to C:\\MCP)" },
                        sharedFolderPath = new { type = "string", description = "Shared folder (maps to C:\\Shared)" },
                        outputFolderPath = new { type = "string" },
                        bootTimeoutMs = new { type = "integer", description = "Default: 60000" }
                    },
                    required = new[] { "appPath", "appExe", "mcpServerPath", "sharedFolderPath" }
                }
            },
            new
            {
                name = "close_sandbox",
                description = "Close Windows Sandbox",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        timeoutMs = new { type = "integer", description = "Default: 10000" }
                    }
                }
            },
            new
            {
                name = "list_sandbox_apps",
                description = "List executables in sandbox app folder",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        appFolder = new { type = "string", description = "Default: C:\\App" }
                    }
                }
            },
            // System Tools
            new
            {
                name = "get_capabilities",
                description = "Get server capabilities (sandbox, OS, features)",
                inputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new
            {
                name = "get_dpi_info",
                description = "Get DPI/scaling info for coordinate normalization",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        windowTitle = new { type = "string", description = "For per-monitor DPI" }
                    }
                }
            },
            // Event Tools
            new
            {
                name = "subscribe_to_events",
                description = "Subscribe to UI events (window_opened, dialog_shown, structure_changed, property_changed)",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        event_types = new
                        {
                            type = "array",
                            items = new { type = "string" }
                        }
                    },
                    required = new[] { "event_types" }
                }
            },
            new
            {
                name = "get_pending_events",
                description = "Get and clear queued events",
                inputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            // Advanced Element Tools
            new
            {
                name = "find_element_near_anchor",
                description = "Find element relative to anchor (self-healing)",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        anchorElementId = new { type = "string" },
                        anchorAutomationId = new { type = "string" },
                        anchorName = new { type = "string" },
                        targetControlType = new { type = "string" },
                        targetNamePattern = new { type = "string" },
                        targetAutomationIdPattern = new { type = "string" },
                        searchDirection = new { type = "string", description = "siblings, children, parent_children" },
                        maxDistance = new { type = "integer", description = "Default: 10" }
                    }
                }
            },
            new
            {
                name = "mark_for_expansion",
                description = "Mark element for progressive disclosure in get_ui_tree",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementKey = new { type = "string", description = "AutomationId or Name" },
                        elementId = new { type = "string" }
                    }
                }
            },
            new
            {
                name = "clear_expansion_marks",
                description = "Clear progressive disclosure marks",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementKey = new { type = "string", description = "Specific element, or omit for all" }
                    }
                }
            },
            new
            {
                name = "relocate_element",
                description = "Re-find stale element",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string" },
                        automationId = new { type = "string" },
                        name = new { type = "string" },
                        className = new { type = "string" },
                        controlType = new { type = "string" }
                    }
                }
            },
            new
            {
                name = "check_element_stale",
                description = "Check if cached element is stale",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        elementId = new { type = "string" }
                    },
                    required = new[] { "elementId" }
                }
            },
            // Cache Tools
            new
            {
                name = "get_cache_stats",
                description = "Get tree cache hit rate and age",
                inputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new
            {
                name = "invalidate_cache",
                description = "Invalidate UI tree cache",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        reset_stats = new { type = "boolean" }
                    }
                }
            },
            // Confirmation Tools
            new
            {
                name = "confirm_action",
                description = "Request confirmation for destructive action. Returns token valid 60s.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        action = new { type = "string", description = "close_app, force_close, send_keys_dangerous, custom" },
                        description = new { type = "string" },
                        target = new { type = "string" },
                        parameters = new { type = "object" }
                    },
                    required = new[] { "action", "description" }
                }
            },
            new
            {
                name = "execute_confirmed_action",
                description = "Execute confirmed action with token",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        confirmationToken = new { type = "string" }
                    },
                    required = new[] { "confirmationToken" }
                }
            },
            // Script Execution
            new
            {
                name = "run_script",
                description = "Execute multiple commands sequentially. Steps can reference previous results via $stepId.result.path",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        script = new
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
                                            id = new { type = "string", description = "Step ID for result reference" },
                                            tool = new { type = "string" },
                                            args = new { type = "object" },
                                            delay_after_ms = new { type = "integer" }
                                        },
                                        required = new[] { "tool" }
                                    }
                                },
                                options = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        stop_on_error = new { type = "boolean", description = "Default: true" },
                                        default_delay_ms = new { type = "integer" },
                                        timeout_ms = new { type = "integer", description = "Default: 60000" }
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
