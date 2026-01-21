# Tasks: Minimal MCP API

## Overview

Implementation checklist for consolidating 52 tools into 8 orthogonal primitives. Tasks follow the migration strategy: add new handlers alongside old, then deprecate, then remove.

---

## Phase 1: Foundation (Non-Breaking)

### 1.1 Create New Handler Files

- [ ] Create `Handlers/AppHandler.cs` - empty shell with handler registration
- [ ] Create `Handlers/FindHandler.cs` - empty shell
- [ ] Create `Handlers/ClickHandler.cs` - empty shell
- [ ] Create `Handlers/TypeHandler.cs` - empty shell
- [ ] Create `Handlers/DragHandler.cs` - empty shell
- [ ] Create `Handlers/GestureHandler.cs` - empty shell
- [ ] Create `Handlers/ScreenshotHandler.cs` - empty shell
- [ ] Create `Handlers/ScriptHandler.cs` - empty shell (or reuse existing ScriptRunner)

### 1.2 Create Tool Definitions

- [ ] Add `app` tool definition to `Protocol/ToolDefinitions.cs`
- [ ] Add `find` tool definition with `at: "root"` support
- [ ] Add `click` tool definition with `input` parameter
- [ ] Add `type` tool definition
- [ ] Add `drag` tool definition with `path` array
- [ ] Add `gesture` tool definition
- [ ] Add `screenshot` tool definition (no region)
- [ ] Add `script` tool definition (or keep existing run_script)

### 1.3 Register New Handlers

- [ ] Update `McpServer.cs` or handler registry to include new handlers
- [ ] Verify new tools appear in `tools/list` response
- [ ] Verify old tools still work (non-breaking)

---

## Phase 2: Implement Core Tools

### 2.1 Implement `app` Handler

- [ ] Implement `action: launch` - delegate to existing launch logic
- [ ] Implement `action: attach` - delegate to existing attach logic
- [ ] Implement `action: close` - delegate to existing close logic
- [ ] Implement `action: info` - delegate to existing process info logic
- [ ] Add `sandbox: true` flag support (delegate to sandboxed launch)
- [ ] Add `hotreload: true` flag support
- [ ] Add `wait_ms` parameter for launch/attach
- [ ] Add tests for `AppHandler`

### 2.2 Implement `find` Handler

- [ ] Implement basic find with `name`, `automationId`, `className`, `controlType`
- [ ] Implement `at: "root"` pseudo-element (search all tracked windows)
- [ ] Implement `at: elementId` (search within subtree)
- [ ] Implement `recursive: true` for tree traversal
- [ ] Implement `depth` parameter for tree depth limiting
- [ ] Implement `point: {x, y}` for element-at-point
- [ ] Implement `near: {element, direction}` for spatial search
- [ ] Implement `wait_ms` for element waiting
- [ ] Add tests for `FindHandler`

### 2.3 Implement `click` Handler

- [ ] Implement mouse click (default `input: mouse`)
- [ ] Implement touch tap (`input: touch`)
- [ ] Implement pen tap (`input: pen`)
- [ ] Implement `right: true` for right-click / barrel button
- [ ] Implement `double: true` for double-click/tap
- [ ] Implement `hold_ms` for long-press
- [ ] Implement `pressure` for pen (0-1024)
- [ ] Implement `eraser: true` for pen eraser tip
- [ ] Support `target` (element ID) or `x, y` coordinates
- [ ] Add auto-relocate logic for stale elements
- [ ] Add tests for `ClickHandler`

### 2.4 Implement `type` Handler

- [ ] Implement `text` into `target` element
- [ ] Implement global key sending (no `target`)
- [ ] Implement `clear: true` to clear field first
- [ ] Implement `keys: true` for key code interpretation
- [ ] Add auto-relocate logic for stale elements
- [ ] Add tests for `TypeHandler`

### 2.5 Implement `drag` Handler

- [ ] Implement `path` array parsing (min 2 points)
- [ ] Implement mouse drag (default `input: mouse`)
- [ ] Implement touch drag (`input: touch`)
- [ ] Implement pen stroke (`input: pen`)
- [ ] Implement `button: left|right|middle` for mouse
- [ ] Implement `pressure` per-point for pen
- [ ] Implement `eraser: true` for pen eraser stroke
- [ ] Implement `duration_ms` for drag timing
- [ ] Add tests for `DragHandler`

### 2.6 Implement `gesture` Handler

- [ ] Implement `type: pinch` with `center`, `start_distance`, `end_distance`
- [ ] Implement `type: rotate` with `center`, `radius`, `start_angle`, `end_angle`
- [ ] Implement `type: custom` with `fingers` array of paths
- [ ] Implement `duration_ms` for gesture timing
- [ ] Add tests for `GestureHandler`

### 2.7 Implement `screenshot` Handler

- [ ] Implement capture of active window (no params)
- [ ] Implement `target` as element ID
- [ ] Implement `target` as window title
- [ ] Implement `file` parameter for saving to disk
- [ ] Return base64 by default
- [ ] Add tests for `ScreenshotHandler`

### 2.8 Implement `script` Handler

- [ ] Implement `steps` array parsing
- [ ] Implement step execution with tool dispatch
- [ ] Implement `$stepId.result` variable interpolation
- [ ] Implement `stop_on_error` flag (default true)
- [ ] Return results for all executed steps
- [ ] Add tests for `ScriptHandler`

---

## Phase 3: Auto-Relocate Infrastructure

### 3.1 Element Relocation

- [ ] Add `OriginalSelector` property to cached elements (store search criteria)
- [ ] Create `ElementRelocator` class in `Services/`
- [ ] Implement `TryRelocate(elementId)` method
- [ ] Return `RelocationInfo` with stale_id, new_id, selector

### 3.2 Integrate Relocation into Handlers

- [ ] Update `ClickHandler` to use auto-relocate on stale element
- [ ] Update `TypeHandler` to use auto-relocate on stale element
- [ ] Update `DragHandler` to use auto-relocate if target specified
- [ ] Add `relocated` property to response when relocation occurs
- [ ] Add tests for relocation scenarios

---

## Phase 4: Deprecation

### 4.1 Mark Old Tools Deprecated

- [ ] Add `(DEPRECATED)` prefix to old tool descriptions
- [ ] Add deprecation warning to old handler responses
- [ ] Log warning when old tools are called
- [ ] Document migration path in tool descriptions

### 4.2 Update Documentation

- [ ] Update `docs/MCP_TOOLS.md` with new 8-tool API
- [ ] Add migration guide section
- [ ] Update `CLAUDE.md` handler table

---

## Phase 5: Removal (After Validation)

### 5.1 Remove Old Handlers

- [ ] Remove `ProcessHandlers.cs` (replaced by `app`)
- [ ] Remove `ElementHandlers.cs` (replaced by `find`, `click`, `type`)
- [ ] Remove `InputHandlers.cs` (replaced by `click`, `drag`)
- [ ] Remove `TouchPenHandlers.cs` (replaced by `click`, `drag`, `gesture`)
- [ ] Remove `ValidationHandlers.cs` (replaced by `find`)
- [ ] Remove `ObservationHandlers.cs` (replaced by `find`, `screenshot`)
- [ ] Remove `AdvancedHandlers.cs` (capabilities in initialize, others dropped)
- [ ] Keep `SandboxHandlers.cs` (sandbox-specific tools remain)

### 5.2 Remove Old Tool Definitions

- [ ] Remove 44 deprecated tool definitions from `ToolDefinitions.cs`
- [ ] Verify only 8 new tools + sandbox tools remain
- [ ] Update token count documentation

### 5.3 Clean Up Tests

- [ ] Remove tests for old handlers
- [ ] Ensure new handler tests have full coverage
- [ ] Run full test suite

---

## Verification Checklist

### Build Verification

- [ ] `dotnet build Rhombus.WinFormsMcp.sln` succeeds with 0 errors
- [ ] All warnings addressed or documented

### Test Verification

- [ ] `dotnet test` passes all tests
- [ ] New handlers have unit test coverage
- [ ] Integration tests exercise all 8 tools

### E2E Verification (Windows Sandbox)

- [ ] Launch MCP server in sandbox
- [ ] Test `app` tool (launch, attach, close, info)
- [ ] Test `find` tool (root, subtree, recursive, wait)
- [ ] Test `click` tool (mouse, touch, pen, right, double)
- [ ] Test `type` tool (element, global, clear, keys)
- [ ] Test `drag` tool (mouse, touch, pen, path)
- [ ] Test `gesture` tool (pinch, rotate, custom)
- [ ] Test `screenshot` tool (window, element, file)
- [ ] Test `script` tool (steps, interpolation, stop_on_error)
- [ ] Test auto-relocate on stale element

### Token Verification

- [ ] Measure actual tool definition token count
- [ ] Verify ~90% reduction from 52-tool baseline
- [ ] Document final token counts

---

## Notes

- Phase 1-2 are non-breaking: old and new tools coexist
- Phase 3 adds auto-relocate infrastructure
- Phase 4 is deprecation period (can be short for solo use)
- Phase 5 removes old code
- UI diff is POST-MIGRATION enhancement (separate spec)

## Progress Summary

| Phase | Status | Notes |
|-------|--------|-------|
| 1: Foundation | Not Started | Create handler shells, tool definitions |
| 2: Core Tools | Not Started | Implement 8 handlers |
| 3: Auto-Relocate | Not Started | Element relocation infrastructure |
| 4: Deprecation | Not Started | Mark old tools deprecated |
| 5: Removal | Not Started | Delete old handlers and definitions |
