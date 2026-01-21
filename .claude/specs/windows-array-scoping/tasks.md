# Tasks: Windows Array Scoping

> **⚠️ ARCHIVED**: This spec has been superseded by `unified-refactor/`. All tasks from this spec were consolidated and completed in the unified refactoring plan.

Implementation checklist for process-scoped window filtering to reduce response size (measured in character count).

**Key definitions:**
- **Token reduction** = Character count reduction in JSON responses (not LLM tokens)
- **Stale process** = A tracked PID where the process has exited externally

---

## Phase 1: Core Infrastructure

### 1. Add process tracking to SessionManager

- [ ] 1.1. Add `_trackedProcessIds` HashSet to `src/Rhombus.WinFormsMcp.Server/Program.cs` (SessionManager class)
  - Type: `HashSet<int>`
  - Purpose: Track PIDs for window scoping

- [ ] 1.2. Implement `TrackProcess(int pid)` method
  - Adds PID to the tracked set
  - Called by `launch_app` and `attach_to_process`

- [ ] 1.3. Implement `UntrackProcess(int pid)` method
  - Removes PID from the tracked set
  - Calls `CleanupStaleProcesses()` to handle externally terminated processes
  - Called by `close_app`

- [ ] 1.4. Implement `GetTrackedProcessIds()` method
  - Returns `IReadOnlySet<int>` of valid tracked PIDs
  - Calls `CleanupStaleProcesses()` before returning
  - Filters out stale PIDs automatically

- [ ] 1.5. Implement `HasTrackedProcesses` property
  - Returns `bool` indicating if any processes are tracked

- [ ] 1.6. Implement `CleanupStaleProcesses()` private method
  - Uses `Process.GetProcessById(pid)` with try/catch for `ArgumentException`
  - Checks `process.HasExited` for valid PIDs
  - Removes stale PIDs from the tracked set

### 2. Add process-filtered window enumeration to WindowManager

- [ ] 2.1. Add `GetWindowThreadProcessId` P/Invoke to `src/Rhombus.WinFormsMcp.Server/Automation/WindowManager.cs`
  - Already may exist; verify or add if missing
  - Signature: `uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId)`

- [ ] 2.2. Implement `GetWindowProcessId(IntPtr hwnd)` method
  - Returns `int?` for the process ID owning a window
  - Returns `null` for invalid handles

- [ ] 2.3. Implement `GetProcessIdForWindow(string? windowHandle, string? windowTitle)` method
  - Uses `FindWindow()` to resolve handle/title to WindowInfo
  - Returns `int?` using `GetWindowProcessId()`

- [ ] 2.4. Implement `GetWindowsByProcessIds(IReadOnlySet<int> processIds)` method
  - Enumerates windows filtered by PID set
  - Returns `List<WindowInfo>` containing only matching windows
  - Returns empty list if processIds is empty (not all windows)

---

## Phase 2: Window Scope Context Model

### 3. Create WindowScopeContext model

- [ ] 3.1. Create `src/Rhombus.WinFormsMcp.Server/Models/WindowScopeContext.cs`
  - Namespace: `Rhombus.WinFormsMcp.Server.Models`

- [ ] 3.2. Add properties:
  - `HashSet<int> ProcessIds` - Explicit PIDs to include
  - `bool IncludeAllWindows` - Override flag (from US-6's `includeAllWindows` parameter)
  - `bool IsErrorContext` - Error expansion flag
  - `string? WindowHandle` - From request
  - `string? WindowTitle` - From request
  - `string? ElementId` - From request (elementId or elementPath)

- [ ] 3.3. Add `ScopeDescription` computed property
  - Returns `"all"` if `IncludeAllWindows` or `IsErrorContext`
  - Returns `"process"` if `ProcessIds.Count > 0`
  - Returns `"tracked"` otherwise

---

## Phase 3: ToolResponse Extensions

### 4. Add scoped response methods to ToolResponse

- [ ] 4.1. Add `WindowScope` property to `src/Rhombus.WinFormsMcp.Server/ToolResponse.cs`
  - Type: `string?`
  - JSON: `"windowScope"` with `JsonIgnore(Condition = WhenWritingNull)`
  - Values: `"all"`, `"process"`, `"tracked"`

- [ ] 4.2. Implement `OkScoped()` static factory method
  - Parameters: `object? result`, `WindowManager`, `SessionManager`, `WindowScopeContext`
  - Calls `ResolveWindows()` to get filtered window list
  - Sets `WindowScope` from `scope.ScopeDescription`

- [ ] 4.3. Implement `FailWithContext()` static factory method
  - Parameters: `string error`, `WindowManager`
  - Always returns all windows (error expansion per AC-4.1/4.2/4.3)
  - Sets `WindowScope = "all"`

- [ ] 4.4. Implement `ResolveWindows()` private static method
  - Logic:
    1. If `IsErrorContext` or `IncludeAllWindows`: return `GetAllWindows()`
    2. Build PID set from `scope.ProcessIds`
    3. Add PID from `WindowHandle`/`WindowTitle` via `GetProcessIdForWindow()`
    4. If PID set empty, add all tracked PIDs from `SessionManager`
    5. If still empty, return `GetAllWindows()` (discovery mode)
    6. Otherwise return `GetWindowsByProcessIds(pids)`

---

## Phase 4: HandlerBase Extensions

### 5. Add scoped response helpers to HandlerBase

- [ ] 5.1. Add `GetScopeContext(JsonElement args)` method to `src/Rhombus.WinFormsMcp.Server/Handlers/HandlerBase.cs`
  - Extracts `includeAllWindows`, `windowHandle`, `windowTitle`, `elementId`/`elementPath`
  - Returns `WindowScopeContext`

- [ ] 5.2. Add `SuccessScoped(WindowScopeContext, params properties)` method
  - Creates result dictionary from properties
  - Returns `ToolResponse.OkScoped().ToJsonElement()`

- [ ] 5.3. Add `ErrorWithContext(string message)` method
  - Returns `ToolResponse.FailWithContext().ToJsonElement()`
  - Provides full window context for error recovery

---

## Phase 5: Update Handlers for Process Tracking

### 6. Update ProcessHandlers for tracking

- [ ] 6.1. Update `launch_app` in `src/Rhombus.WinFormsMcp.Server/Handlers/ProcessHandlers.cs`
  - After successful launch, call `Session.TrackProcess(process.Id)`
  - Use `SuccessScoped()` with scope including launched PID
  - Implements AC-1.1, AC-1.2, AC-1.3

- [ ] 6.2. Update `attach_to_process` handler
  - Call `Session.TrackProcess(pid)` for the attached process
  - Use `SuccessScoped()` with scope including attached PID
  - Implements AC-5.1

- [ ] 6.3. Update `close_app` handler
  - Call `Session.UntrackProcess(pid)` after closing
  - Use `SuccessScoped()` (will use remaining tracked PIDs)
  - Implements AC-5.4

### 7. Update discovery tools to always return all windows

- [ ] 7.1. Verify `list_sandbox_apps` in `SandboxHandlers.cs` returns all windows
  - Should use `Success()` not `SuccessScoped()` (discovery tool)
  - Implements AC-3.1

- [ ] 7.2. Verify `get_capabilities` in `AdvancedHandlers.cs` returns all windows
  - Should use `Success()` not `SuccessScoped()` (discovery tool)
  - Implements AC-3.2

---

## Phase 6: Update Remaining Handlers for Scoping

### 8. Update ElementHandlers

- [ ] 8.1. Update `find_element` in `src/Rhombus.WinFormsMcp.Server/Handlers/ElementHandlers.cs`
  - Extract scope context from args
  - Add element's window PID to scope
  - Use `SuccessScoped()` / `ErrorWithContext()`
  - Implements AC-2.1, AC-2.2

- [ ] 8.2. Update `click_element` handler
  - Use `SuccessScoped()` with scope from cached element's window

- [ ] 8.3. Update `type_text` handler
  - Use `SuccessScoped()` with scope from element's window

- [ ] 8.4. Update `set_value` handler
  - Use `SuccessScoped()` with scope from element's window

- [ ] 8.5. Update `click_by_automation_id` handler
  - Use `SuccessScoped()` with window title scope

- [ ] 8.6. Update `list_elements` handler
  - Use `SuccessScoped()` with window title scope

- [ ] 8.7. Update `find_element_near_anchor` handler
  - Use `SuccessScoped()` with anchor element's scope

### 9. Update InputHandlers

- [ ] 9.1. Update `mouse_click` in `src/Rhombus.WinFormsMcp.Server/Handlers/InputHandlers.cs`
  - Extract scope context (windowHandle/Title)
  - Use `SuccessScoped()` / `ErrorWithContext()`

- [ ] 9.2. Update `mouse_drag` handler
  - Use scoped responses

- [ ] 9.3. Update `mouse_drag_path` handler
  - Use scoped responses

- [ ] 9.4. Update `drag_drop` handler
  - Use scoped responses

- [ ] 9.5. Update `send_keys` handler
  - Use scoped responses

### 10. Update TouchPenHandlers

- [ ] 10.1. Update `touch_tap` in `src/Rhombus.WinFormsMcp.Server/Handlers/TouchPenHandlers.cs`
  - Use scoped responses

- [ ] 10.2. Update `touch_drag` handler
  - Use scoped responses

- [ ] 10.3. Update `pinch_zoom` handler
  - Use scoped responses

- [ ] 10.4. Update `pen_tap` handler
  - Use scoped responses

- [ ] 10.5. Update `pen_stroke` handler
  - Use scoped responses

### 11. Update ObservationHandlers

- [ ] 11.1. Update `get_ui_tree` in `src/Rhombus.WinFormsMcp.Server/Handlers/ObservationHandlers.cs`
  - Use scoped responses with window title scope

- [ ] 11.2. Update `expand_collapse` handler
  - Use scoped responses

- [ ] 11.3. Update `scroll` handler
  - Use scoped responses

- [ ] 11.4. Update `get_element_at_point` handler
  - Use scoped responses

- [ ] 11.5. Update `capture_ui_snapshot` handler
  - Use scoped responses

- [ ] 11.6. Update `compare_ui_snapshots` handler
  - Use scoped responses

### 12. Update ValidationHandlers

- [ ] 12.1. Update `wait_for_element` in `src/Rhombus.WinFormsMcp.Server/Handlers/ValidationHandlers.cs`
  - Use scoped responses

- [ ] 12.2. Update `check_element_state` handler
  - Use scoped responses

### 13. Update WindowHandlers

- [ ] 13.1. Update `get_window_bounds` in `src/Rhombus.WinFormsMcp.Server/Handlers/WindowHandlers.cs`
  - Use scoped responses

- [ ] 13.2. Update `focus_window` handler
  - Use scoped responses

### 14. Update ScreenshotHandlers

- [ ] 14.1. Update `take_screenshot` in `src/Rhombus.WinFormsMcp.Server/Handlers/ScreenshotHandlers.cs`
  - Use scoped responses with window scope

### 15. Update AdvancedHandlers

- [ ] 15.1. Update element-related tools in `src/Rhombus.WinFormsMcp.Server/Handlers/AdvancedHandlers.cs`
  - `mark_for_expansion`, `clear_expansion_marks`
  - `relocate_element`, `check_element_stale`
  - Use scoped responses where applicable

- [ ] 15.2. Update event tools
  - `subscribe_to_events`, `get_pending_events`
  - Use scoped responses

- [ ] 15.3. Update cache tools
  - `get_cache_stats`, `invalidate_cache`
  - Use scoped responses

- [ ] 15.4. Update DPI tool
  - `get_dpi_info`
  - Use scoped responses with window scope

- [ ] 15.5. Verify `confirm_action` and `execute_confirmed_action`
  - Use appropriate scoped responses based on action type

---

## Phase 7: Add includeAllWindows Parameter

### 16. Add parameter to tool definitions

- [ ] 16.1. Update `src/Rhombus.WinFormsMcp.Server/Protocol/ToolDefinitions.cs` (or equivalent)
  - Add `includeAllWindows` boolean parameter to all tools that return windows
  - Default: `false`
  - Description: "When true, return all desktop windows regardless of scoping"
  - Implements AC-6.1, AC-6.2, AC-6.3 (US-6's includeAllWindows parameter)

- [ ] 16.2. List of tools needing the parameter:
  - Process tools: `launch_app`, `close_app`, `attach_to_process`, `get_process_info`
  - Element tools: `find_element`, `click_element`, `type_text`, `set_value`, `click_by_automation_id`, `list_elements`, `find_element_near_anchor`
  - Input tools: `mouse_click`, `mouse_drag`, `mouse_drag_path`, `drag_drop`, `send_keys`
  - Touch/pen tools: `touch_tap`, `touch_drag`, `pinch_zoom`, `pen_tap`, `pen_stroke`
  - Observation tools: `get_ui_tree`, `expand_collapse`, `scroll`, `get_element_at_point`, `capture_ui_snapshot`, `compare_ui_snapshots`
  - Validation tools: `wait_for_element`, `check_element_state`
  - Window tools: `get_window_bounds`, `focus_window`
  - Screenshot tools: `take_screenshot`
  - Advanced tools: DPI, events, cache tools

---

## Phase 8: Error Expansion

### 17. Verify error responses expand to all windows

- [ ] 17.1. Verify `FailWithPartialMatches` in `ToolResponse.cs` returns all windows
  - Used for "window not found" with similar titles
  - Implements AC-4.1

- [ ] 17.2. Verify `FailWithMultipleMatches` returns all windows
  - Used when multiple windows match title
  - Implements AC-4.3

- [ ] 17.3. Ensure all `Error()` calls in handlers use `ErrorWithContext()`
  - Audit each handler for error paths
  - Implements AC-4.2

---

## Phase 9: Documentation

### 18. Update documentation

- [ ] 18.1. Update `docs/MCP_TOOLS.md`
  - Document `includeAllWindows` parameter
  - Document `windowScope` response field
  - Document scoping behavior for each tool category

- [ ] 18.2. Update tool descriptions in schema definitions
  - Add note about window scoping to affected tools

---

## Phase 10: Integration Tests

### 19. Write integration tests (requires Windows)

- [ ] 19.1. Test single-app workflow
  - Launch TestApp
  - Verify `windows` contains only TestApp window
  - Click button
  - Verify still scoped to TestApp
  - Close TestApp
  - Verify `windows` reflects removal

- [ ] 19.2. Test multi-app workflow (AC-5.2, AC-5.3)
  - Launch TestApp
  - Launch second app (e.g., Calculator)
  - Verify `windows` contains both
  - Close second app
  - Verify `windows` contains only TestApp

- [ ] 19.3. Test error expansion (AC-4.1, AC-4.2)
  - Launch TestApp
  - Request non-existent window
  - Verify error response has ALL windows

- [ ] 19.4. Test `includeAllWindows` override (AC-6.2)
  - Launch TestApp
  - Call `mouse_click` with `includeAllWindows: true`
  - Verify response has ALL windows

- [ ] 19.5. Test stale process cleanup
  - Launch TestApp
  - Kill process externally (taskkill)
  - Call another tool
  - Verify stale PID removed from tracking

- [ ] 19.6. Measure character count reduction
  - Compare response sizes before/after scoping
  - Target: 60-80% reduction for single-app workflows (NFR-3)

---

## Verification Checklist

After all tasks complete:

- [ ] `SessionManager` tracks PIDs from `launch_app` and `attach_to_process`
- [ ] `SessionManager` removes PIDs on `close_app` and cleans up stale processes
- [ ] `WindowManager` can filter windows by PID set
- [ ] `WindowScopeContext` model captures scoping decisions
- [ ] `ToolResponse.OkScoped()` returns filtered windows based on context
- [ ] `ToolResponse.FailWithContext()` always returns all windows
- [ ] All handler error paths use `ErrorWithContext()`
- [ ] `includeAllWindows` parameter added to tool schemas (AC-3.3 references US-6)
- [ ] `windowScope` field appears in all responses
- [ ] Discovery tools (`list_sandbox_apps`, `get_capabilities`) return all windows
- [ ] Documentation updated in `docs/MCP_TOOLS.md`
- [ ] Character count reduction measured at 60%+ for single-app workflows
