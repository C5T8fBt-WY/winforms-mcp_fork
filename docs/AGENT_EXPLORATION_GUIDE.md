# Agent Exploration Guide

A guide for AI agents to effectively explore and interact with Windows desktop applications using the WinForms MCP server.

## Core Concepts

### The OODA Loop

Effective UI automation follows the OODA loop pattern:

1. **Observe**: Gather UI state via `get_ui_tree`
2. **Orient**: Analyze the tree to understand current context
3. **Decide**: Plan the next action based on your goal
4. **Act**: Execute the action via interaction tools

After acting, return to Observe to verify the action succeeded.

### Element Identity

Elements can be identified by multiple properties:
- **AutomationId**: Stable developer-assigned ID (preferred)
- **Name**: Visible text label
- **ControlType**: Button, TextBox, ListItem, etc.
- **ClassName**: Windows class name

Prefer `AutomationId` when available as it's most stable across UI changes.

---

## Exploration Patterns

### Pattern 1: Progressive Disclosure

Start with a shallow tree and expand only where needed:

```
1. get_ui_tree(maxDepth=2)
   → See top-level structure

2. Identify target container (e.g., "MainPanel")

3. mark_for_expansion(elementKey="MainPanel")

4. get_ui_tree(maxDepth=5)
   → MainPanel expanded to depth 5, others remain at depth 2
```

**Benefits**: Reduces token usage, faster responses, focused exploration.

### Pattern 2: Anchor-Based Navigation

Find elements relative to stable landmarks:

```
1. Locate stable anchor (e.g., a Label that doesn't move)
   → anchor = find_element(name="Username:")

2. Find target near anchor
   → field = find_element_near_anchor(
       anchorElementId=anchor.elementId,
       targetControlType="Edit",
       searchDirection="siblings"
     )
```

**Benefits**: More resilient to UI layout changes than absolute coordinates.

### Pattern 3: State Change Detection

Verify actions succeeded by detecting UI changes:

```
1. capture_ui_snapshot(snapshotId="before_click")

2. click_element(elementPath="btnSubmit")

3. compare_ui_snapshots(beforeSnapshotId="before_click")
   → {
       stateChanged: true,
       diffSummary: "Added: Dialog[ConfirmDialog]. Modified: Button[btnSubmit] (IsEnabled=false)."
     }
```

**Benefits**: Explicit feedback on action success/failure.

---

## Self-Healing Recovery

### Handling Stale References

When UI changes (e.g., app restarted), cached element references become stale:

```
1. click_element(elementPath="elem_5")
   → Error: "Element stale"

2. check_element_stale(elementId="elem_5")
   → { is_stale: true, reason: "Element no longer in tree" }

3. relocate_element(automationId="btnSubmit")
   → { success: true, new_element_id: "elem_12" }

4. click_element(elementPath="elem_12")
   → Success
```

### Recovery Strategies

| Error | Recovery |
|-------|----------|
| Element stale | Use `relocate_element` with original criteria |
| Element not found | Increase timeout, check if UI state changed |
| Click failed | Check if element enabled, check if occluded |

---

## Interaction Workflow

### Clicking Elements

```
# Option 1: Two-step (find then click)
elem = find_element(automationId="btnSave")
click_element(elementPath=elem.elementId)

# Option 2: One-step
click_by_automation_id(automationId="btnSave")
```

### Filling Forms

```
# Type into focused field
find_element(automationId="txtUsername")
type_text(elementPath=elem.elementId, text="admin")

# Or use set_value for replacing content
set_value(elementPath=elem.elementId, value="newvalue")
```

### Sending Special Keys

```
# Press Enter to submit
send_keys(keys="{ENTER}")

# Ctrl+S to save
send_keys(keys="^s")

# Tab to next field
send_keys(keys="{TAB}")
```

Key format: `{ENTER}`, `{TAB}`, `{ESC}`, `{F1}`, `^` (Ctrl), `+` (Shift), `%` (Alt)

---

## Coordinate-Based Actions

### Window-Relative Coordinates

All coordinate tools support window-relative positioning:

```
# Click at position (100, 50) within the app window
mouse_click(x=100, y=50, windowTitle="MyApp")
```

Without `windowTitle` or `windowHandle`, coordinates are screen-absolute.

### Touch and Pen Input

```
# Touch tap
touch_tap(x=200, y=300, windowTitle="MyApp")

# Touch drag (swipe)
touch_drag(x1=100, y1=300, x2=400, y2=300, windowTitle="MyApp")

# Pen stroke with pressure
pen_stroke(x1=50, y1=50, x2=200, y2=200, pressure=512, windowTitle="InkCanvas")
```

---

## Event-Driven Workflows

### Subscribing to Events

```
1. subscribe_to_events(event_types=["window_opened", "dialog_shown"])

2. click_element(elementPath="btnOpen")

3. get_pending_events()
   → {
       events: [
         { type: "dialog_shown", timestamp: "...", title: "Open File" }
       ]
     }
```

**Use cases**: Wait for dialogs, detect modals, track navigation.

---

## Scripted Automation

### Batch Execution

Execute multiple steps without round-trip overhead:

```json
{
  "script": {
    "steps": [
      { "id": "launch", "tool": "launch_app", "args": { "path": "C:\\MyApp.exe" }, "delay_after_ms": 2000 },
      { "id": "find", "tool": "find_element", "args": { "automationId": "btnLogin" } },
      { "tool": "click_element", "args": { "elementPath": "$find.result.elementId" } }
    ],
    "options": {
      "stop_on_error": true,
      "timeout_ms": 60000
    }
  }
}
```

**Variable interpolation**: Use `$stepId.result.path` to reference previous results.

---

## Best Practices

### Performance

1. **Start shallow**: Use `maxDepth=2` for initial exploration
2. **Cache stats**: Check `get_cache_stats` to monitor hit rates
3. **Invalidate strategically**: Call `invalidate_cache` after major UI changes

### Reliability

1. **Verify actions**: Use `compare_ui_snapshots` after interactions
2. **Handle staleness**: Implement retry with `relocate_element`
3. **Wait for elements**: Use `wait_for_element` for dynamic UI

### Debugging

1. **List elements**: Use `list_elements` to see all elements in a window
2. **Check state**: Use `check_element_state` for detailed property info
3. **Take screenshots**: Use `take_screenshot` for visual debugging

---

## Common Workflows

### Login Flow

```
1. launch_app(path="C:\\MyApp.exe")
2. wait_for_element(automationId="txtUsername", timeoutMs=10000)
3. find_element(automationId="txtUsername") → elem1
4. type_text(elementPath=elem1, text="admin")
5. find_element(automationId="txtPassword") → elem2
6. type_text(elementPath=elem2, text="password123")
7. click_by_automation_id(automationId="btnLogin")
8. wait_for_element(automationId="MainDashboard", timeoutMs=5000)
```

### File Dialog Navigation

```
1. click_by_automation_id(automationId="btnOpen")
2. wait_for_element(automationId="FileDialog", timeoutMs=5000)
3. find_element(automationId="FileNameBox") → fileBox
4. set_value(elementPath=fileBox, value="C:\\Documents\\report.txt")
5. click_by_automation_id(automationId="btnDialogOK")
```

### Tab Navigation

```
1. get_ui_tree(maxDepth=3)
2. find_element(controlType="Tab", name="Settings") → settingsTab
3. click_element(elementPath=settingsTab.elementId)
4. capture_ui_snapshot(snapshotId="after_tab_switch")
5. get_ui_tree(maxDepth=4)
   → See Settings panel contents
```

---

## Error Recovery Checklist

When something fails:

1. **Element not found?**
   - Increase `timeoutMs`
   - Check if window title changed
   - Use `list_elements` to see what's available

2. **Click didn't work?**
   - Check `check_element_state` for `IsEnabled`
   - Check if another window is blocking
   - Try `focus_window` first

3. **Stale element?**
   - Use `relocate_element` with original search criteria
   - Or re-run `find_element`

4. **Wrong window?**
   - Use `windowTitle` parameter to scope searches
   - Use `focus_window` to bring target to front

5. **Coordinates off?**
   - Use `get_dpi_info` to check scaling
   - Use `get_window_bounds` to verify window position
   - Prefer element-based clicks over coordinates
