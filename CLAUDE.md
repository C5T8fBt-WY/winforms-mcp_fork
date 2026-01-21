# CLAUDE.md

## Project Overview

**Rhombus.WinFormsMcp** - MCP server for headless WinForms automation using FlaUI (UIA2 backend).

## Commands

```bash
dotnet build Rhombus.WinFormsMcp.sln                    # Build
dotnet test Rhombus.WinFormsMcp.sln                     # Test
dotnet run --project src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj  # Run server
dotnet run --project src/Rhombus.WinFormsMcp.TestApp/Rhombus.WinFormsMcp.TestApp.csproj # Run test app
dotnet publish src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj -c Release -o publish
```

## Architecture

| Component | Path | Purpose |
|-----------|------|---------|
| Server | `src/Rhombus.WinFormsMcp.Server/` | MCP server with JSON-RPC 2.0 over stdio/TCP |
| Handlers | `src/Rhombus.WinFormsMcp.Server/Handlers/` | Tool implementations by category |
| Protocol | `src/Rhombus.WinFormsMcp.Server/Protocol/` | JSON-RPC parsing, tool definitions |
| Automation | `src/Rhombus.WinFormsMcp.Server/Automation/` | FlaUI wrappers, window management |
| Script | `src/Rhombus.WinFormsMcp.Server/Script/` | Batch script execution with variable interpolation |
| Services | `src/Rhombus.WinFormsMcp.Server/Services/` | Extracted testable services (DI-ready) |
| Utilities | `src/Rhombus.WinFormsMcp.Server/Utilities/` | Static helpers (ArgHelpers, CoordinateMath, etc.) |
| Input | `src/Rhombus.WinFormsMcp.Server/Input/` | Touch, pen, mouse input injection wrappers |
| Abstractions | `src/Rhombus.WinFormsMcp.Server/Abstractions/` | Interfaces for testability (ITimeProvider, etc.) |
| Interop | `src/Rhombus.WinFormsMcp.Server/Interop/` | Win32 P/Invoke declarations |
| TestApp | `src/Rhombus.WinFormsMcp.TestApp/` | Sample WinForms app for testing |
| Tests | `tests/Rhombus.WinFormsMcp.Tests/` | NUnit test suite |

**Stack**: .NET 8.0-windows, FlaUI 4.0.0 (UIA2), NUnit 3.14.0

### Services Architecture

SessionManager is a facade delegating to extracted services for testability:

| Service | Interface | Purpose |
|---------|-----------|---------|
| ElementCache | IElementCache | Cache AutomationElements with staleness detection |
| ProcessContext | IProcessContext | Track launched apps by executable path |
| ProcessTracker | IProcessTracker | Track PIDs for window scoping |
| SnapshotCache | ISnapshotCache | LRU cache for UI tree snapshots |
| EventService | IEventService | Queue UI events for async retrieval |
| ConfirmationService | IConfirmationService | Pending confirmations for destructive actions |
| TreeExpansionService | ITreeExpansionService | Mark elements for tree expansion |

All services are thread-safe and injectable for testing.

### Handler Architecture

Tools are implemented in modular handlers inheriting from `HandlerBase`:

| Handler | Tools |
|---------|-------|
| ProcessHandlers | `launch_app`, `attach_to_process`, `close_app`, `get_process_info` |
| ElementHandlers | `find_element`, `click_element`, `type_text`, `set_value`, `get_property`, `click_by_automation_id`, `list_elements`, `find_element_near_anchor` |
| InputHandlers | `send_keys`, `drag_drop`, `mouse_drag`, `mouse_drag_path`, `mouse_click` |
| TouchPenHandlers | `touch_tap`, `touch_drag`, `pinch_zoom`, `rotate_gesture`, `multi_touch_gesture`, `pen_stroke`, `pen_tap` |
| ScreenshotHandlers | `take_screenshot` |
| WindowHandlers | `get_window_bounds`, `focus_window` |
| ValidationHandlers | `element_exists`, `wait_for_element`, `check_element_state` |
| ObservationHandlers | `get_ui_tree`, `expand_collapse`, `scroll`, `get_element_at_point`, `capture_ui_snapshot`, `compare_ui_snapshots` |
| SandboxHandlers | `launch_app_sandboxed`, `close_sandbox`, `list_sandbox_apps` |
| AdvancedHandlers | `get_capabilities`, `get_dpi_info`, `subscribe_to_events`, `get_pending_events`, `mark_for_expansion`, `clear_expansion_marks`, `relocate_element`, `check_element_stale`, `get_cache_stats`, `invalidate_cache`, `confirm_action`, `execute_confirmed_action` |

Script execution via `run_script` in `ScriptRunner.cs`.

Session state: cached elements (elem_1, elem_2...), active AutomationHelper, process PIDs.

### Window Scoping

Tool responses include a `windows` array with visible windows. By default, windows are scoped to tracked processes:

- `launch_app` / `attach_to_process`: Automatically track the PID
- `close_app`: Automatically untrack the PID
- Response includes `windowScope`: "All", "Process", or "Tracked"
- Each window includes `processId` for filtering

Handlers can use `ScopedSuccess()` or `GetScopedWindows()` helpers.

## CI/CD

- Version in `VERSION` file, auto-bumped on master commits
- Publishes to NuGet (`Rhombus.WinFormsMcp`) and NPM (`@rhom6us/winforms-mcp`)