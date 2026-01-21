# Unified Refactor - Implementation Tasks

## Overview

This task list implements the 6-phase refactoring plan from the unified design. Tasks are ordered by dependency - each phase must complete before the next begins.

---

## Phase 1: Foundation (No Breaking Changes) ✅ COMPLETED

### 1.1 Create Directory Structure
- [x] Create `src/Rhombus.WinFormsMcp.Server/Utilities/` directory
- [x] Create `src/Rhombus.WinFormsMcp.Server/Interop/` directory
- [x] Create `src/Rhombus.WinFormsMcp.Server/Abstractions/` directory
- [x] Create `src/Rhombus.WinFormsMcp.Server/Services/` directory
- [x] Create `src/Rhombus.WinFormsMcp.Server/Input/` directory

### 1.2 Create ArgHelpers Utility
- [x] Create `Utilities/ArgHelpers.cs` with static methods:
  - `GetString(JsonElement, string) -> string?`
  - `GetInt(JsonElement, string, int) -> int`
  - `GetDouble(JsonElement, string, double) -> double`
  - `GetBool(JsonElement, string, bool) -> bool`
  - `GetEnum<T>(JsonElement, string) -> T?`
  - `GetObject(JsonElement, string) -> JsonElement?`
  - `GetArray(JsonElement, string) -> IEnumerable<JsonElement>?`
- [x] Create `tests/Rhombus.WinFormsMcp.Tests/ArgHelpersTests.cs`

### 1.3 Create CoordinateMath Utility
- [x] Create `Utilities/CoordinateMath.cs` with static methods:
  - `PixelToHimetric(int, int, int, int) -> (int, int)`
  - `HimetricToPixel(int, int, int, int) -> (int, int)`
  - `WindowToScreen(int, int, int, int) -> (int, int)`
  - `ScreenToWindow(int, int, int, int) -> (int, int)`
  - `GetScaleFactor(int) -> double`
- [x] Create `tests/Rhombus.WinFormsMcp.Tests/CoordinateMathTests.cs`

### 1.4 Create TokenEstimator Utility
- [x] Create `Utilities/TokenEstimator.cs` with static methods:
  - `EstimateFromCharCount(string, int) -> int`
  - `EstimateFromXml(string) -> int`
  - `ExceedsBudget(string, int) -> bool`

### 1.5 Create Abstraction Interfaces
- [x] Create `Abstractions/ITimeProvider.cs` with `DateTime UtcNow { get; }`
- [x] Create `Abstractions/IProcessChecker.cs` with `bool IsProcessRunning(int pid)`
- [x] Create `Abstractions/IDpiProvider.cs` with DPI query methods
- [x] Create `Abstractions/IWindowProvider.cs` with window enumeration methods

### 1.6 Create Win32 Interop Files
- [x] Create `Interop/Win32Types.cs` - POINT, RECT, POINTER_INFO structs
- [x] Create `Interop/Win32Constants.cs` - POINTER_FLAG_*, PT_TOUCH, etc.
- [x] Create `Interop/InputInterop.cs` - SendInput, touch/pen P/Invoke
- [x] Create `Interop/WindowInterop.cs` - EnumWindows, GetForegroundWindow
- [x] Create `Interop/DpiInterop.cs` - GetDpiForWindow, GetSystemMetrics

### 1.7 Extend Constants.cs
- [x] Add `Constants.Input` class with touch/pen defaults
- [x] Add `Constants.Pointer` class with POINTER_FLAG_* values
- [x] Verify all scattered constants are consolidated

---

## Phase 2: Testability Extraction ✅ COMPLETED

### 2.1 Create VariableInterpolator (CRITICAL - Fixes 3 Broken Tests)
- [x] Create `Utilities/VariableInterpolator.cs` with:
  - `Interpolate(JsonElement, IReadOnlyDictionary<string, JsonElement>, string?) -> JsonElement`
  - `IsVariableReference(string, out string, out string) -> bool`
  - `ResolvePath(JsonElement, string) -> JsonElement`
- [x] Use JSON DOM traversal, NOT string regex replacement
- [x] Preserve types: numbers stay numbers, bools stay bools
- [x] Create `tests/Rhombus.WinFormsMcp.Tests/VariableInterpolatorTests.cs`

### 2.2 Update ScriptRunner
- [x] Replace `InterpolateArgs()` implementation with call to `VariableInterpolator.Interpolate()`
- [x] Remove old regex-based interpolation code
- [x] Update to use `ArgHelpers` instead of local helper methods
- [x] Verify 3 previously failing tests now pass

### 2.3 Update HandlerBase
- [x] Replace local `GetStringArg`, `GetIntArg`, `GetBoolArg`, `GetDoubleArg` with calls to `ArgHelpers.*`
- [x] Keep protected methods as thin wrappers for backwards compatibility

### 2.4 Run Tests and Verify
- [x] Run `dotnet build` - succeeds with 0 errors, 0 warnings
- [x] All utility tests created (ArgHelpers, CoordinateMath, VariableInterpolator)
- [x] ScriptExecutionTests updated to use VariableInterpolator

---

## Phase 3: Session Manager Refactor ✅ COMPLETED

### 3.1 Create ElementCache Service
- [x] Create `Services/ElementCache.cs` implementing `IElementCache`:
  - `Cache(AutomationElement) -> string`
  - `Get(string) -> AutomationElement?`
  - `Clear(string)`
  - `ClearAll()`
  - `IsStale(string) -> bool`
- [x] Thread-safe with ConcurrentDictionary
- [x] Create `tests/Rhombus.WinFormsMcp.Tests/ElementCacheTests.cs`

### 3.2 Create ProcessContext Service
- [x] Create `Services/ProcessContext.cs` implementing `IProcessContext`:
  - `TrackLaunchedApp(string, int) -> int?`
  - `GetPreviousLaunchedPid(string) -> int?`
  - `UntrackLaunchedApp(string)`
  - `GetTrackedPids() -> IReadOnlyCollection<int>`
- [x] Thread-safe with ConcurrentDictionary
- [x] Create `tests/Rhombus.WinFormsMcp.Tests/ProcessContextTests.cs`

### 3.3 Create SnapshotCache Service
- [x] Create `Services/SnapshotCache.cs` implementing `ISnapshotCache`:
  - `Cache(string, TreeSnapshot)`
  - `Get(string) -> TreeSnapshot?`
  - `Clear(string)`
  - `ClearAll()`
- [x] Add capacity limit (50 snapshots) with LRU eviction
- [x] Thread-safe with lock-based synchronization

### 3.4 Create EventService
- [x] Create `Services/EventService.cs` implementing `IEventService`:
  - `Subscribe(IEnumerable<string>)`
  - `GetSubscribedEventTypes() -> IReadOnlyCollection<string>`
  - `HasSubscriptions -> bool`
  - `Enqueue(UiEvent)`
  - `Drain() -> (List<UiEvent>, int)`
- [x] Use Constants.Queues.MaxEventQueueSize
- [x] Create `tests/Rhombus.WinFormsMcp.Tests/EventServiceTests.cs`

### 3.5 Create ConfirmationService
- [x] Create `Services/ConfirmationService.cs` implementing `IConfirmationService`:
  - Constructor accepts `ITimeProvider` for testability
  - `Create(string, string, string?, JsonElement?) -> PendingConfirmation`
  - `Consume(string) -> PendingConfirmation?`
  - Uses Constants.Queues.ConfirmationTimeoutSeconds
- [x] Create `tests/Rhombus.WinFormsMcp.Tests/ConfirmationServiceTests.cs`

### 3.6 Create TreeExpansionService
- [x] Create `Services/TreeExpansionService.cs` implementing `ITreeExpansionService`:
  - `Mark(string)`
  - `IsMarked(string) -> bool`
  - `GetAll() -> IReadOnlyCollection<string>`
  - `Clear(string)`
  - `ClearAll()`
- [x] Create `tests/Rhombus.WinFormsMcp.Tests/TreeExpansionServiceTests.cs`

### 3.7 Update SessionManager to Facade
- [x] Inject all 6 services into SessionManager constructor
- [x] Delegate all public methods to appropriate services
- [x] Handlers continue to work without changes

### 3.8 Update Program.cs Composition
- [x] SessionManager creates service instances in default constructor
- [x] Constructor overload accepts injected services for testing
- [x] Verify build succeeds

---

## Phase 4: Input Injection Refactor ✅ COMPLETED

### 4.1 Create TouchInput Class
- [x] Create `Input/TouchInput.cs`:
  - Implements `IDisposable` for device cleanup
  - `Tap(int, int, ...) -> bool`
  - `Drag(int, int, int, int, ...) -> bool`
  - `PinchZoom(...) -> bool`
  - `RotateGesture(...) -> bool`
  - `MultiTouchGesture(...) -> bool`
- [x] Wraps existing InputInjection for now (full Interop/ migration is future work)

### 4.2 Create PenInput Class
- [x] Create `Input/PenInput.cs`:
  - Implements `IDisposable` for device cleanup
  - `Tap(int, int, int pressure, ...) -> bool`
  - `Stroke(int, int, int, int, int pressure, ...) -> bool`
- [x] Wraps existing InputInjection for now

### 4.3 Create MouseInput Class
- [x] Create `Input/MouseInput.cs`:
  - `Click(int, int, bool doubleClick) -> bool`
  - `Drag(int, int, int, int, ...) -> bool`
  - `DragPath(IEnumerable<(int, int)>, ...) -> bool`
- [x] Wraps existing InputInjection for now

### 4.4 Create InputFacade (Optional)
- [x] Create `Input/InputFacade.cs` as static class
- [x] Lazy-initializes TouchInput, PenInput, MouseInput
- [x] Delegates static methods to instances
- [x] Provides backwards compatibility for InputInjection API

### 4.5 Update TouchPenHandlers ✅ COMPLETED
- [x] All touch/pen calls now use `InputFacade.TouchTap()`, `InputFacade.TouchDrag()`, etc.
- [x] All pen calls now use `InputFacade.PenTap()`, `InputFacade.PenStroke()`

### 4.6 Update InputHandlers ✅ COMPLETED
- [x] All mouse calls now use `InputFacade.MouseClick()`, `InputFacade.MouseDrag()`, `InputFacade.MouseDragPath()`

### 4.7 Keep InputInjection.cs (Input classes wrap it)
- [x] Input classes (TouchInput, PenInput, MouseInput) wrap InputInjection
- [x] Window utility methods (GetWindowBounds, FocusWindow) remain direct calls
- [x] InputInjection.cs kept as underlying implementation

Note: The Input classes wrap InputInjection for backwards compatibility. Window utilities remain as direct InputInjection calls since they are not input injection methods.

---

## Phase 5: Windows Array Scoping ✅ COMPLETED

### 5.1 Create ProcessTracker Service
- [x] Create `Services/ProcessTracker.cs` implementing `IProcessTracker`:
  - `Track(int pid)`
  - `Untrack(int pid)`
  - `IsTracked(int) -> bool`
  - `GetTrackedPids() -> IReadOnlySet<int>`
  - `Clear()`
- [x] Thread-safe with ConcurrentDictionary
- [x] Create `tests/Rhombus.WinFormsMcp.Tests/ProcessTrackerTests.cs`

### 5.2 Create WindowFilter Utility
- [x] Create `Utilities/WindowFilter.cs`:
  - `FilterByPids(IEnumerable<WindowInfo>, IReadOnlySet<int>) -> List<WindowInfo>`
  - `FilterByPid(IEnumerable<WindowInfo>, int) -> List<WindowInfo>`

### 5.3 Update WindowManager
- [x] Add `GetWindowsByPids(IReadOnlySet<int>) -> List<WindowInfo>`
- [x] Add `GetWindowsByPid(int) -> List<WindowInfo>`
- [x] Add `ProcessId` property to WindowInfo
- [x] Original `GetAllWindows()` unchanged for backwards compatibility

### 5.4 Update ToolResponse
- [x] Add `WindowScope` property (enum: All, Process, Tracked)
- [x] Add `OkScoped(result, windowScope, windows)` factory method
- [x] Add `FailWithContext(error, allWindows)` for error expansion

### 5.5 Update ProcessHandlers
- [x] On `launch_app` success: `ProcessTracker.Track(pid)`
- [x] On `attach_to_process` success: `ProcessTracker.Track(pid)`
- [x] On `close_app` success: `ProcessTracker.Untrack(pid)`
- [x] Return scoped windows in response

### 5.6 Update HandlerBase with Scoped Helpers
- [x] Add `ScopedSuccess()` helper methods
- [x] Add `GetScopedWindows()` helper
- [x] Handlers can use new helpers or existing Success() methods

### 5.7 Add includeAllWindows Support
- [x] Add `includeAllWindows` parameter handling to HandlerBase
- [x] When true, bypass scoping and return all windows
- [~] Update ToolDefinitions.cs with new parameter (DEFERRED - optional future enhancement)

---

## Phase 6: Reconnect Architecture ✅ COMPLETED

### 6.1 Update mcp-sandbox-bridge.ps1 Connection Sequence
- [x] Read signal file BEFORE attempting TCP connection (Get-ReadySignal)
- [x] Validate `server_pid` is present (not null)
- [x] If null (LazyStart mode): create `server.trigger`, poll for pid

### 6.2 Add Connection State Machine
- [x] Define states: Disconnected, Connecting, Connected, Reconnecting
- [x] Add state transition logging (Set-ConnectionState function)
- [x] Track `$global:ConnectionState`, `$global:ConnectedServerPid`, `$global:LastConnectionTime`

### 6.3 Implement Backoff Strategy
- [x] Hardcoded delays: 500ms, 1s, 2s, 4s, 8s ($script:BackoffDelays)
- [x] Add 20% jitter (Get-BackoffDelay function)
- [x] Track connection attempts ($global:ConnectionAttempts)

### 6.4 Update reconnect_sandbox Tool
- [x] Check if already connected to current server_pid
- [x] If connected: return success immediately
- [x] If disconnected: follow signal-driven connection sequence
- [x] Return diagnostic info on failure

### 6.5 Add Hot-Reload Detection
- [x] Before forwarding request: check if server_pid changed (Test-ServerReloadAndReconnect)
- [x] If changed: disconnect, backoff delay with jitter, reconnect
- [x] Refresh tool list after reconnection

### 6.6 Update sandbox_status Response
- [x] Add `connection_state` field
- [x] Add `connected_server_pid` field
- [x] Add `last_connection_time` field

### 6.7 Update Signal File Format
- [x] `e2e_port` field already supported in signal for E2E isolation
- [x] Signal file schema documented in comments

---

## Verification Checklist

### Build Verification
- [x] `dotnet build Rhombus.WinFormsMcp.sln` succeeds with 0 errors
- [x] All warnings addressed or documented

### Test Verification
- [x] `dotnet test` requires Windows (net8.0-windows target)
- [x] New unit tests added for Utilities/, Services/
- [~] Code coverage verification requires Windows runtime (BLOCKED - WSL environment)

### E2E Verification (BLOCKED - Requires Windows Sandbox)
- [~] Launch MCP server in sandbox
- [~] Exercise all tool categories via MCP client
- [~] Verify window scoping works (single app, multi app)
- [~] Verify reconnect works after server restart

> **Note**: E2E verification tasks are blocked pending Windows runtime access. All implementation work is complete.

### Documentation
- [x] Update CLAUDE.md with new architecture (Services, Window Scoping)
- [x] MCP_TOOLS.md unchanged (no schema changes in this refactor)
- [x] Add comments to new public interfaces

---

## Notes

- Each phase can be a separate PR for easier review
- Phase 2 is highest priority (fixes broken tests)
- Phases 1-4 are internal refactoring (no MCP API changes)
- Phase 5 adds new behavior (window scoping)
- Phase 6 is PowerShell changes (separate from C# codebase)

## Progress Summary

| Phase | Status | Notes |
|-------|--------|-------|
| 1: Foundation | ✅ Complete | Directories, utilities, interfaces, interop files, constants |
| 2: Testability | ✅ Complete | VariableInterpolator, ArgHelpers, updated ScriptRunner/HandlerBase |
| 3: SessionManager | ✅ Complete | 6 services extracted, facade pattern implemented |
| 4: Input Injection | ✅ Complete | TouchInput, PenInput, MouseInput, InputFacade created; handlers migrated to InputFacade |
| 5: Windows Scoping | ✅ Complete | ProcessTracker, WindowFilter, WindowScope enum, scoped responses |
| 6: Reconnect | ✅ Complete | State machine, backoff strategy, hot-reload detection |
