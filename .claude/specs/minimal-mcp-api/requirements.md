# Requirements: Minimal MCP API

## Problem Statement

The current WinForms MCP server exposes 52 tools, consuming ~9,275 tokens just for tool definitions. Many tools overlap in functionality (mouse_click, touch_tap, pen_tap are all "click"), expose implementation details (cache, expansion marks), or are rarely used (events, confirmation flow). This creates:

1. **Token overhead**: ~9K tokens per session just for tool list
2. **Cognitive load**: Agents must choose between similar tools
3. **Maintenance burden**: 52 tools × handlers × tests × docs

## Goals

- Reduce tool count from 52 to ~8-10 orthogonal primitives
- Preserve all essential functionality
- Cut token cost by 60%+
- Simplify agent decision-making

## Non-Goals

- Changing the underlying automation implementation
- Breaking the sandbox architecture
- Changing the MCP protocol itself

---

## User Stories

### US-1: Unified Input Device

**As an** automation agent,
**I want** a single `click` tool with an `input` parameter,
**so that** I don't need to choose between mouse_click, touch_tap, and pen_tap.

**Acceptance Criteria:**
- AC-1.1: `click` accepts `input: mouse | touch | pen` (default: mouse)
- AC-1.2: `right: true` means right-click for mouse, barrel-button for pen
- AC-1.3: `pressure` parameter only applies to pen input
- AC-1.4: `eraser: true` only applies to pen input
- AC-1.5: `hold_ms` enables long-press for any input type
- AC-1.6: `double: true` enables double-click/tap for any input type

### US-2: Unified Drag/Stroke

**As an** automation agent,
**I want** a single `drag` tool for mouse drag, touch drag, and pen stroke,
**so that** I use one tool for all linear motion input.

**Acceptance Criteria:**
- AC-2.1: `drag` accepts `input: mouse | touch | pen`
- AC-2.2: `pressure` parameter applies to pen input for stroke weight
- AC-2.3: `eraser: true` applies to pen input for eraser strokes
- AC-2.4: `path` parameter accepts array of points for complex paths
- AC-2.5: Mouse drag, touch drag, and pen stroke all use this single tool

### US-3: Multi-Touch Gestures

**As an** automation agent,
**I want** a `gesture` tool for multi-finger touch operations,
**so that** I can perform pinch-zoom and rotation.

**Acceptance Criteria:**
- AC-3.1: `gesture` accepts `type: pinch | rotate | custom`
- AC-3.2: Pinch requires `center`, `start_distance`, `end_distance`
- AC-3.3: Rotate requires `center`, `radius`, `start_angle`, `end_angle`
- AC-3.4: Custom accepts `fingers` array for arbitrary multi-touch paths

### US-4: Element Discovery with Tree Support

**As an** automation agent,
**I want** `find` to support recursive tree traversal,
**so that** I don't need separate `get_ui_tree` and `find_element` tools.

**Acceptance Criteria:**
- AC-4.1: `find` with `recursive: true` returns element tree
- AC-4.2: `find` with `at: elementId` scopes search to that subtree
- AC-4.3: `find` with `at: "root"` searches all tracked windows
- AC-4.4: `at: "root"` is a pseudo-element representing the desktop/all windows
- AC-4.5: Replaces: find_element, get_ui_tree, list_elements, mark_for_expansion, clear_expansion_marks

### US-5: Application Lifecycle

**As an** automation agent,
**I want** a single `app` tool for all process operations,
**so that** launch, attach, and close are unified.

**Acceptance Criteria:**
- AC-5.1: `app` with `action: launch` starts application
- AC-5.2: `app` with `action: attach` attaches to running process
- AC-5.3: `app` with `action: close` terminates application
- AC-5.4: `action: info` returns process details
- AC-5.5: Replaces: launch_app, attach_to_process, close_app, get_process_info

### US-6: Interaction Feedback with UI Diff (POST-MIGRATION)

**As an** automation agent,
**I want** interaction responses to include UI changes,
**so that** I immediately see the effect of my action.

**Acceptance Criteria:**
- AC-6.1: `click`, `type`, `drag` responses include `changes` object
- AC-6.2: `changes.added` lists new elements (dialogs, buttons, etc.)
- AC-6.3: `changes.removed` lists disappeared elements
- AC-6.4: `changes.modified` lists changed properties (text, state)
- AC-6.5: Replaces: capture_ui_snapshot, compare_ui_snapshots

> **Note**: UI diff is deferred until after the core 8-tool migration is complete. Initial implementation will return standard responses without automatic diff.

### US-7: Text Input

**As an** automation agent,
**I want** a single `type` tool for text entry and keyboard shortcuts,
**so that** I don't need separate type_text, set_value, send_keys.

**Acceptance Criteria:**
- AC-7.1: `type` with `target` types into that element
- AC-7.2: `type` without `target` sends keys globally (shortcuts)
- AC-7.3: `clear: true` clears field before typing
- AC-7.4: Replaces: type_text, set_value, send_keys

### US-8: Screenshot

**As an** automation agent,
**I want** a simple `screenshot` tool,
**so that** I can capture visual state.

**Acceptance Criteria:**
- AC-8.1: `screenshot` with no params captures active window
- AC-8.2: `target: window_title` captures specific window
- AC-8.3: `target: elementId` captures specific element
- AC-8.4: Returns base64 by default, `file: path` saves to disk

> **Note**: Region capture removed (YAGNI). Use element bounds + client-side crop if needed.

### US-9: Script Batching

**As an** automation agent,
**I want** `script` to batch multiple operations,
**so that** I reduce round-trip overhead.

**Acceptance Criteria:**
- AC-9.1: `script` accepts array of tool calls
- AC-9.2: Steps can reference previous results via `$stepId.result`
- AC-9.3: Stops on first error by default
- AC-9.4: Returns results for all executed steps

---

## Functionality Mapping

### Kept (Consolidated)

| New Tool | Replaces |
|----------|----------|
| `app` | launch_app, attach_to_process, close_app, get_process_info |
| `find` | find_element, get_ui_tree, list_elements, element_exists, wait_for_element, check_element_state, get_property, find_element_near_anchor, mark_for_expansion, clear_expansion_marks, get_element_at_point |
| `click` | click_element, click_by_automation_id, mouse_click, touch_tap, pen_tap |
| `type` | type_text, set_value, send_keys |
| `drag` | drag_drop, mouse_drag, mouse_drag_path, touch_drag, pen_stroke |
| `gesture` | pinch_zoom, rotate, multi_touch_gesture |
| `screenshot` | take_screenshot |
| `script` | run_script |

### Dropped (Made Automatic)

| Old Tool | Disposition |
|----------|-------------|
| get_capabilities | Return in `initialize` response |
| get_dpi_info | Include in window info automatically |
| subscribe_to_events | Drop - agents poll anyway |
| get_pending_events | Drop - agents poll anyway |
| relocate_element | Automatic on stale element access |
| check_element_stale | Automatic - relocate transparently |
| get_cache_stats | Internal - no agent value |
| invalidate_cache | Automatic on interactions |
| confirm_action | Move to agent layer |
| execute_confirmed_action | Move to agent layer |
| get_window_bounds | Include in `find` response for windows |
| focus_window | Automatic before interactions |
| expand_collapse | Use `click` on expander element |
| scroll | Use `drag` or `click` on scrollbar |

### Dropped (Sandbox-Specific)

| Old Tool | Disposition |
|----------|-------------|
| launch_app_sandboxed | Separate sandbox MCP or `app` with `sandbox: true` |
| close_sandbox | Separate sandbox MCP |
| list_sandbox_apps | Separate sandbox MCP |

---

## Resolved Questions

1. **Wait semantics**: Explicit `wait_ms` parameter. Server polls internally, returns as soon as found (doesn't wait full duration). MCP transport has its own timeout too.

2. **Drag paths**: Always use `path: [...]` array. Two-point drag is just `path: [{x, y}, {x, y}]`. Simpler, orthogonal.

3. **Snapshot diff scope**: Entire UI, with smarts. If clicking a button opens a new window, that window must be in the diff even though it's not the target. Agent needs full picture of what changed.

4. **Custom gestures**: Keep `gesture(type: custom, fingers: [...])` for arbitrary multi-finger paths. Useful for complex gestures.

5. **Stale elements**: Auto-relocate transparently but include context. Skip the round trip - attempt re-find automatically and proceed with the action if successful:
   ```json
   // Success - relocated and action completed
   {
     "result": { ... },
     "relocated": {
       "stale_id": "elem_3",
       "new_id": "elem_7",
       "selector": { "automationId": "SaveButton" }
     }
   }

   // Failure - couldn't relocate
   {
     "error": "element_not_found",
     "context": {
       "original_id": "elem_3",
       "selector": { "automationId": "SaveButton" },
       "reason": "No matching element in current UI"
     }
   }
   ```

6. **Sandbox**: Keep current tools. Enhance `app` with `sandbox: true, hotreload: true` flags. Current signal file approach works but is fragile - future enhancement could watch app folder for changes instead.

## Future Enhancements (Out of Scope)

1. **Deploy tool**: Add `deploy` as MCP tool instead of external script. See `../cad` for deploy script patterns. Agents deploying apps into sandbox shouldn't need separate scripts.

---

## Token Estimate

| Component | Current | Proposed | Savings |
|-----------|---------|----------|---------|
| Tool definitions | ~9,275 | ~3,500 | 62% |
| Per-response windows | ~500 | ~75 | 85% |
| **20-call workflow** | ~19,275 | ~5,000 | 74% |
