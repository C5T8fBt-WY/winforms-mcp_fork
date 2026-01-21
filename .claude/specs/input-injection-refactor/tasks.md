# Tasks: InputInjection Refactoring

> **⚠️ ARCHIVED**: This spec has been superseded by `unified-refactor/`. All tasks from this spec were consolidated and completed in the unified refactoring plan.

## Phase A: Create Shared Infrastructure

### A1: Create Directory Structure
- [ ] **A1.1**: Create `src/Rhombus.WinFormsMcp.Server/Automation/Interop/` directory
- [ ] **A1.2**: Create `src/Rhombus.WinFormsMcp.Server/Automation/Input/` directory

### A2: Extract Win32Interop
- [ ] **A2.1**: Create `src/Rhombus.WinFormsMcp.Server/Automation/Interop/Win32Interop.cs`
  - Extract all P/Invoke declarations (CreateSyntheticPointerDevice, InjectSyntheticPointerInput, DestroySyntheticPointerDevice, etc.)
  - Extract struct definitions (POINTER_INFO, POINTER_TOUCH_INFO, POINTER_PEN_INFO, POINTER_TYPE_INFO_*, POINT, RECT)
  - Extract constants (POINTER_FLAG_*, PEN_FLAG_*, PEN_MASK_*, PT_TOUCH, PT_PEN, TOUCH_MASK_*, POINTER_FEEDBACK_*)
  - Add XML doc comments explaining struct alignment requirements (8-byte alignment for x64)
- [ ] **A2.2**: Run `dotnet build` to verify structs compile correctly

### A3: Extract CoordinateUtils
- [ ] **A3.1**: Create `src/Rhombus.WinFormsMcp.Server/Automation/Interop/CoordinateUtils.cs`
  - Extract `PixelToHimetric()` method
  - Extract `GetSystemDpi()` method with DPI caching
  - Extract `GetVirtualScreenOrigin()` method
  - Extract `InvalidateDpiCache()` method
  - Add constants: HIMETRIC_PER_INCH, LOGPIXELSX, LOGPIXELSY, SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN
  - **Add code comments documenting multi-monitor edge cases**:
    - Virtual screen origin may be negative on multi-monitor setups
    - Per-monitor DPI awareness affects coordinate translation
    - DPI cache should be invalidated on display configuration changes
- [ ] **A3.2**: Make class non-static with thread-safe DPI cache (use `lock` pattern from original)
- [ ] **A3.3**: Run `dotnet build` to verify compilation

### A4: Extract PointerIdManager
- [ ] **A4.1**: Create `src/Rhombus.WinFormsMcp.Server/Automation/Interop/PointerIdManager.cs`
  - Thread-safe `GetNextPointerId()` method
  - Thread-safe `GetNextFrameId()` method
  - Thread-safe `GetPointerIds(int count)` for batch allocation
  - Use `lock` for thread safety (matching original pattern)
- [ ] **A4.2**: Run `dotnet build` to verify compilation

### A5: Extract DebugLogger
- [ ] **A5.1**: Create `src/Rhombus.WinFormsMcp.Server/Automation/Interop/DebugLogger.cs`
  - Extract `GetDebugLogPath()` (sandbox vs host detection)
  - Extract `LogStructLayout<T>()` for debugging struct memory layout
  - Add `Log(string message)` helper
  - Add `LogPenDeviceCreated()` for device initialization logging
  - Add `LogDeviceRects()` for coordinate space debugging
  - Preserve version tag logging (PEN_INJECTION_VERSION)
- [ ] **A5.2**: Run `dotnet build` to verify compilation

### A6: Extract WindowTargeting
- [ ] **A6.1**: Create `src/Rhombus.WinFormsMcp.Server/Automation/Interop/WindowTargeting.cs`
  - Extract `FindWindowByPartialTitle()` using EnumWindows
  - Extract `GetWindowBounds()` returning nullable tuple
  - Extract `FocusWindow()` using SetForegroundWindow
  - Extract `WindowToScreen()` for coordinate translation
  - **Add code comments documenting DPI considerations**:
    - Window bounds are in screen coordinates at system DPI
    - High-DPI apps may report different bounds under per-monitor DPI
- [ ] **A6.2**: Add interface `IWindowTargeting` for testability
- [ ] **A6.3**: Run `dotnet build` to verify compilation

### A7: Verification
- [ ] **A7.1**: Run `dotnet build Rhombus.WinFormsMcp.sln` - all infrastructure compiles
- [ ] **A7.2**: Run `dotnet test Rhombus.WinFormsMcp.sln` - existing tests still pass

## Phase B: Create Input Classes

### B1: Create TouchInput
- [ ] **B1.1**: Create `src/Rhombus.WinFormsMcp.Server/Automation/Input/ITouchInput.cs` interface
  - `TouchTap(int x, int y, int holdMs = 0)`
  - `LegacyTouchTap(int x, int y, int holdMs = 0)`
  - `TouchDrag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 5)`
  - `PinchZoom(int centerX, int centerY, int startDistance, int endDistance, int steps = 20, int delayMs = 0)`
  - `Rotate(int centerX, int centerY, int radius, double startAngle, double endAngle, int steps = 20, int delayMs = 0)`
  - `MultiTouchGesture((int x, int y, int timeMs)[][] fingers, int interpolationSteps = 5)`
  - `InjectMultiTouch(params (int x, int y, uint pointerId, uint flags)[] contacts)`
- [ ] **B1.2**: Create `src/Rhombus.WinFormsMcp.Server/Automation/Input/TouchInput.cs`
  - **Implement `IDisposable`** for device handle cleanup
  - Constructor takes: CoordinateUtils, PointerIdManager, DebugLogger
  - Move `EnsureTouchInitialized()` to `EnsureInitialized()`
  - Move `InjectTouch()` as private helper
  - Move `InjectLegacyTouch()` and `InitializeLegacyTouch()`
  - Move `InterpolatePosition()` as private helper
  - Move `TouchTap`, `TouchDrag`, `PinchZoom`, `Rotate`, `MultiTouchGesture`, `InjectMultiTouch`
  - Dispose pattern: call `DestroySyntheticPointerDevice` in Dispose
- [ ] **B1.3**: Run `dotnet build` to verify TouchInput compiles

### B2: Create PenInput
- [ ] **B2.1**: Create `src/Rhombus.WinFormsMcp.Server/Automation/Input/IPenInput.cs` interface
  - `PenStroke(int x1, int y1, int x2, int y2, int steps = 20, uint pressure = 512, bool eraser = false, int delayMs = 2, IntPtr hwndTarget = default)`
  - `PenTap(int x, int y, uint pressure = 512, int holdMs = 0, IntPtr hwndTarget = default)`
- [ ] **B2.2**: Create `src/Rhombus.WinFormsMcp.Server/Automation/Input/PenInput.cs`
  - **Implement `IDisposable`** for device handle cleanup
  - Constructor takes: CoordinateUtils, PointerIdManager, DebugLogger
  - Move `EnsurePenInitialized()` to `EnsureInitialized()`
  - Move `InjectPen()` as internal method (handlers may need raw access)
  - Move `PenStroke`, `PenTap`
  - Move `QueryAndLogDeviceRects()` to initialization
  - Preserve VERSION constant for deployment verification
  - Dispose pattern: call `DestroySyntheticPointerDevice` in Dispose
- [ ] **B2.3**: Run `dotnet build` to verify PenInput compiles

### B3: Create MouseInput
- [ ] **B3.1**: Create `src/Rhombus.WinFormsMcp.Server/Automation/Input/IMouseInput.cs` interface
  - `MouseClick(int x, int y, bool doubleClick = false, int delayMs = 0)`
  - `MouseDrag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 0, string? targetWindow = null)`
  - `MouseDragPath((int x, int y)[] waypoints, int stepsPerSegment = 1, int delayMs = 0)`
- [ ] **B3.2**: Create `src/Rhombus.WinFormsMcp.Server/Automation/Input/MouseInput.cs`
  - No IDisposable needed (uses FlaUI, no native handles)
  - Move `EnsureFastMouseSpeed()` as private method
  - Move `MouseClick`, `MouseDrag`, `MouseDragPath`
  - Preserve error recovery (mouse button release on exception)
- [ ] **B3.3**: Run `dotnet build` to verify MouseInput compiles

### B4: Verification
- [ ] **B4.1**: Run `dotnet build Rhombus.WinFormsMcp.sln` - all input classes compile
- [ ] **B4.2**: Run `dotnet test Rhombus.WinFormsMcp.sln` - existing tests still pass

## Phase C: Update InputInjection Facade

### C1: Convert to Thin Facade
- [ ] **C1.1**: Replace implementation in `src/Rhombus.WinFormsMcp.Server/Automation/InputInjection.cs`
  - **Keep facade thin** - pure delegation, no logic
  - Use `Lazy<T>` for lazy initialization of infrastructure and input classes
  - Preserve all existing public method signatures (backwards compat)
  - Delegate each method to appropriate input class
  - **Facade should be ~150 lines** (delegation only)
- [ ] **C1.2**: Expose constants from Win32Interop for handler compatibility
  - Re-export POINTER_FLAG_* constants
  - Re-export PEN_FLAG_* constants
  - Re-export PT_TOUCH, PT_PEN
- [ ] **C1.3**: Add cleanup methods that delegate to Dispose
  - `CleanupTouchDevice()` -> `_touchInput.Value.Dispose()`
  - `CleanupPenDevice()` -> `_penInput.Value.Dispose()`
- [ ] **C1.4**: Run `dotnet build` to verify facade compiles

### C2: Verification - No Handler Changes Required
- [ ] **C2.1**: Verify `TouchPenHandlers.cs` compiles unchanged (uses InputInjection.*)
- [ ] **C2.2**: Verify `InputHandlers.cs` compiles unchanged (uses InputInjection.*)
- [ ] **C2.3**: Verify `WindowHandlers.cs` compiles unchanged (uses InputInjection.*)
- [ ] **C2.4**: Run `dotnet build Rhombus.WinFormsMcp.sln` - full solution compiles
- [ ] **C2.5**: Run `dotnet test Rhombus.WinFormsMcp.sln` - all tests pass

## Phase D: Remove Dead Code

### D1: Clean Up Original File
- [ ] **D1.1**: Remove all implementation code from InputInjection.cs
  - Remove P/Invoke declarations (now in Win32Interop)
  - Remove struct definitions (now in Win32Interop)
  - Remove private helper methods (now in input classes)
  - Remove private static fields (_touchDevice, _penDevice, _nextPointerId, etc.)
- [ ] **D1.2**: Verify InputInjection.cs is now thin facade (~150 lines)
- [ ] **D1.3**: Run `dotnet build` to verify no dead code references

### D2: Final Verification
- [ ] **D2.1**: Run `dotnet build Rhombus.WinFormsMcp.sln` - clean build
- [ ] **D2.2**: Run `dotnet test Rhombus.WinFormsMcp.sln` - all tests pass

## Phase E: Documentation and Cleanup

### E1: Update CLAUDE.md
- [ ] **E1.1**: Add Automation/Interop/ to architecture table
- [ ] **E1.2**: Add Automation/Input/ to architecture table
- [ ] **E1.3**: Document new file structure in handler architecture section

### E2: Code Metrics Verification
- [ ] **E2.1**: Count final LOC per file:
  - [ ] InputInjection.cs (facade): target ~150 LOC
  - [ ] TouchInput.cs: target ~350 LOC
  - [ ] PenInput.cs: target ~200 LOC
  - [ ] MouseInput.cs: target ~150 LOC
  - [ ] Win32Interop.cs: target ~250 LOC
  - [ ] CoordinateUtils.cs: target ~100 LOC
  - [ ] WindowTargeting.cs: target ~100 LOC
  - [ ] PointerIdManager.cs: target ~50 LOC
  - [ ] DebugLogger.cs: target ~80 LOC

### E3: E2E Verification (Requires Windows)
- [ ] **E3.1**: Run `tests/run-gui-tests.ps1` in Windows sandbox
- [ ] **E3.2**: Verify touch_tap tool works
- [ ] **E3.3**: Verify pen_stroke tool works
- [ ] **E3.4**: Verify mouse_drag tool works
- [ ] **E3.5**: Verify pinch_zoom tool works

## Verification Checklist

After all phases complete:

- [ ] All handlers compile unchanged (TouchPenHandlers, InputHandlers, WindowHandlers)
- [ ] All MCP tools respond identically to before
- [ ] No circular dependencies (build succeeds)
- [ ] InputInjection.cs reduced to thin facade (~150 LOC)
- [ ] Each new class < 400 LOC
- [ ] TouchInput and PenInput implement IDisposable
- [ ] DPI/multi-monitor edge cases documented in CoordinateUtils
- [ ] Window coordinate edge cases documented in WindowTargeting
- [ ] Debug logging preserved (pen-debug.log works in sandbox)

## Architecture Summary

```
src/Rhombus.WinFormsMcp.Server/
├── Automation/
│   ├── InputInjection.cs           # Thin facade (~150 LOC)
│   ├── Input/
│   │   ├── ITouchInput.cs          # Interface
│   │   ├── TouchInput.cs           # Implementation + IDisposable (~350 LOC)
│   │   ├── IPenInput.cs            # Interface
│   │   ├── PenInput.cs             # Implementation + IDisposable (~200 LOC)
│   │   ├── IMouseInput.cs          # Interface
│   │   └── MouseInput.cs           # Implementation (~150 LOC)
│   └── Interop/
│       ├── Win32Interop.cs         # P/Invoke, structs, constants (~250 LOC)
│       ├── CoordinateUtils.cs      # Pixel/HIMETRIC, DPI (~100 LOC)
│       ├── WindowTargeting.cs      # Window find/focus + IWindowTargeting (~100 LOC)
│       ├── PointerIdManager.cs     # Thread-safe ID allocation (~50 LOC)
│       └── DebugLogger.cs          # Debug logging utilities (~80 LOC)
```

## Estimated Effort

| Phase | Tasks | Complexity |
|-------|-------|------------|
| A: Infrastructure | 14 | Medium |
| B: Input Classes | 10 | Medium |
| C: Facade | 9 | Low |
| D: Cleanup | 4 | Low |
| E: Documentation | 10 | Low |
| **Total** | **47** | |

## Key Design Decisions

1. **IDisposable for device cleanup**: TouchInput and PenInput implement IDisposable to properly release synthetic pointer device handles
2. **Thin facade pattern**: InputInjection remains static for backwards compat, but contains zero logic - pure delegation only
3. **Documented edge cases**: Multi-monitor DPI and coordinate translation edge cases documented in code comments per feedback
4. **No handler changes**: Handlers continue using `InputInjection.*` static methods unchanged
