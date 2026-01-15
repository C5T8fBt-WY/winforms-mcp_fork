# Tasks: MCP TCP Sandbox Control

Implementation checklist for window-relative coordinates and window context responses.

---

## Phase 1: Core Infrastructure

- [ ] 1. Create WindowManager component
  - Implements requirement 2.6 (UI element discovery) and design section 1
  - [ ] 1.1. Create `src/Rhombus.WinFormsMcp.Server/Automation/WindowManager.cs`
  - [ ] 1.2. Implement `GetAllWindows()` - enumerate visible top-level windows
  - [ ] 1.3. Implement `FindWindow(windowHandle, windowTitle)` - resolve to WindowInfo
  - [ ] 1.4. Implement `TranslateCoordinates(window, x, y)` - add window bounds to coords
  - [ ] 1.5. Write unit tests for WindowManager in `tests/Rhombus.WinFormsMcp.Tests/WindowManagerTests.cs`

- [ ] 2. Create ToolResponse wrapper
  - Implements requirement 6.2 (window context in every response) and design section 2
  - [ ] 2.1. Create `src/Rhombus.WinFormsMcp.Server/ToolResponse.cs`
  - [ ] 2.2. Implement `Ok(result, windowManager)` and `Fail(error, windowManager)` factory methods
  - [ ] 2.3. Add JSON serialization with `windows` array
  - [ ] 2.4. Write unit tests for ToolResponse serialization

- [ ] 3. Create WindowInfo model
  - Implements design section data models
  - [ ] 3.1. Create `src/Rhombus.WinFormsMcp.Server/Models/WindowInfo.cs`
  - [ ] 3.2. Add properties: Handle, Title, AutomationId, Bounds, IsActive
  - [ ] 3.3. Add JSON serialization attributes

---

## Phase 2: Update Tool Handlers

- [ ] 4. Update process management tools
  - Implements requirements 2.5, 2.9 (launch/close app)
  - [ ] 4.1. Update `launch_app` handler to return ToolResponse with windows
  - [ ] 4.2. Update `close_app` handler to return ToolResponse with windows
  - [ ] 4.3. Update `attach_to_process` handler to return ToolResponse with windows

- [ ] 5. Update element discovery tools
  - Implements requirement 2.6 (UI element discovery)
  - [ ] 5.1. Update `find_element` to accept windowHandle/windowTitle, return ToolResponse
  - [ ] 5.2. Update `list_elements` to return ToolResponse with windows
  - [ ] 5.3. Update `get_window_bounds` to return ToolResponse with windows

- [ ] 6. Update UI interaction tools
  - Implements requirement 2.7 (UI interaction)
  - [ ] 6.1. Update `click_element` to return ToolResponse with windows
  - [ ] 6.2. Update `click_by_automation_id` to return ToolResponse with windows
  - [ ] 6.3. Update `type_text` to return ToolResponse with windows
  - [ ] 6.4. Update `drag_drop` to return ToolResponse with windows

- [ ] 7. Update mouse input tools with window-relative coordinates
  - Implements requirement 6.1 (window-relative coordinates)
  - [ ] 7.1. Update `mouse_click` to accept windowHandle/windowTitle, translate coords
  - [ ] 7.2. Update `mouse_drag` to accept windowHandle/windowTitle, translate coords
  - [ ] 7.3. Update `mouse_drag_path` to accept windowHandle/windowTitle, translate coords

- [ ] 8. Update touch input tools with window-relative coordinates
  - Implements requirement 6.1 (window-relative coordinates)
  - [ ] 8.1. Update `touch_tap` to accept windowHandle/windowTitle, translate coords
  - [ ] 8.2. Update `touch_drag` to accept windowHandle/windowTitle, translate coords
  - [ ] 8.3. Update `pinch_zoom` to accept windowHandle/windowTitle, translate coords

- [ ] 9. Update pen input tools with window-relative coordinates
  - Implements requirement 6.1 (window-relative coordinates)
  - [ ] 9.1. Update `pen_tap` to accept windowHandle/windowTitle, translate coords
  - [ ] 9.2. Update `pen_stroke` to accept windowHandle/windowTitle, translate coords

- [ ] 10. Update capture tools
  - Implements requirement 2.7.4 (screenshot capture)
  - [ ] 10.1. Update `take_screenshot` to return ToolResponse with windows
  - [ ] 10.2. Update `capture_ui_snapshot` to return ToolResponse with windows
  - [ ] 10.3. Update `compare_ui_snapshots` to return ToolResponse with windows

---

## Phase 3: Bootstrap Updates

- [ ] 11. Update bootstrap.ps1 for LazyStart mode
  - Implements requirement 2.0 (ensure sandbox running) and design section 4
  - [ ] 11.1. Add `-LazyStart` parameter to bootstrap.ps1
  - [ ] 11.2. Make server startup conditional on `-LazyStart` flag
  - [ ] 11.3. Simplify `Update-ReadySignal` to handle null server_pid
  - [ ] 11.4. Remove deprecated `app.trigger` handling from polling loop
  - [ ] 11.5. Test bootstrap with `-LazyStart` flag in sandbox

---

## Phase 4: Error Handling

- [ ] 12. Implement window resolution errors
  - Implements design section error handling
  - [ ] 12.1. Return `partialMatches` when window not found but similar titles exist
  - [ ] 12.2. Return `matches` array when multiple windows match title
  - [ ] 12.3. Return clear error when window handle valid but window closed

- [ ] 13. Implement coordinate errors
  - Implements design section error handling
  - [ ] 13.1. Add warning (not error) when coordinates outside window bounds
  - [ ] 13.2. Return error with suggestion when window is minimized

---

## Phase 5: Integration Tests

- [ ] 14. Write coordinate translation integration tests
  - Implements design section testing strategy
  - [ ] 14.1. Test `mouse_click` with window-relative coords hits correct screen position
  - [ ] 14.2. Test `mouse_drag` with window-relative coords
  - [ ] 14.3. Test error handling for invalid window handles

- [ ] 15. Write window enumeration integration tests
  - Implements design section testing strategy
  - [ ] 15.1. Test launched app appears in `windows` response
  - [ ] 15.2. Test closed app disappears from `windows` response
  - [ ] 15.3. Test dialog appears alongside main window with correct `isActive`

- [ ] 16. Write E2E workflow tests
  - Implements design section testing strategy
  - [ ] 16.1. Test full workflow: launch → list_elements → click → screenshot
  - [ ] 16.2. Test hot reload: launch → detect bug → close → relaunch → verify fix

---

## Phase 6: Schema Updates

- [ ] 17. Update MCP tool schemas
  - Implements requirement 6.1 (window-relative coordinates)
  - [ ] 17.1. Add `windowHandle` and `windowTitle` parameters to coordinate-based tools
  - [ ] 17.2. Update tool descriptions to document window-relative behavior
  - [ ] 17.3. Update return type documentation to include `windows` array

---

## Verification Checklist

After all tasks complete:
- [ ] All coordinate-based tools accept `windowHandle` or `windowTitle`
- [ ] All tool responses include `windows` array
- [ ] Bootstrap.ps1 `-LazyStart` mode works in sandbox
- [ ] Unit tests pass for WindowManager and ToolResponse
- [ ] Integration tests pass for coordinate translation
- [ ] E2E workflow test passes
