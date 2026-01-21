# InputInjection God Class Refactoring - Requirements

## Problem Statement

`InputInjection.cs` is a 1,511-line static class with CRAP score 1640, containing multiple distinct responsibilities:
- Touch input injection (Synthetic Pointer API + Legacy InjectTouchInput)
- Pen input injection (Synthetic Pointer API)
- Mouse input injection (via FlaUI)
- Win32 P/Invoke declarations and structs
- Coordinate translation (pixel to HIMETRIC, DPI handling)
- Window targeting utilities
- Debug logging infrastructure

This violates the Single Responsibility Principle and makes the code difficult to test, maintain, and extend.

## Stakeholders

- **MCP Tool Authors**: Need stable, well-documented input APIs
- **Test Engineers**: Need mockable interfaces for unit testing
- **Maintainers**: Need clear separation of concerns for bug fixes
- **Future Developers**: Need extensible architecture for new input types

## Functional Requirements

### FR-1: Touch Input Separation
**EARS Format**: The system SHALL provide a dedicated TouchInput class that encapsulates all touch-related functionality including single-touch tap, single-touch drag, multi-touch gestures, pinch-zoom, and rotate gestures.

**Acceptance Criteria**:
- TouchInput class handles: `TouchTap`, `TouchDrag`, `PinchZoom`, `Rotate`, `MultiTouchGesture`, `InjectMultiTouch`
- Both Synthetic Pointer API and Legacy InjectTouchInput paths are preserved
- Pointer ID management is internal to the class

### FR-2: Pen Input Separation
**EARS Format**: The system SHALL provide a dedicated PenInput class that encapsulates all pen/stylus-related functionality including pen strokes and pen taps with pressure, tilt, and eraser support.

**Acceptance Criteria**:
- PenInput class handles: `PenStroke`, `PenTap`, `InjectPen`
- Pen flags (ERASER, BARREL, INVERTED) remain accessible
- Pressure (0-1024) and tilt (-90 to +90) support preserved

### FR-3: Mouse Input Separation
**EARS Format**: The system SHALL provide a dedicated MouseInput class that encapsulates all mouse-related functionality including clicks, drags, and multi-waypoint drag paths.

**Acceptance Criteria**:
- MouseInput class handles: `MouseClick`, `MouseDrag`, `MouseDragPath`
- FlaUI integration with fast mouse speed settings preserved
- Error recovery (mouse button release on error) maintained

### FR-4: Shared Win32 Infrastructure
**EARS Format**: The system SHALL provide a shared Win32Interop module that contains all P/Invoke declarations, struct definitions, and constants used by multiple input classes.

**Acceptance Criteria**:
- POINTER_INFO, POINTER_TOUCH_INFO, POINTER_PEN_INFO structs in shared location
- POINTER_FLAG_* constants accessible to all input classes
- DPI query functions available to coordinate translation code

### FR-5: Coordinate Translation
**EARS Format**: The system SHALL provide a CoordinateTranslation utility that handles pixel-to-HIMETRIC conversion, DPI queries, and virtual screen origin calculations.

**Acceptance Criteria**:
- `PixelToHimetric()` usable by touch and pen classes
- DPI caching with invalidation support
- Virtual screen origin offset calculation

### FR-6: Window Targeting
**EARS Format**: WHEN window targeting is requested, the system SHALL resolve window handles by title (exact or partial match) and translate client-relative coordinates to screen coordinates.

**Acceptance Criteria**:
- `FindWindowByPartialTitle()` functionality preserved
- `GetWindowBounds()` returns nullable tuple
- `WindowToScreen()` coordinate translation works

### FR-7: Backwards Compatibility
**EARS Format**: The system SHALL maintain backwards compatibility with existing handler code (TouchPenHandlers, InputHandlers) through a facade or by preserving the existing static API.

**Acceptance Criteria**:
- Existing handler code requires minimal changes (ideally just namespace updates)
- All 12 public methods remain callable with same signatures
- No breaking changes to MCP tool behavior

## Non-Functional Requirements

### NFR-1: Testability
**EARS Format**: The system SHALL be designed to allow unit testing of individual input classes without requiring actual Windows input injection.

**Acceptance Criteria**:
- Input classes can be instantiated (not static) or have mockable interfaces
- Device initialization can be bypassed in tests
- CRAP score of individual classes < 100

### NFR-2: Code Metrics
**EARS Format**: The system SHALL achieve improved code quality metrics after refactoring.

**Acceptance Criteria**:
- No single class > 400 lines of code
- Cyclomatic complexity per method < 15
- Each class has single clear responsibility

### NFR-3: Debug Logging
**EARS Format**: The system SHALL preserve existing debug logging behavior to pen-debug.log for troubleshooting injection issues.

**Acceptance Criteria**:
- Log path resolution (sandbox vs host) preserved
- Struct layout logging for debugging remains available
- Log messages include version tags for deployment verification

### NFR-4: Thread Safety
**EARS Format**: The system SHALL maintain thread safety for device initialization and pointer ID allocation.

**Acceptance Criteria**:
- Device handles (_touchDevice, _penDevice) safely managed
- Pointer ID counter (_nextPointerId) thread-safe
- DPI cache invalidation is atomic

## Out of Scope

- Changing the Win32 APIs used (must remain compatible with Windows 10 1809+)
- Adding new input types (gamepad, etc.)
- Modifying the MCP protocol or handler architecture
- Performance optimization beyond current levels

## Dependencies

- FlaUI 4.0.0 (for mouse input)
- .NET 8.0-windows
- Windows 10 1809+ (for Synthetic Pointer API)
- Windows 8+ (for Legacy Touch API fallback)

## Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| Breaking existing handlers | High | Facade pattern or minimal API changes |
| Subtle coordinate bugs | High | Extensive E2E testing before/after |
| Thread safety regressions | Medium | Preserve existing locking patterns |
| Debug logging broken | Low | Test logging in sandbox environment |
