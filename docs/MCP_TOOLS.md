# MCP Tool Reference

Complete reference for all MCP tools provided by Rhombus.WinFormsMcp.

## Table of Contents

- [Process Management](#process-management)
- [Element Discovery](#element-discovery)
- [UI Interaction](#ui-interaction)
- [Input Injection](#input-injection)
- [UI Tree & Observation](#ui-tree--observation)
- [State Change Detection](#state-change-detection)
- [Self-Healing](#self-healing)
- [Progressive Disclosure](#progressive-disclosure)
- [Event System](#event-system)
- [Performance & Caching](#performance--caching)
- [DPI & Coordinates](#dpi--coordinates)
- [Confirmation Flow](#confirmation-flow)
- [Sandbox Tools](#sandbox-tools)

---

## Process Management

### `launch_app`

Start a new Windows application.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| path | string | Yes | Path to executable |
| arguments | string | No | Command line arguments |
| workingDirectory | string | No | Working directory |

**Returns:** `{ success, pid, processName, execution_time_ms }`

### `attach_to_process`

Connect to a running process.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| pid | integer | No | Process ID |
| processName | string | No | Process name (if PID not provided) |

**Returns:** `{ success, pid, processName, mainWindowTitle }`

### `close_app`

Terminate an application gracefully or forcefully.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| pid | integer | Yes | Process ID to close |
| force | boolean | No | Force kill (default: false) |

**Returns:** `{ success, closed, forced }`

---

## Element Discovery

### `find_element`

Locate UI elements by various properties. Supports regex patterns.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| automationId | string | No | Exact AutomationId match |
| automationIdPattern | string | No | Regex pattern for AutomationId |
| name | string | No | Exact Name match |
| namePattern | string | No | Regex pattern for Name |
| className | string | No | ClassName |
| controlType | string | No | ControlType filter |
| windowTitle | string | No | Window to search in |
| pid | integer | No | Process ID filter |
| timeoutMs | integer | No | Timeout (default: 5000) |

**Returns:** `{ success, elementId, automationId, name, controlType, bounds, matched_by }`

### `find_element_near_anchor`

Find elements relative to a known anchor element. Useful for self-healing selectors.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| anchorElementId | string | No | Cached element ID of anchor |
| anchorAutomationId | string | No | AutomationId to find anchor |
| anchorName | string | No | Name to find anchor |
| targetControlType | string | No | ControlType of target |
| targetNamePattern | string | No | Regex for target name |
| targetAutomationIdPattern | string | No | Regex for target AutomationId |
| searchDirection | string | No | "siblings", "children", or "parent_children" |
| maxDistance | integer | No | Max elements to search (default: 10) |

**Returns:** `{ success, elementId, distance_from_anchor, matched_by }`

### `list_elements`

List all UI elements in a window for debugging.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| windowTitle | string | Yes | Window to list elements from |
| maxDepth | integer | No | Maximum depth (default: 3) |

**Returns:** Array of element info with AutomationId, Name, ClassName, ControlType, bounds

---

## UI Interaction

### `click_element`

Click on a cached element.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| elementPath | string | Yes | Element ID or path |
| doubleClick | boolean | No | Double-click (default: false) |

**Returns:** `{ success, clicked_at }`

### `click_by_automation_id`

Find and click an element in one operation.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| automationId | string | Yes | AutomationId to find and click |
| windowTitle | string | No | Window to search in |
| doubleClick | boolean | No | Double-click (default: false) |

**Returns:** `{ success, elementId }`

### `type_text`

Enter text into a text field.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| elementPath | string | Yes | Element ID |
| text | string | Yes | Text to type |
| clearFirst | boolean | No | Clear before typing (default: false) |

**Returns:** `{ success }`

### `send_keys`

Send keyboard input to focused element.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| keys | string | Yes | Keys to send (e.g., "{ENTER}", "^c") |

**Returns:** `{ success }`

---

## Input Injection

### `mouse_click`

Click at screen coordinates.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| x | integer | Yes | X coordinate |
| y | integer | Yes | Y coordinate |
| doubleClick | boolean | No | Double-click (default: false) |

### `mouse_drag`

Drag from one point to another.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| x1 | integer | Yes | Start X |
| y1 | integer | Yes | Start Y |
| x2 | integer | Yes | End X |
| y2 | integer | Yes | End Y |
| steps | integer | No | Intermediate points (default: 20) |

**Performance Note:** Keep waypoint count low. Each waypoint has ~100ms overhead. For straight drags, FlaUI interpolates smoothly between just start/end points.

### `touch_tap`

Simulate touch tap at coordinates.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| x | integer | Yes | X coordinate |
| y | integer | Yes | Y coordinate |

### `touch_drag`

Simulate touch drag gesture.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| x1 | integer | Yes | Start X |
| y1 | integer | Yes | Start Y |
| x2 | integer | Yes | End X |
| y2 | integer | Yes | End Y |
| steps | integer | No | Intermediate points (default: 10) |

### `pen_stroke`

Simulate pen stroke with pressure.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| x1 | integer | Yes | Start X |
| y1 | integer | Yes | Start Y |
| x2 | integer | Yes | End X |
| y2 | integer | Yes | End Y |
| pressure | integer | No | Pen pressure 0-1024 (default: 512) |
| steps | integer | No | Points (default: 20) |
| eraser | boolean | No | Use eraser end (default: false) |

### `pinch_zoom`

Simulate two-finger pinch gesture.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| centerX | integer | Yes | Center X |
| centerY | integer | Yes | Center Y |
| startDistance | integer | Yes | Initial finger distance |
| endDistance | integer | Yes | Final finger distance |
| steps | integer | No | Animation steps (default: 20) |

---

## UI Tree & Observation

### `get_ui_tree`

Get hierarchical UI tree with configurable depth.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| windowTitle | string | No | Window to get tree from |
| maxDepth | integer | No | Max depth (default: 3) |
| maxTokenBudget | integer | No | Token limit (default: 5000) |
| includeInvisible | boolean | No | Include offscreen elements |
| skipInternalParts | boolean | No | Skip PART_* elements (default: true) |

**Returns:** XML tree with metadata (dpi_scale_factor, token_count, element_count, cache_hit)

### `check_element_state`

Get detailed state of an element.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| elementId | string | No | Cached element ID |
| automationId | string | No | AutomationId to find |
| windowTitle | string | No | Window to search |

**Returns:** Full element state including IsEnabled, Value, ToggleState, IsSelected, RangeValue, bounds, etc.

### `expand_collapse`

Toggle expand/collapse pattern elements (TreeView, ComboBox).

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| elementId | string | No | Cached element ID |
| automationId | string | No | AutomationId to find |
| expand | boolean | Yes | true = expand, false = collapse |

### `scroll`

Scroll scrollable containers.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| elementId | string | No | Cached element ID |
| automationId | string | No | AutomationId to find |
| direction | string | Yes | "up", "down", "left", "right" |
| amount | string | No | "small" or "large" (default: small) |

---

## State Change Detection

### `capture_ui_snapshot`

Capture current UI state for later comparison.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| snapshotId | string | Yes | Unique ID for this snapshot |
| windowTitle | string | No | Window to capture |

**Returns:** `{ success, snapshotId, hash, element_count, timestamp }`

### `compare_ui_snapshots`

Compare two snapshots to detect changes.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| beforeSnapshotId | string | Yes | ID of "before" snapshot |
| afterSnapshotId | string | No | ID of "after" snapshot (defaults to current state) |

**Returns:** `{ state_changed, added_count, removed_count, modified_count, diff_summary }`

**Usage Pattern:**
```
1. capture_ui_snapshot(snapshotId="before_click")
2. click_element(...)
3. compare_ui_snapshots(beforeSnapshotId="before_click")
```

---

## Self-Healing

### `check_element_stale`

Check if a cached element reference is still valid.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| elementId | string | Yes | Cached element ID |

**Returns:** `{ is_stale, reason, recommendation }`

### `relocate_element`

Re-find a stale element using original search criteria.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| elementId | string | No | Original cached element ID |
| automationId | string | No | AutomationId to search |
| name | string | No | Name to search |
| className | string | No | ClassName to search |
| controlType | string | No | ControlType for heuristic matching |

**Returns:** `{ success, relocated, new_element_id, old_element_id, matched_by }`

---

## Progressive Disclosure

### `mark_for_expansion`

Mark an element for deeper tree exploration on next get_ui_tree call.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| elementKey | string | No | AutomationId or Name to mark |
| elementId | string | No | Cached element ID |

**Returns:** `{ success, element_key, total_marked }`

### `clear_expansion_marks`

Clear progressive disclosure marks.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| elementKey | string | No | Specific element (omit for all) |

**Returns:** `{ success, cleared_count, remaining_marked }`

---

## Event System

### `subscribe_to_events`

Subscribe to UI events for asynchronous monitoring.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| event_types | array | Yes | Types: "window_opened", "dialog_shown", "structure_changed", "property_changed" |

**Returns:** `{ success, subscribed_to, queue_max_size }`

### `get_pending_events`

Retrieve queued events and clear the queue.

**Returns:** `{ success, events[], events_count, events_dropped }`

---

## Performance & Caching

### `get_cache_stats`

Get tree cache statistics.

**Returns:**
```json
{
  "cache_hits": 42,
  "cache_misses": 8,
  "hit_rate_percent": "84.0%",
  "is_dirty": false,
  "has_cached_data": true,
  "cache_age_ms": 1200,
  "max_cache_age_ms": 5000
}
```

### `invalidate_cache`

Invalidate the UI tree cache.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| reset_stats | boolean | No | Also reset statistics (default: false) |

---

## DPI & Coordinates

### `get_dpi_info`

Get DPI scaling information for coordinate normalization.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| windowTitle | string | No | Get DPI for specific window |

**Returns:**
```json
{
  "system_dpi": 144,
  "system_scale_factor": 1.5,
  "window_dpi": 144,
  "window_scale_factor": 1.5,
  "is_per_monitor_aware": true,
  "standard_dpi": 96
}
```

**Note:** All coordinate-based tools accept logical coordinates. Use `get_dpi_info` to understand the current scaling.

---

## Confirmation Flow

### `confirm_action`

Request confirmation for destructive actions.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| action | string | Yes | "close_app", "force_close", "send_keys_dangerous", "custom" |
| description | string | Yes | Human-readable description |
| target | string | No | Target of action |
| parameters | object | No | Parameters for execution |

**Returns:** `{ confirmation_token, expires_in_seconds: 60 }`

### `execute_confirmed_action`

Execute a confirmed action using its token.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| confirmationToken | string | Yes | Token from confirm_action |

---

## Sandbox Tools

### `launch_app_sandboxed`

Start Windows Sandbox with target app and MCP server.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| appPath | string | Yes | Path to application |
| mcpServerPath | string | No | Path to MCP server |
| outputFolder | string | No | Output folder for results |

**Returns:** `{ success, sandbox_pid, communication_path }`

### `close_sandbox`

Gracefully shutdown sandbox VM.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| force | boolean | No | Force kill (default: false) |
| timeoutMs | integer | No | Graceful shutdown timeout |

---

## Capability Detection

### `get_capabilities`

Detect MCP server capabilities.

**Returns:**
```json
{
  "sandbox_available": true,
  "os_version": "Windows 11 10.0 Build 26200",
  "flaui_version": "4.0.0.0",
  "uia_backend": "UIA2",
  "max_depth_supported": 10,
  "token_budget": 5000,
  "features": ["launch_app", "find_element", ...]
}
```

### `get_process_info`

Get process metadata for window targeting.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| windowHandle | string | No | HWND in hex/decimal |
| windowTitle | string | No | Window title (partial match) |

**Returns:** `{ pid, processName, responding, window_state, main_window_title }`

---

## Error Handling

All tools return a consistent structure:

**Success:**
```json
{
  "success": true,
  "execution_time_ms": 42,
  ...result-specific fields...
}
```

**Failure:**
```json
{
  "success": false,
  "error": "Human-readable error message",
  "error_code": "OPTIONAL_CODE",
  "suggestions": ["Recovery suggestion 1", "Recovery suggestion 2"]
}
```

## Common Error Codes

| Code | Description | Recovery |
|------|-------------|----------|
| ELEMENT_NOT_FOUND | Element could not be located | Increase timeout, verify selector |
| ELEMENT_STALE | Cached element no longer valid | Use relocate_element |
| INVALID_TOKEN | Confirmation token invalid/expired | Request new confirmation |
| SANDBOX_NOT_AVAILABLE | Windows Sandbox not available | Check Windows edition |
| TIMEOUT | Operation exceeded timeout | Increase timeoutMs parameter |
