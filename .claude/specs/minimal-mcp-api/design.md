# Design: Minimal MCP API

## Overview

This design consolidates 52 tools into 8 orthogonal primitives while preserving all essential functionality. The architecture prioritizes:
- **Token efficiency**: 62% reduction in tool definitions
- **Orthogonality**: Each tool has one clear purpose with composable parameters
- **Transparency**: Auto-recovery with explicit context (relocations, UI diffs)

---

## Tool Schemas

### 1. `app` - Application Lifecycle

```json
{
  "name": "app",
  "description": "Manage application lifecycle: launch, attach, close, or get info",
  "inputSchema": {
    "type": "object",
    "properties": {
      "action": {
        "type": "string",
        "enum": ["launch", "attach", "close", "info"],
        "description": "Operation to perform"
      },
      "path": {
        "type": "string",
        "description": "Executable path (launch only)"
      },
      "args": {
        "type": "string",
        "description": "Command line arguments (launch only)"
      },
      "pid": {
        "type": "integer",
        "description": "Process ID (attach/close/info)"
      },
      "title": {
        "type": "string",
        "description": "Window title pattern (attach only)"
      },
      "sandbox": {
        "type": "boolean",
        "description": "Launch in Windows Sandbox (launch only)"
      },
      "hotreload": {
        "type": "boolean",
        "description": "Enable hot-reload watching (sandbox only)"
      },
      "wait_ms": {
        "type": "integer",
        "description": "Max wait for window to appear (launch/attach)"
      }
    },
    "required": ["action"]
  }
}
```

**Replaces**: `launch_app`, `attach_to_process`, `close_app`, `get_process_info`, `launch_app_sandboxed`

### 2. `find` - Element Discovery

```json
{
  "name": "find",
  "description": "Find UI elements. Use at:'root' for all windows, at:elementId for subtree",
  "inputSchema": {
    "type": "object",
    "properties": {
      "name": { "type": "string", "description": "Element name pattern" },
      "automationId": { "type": "string", "description": "Automation ID" },
      "className": { "type": "string", "description": "Control class name" },
      "controlType": { "type": "string", "description": "Control type (Button, Edit, etc.)" },
      "at": { "type": "string", "description": "Element ID or 'root' for all windows" },
      "recursive": { "type": "boolean", "description": "Return full tree structure" },
      "depth": { "type": "integer", "description": "Max tree depth (with recursive)" },
      "point": {
        "type": "object",
        "properties": { "x": { "type": "integer" }, "y": { "type": "integer" } },
        "description": "Find element at screen coordinates"
      },
      "near": {
        "type": "object",
        "properties": {
          "element": { "type": "string" },
          "direction": { "type": "string", "enum": ["above", "below", "left", "right"] }
        },
        "description": "Find element near anchor"
      },
      "wait_ms": { "type": "integer", "description": "Max wait for element to appear" }
    }
  }
}
```

**Replaces**: `find_element`, `get_ui_tree`, `list_elements`, `element_exists`, `wait_for_element`, `check_element_state`, `get_property`, `find_element_near_anchor`, `mark_for_expansion`, `clear_expansion_marks`, `get_element_at_point`

**The `root` pseudo-element**:
- `at: "root"` represents the desktop/all tracked windows
- `find(at: "root")` → list all top-level windows
- `find(at: "root", recursive: true)` → full UI tree
- `find(at: "root", name: "Save")` → find "Save" across all windows
- Makes API fully orthogonal - no special "omit at" behavior

### 3. `click` - Unified Click/Tap

```json
{
  "name": "click",
  "description": "Click, tap, or pen-tap at element or coordinates",
  "inputSchema": {
    "type": "object",
    "properties": {
      "target": { "type": "string", "description": "Element ID to click" },
      "x": { "type": "integer", "description": "Screen X (if no target)" },
      "y": { "type": "integer", "description": "Screen Y (if no target)" },
      "input": {
        "type": "string",
        "enum": ["mouse", "touch", "pen"],
        "default": "mouse",
        "description": "Input device type"
      },
      "right": { "type": "boolean", "description": "Right-click (mouse) or barrel button (pen)" },
      "double": { "type": "boolean", "description": "Double-click/tap" },
      "hold_ms": { "type": "integer", "description": "Hold duration for long-press" },
      "pressure": { "type": "integer", "description": "Pen pressure 0-1024 (pen only)" },
      "eraser": { "type": "boolean", "description": "Use eraser tip (pen only)" }
    }
  }
}
```

**Replaces**: `click_element`, `click_by_automation_id`, `mouse_click`, `touch_tap`, `pen_tap`

### 4. `type` - Text Input

```json
{
  "name": "type",
  "description": "Type text into element or send keystrokes globally",
  "inputSchema": {
    "type": "object",
    "properties": {
      "text": { "type": "string", "description": "Text to type or key sequence" },
      "target": { "type": "string", "description": "Element ID (omit for global keys)" },
      "clear": { "type": "boolean", "description": "Clear field before typing" },
      "keys": { "type": "boolean", "description": "Interpret as key codes (Ctrl+S, Enter, etc.)" }
    },
    "required": ["text"]
  }
}
```

**Replaces**: `type_text`, `set_value`, `send_keys`

### 5. `drag` - Unified Drag/Stroke

```json
{
  "name": "drag",
  "description": "Drag, swipe, or pen stroke along a path",
  "inputSchema": {
    "type": "object",
    "properties": {
      "path": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "x": { "type": "integer" },
            "y": { "type": "integer" },
            "pressure": { "type": "integer", "description": "Pen pressure at this point" }
          },
          "required": ["x", "y"]
        },
        "description": "Array of points [{x,y}, ...]. Min 2 points."
      },
      "input": {
        "type": "string",
        "enum": ["mouse", "touch", "pen"],
        "default": "mouse"
      },
      "button": {
        "type": "string",
        "enum": ["left", "right", "middle"],
        "default": "left",
        "description": "Mouse button (mouse only)"
      },
      "eraser": { "type": "boolean", "description": "Use eraser tip (pen only)" },
      "duration_ms": { "type": "integer", "description": "Total drag duration" }
    },
    "required": ["path"]
  }
}
```

**Replaces**: `drag_drop`, `mouse_drag`, `mouse_drag_path`, `touch_drag`, `pen_stroke`

### 6. `gesture` - Multi-Touch Gestures

```json
{
  "name": "gesture",
  "description": "Multi-finger touch gestures: pinch, rotate, or custom paths",
  "inputSchema": {
    "type": "object",
    "properties": {
      "type": {
        "type": "string",
        "enum": ["pinch", "rotate", "custom"],
        "description": "Gesture type"
      },
      "center": {
        "type": "object",
        "properties": { "x": { "type": "integer" }, "y": { "type": "integer" } },
        "description": "Center point for pinch/rotate"
      },
      "start_distance": { "type": "integer", "description": "Starting finger distance (pinch)" },
      "end_distance": { "type": "integer", "description": "Ending finger distance (pinch)" },
      "start_angle": { "type": "number", "description": "Starting angle in degrees (rotate)" },
      "end_angle": { "type": "number", "description": "Ending angle in degrees (rotate)" },
      "radius": { "type": "integer", "description": "Rotation radius (rotate)" },
      "fingers": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "path": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": { "x": { "type": "integer" }, "y": { "type": "integer" } }
              }
            }
          }
        },
        "description": "Custom finger paths (custom only)"
      },
      "duration_ms": { "type": "integer", "description": "Gesture duration" }
    },
    "required": ["type"]
  }
}
```

**Replaces**: `pinch_zoom`, `rotate_gesture`, `multi_touch_gesture`

### 7. `screenshot` - Capture Visual State

```json
{
  "name": "screenshot",
  "description": "Capture screenshot of window or element",
  "inputSchema": {
    "type": "object",
    "properties": {
      "target": { "type": "string", "description": "Element ID or window title (omit for active window)" },
      "file": { "type": "string", "description": "Save to file path instead of returning base64" }
    }
  }
}
```

**Replaces**: `take_screenshot`

### 8. `script` - Batch Operations

```json
{
  "name": "script",
  "description": "Execute multiple operations in sequence with variable interpolation",
  "inputSchema": {
    "type": "object",
    "properties": {
      "steps": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "id": { "type": "string", "description": "Step ID for result references" },
            "tool": { "type": "string", "description": "Tool name to call" },
            "args": { "type": "object", "description": "Tool arguments, can use $stepId.path refs" }
          },
          "required": ["tool", "args"]
        }
      },
      "stop_on_error": { "type": "boolean", "default": true }
    },
    "required": ["steps"]
  }
}
```

**Replaces**: `run_script`

---

## Response Structures

### Standard Success Response

```json
{
  "success": true,
  "result": { /* tool-specific result */ },
  "windows": [ /* scoped to tracked PIDs */ ],
  "changes": { /* UI diff from before action */ }
}
```

### UI Diff Structure (`changes`)

Included in responses for `click`, `type`, `drag`, `gesture`:

```json
{
  "changes": {
    "added": [
      { "id": "elem_12", "type": "Window", "name": "Save As", "automationId": "SaveDialog" }
    ],
    "removed": [
      { "id": "elem_5", "type": "Button", "name": "Loading..." }
    ],
    "modified": [
      {
        "id": "elem_3",
        "property": "text",
        "from": "Submit",
        "to": "Submitting..."
      }
    ],
    "scope": "full_ui"
  }
}
```

### Relocation Context

When an element is stale but successfully relocated:

```json
{
  "success": true,
  "result": { /* action result */ },
  "relocated": {
    "stale_id": "elem_3",
    "new_id": "elem_7",
    "selector": { "automationId": "SaveButton" }
  }
}
```

### Error Response

```json
{
  "success": false,
  "error": {
    "code": "element_not_found",
    "message": "Could not relocate stale element",
    "context": {
      "original_id": "elem_3",
      "selector": { "automationId": "SaveButton" },
      "reason": "No matching element in current UI"
    }
  },
  "windows": [ /* current window state for recovery */ ]
}
```

---

## Handler Architecture

### New Handler Structure

```
Handlers/
├── AppHandler.cs          # app tool
├── FindHandler.cs         # find tool
├── ClickHandler.cs        # click tool (delegates to input type)
├── TypeHandler.cs         # type tool
├── DragHandler.cs         # drag tool (delegates to input type)
├── GestureHandler.cs      # gesture tool
├── ScreenshotHandler.cs   # screenshot tool
└── ScriptHandler.cs       # script tool (orchestrates other handlers)
```

### Handler Base Enhancements

```csharp
public abstract class HandlerBase
{
    // Existing
    protected ToolResponse Success(object result);
    protected ToolResponse Fail(string error);

    // New: Response with UI diff
    protected ToolResponse SuccessWithDiff(object result, UiDiff changes);

    // New: Response with relocation context
    protected ToolResponse SuccessRelocated(object result, RelocationInfo relocated);

    // New: Get element with auto-relocate
    protected (AutomationElement? element, RelocationInfo? relocated)
        GetElementWithRelocate(string elementId);
}
```

### Input Dispatch Pattern

For `click` and `drag`, dispatch based on `input` parameter:

```csharp
public class ClickHandler : HandlerBase
{
    public override ToolResponse Handle(JsonElement args)
    {
        var input = GetEnumArg<InputType>(args, "input", InputType.Mouse);

        // Capture UI state before
        var beforeState = CaptureUiState();

        var result = input switch
        {
            InputType.Mouse => ExecuteMouseClick(args),
            InputType.Touch => ExecuteTouchTap(args),
            InputType.Pen => ExecutePenTap(args),
            _ => Fail($"Unknown input type: {input}")
        };

        // Capture UI state after and compute diff
        var afterState = CaptureUiState();
        var diff = ComputeDiff(beforeState, afterState);

        return SuccessWithDiff(result, diff);
    }
}
```

---

## UI Diff Implementation (POST-MIGRATION)

> **Deferred**: UI diff will be implemented after the core 8-tool migration is complete. Initial implementation returns standard responses without automatic diff.

### Capture Strategy (Future)

1. **Before action**: Snapshot visible elements (id, type, name, bounds, key properties)
2. **After action**: Re-snapshot same scope
3. **Compare**: Set operations on element IDs + property diffs

### Scope Rules (Future)

- Always capture **all tracked PID windows** (not just target window)
- Include **new windows** that appear (dialogs, popups)
- Track **removed windows** (closed dialogs)
- For modified: track `text`, `isEnabled`, `isVisible`, `isSelected`

---

## Migration Strategy

### Phase 1: Add New Handlers (Non-Breaking)

1. Create new handler files alongside existing
2. Register new tools in ToolDefinitions
3. Both old and new tools work simultaneously

### Phase 2: Deprecation Period

1. Mark old tools as deprecated in descriptions
2. Log warning when old tools are called
3. Document migration in CHANGELOG

### Phase 3: Remove Old Tools

1. Remove deprecated handlers
2. Remove from ToolDefinitions
3. Update tests to use new API

---

## Tool Definition Token Counts

| Tool | Schema Size (chars) | Estimated Tokens |
|------|---------------------|------------------|
| app | 450 | 112 |
| find | 600 | 150 |
| click | 500 | 125 |
| type | 300 | 75 |
| drag | 550 | 137 |
| gesture | 600 | 150 |
| screenshot | 250 | 62 |
| script | 400 | 100 |
| **Total** | **3,650** | **911** |

Current 52 tools: ~37,000 chars (~9,250 tokens)
**Savings: 90% on tool definitions**

---

## Open Design Questions

None - all questions resolved in requirements phase.

---

## Test Strategy

### Unit Tests

- Each handler tested in isolation
- Mock InputFacade for input tests
- Mock SessionManager for state tests

### Integration Tests

- UI diff accuracy (before/after comparisons)
- Relocation behavior (element staleness scenarios)
- Script variable interpolation

### E2E Tests (Sandbox)

- Full workflow: launch → find → interact → verify
- Multi-window scenarios (dialog opens)
- Input type switching (mouse → touch → pen)
