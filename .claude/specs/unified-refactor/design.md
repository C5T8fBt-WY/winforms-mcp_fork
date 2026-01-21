# Unified Refactor Design Document

## Overview

This document consolidates six requirement specifications into a unified refactoring plan for the WinForms MCP server. The goal is to maximize testability, reduce coupling, and maintain backwards compatibility while addressing all specification requirements.

### Source Specifications

| Spec | Primary Focus | Key Deliverables |
|------|---------------|------------------|
| session-manager-refactor | Extract services from SessionManager god object | 6 focused services with interfaces |
| input-injection-refactor | Break up 1511-line InputInjection class | TouchInput, PenInput, MouseInput, shared Win32 |
| testability-extraction | Extract pure logic from Windows dependencies | ArgHelpers, VariableInterpolator, CoordinateMath |
| run-script-behavior | Fix type-preserving variable interpolation | VariableInterpolator with JSON DOM operations |
| windows-array-scoping | Scope window lists to relevant processes | ProcessTracker, response filtering |
| reconnect-architecture | Reliable bridge reconnection | Signal-driven connection, state machine |

---

## Part 1: Shared Components

These components are needed by multiple specifications and must be implemented first.

### 1.1 Pure Utility Classes (No Windows Dependencies)

```
src/Rhombus.WinFormsMcp.Server/
├── Utilities/
│   ├── ArgHelpers.cs          # JSON argument parsing (used by all handlers, ScriptRunner)
│   ├── VariableInterpolator.cs # Type-preserving interpolation (run-script, testability)
│   ├── CoordinateMath.cs       # Pixel/HIMETRIC/DPI calculations (input-injection, testability)
│   └── TokenEstimator.cs       # UI tree budget estimation (testability)
```

**ArgHelpers** - Consolidates duplicated argument parsing from HandlerBase and ScriptRunner:
```csharp
public static class ArgHelpers
{
    public static string? GetString(JsonElement args, string key);
    public static int GetInt(JsonElement args, string key, int defaultValue = 0);
    public static double GetDouble(JsonElement args, string key, double defaultValue = 0);
    public static bool GetBool(JsonElement args, string key, bool defaultValue = false);
    public static T? GetEnum<T>(JsonElement args, string key) where T : struct, Enum;
    public static JsonElement? GetObject(JsonElement args, string key);
    public static IEnumerable<JsonElement>? GetArray(JsonElement args, string key);
}
```

**VariableInterpolator** - Type-preserving JSON interpolation (fixes failing ScriptRunner tests):
```csharp
public static class VariableInterpolator
{
    /// <summary>
    /// Interpolate variable references in JSON using DOM manipulation, not string replacement.
    /// Preserves JSON types: numbers stay numbers, booleans stay booleans.
    /// </summary>
    public static JsonElement Interpolate(
        JsonElement args,
        IReadOnlyDictionary<string, JsonElement> stepResults,
        string? lastStepId);

    /// <summary>
    /// Resolve a path like "result.element.bounds.x" in a JsonElement.
    /// </summary>
    public static JsonElement ResolvePath(JsonElement root, string path);
}
```

**CoordinateMath** - Pure coordinate calculations (no Win32 calls):
```csharp
public static class CoordinateMath
{
    public const int StandardDpi = 96;
    public const double HimetricPerInch = 2540.0;

    public static (int himetricX, int himetricY) PixelToHimetric(int pixelX, int pixelY, int dpiX, int dpiY);
    public static (int pixelX, int pixelY) HimetricToPixel(int himetricX, int himetricY, int dpiX, int dpiY);
    public static (int screenX, int screenY) WindowToScreen(int windowX, int windowY, int windowLeft, int windowTop);
    public static double GetScaleFactor(int dpi);
}
```

### 1.2 Shared Win32 Interop (Used by Input and Window classes)

```
src/Rhombus.WinFormsMcp.Server/
├── Interop/
│   ├── Win32Types.cs           # POINT, RECT, POINTER_INFO, etc.
│   ├── Win32Constants.cs       # POINTER_FLAG_*, PT_TOUCH, etc.
│   ├── InputInterop.cs         # SendInput, touch/pen P/Invoke
│   ├── WindowInterop.cs        # EnumWindows, Get/SetForeground, etc.
│   └── DpiInterop.cs           # GetDpiForWindow, GetSystemMetrics
```

### 1.3 Shared Interfaces (Enable Mocking)

```
src/Rhombus.WinFormsMcp.Server/
├── Abstractions/
│   ├── IProcessChecker.cs      # Check if process is alive (testability)
│   ├── IWindowProvider.cs      # Window enumeration abstraction (windows-array)
│   ├── ITimeProvider.cs        # Injectable clock for confirmations (session-manager)
│   └── IDpiProvider.cs         # DPI queries abstraction (input-injection)
```

---

## Part 2: Dependency Order

Changes must be applied in this order to avoid breaking the build:

```
Phase 1: Foundation (No Breaking Changes)
├── 1.1 Create Utilities/ with ArgHelpers, CoordinateMath, TokenEstimator
├── 1.2 Create Interop/ with Win32 types and constants
├── 1.3 Create Abstractions/ interfaces
└── 1.4 Add to Constants.cs any missing values

Phase 2: Testability Extraction (Internal Refactoring)
├── 2.1 Create VariableInterpolator (fixes ScriptRunner tests)
├── 2.2 Update ScriptRunner to use VariableInterpolator
├── 2.3 Update HandlerBase to use ArgHelpers
└── 2.4 Add unit tests for Utilities/

Phase 3: Session Manager Refactor (Service Extraction)
├── 3.1 Create Services/ directory
├── 3.2 Extract IElementCache + ElementCache
├── 3.3 Extract IProcessContext + ProcessContext
├── 3.4 Extract ISnapshotCache + SnapshotCache
├── 3.5 Extract IEventService + EventService
├── 3.6 Extract IConfirmationService + ConfirmationService
├── 3.7 Extract ITreeExpansionService + TreeExpansionService
├── 3.8 SessionManager becomes thin facade delegating to services
└── 3.9 Add unit tests for each service

Phase 4: Input Injection Refactor (Class Splitting)
├── 4.1 Create Input/ directory
├── 4.2 Extract TouchInput class (uses Interop/, CoordinateMath)
├── 4.3 Extract PenInput class
├── 4.4 Extract MouseInput class (wraps FlaUI)
├── 4.5 InputInjection becomes facade or deleted
├── 4.6 Update TouchPenHandlers to use new classes
└── 4.7 Update InputHandlers to use new classes

Phase 5: Windows Array Scoping (Response Filtering)
├── 5.1 Add IProcessTracker service (tracks PIDs of interest)
├── 5.2 Add WindowFilter utility (filters windows by process set)
├── 5.3 Update WindowManager.GetAllWindows() to accept optional filter
├── 5.4 Update ToolResponse to use filtered windows
├── 5.5 Update launch_app to scope to launched process
├── 5.6 Update element operations to scope to element's process
└── 5.7 Add includeAllWindows parameter to tools

Phase 6: Reconnect Architecture (Bridge Changes)
├── 6.1 Update mcp-sandbox-bridge.ps1 connection sequence
├── 6.2 Add connection state machine
├── 6.3 Update reconnect_sandbox tool
├── 6.4 Add hot-reload detection
└── 6.5 Update signal file format
```

---

## Part 3: Target Architecture

### 3.1 Directory Structure After Refactoring

```
src/Rhombus.WinFormsMcp.Server/
├── Abstractions/               # Interfaces for mocking
│   ├── IProcessChecker.cs
│   ├── IWindowProvider.cs
│   ├── ITimeProvider.cs
│   └── IDpiProvider.cs
│
├── Automation/                 # FlaUI wrappers (existing, modified)
│   ├── AutomationHelper.cs
│   ├── AutomationTypes.cs
│   ├── DpiHelper.cs
│   ├── StateChangeDetector.cs
│   ├── TreeBuilder.cs
│   ├── TreeCache.cs
│   └── WindowManager.cs        # Updated to support filtered queries
│
├── Handlers/                   # MCP tool handlers (existing, modified)
│   ├── AdvancedHandlers.cs
│   ├── ElementHandlers.cs
│   ├── HandlerBase.cs          # Updated to use ArgHelpers
│   ├── InputHandlers.cs        # Updated to use Input/MouseInput
│   ├── IToolHandler.cs
│   ├── ObservationHandlers.cs
│   ├── ProcessHandlers.cs
│   ├── SandboxHandlers.cs
│   ├── ScreenshotHandlers.cs
│   ├── TouchPenHandlers.cs     # Updated to use Input/Touch+PenInput
│   ├── ValidationHandlers.cs
│   └── WindowHandlers.cs
│
├── Input/                      # NEW: Input injection classes
│   ├── TouchInput.cs           # Touch injection (tap, drag, pinch, rotate)
│   ├── PenInput.cs             # Pen injection (stroke, tap, pressure)
│   ├── MouseInput.cs           # Mouse injection (click, drag, path)
│   └── InputFacade.cs          # Optional: backwards-compat static API
│
├── Interop/                    # NEW: Win32 P/Invoke
│   ├── Win32Types.cs
│   ├── Win32Constants.cs
│   ├── InputInterop.cs
│   ├── WindowInterop.cs
│   └── DpiInterop.cs
│
├── Models/                     # Data models (existing)
│   └── WindowInfo.cs
│
├── Protocol/                   # MCP protocol (existing)
│   ├── McpProtocol.cs
│   └── ToolDefinitions.cs
│
├── Sandbox/                    # Sandbox management (existing)
│   ├── SandboxManager.cs
│   └── WsbConfigBuilder.cs
│
├── Script/                     # Script execution (existing, modified)
│   └── ScriptRunner.cs         # Updated to use VariableInterpolator
│
├── Services/                   # NEW: Extracted services
│   ├── ElementCache.cs         # IElementCache implementation
│   ├── ProcessContext.cs       # IProcessContext implementation
│   ├── SnapshotCache.cs        # ISnapshotCache implementation
│   ├── EventService.cs         # IEventService implementation
│   ├── ConfirmationService.cs  # IConfirmationService implementation
│   ├── TreeExpansionService.cs # ITreeExpansionService implementation
│   └── ProcessTracker.cs       # IProcessTracker implementation (windows-array)
│
├── Utilities/                  # NEW: Pure testable utilities
│   ├── ArgHelpers.cs
│   ├── VariableInterpolator.cs
│   ├── CoordinateMath.cs
│   └── TokenEstimator.cs
│
├── Constants.cs                # Centralized constants (existing, extended)
├── Program.cs                  # Entry point + SessionManager facade
└── ToolResponse.cs             # Response formatting (existing, modified)
```

### 3.2 Class Diagram (Key Relationships)

```
┌─────────────────────────────────────────────────────────────────────┐
│                           Handlers Layer                             │
├─────────────────────────────────────────────────────────────────────┤
│  HandlerBase ←── ElementHandlers, ProcessHandlers, InputHandlers,   │
│                  TouchPenHandlers, etc.                              │
│                                                                      │
│  Dependencies:                                                       │
│    • SessionManager (facade) → delegates to Services                 │
│    • WindowManager → queries, filtering                              │
│    • ArgHelpers (static) → argument parsing                          │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│                          Services Layer                              │
├─────────────────────────────────────────────────────────────────────┤
│  IElementCache ─────────► ElementCache                               │
│  IProcessContext ───────► ProcessContext                             │
│  ISnapshotCache ────────► SnapshotCache                              │
│  IEventService ─────────► EventService                               │
│  IConfirmationService ──► ConfirmationService                        │
│  ITreeExpansionService ─► TreeExpansionService                       │
│  IProcessTracker ───────► ProcessTracker (windows-array scoping)     │
│                                                                      │
│  Each service:                                                       │
│    • Has single responsibility                                       │
│    • Thread-safe (ConcurrentDictionary or locks)                     │
│    • Independently testable                                          │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│                          Input Layer                                 │
├─────────────────────────────────────────────────────────────────────┤
│  TouchInput ────────────► Interop/InputInterop                       │
│  PenInput ──────────────► Interop/InputInterop                       │
│  MouseInput ────────────► FlaUI.Core.Input.Mouse                     │
│                                                                      │
│  Each uses:                                                          │
│    • CoordinateMath for calculations                                 │
│    • IDpiProvider for DPI queries                                    │
│    • Win32Types for structs                                          │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│                        Utilities Layer                               │
├─────────────────────────────────────────────────────────────────────┤
│  ArgHelpers (static) ───► JSON argument parsing                      │
│  VariableInterpolator ──► Type-preserving JSON interpolation         │
│  CoordinateMath (static)► Pure coordinate calculations               │
│  TokenEstimator (static)► UI tree budget estimation                  │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Part 4: Key Interfaces

### 4.1 Service Interfaces (Session Manager Extraction)

```csharp
// Services/IElementCache.cs
public interface IElementCache
{
    /// <summary>Cache an element and return its ID (elem_N).</summary>
    string Cache(AutomationElement element);

    /// <summary>Get cached element by ID, or null if not found.</summary>
    AutomationElement? Get(string elementId);

    /// <summary>Remove a single cached element.</summary>
    void Clear(string elementId);

    /// <summary>Remove all cached elements.</summary>
    void ClearAll();

    /// <summary>Check if an element reference is stale.</summary>
    bool IsStale(string elementId);
}

// Services/IProcessContext.cs
public interface IProcessContext
{
    /// <summary>Track a launched app. Returns previous PID if any.</summary>
    int? TrackLaunchedApp(string exePath, int pid);

    /// <summary>Get previously tracked PID for an exe path.</summary>
    int? GetPreviousLaunchedPid(string exePath);

    /// <summary>Stop tracking an app.</summary>
    void UntrackLaunchedApp(string exePath);

    /// <summary>Get all tracked PIDs.</summary>
    IReadOnlyCollection<int> GetTrackedPids();
}

// Services/IEventService.cs
public interface IEventService
{
    void Subscribe(IEnumerable<string> eventTypes);
    IReadOnlyCollection<string> GetSubscribedEventTypes();
    bool HasSubscriptions { get; }
    void Enqueue(UiEvent evt);
    (List<UiEvent> events, int droppedCount) Drain();
}

// Services/IConfirmationService.cs
public interface IConfirmationService
{
    PendingConfirmation Create(string action, string description, string? target, JsonElement? parameters);
    PendingConfirmation? Consume(string token);
}

// Services/ISnapshotCache.cs
public interface ISnapshotCache
{
    void Cache(string snapshotId, TreeSnapshot snapshot);
    TreeSnapshot? Get(string snapshotId);
    void Clear(string snapshotId);
    void ClearAll();
}

// Services/ITreeExpansionService.cs
public interface ITreeExpansionService
{
    void Mark(string elementKey);
    bool IsMarked(string elementKey);
    IReadOnlyCollection<string> GetAll();
    void Clear(string elementKey);
    void ClearAll();
}
```

### 4.2 Process Tracking Interface (Windows Array Scoping)

```csharp
// Services/IProcessTracker.cs
public interface IProcessTracker
{
    /// <summary>Add a PID to the tracked set.</summary>
    void Track(int pid);

    /// <summary>Remove a PID from the tracked set.</summary>
    void Untrack(int pid);

    /// <summary>Check if a PID is tracked.</summary>
    bool IsTracked(int pid);

    /// <summary>Get all tracked PIDs.</summary>
    IReadOnlySet<int> GetTrackedPids();

    /// <summary>Clear all tracked PIDs.</summary>
    void Clear();
}
```

### 4.3 Abstraction Interfaces (Testability)

```csharp
// Abstractions/ITimeProvider.cs
public interface ITimeProvider
{
    DateTime UtcNow { get; }
}

public class SystemTimeProvider : ITimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}

// Abstractions/IProcessChecker.cs
public interface IProcessChecker
{
    bool IsProcessRunning(int pid);
    int? GetProcessId(string processName);
}

// Abstractions/IDpiProvider.cs
public interface IDpiProvider
{
    (int dpiX, int dpiY) GetSystemDpi();
    (int dpiX, int dpiY) GetWindowDpi(IntPtr hwnd);
    int GetVirtualScreenOriginX();
    int GetVirtualScreenOriginY();
}

// Abstractions/IWindowProvider.cs
public interface IWindowProvider
{
    List<WindowInfo> GetAllWindows();
    List<WindowInfo> GetWindowsByPids(IReadOnlySet<int> pids);
    WindowInfo? FindByHandle(string handleHex);
    WindowInfo? FindByTitle(string titleSubstring);
}
```

---

## Part 5: Concrete Files to Create/Modify

### Phase 1: Foundation

**Create:**
- `src/.../Utilities/ArgHelpers.cs`
- `src/.../Utilities/CoordinateMath.cs`
- `src/.../Utilities/TokenEstimator.cs`
- `src/.../Interop/Win32Types.cs`
- `src/.../Interop/Win32Constants.cs`
- `src/.../Interop/InputInterop.cs`
- `src/.../Interop/WindowInterop.cs`
- `src/.../Interop/DpiInterop.cs`
- `src/.../Abstractions/ITimeProvider.cs`
- `src/.../Abstractions/IProcessChecker.cs`
- `src/.../Abstractions/IDpiProvider.cs`
- `src/.../Abstractions/IWindowProvider.cs`

**Modify:**
- `src/.../Constants.cs` - Add any missing constants from InputInjection

### Phase 2: Testability Extraction

**Create:**
- `src/.../Utilities/VariableInterpolator.cs`
- `tests/.../VariableInterpolatorTests.cs`
- `tests/.../ArgHelpersTests.cs`
- `tests/.../CoordinateMathTests.cs`

**Modify:**
- `src/.../Script/ScriptRunner.cs` - Use VariableInterpolator
- `src/.../Handlers/HandlerBase.cs` - Use ArgHelpers

### Phase 3: Session Manager Refactor

**Create:**
- `src/.../Services/ElementCache.cs`
- `src/.../Services/ProcessContext.cs`
- `src/.../Services/SnapshotCache.cs`
- `src/.../Services/EventService.cs`
- `src/.../Services/ConfirmationService.cs`
- `src/.../Services/TreeExpansionService.cs`
- `tests/.../ElementCacheTests.cs`
- `tests/.../ProcessContextTests.cs`
- `tests/.../EventServiceTests.cs`
- `tests/.../ConfirmationServiceTests.cs`

**Modify:**
- `src/.../Program.cs` - SessionManager delegates to services

### Phase 4: Input Injection Refactor

**Create:**
- `src/.../Input/TouchInput.cs`
- `src/.../Input/PenInput.cs`
- `src/.../Input/MouseInput.cs`
- `src/.../Input/InputFacade.cs` (optional)
- `tests/.../CoordinateMathTests.cs` (extend)

**Modify:**
- `src/.../Handlers/TouchPenHandlers.cs` - Use Input/ classes
- `src/.../Handlers/InputHandlers.cs` - Use Input/MouseInput

**Delete (eventually):**
- `src/.../Automation/InputInjection.cs` - After migration complete

### Phase 5: Windows Array Scoping

**Create:**
- `src/.../Services/ProcessTracker.cs`
- `src/.../Utilities/WindowFilter.cs`
- `tests/.../ProcessTrackerTests.cs`
- `tests/.../WindowFilterTests.cs`

**Modify:**
- `src/.../Automation/WindowManager.cs` - Add filtered query methods
- `src/.../ToolResponse.cs` - Support scoped windows
- `src/.../Handlers/ProcessHandlers.cs` - Track PIDs on launch
- `src/.../Handlers/ElementHandlers.cs` - Scope to element's process

### Phase 6: Reconnect Architecture

**Modify:**
- `mcp-sandbox-bridge.ps1` - Connection state machine, signal-driven
- `src/.../Handlers/SandboxHandlers.cs` - Update reconnect_sandbox
- `sandbox/bootstrap.ps1` - Signal file format updates

---

## Part 6: Migration Strategy

### 6.1 Backwards Compatibility

**SessionManager Facade Pattern:**
```csharp
// During migration, SessionManager delegates to services but keeps same API
class SessionManager
{
    private readonly IElementCache _elementCache;
    private readonly IProcessContext _processContext;
    // ... other services

    public SessionManager()
    {
        _elementCache = new ElementCache();
        _processContext = new ProcessContext();
        // ...
    }

    // Existing API - handlers continue to work
    public string CacheElement(AutomationElement element)
        => _elementCache.Cache(element);

    public AutomationElement? GetElement(string elementId)
        => _elementCache.Get(elementId);

    // ... delegate all existing methods
}
```

**InputInjection Facade Pattern:**
```csharp
// Optional: Keep static API working during migration
public static class InputInjection
{
    private static readonly TouchInput _touch = new();
    private static readonly PenInput _pen = new();
    private static readonly MouseInput _mouse = new();

    // Existing static methods delegate to instances
    public static bool TouchTap(int x, int y, ...)
        => _touch.Tap(x, y, ...);

    public static bool PenStroke(int x1, int y1, int x2, int y2, ...)
        => _pen.Stroke(x1, y1, x2, y2, ...);
}
```

### 6.2 Test Strategy

**Unit Tests (Pure Logic):**
- ArgHelpers: All overloads, null handling, type coercion
- VariableInterpolator: Type preservation, path resolution, error messages
- CoordinateMath: DPI scaling, HIMETRIC conversion, window translation
- TokenEstimator: Budget calculations, element counting
- All Services: Thread safety, edge cases, state transitions

**Integration Tests (Thin, Windows-Required):**
- Services wired correctly to handlers
- Input classes actually inject events
- Windows enumeration works
- FlaUI integration intact

### 6.3 Rollback Plan

Each phase is independently deployable:
1. If Phase N breaks, revert Phase N commits
2. Facade patterns mean old code paths remain callable
3. No MCP tool API changes means agents unaffected

---

## Part 7: Success Criteria

### Testability Metrics

| Metric | Before | After | Target |
|--------|--------|-------|--------|
| Unit-testable code % | ~30% | ~60% | 50%+ |
| Failing tests | 3 | 0 | 0 |
| New unit tests | 0 | 50+ | 50+ |
| Avg lines per class | 400+ | <200 | <200 |

### Architecture Metrics

| Metric | Before | After | Target |
|--------|--------|-------|--------|
| SessionManager responsibilities | 11 | 1 (facade) | 1 |
| InputInjection lines | 1511 | deleted | <400/class |
| Shared Win32 types | duplicated | Interop/ | single source |
| Services with interfaces | 0 | 7 | 7 |

### Backwards Compatibility

| Requirement | Verification |
|-------------|-------------|
| No MCP tool API changes | Tool schema unchanged |
| Handlers compile without changes during migration | Build succeeds |
| All existing E2E tests pass | CI green |
| Window array still in responses | Response schema unchanged |

---

## Part 8: Open Questions

### Resolved

1. **Q: Use DI container?** A: No. Manual composition keeps complexity low.
2. **Q: Where do interfaces live?** A: Same file as implementation (internal).
3. **Q: How to handle InputInjection's static state?** A: Singleton instances in facade.

### Pending

1. **Q: Should ProcessTracker be combined with ProcessContext?**
   - ProcessContext tracks launched apps by path
   - ProcessTracker tracks PIDs for window scoping
   - Recommendation: Keep separate, different responsibilities

2. **Q: Should VariableInterpolator support array indexing?**
   - `$step.result.items[0].name` syntax
   - Out of scope per run-script-behavior spec, but design for extensibility

3. **Q: How to handle reconnect_sandbox in stdio mode?**
   - Bridge is TCP-only, reconnect only makes sense for sandboxed TCP mode
   - Recommendation: Return error in stdio mode with clear message

---

## Appendix A: Constants.cs Additions

```csharp
// Add to Constants.cs for shared use

public static class Input
{
    /// <summary>Default contact size for touch injection.</summary>
    public const int TouchContactSize = 2;

    /// <summary>Default touch pressure value.</summary>
    public const uint TouchPressureDefault = 32000;

    /// <summary>Default pen pressure value (midpoint of 0-1024).</summary>
    public const uint PenPressureDefault = 512;

    /// <summary>Perpendicular orientation value.</summary>
    public const uint OrientationPerpendicular = 90;
}

public static class Pointer
{
    // Move POINTER_FLAG_* constants here from InputInjection
    public const uint FlagNone = 0x00000000;
    public const uint FlagNew = 0x00000001;
    public const uint FlagInRange = 0x00000002;
    public const uint FlagInContact = 0x00000004;
    // ... etc
}
```

---

## Appendix B: File Dependency Graph

```
VariableInterpolator
    └── (no dependencies - pure)

CoordinateMath
    └── Constants.Display

ArgHelpers
    └── (no dependencies - pure)

ElementCache
    ├── FlaUI.Core.AutomationElements
    └── ITimeProvider (optional, for staleness)

ProcessContext
    └── (no dependencies - pure collections)

EventService
    └── Constants.Queues

ConfirmationService
    ├── ITimeProvider
    └── Constants.Queues

ProcessTracker
    └── (no dependencies - pure collections)

TouchInput
    ├── Interop/InputInterop
    ├── Interop/Win32Types
    ├── CoordinateMath
    └── IDpiProvider

PenInput
    ├── Interop/InputInterop
    ├── Interop/Win32Types
    ├── CoordinateMath
    └── IDpiProvider

MouseInput
    ├── FlaUI.Core.Input.Mouse
    └── IWindowProvider (for coordinate translation)

WindowManager : IWindowProvider
    ├── Interop/WindowInterop
    └── ProcessTracker (optional, for filtering)

ScriptRunner
    ├── VariableInterpolator
    ├── ArgHelpers
    └── Constants.Timeouts

HandlerBase
    ├── ArgHelpers
    ├── SessionManager (facade)
    └── WindowManager

SessionManager (facade)
    ├── ElementCache
    ├── ProcessContext
    ├── SnapshotCache
    ├── EventService
    ├── ConfirmationService
    ├── TreeExpansionService
    ├── AutomationHelper
    └── SandboxManager
```
