# MCP Tools Inventory

Complete documentation of all 43 MCP tools for WinForms automation.

**Modes:**
- **Sandbox Mode** - MCP server runs inside Windows Sandbox, agent connects via TCP
- **Direct Mode** - MCP server runs on host, agent connects via stdio or TCP

**Legend:**
- ✅ Works in this mode
- ❌ Not applicable
- ⚠️ Works with caveats

---

## Summary Table

| # | Tool | Sandbox | Direct | Category |
|---|------|---------|--------|----------|
| 1 | find_element | ✅ | ✅ | Element Discovery |
| 2 | click_element | ✅ | ✅ | Interaction |
| 3 | type_text | ✅ | ✅ | Interaction |
| 4 | set_value | ✅ | ✅ | Interaction |
| 5 | drag_drop | ✅ | ✅ | Interaction |
| 6 | close_app | ✅ | ✅ | Process |
| 7 | wait_for_element | ✅ | ✅ | Element Discovery |
| 8 | launch_app | ⚠️ | ✅ | Process |
| 9 | take_screenshot | ⚠️ | ✅ | Capture |
| 10 | touch_tap | ✅ | ✅ | Touch Input |
| 11 | touch_drag | ✅ | ✅ | Touch Input |
| 12 | pinch_zoom | ✅ | ✅ | Touch Input |
| 13 | pen_stroke | ✅ | ✅ | Pen Input |
| 14 | pen_tap | ✅ | ✅ | Pen Input |
| 15 | mouse_drag | ✅ | ✅ | Mouse Input |
| 16 | mouse_drag_path | ✅ | ✅ | Mouse Input |
| 17 | mouse_click | ✅ | ✅ | Mouse Input |
| 18 | get_window_bounds | ✅ | ✅ | Window |
| 19 | focus_window | ✅ | ✅ | Window |
| 20 | click_by_automation_id | ✅ | ✅ | Interaction |
| 21 | list_elements | ✅ | ✅ | Element Discovery |
| 22 | get_ui_tree | ✅ | ✅ | Element Discovery |
| 23 | expand_collapse | ✅ | ✅ | Interaction |
| 24 | scroll | ✅ | ✅ | Interaction |
| 25 | check_element_state | ✅ | ✅ | Element Query |
| 26 | capture_ui_snapshot | ✅ | ✅ | Capture |
| 27 | compare_ui_snapshots | ✅ | ✅ | Capture |
| 28 | launch_app_sandboxed | ❌ | ✅ | Sandbox Control |
| 29 | close_sandbox | ❌ | ✅ | Sandbox Control |
| 30 | get_capabilities | ✅ | ✅ | Info |
| 31 | get_dpi_info | ✅ | ✅ | Info |
| 32 | get_process_info | ✅ | ✅ | Process |
| 33 | subscribe_to_events | ✅ | ✅ | Events |
| 34 | get_pending_events | ✅ | ✅ | Events |
| 35 | find_element_near_anchor | ✅ | ✅ | Element Discovery |
| 36 | mark_for_expansion | ✅ | ✅ | Tree Cache |
| 37 | clear_expansion_marks | ✅ | ✅ | Tree Cache |
| 38 | relocate_element | ✅ | ✅ | Element Discovery |
| 39 | check_element_stale | ✅ | ✅ | Element Query |
| 40 | get_cache_stats | ✅ | ✅ | Tree Cache |
| 41 | invalidate_cache | ✅ | ✅ | Tree Cache |
| 42 | confirm_action | ✅ | ✅ | Safety |
| 43 | execute_confirmed_action | ✅ | ✅ | Safety |

---

## Category: Process Management

### launch_app
**Description:** Launch a WinForms application.

**Parameters:**
- `path` (required): Path to the executable
- `arguments`: Command-line arguments
- `workingDirectory`: Working directory
- `idleTimeoutMs`: Wait timeout for app to become idle (default: 5000)

**Returns:** `{ success, pid, processName, windows: [...] }`

**Sandbox Mode:** ⚠️ Agent must use sandbox paths:
- Pass: `path: "C:\\App\\MyApp.exe"` (sandbox path)
- App binaries must be in mapped folder (`C:\TransportTest\App\` → `C:\App\`)

**Direct Mode:** Path is on host filesystem, no translation needed.

**Example (Sandbox):**
```
Host: C:\TransportTest\App\MyApp.exe
Agent calls: launch_app { path: "C:\\App\\MyApp.exe" }
```

---

### close_app
**Description:** Close a running application by process ID.

**Parameters:**
- `pid` (required): Process ID
- `force`: Force kill if true (default: false)
- `closeTimeoutMs`: Graceful close timeout (default: 5000)

**Returns:** `{ success, windows: [...] }`

**Both Modes:** Works identically. PID must be from same environment. Response includes remaining windows.

---

### get_process_info
**Description:** Get process metadata from window handle or title.

**Parameters:**
- `windowHandle` or `windowTitle` (one required): Target window

**Returns:** `{ pid, processName, responding, windowState, windows: [...] }`

**Both Modes:** Works identically. Response includes all windows for targeting.

---

## Category: Element Discovery

### find_element
**Description:** Find a UI element by various criteria. Supports regex patterns.

**Parameters:**
- `automationId`: Exact AutomationId
- `automationIdPattern`: Regex pattern for AutomationId
- `name`: Element name
- `namePattern`: Regex pattern for name
- `className`: ClassName
- `controlType`: ControlType (Button, Edit, etc.)
- `parent`: Parent element path
- `pollIntervalMs`: Search interval (default: 100)

**Returns:** `{ success, elementId, name, automationId, controlType, matched_by, execution_time_ms }`

**Both Modes:** Works identically. Returns cached `elementId` for subsequent operations.

---

### wait_for_element
**Description:** Wait for an element to appear.

**Parameters:**
- `automationId` (required): AutomationId to wait for
- `parent`: Parent element path
- `timeoutMs`: Wait timeout (default: 10000)
- `pollIntervalMs`: Poll interval (default: 100)

**Returns:** `{ success, elementId, ... }`

**Both Modes:** Works identically.

---

### list_elements
**Description:** Enumerate all UI elements in a window for debugging.

**Parameters:**
- `windowHandle` or `windowTitle` (one required): Target window
- `maxDepth`: Tree depth limit (default: 3)

**Returns:** `{ success, elementCount, elements: [...], windows: [...] }`

**Both Modes:** Works identically. Response includes all windows for targeting.

---

### get_ui_tree
**Description:** Get hierarchical XML of UI tree with pruning and token budget.

**Parameters:**
- `windowHandle` or `windowTitle`: Target window (uses desktop if omitted)
- `maxDepth`: Depth limit (default: 3)
- `maxTokenBudget`: Token limit (default: 5000)
- `includeInvisible`: Include hidden elements (default: false)
- `skipInternalParts`: Skip PART_* WPF internals (default: true)

**Returns:** `{ tree: "<xml>...", windows: [...] }`

**Both Modes:** Works identically. Response includes all windows for targeting.

---

### find_element_near_anchor
**Description:** Find element relative to anchor for self-healing selectors.

**Parameters:**
- `anchorElementId` or `anchorAutomationId` or `anchorName`
- `targetControlType`: Target's ControlType
- `targetNamePattern`: Regex for target name
- `targetAutomationIdPattern`: Regex for target AutomationId
- `searchDirection`: 'siblings', 'children', 'parent_children'
- `maxDistance`: Max elements to search (default: 10)

**Returns:** `{ success, elementId, ... }`

**Both Modes:** Works identically.

---

### relocate_element
**Description:** Re-find a stale element using original criteria.

**Parameters:**
- `elementId`: Original stale element ID
- `automationId`, `name`, `className`, `controlType`: Search criteria

**Returns:** `{ success, newElementId }`

**Both Modes:** Works identically.

---

## Category: Interaction

### click_element
**Description:** Click on a UI element.

**Parameters:**
- `elementPath` (required): Element ID from find_element
- `doubleClick`: Double-click if true

**Returns:** `{ success, windows: [...] }`

**Both Modes:** Works identically. Response includes all windows for next action.

---

### click_by_automation_id
**Description:** Find and click by AutomationId in one operation.

**Parameters:**
- `automationId` (required): AutomationId to find
- `windowHandle` or `windowTitle`: Window to search (optional)
- `doubleClick`: Double-click if true

**Returns:** `{ success, windows: [...] }`

**Both Modes:** Works identically. Response includes all windows for next action.

---

### type_text
**Description:** Type text into a text field.

**Parameters:**
- `elementPath` (required): Element ID
- `text` (required): Text to type
- `clearFirst`: Clear before typing
- `clearDelayMs`: Delay after clear (default: 100)

**Returns:** `{ success, windows: [...] }`

**Both Modes:** Works identically. Response includes all windows for next action.

---

### set_value
**Description:** Set input value by select-all and type.

**Parameters:**
- `elementPath` (required): Element ID
- `value` (required): Value to set
- `selectAllDelayMs`: Delay after select-all (default: 50)

**Returns:** `{ success, windows: [...] }`

**Both Modes:** Works identically. Response includes all windows for next action.

---

### drag_drop
**Description:** Drag element onto another element.

**Parameters:**
- `sourceElementPath` (required): Source element ID
- `targetElementPath` (required): Target element ID
- `dragSetupDelayMs`: Pre-drag delay (default: 100)
- `dropDelayMs`: Pre-drop delay (default: 200)

**Returns:** `{ success, windows: [...] }`

**Both Modes:** Works identically. Response includes all windows for next action.

---

### expand_collapse
**Description:** Expand or collapse tree node/menu.

**Parameters:**
- `elementId` or `automationId` + `windowHandle`/`windowTitle`
- `expand` (required): true=expand, false=collapse
- `uiUpdateDelayMs`: Post-action delay (default: 100)

**Returns:** `{ success, windows: [...] }`

**Both Modes:** Works identically. Response includes all windows for next action.

---

### scroll
**Description:** Scroll a scrollable container.

**Parameters:**
- `elementId` or `automationId` + `windowHandle`/`windowTitle`
- `direction` (required): Up, Down, Left, Right
- `amount`: SmallDecrement or LargeDecrement
- `uiUpdateDelayMs`: Post-scroll delay (default: 100)

**Returns:** `{ success, windows: [...] }`

**Both Modes:** Works identically. Response includes all windows for next action.

---

## Category: Mouse Input

### mouse_click
**Description:** Click at window-relative coordinates.

**Parameters:**
- `windowHandle` or `windowTitle` (one required): Target window
- `x`, `y` (required): Window-relative coordinates (0,0 = top-left of client area)
- `doubleClick`: Double-click if true
- `delayMs`: Pre-click delay (default: 0)

**Returns:** `{ success, windows: [...] }`

**Both Modes:** MCP server translates to screen coordinates. Response includes all windows for next action.

---

### mouse_drag
**Description:** Drag from one point to another within a window.

**Parameters:**
- `windowHandle` or `windowTitle` (one required): Target window
- `x1`, `y1` (required): Start coordinates (window-relative)
- `x2`, `y2` (required): End coordinates (window-relative)
- `steps`: Intermediate points (default: 10)
- `delayMs`: Delay between steps (default: 0)

**Returns:** `{ success, windows: [...] }`

**Both Modes:** MCP server translates to screen coordinates. Response includes all windows for next action.

---

### mouse_drag_path
**Description:** Drag through multiple waypoints within a window for curves/shapes.

**Parameters:**
- `windowHandle` or `windowTitle` (one required): Target window
- `points` (required): Array of {x, y} waypoints (2-1000, window-relative)
- `stepsPerSegment`: Interpolation steps (default: 1)
- `delayMs`: Delay between steps (default: 0)

**Returns:** `{ success, windows: [...] }`

**Performance Note:** Each waypoint has ~100ms overhead. Use minimal points.

**Both Modes:** MCP server translates to screen coordinates. Response includes all windows for next action.

---

## Category: Touch Input

### touch_tap
**Description:** Simulate touch tap at window-relative coordinates.

**Parameters:**
- `windowHandle` or `windowTitle` (one required): Target window
- `x`, `y` (required): Window-relative coordinates
- `holdMs`: Hold time for long-press (default: 0)

**Returns:** `{ success, windows: [...] }`

**Both Modes:** MCP server translates to screen coordinates. Response includes all windows for next action.

---

### touch_drag
**Description:** Simulate touch drag within a window.

**Parameters:**
- `windowHandle` or `windowTitle` (one required): Target window
- `x1`, `y1`, `x2`, `y2` (required): Start/end coordinates (window-relative)
- `steps`: Intermediate points (default: 10)
- `delayMs`: Delay between steps (default: 0)

**Returns:** `{ success, windows: [...] }`

**Both Modes:** MCP server translates to screen coordinates. Response includes all windows for next action.

---

### pinch_zoom
**Description:** Simulate two-finger pinch gesture within a window.

**Parameters:**
- `windowHandle` or `windowTitle` (one required): Target window
- `centerX`, `centerY` (required): Center of gesture (window-relative)
- `startDistance`, `endDistance` (required): Finger distance (larger end = zoom in)
- `steps`: Animation steps (default: 20)
- `delayMs`: Delay between steps (default: 0)

**Returns:** `{ success, windows: [...] }`

**Both Modes:** MCP server translates to screen coordinates. Response includes all windows for next action.

---

## Category: Pen Input

### pen_tap
**Description:** Simulate pen tap at window-relative coordinates.

**Parameters:**
- `windowHandle` or `windowTitle` (one required): Target window
- `x`, `y` (required): Window-relative coordinates
- `pressure`: 0-1024 (default: 512)
- `holdMs`: Hold time (default: 0)

**Returns:** `{ success, windows: [...] }`

**Both Modes:** MCP server translates to screen coordinates. Response includes all windows for next action.

---

### pen_stroke
**Description:** Simulate pen stroke with pressure within a window.

**Parameters:**
- `windowHandle` or `windowTitle` (one required): Target window
- `x1`, `y1`, `x2`, `y2` (required): Start/end coordinates (window-relative)
- `steps`: Intermediate points (default: 20)
- `pressure`: 0-1024 (default: 512)
- `eraser`: Use eraser end (default: false)
- `delayMs`: Delay between steps (default: 0)

**Returns:** `{ success, windows: [...] }`

**Both Modes:** MCP server translates to screen coordinates. Response includes all windows for next action.

---

## Category: Window Management

### get_window_bounds
**Description:** Get window position and size.

**Parameters:**
- `windowHandle` or `windowTitle` (one required): Target window

**Returns:** `{ success, x, y, width, height, windows: [...] }`

**Both Modes:** Returns bounds relative to that environment's screen. Response includes all windows for targeting.

---

### focus_window
**Description:** Bring window to foreground.

**Parameters:**
- `windowHandle` or `windowTitle` (one required): Target window

**Returns:** `{ success, windows: [...] }`

**Both Modes:** Works identically. Response includes all windows for targeting.

---

## Category: Capture

### take_screenshot
**Description:** Capture screenshot to file.

**Parameters:**
- `outputPath` (required): File path for screenshot
- `elementPath`: Specific element to capture (optional)
- `windowHandle` or `windowTitle`: Window to capture (optional)

**Returns:** `{ success, path, windows: [...] }`

**Sandbox Mode:** ⚠️ Agent must use shared folder path explicitly:
- Pass: `outputPath: "C:\\Shared\\screenshot.png"` (sandbox path)
- Read from host: `C:\TransportTest\Shared\screenshot.png`
- Agent is responsible for path translation

**Direct Mode:** Path is on host filesystem, no translation needed.

**Example (Sandbox):**
```
Agent calls:  take_screenshot { outputPath: "C:\\Shared\\screen.png" }
Server saves: C:\Shared\screen.png (inside sandbox)
Agent reads:  C:\TransportTest\Shared\screen.png (on host)
```

---

### capture_ui_snapshot
**Description:** Capture UI tree state for comparison.

**Parameters:**
- `windowHandle` or `windowTitle`: Window to snapshot (desktop if omitted)
- `snapshotId` (required): Identifier for this snapshot

**Returns:** `{ success, snapshotId, elementCount, windows: [...] }`

**Both Modes:** Works identically. Snapshots stored in server memory. Response includes all windows for targeting.

---

### compare_ui_snapshots
**Description:** Compare two snapshots to detect changes.

**Parameters:**
- `beforeSnapshotId` (required): "Before" snapshot ID
- `afterSnapshotId`: "After" snapshot ID (auto-captures if omitted)
- `windowHandle` or `windowTitle`: Window for auto-capture

**Returns:** `{ added, removed, modified, windows: [...] }`

**Both Modes:** Works identically. Response includes all windows for targeting.

---

## Category: Element Query

### check_element_state
**Description:** Get detailed element state.

**Parameters:**
- `elementId` or `automationId` + `windowHandle`/`windowTitle`

**Returns:** `{ isEnabled, isVisible, value, toggleState, selectionState, rangeValue, windows: [...] }`

**Both Modes:** Works identically. Response includes all windows for targeting.

---

### check_element_stale
**Description:** Check if cached element reference is valid.

**Parameters:**
- `elementId` (required): Cached element ID

**Returns:** `{ isStale }`

**Both Modes:** Works identically.

---

## Category: Tree Cache

### mark_for_expansion
**Description:** Mark element for progressive disclosure in get_ui_tree.

**Parameters:**
- `elementKey`: AutomationId or Name
- `elementId`: Alternative to elementKey

**Returns:** `{ success }`

**Both Modes:** Works identically.

---

### clear_expansion_marks
**Description:** Clear expansion marks.

**Parameters:**
- `elementKey`: Specific element (clears all if omitted)

**Returns:** `{ success }`

**Both Modes:** Works identically.

---

### get_cache_stats
**Description:** Get cache statistics for monitoring.

**Parameters:** None

**Returns:** `{ hitRate, cacheAge, ... }`

**Both Modes:** Works identically.

---

### invalidate_cache
**Description:** Clear UI tree cache.

**Parameters:**
- `reset_stats`: Also reset statistics (default: false)

**Returns:** `{ success }`

**Both Modes:** Works identically.

---

## Category: Events

### subscribe_to_events
**Description:** Subscribe to UI events (queued, max 10).

**Parameters:**
- `event_types` (required): Array of: window_opened, dialog_shown, structure_changed, property_changed

**Returns:** `{ success }`

**Both Modes:** Works identically.

---

### get_pending_events
**Description:** Retrieve and clear pending events.

**Parameters:** None

**Returns:** `{ events: [...] }`

**Both Modes:** Works identically.

---

## Category: Sandbox Control

### launch_app_sandboxed
**Description:** Launch app inside Windows Sandbox (creates new sandbox).

**Parameters:**
- `appPath` (required): App directory (mapped to C:\App)
- `appExe` (required): Executable name
- `mcpServerPath` (required): MCP server path (mapped to C:\MCP)
- `sharedFolderPath` (required): Shared folder (mapped to C:\Shared)
- `outputFolderPath`: Output folder (mapped to C:\Output)
- `bootTimeoutMs`: Boot timeout (default: 60000)

**Returns:** `{ success, sandboxPid, mcpPort }`

**Sandbox Mode:** ❌ N/A - already in sandbox

**Direct Mode:** ✅ Creates new sandbox from host

---

### close_sandbox
**Description:** Close running Windows Sandbox.

**Parameters:**
- `timeoutMs`: Graceful shutdown timeout (default: 10000)

**Returns:** `{ success }`

**Sandbox Mode:** ❌ N/A - can't close from inside

**Direct Mode:** ✅ Closes sandbox from host

---

### get_capabilities
**Description:** Get server capabilities and environment info.

**Parameters:** None

**Returns:** `{ sandboxAvailable, osVersion, features }`

**Both Modes:** Works identically. Reports environment capabilities.

---

### get_dpi_info
**Description:** Get DPI scaling info for coordinate accuracy.

**Parameters:**
- `windowHandle` or `windowTitle`: Target window (optional)

**Returns:** `{ systemDpi, scaleFactor, perMonitorAware, windows: [...] }`

**Both Modes:** Returns DPI for that environment's display. Response includes all windows for targeting.

---

## Category: Safety

### confirm_action
**Description:** Request confirmation for destructive actions.

**Parameters:**
- `action` (required): 'close_app', 'force_close', 'send_keys_dangerous', 'custom'
- `description` (required): Human-readable explanation
- `target`: Target description
- `parameters`: Parameters for the action

**Returns:** `{ confirmationToken, expiresIn }`

**Both Modes:** Works identically. Token valid 60 seconds.

---

### execute_confirmed_action
**Description:** Execute confirmed action with token.

**Parameters:**
- `confirmationToken` (required): Token from confirm_action

**Returns:** Action-specific result

**Both Modes:** Works identically.

---

## Communication Protocol

### Sandbox Mode (TCP)

```
Host Agent                    Sandbox MCP Server
    |                               |
    |------ TCP Connect ----------->| (tcp_ip:tcp_port from signal)
    |                               |
    |------ initialize ------------>|
    |<----- result -----------------|
    |                               |
    |------ notifications/init ---->| (no response)
    |                               |
    |------ tools/call ------------>|
    |<----- result -----------------|
    |           ...                 |
```

**Discovery:**
1. Read `C:\TransportTest\Shared\mcp-ready.signal`
2. Parse JSON for `tcp_ip` and `tcp_port`
3. If `server_pid: null`, create `server.trigger` and wait

**Hot Reload:**
1. Copy new binaries to `C:\TransportTest\Server\`
2. Create `C:\TransportTest\Shared\server.trigger`
3. Wait for `mcp-ready.signal` update
4. Reconnect TCP with new endpoint

### Direct Mode (stdio)

```
Agent Process                 MCP Server Process
    |                               |
    |==== spawn process ============|
    |                               |
    |------ initialize (stdin) ---->|
    |<----- result (stdout) --------|
    |                               |
    |------ notifications/init ---->| (no response)
    |                               |
    |------ tools/call ------------>|
    |<----- result -----------------|
    |           ...                 |
```

**Launch:**
```powershell
$proc = Start-Process "Rhombus.WinFormsMcp.Server.exe" -PassThru -RedirectStandardInput -RedirectStandardOutput
```

### Direct Mode (TCP on localhost)

```powershell
# Start server in TCP mode
Start-Process "Rhombus.WinFormsMcp.Server.exe" -ArgumentList "--tcp", "9999"

# Connect from agent
$client = New-Object System.Net.Sockets.TcpClient("localhost", 9999)
```

---

## Path Translation (Sandbox Mode)

| Host Path | Sandbox Path |
|-----------|--------------|
| `C:\TransportTest\Server\` | `C:\Server\` (read-only) |
| `C:\TransportTest\App\` | `C:\App\` (read-only) |
| `C:\TransportTest\Shared\` | `C:\Shared\` (read-write) |
| `C:\TransportTest\DotNet\` | `C:\DotNet\` (read-only) |

**Screenshots:** Save to `C:\Shared\` to retrieve on host.
