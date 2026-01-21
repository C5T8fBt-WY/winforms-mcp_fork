# Design: Testability Extraction

## Overview

This document identifies pure logic that can be extracted from Windows-dependent code for unit testing. The goal is to maximize unit-testable surface area while keeping integration tests thin.

## Analysis of Current Codebase

### High-Value Extraction Targets

#### 1. ScriptRunner Variable Interpolation (CRITICAL)
**Current**: `InterpolateArgs()` at lines 302-376 is regex-based string manipulation
**Problem**: Loses type information, causes 3 failing tests
**Extraction**: New `VariableInterpolator` class with pure JSON DOM manipulation

```csharp
// FULLY UNIT-TESTABLE - no Windows dependencies
public static class VariableInterpolator
{
    public static JsonElement Interpolate(
        JsonElement args,
        IReadOnlyDictionary<string, JsonElement> stepResults,
        string? lastStepId)

    public static bool IsVariableReference(string value, out string stepId, out string path)

    public static JsonElement ResolveVariable(
        string stepId,
        string path,
        IReadOnlyDictionary<string, JsonElement> results)
}
```

**Test cases**: 20+ covering type preservation, nested paths, arrays, errors

#### 2. Argument Helpers (Shared)
**Current**: Duplicated in HandlerBase (lines 37-77) and ScriptRunner (lines 391-424)
**Extraction**: `ArgHelpers` static class

```csharp
// FULLY UNIT-TESTABLE
public static class ArgHelpers
{
    public static string? GetString(JsonElement args, string key)
    public static int GetInt(JsonElement args, string key, int defaultValue = 0)
    public static double GetDouble(JsonElement args, string key, double defaultValue = 0)
    public static bool GetBool(JsonElement args, string key, bool defaultValue = false)
    public static T? GetEnum<T>(JsonElement args, string key) where T : struct, Enum
}
```

**Test cases**: 15+ covering null handling, type coercion, defaults

#### 3. Coordinate Math (InputInjection)
**Current**: Embedded in InputInjection.cs god class
**Extraction**: `CoordinateMath` static class

```csharp
// FULLY UNIT-TESTABLE - pure math
public static class CoordinateMath
{
    public static (int x, int y) PixelToHimetric(int pixelX, int pixelY, int dpi = 96)
    public static (int x, int y) ScreenToWindow(int screenX, int screenY, Rectangle windowBounds)
    public static (int x, int y) WindowToScreen(int windowX, int windowY, Rectangle windowBounds)
    public static double CalculateDpiScale(int dpi) => dpi / 96.0;
}
```

**Test cases**: 10+ covering DPI scaling, multi-monitor offsets, edge cases

#### 4. Token Estimation (TreeBuilder)
**Current**: `EstimateTokens()` at line 298-302 is trivial
**Extraction**: Expand into `TokenEstimator` with configurable algorithms

```csharp
// FULLY UNIT-TESTABLE
public static class TokenEstimator
{
    public static int EstimateFromCharCount(string text, int charsPerToken = 4)
    public static int EstimateFromXml(string xml) // structural estimation
    public static bool ExceedsBudget(string text, int budget)
}
```

#### 5. Tree Filtering Logic (TreeBuilder)
**Current**: `ShouldIncludeElement()` mixes logic with AutomationElement access
**Extraction**: Separate model from FlaUI

```csharp
// UNIT-TESTABLE model
public record ElementInfo(
    string AutomationId,
    string Name,
    string ClassName,
    string ControlType,
    bool IsEnabled,
    bool IsOffscreen,
    Rectangle Bounds);

// UNIT-TESTABLE filter
public static class ElementFilter
{
    public static bool ShouldInclude(ElementInfo element, TreeBuilderOptions options)
    public static bool IsInternalPart(string automationId)
        => automationId?.StartsWith("PART_", StringComparison.OrdinalIgnoreCase) == true;
}
```

#### 6. Process Tracking Logic (SessionManager)
**Current**: Mixed with Windows Process API calls
**Extraction**: Pure tracking logic with interface

```csharp
// UNIT-TESTABLE interface
public interface IProcessChecker
{
    bool IsProcessAlive(int pid);
}

// UNIT-TESTABLE logic
public class ProcessTracker
{
    private readonly HashSet<int> _tracked = new();
    private readonly IProcessChecker _checker;

    public void Track(int pid) { ... }
    public void Untrack(int pid) { ... }
    public IReadOnlyCollection<int> GetAlive() // filters out dead processes
}
```

#### 7. Confirmation Token Management
**Current**: In AdvancedHandlers mixed with handler logic
**Extraction**: Pure service with injectable time

```csharp
// UNIT-TESTABLE with injectable time
public class ConfirmationService
{
    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _expiry;
    private readonly Dictionary<string, PendingConfirmation> _pending;

    public ConfirmationService(Func<DateTime>? clock = null, TimeSpan? expiry = null)

    public string Create(string action, string description, string? target, JsonElement? parameters)
    public PendingConfirmation? Consume(string token)
    public void Cleanup() // removes expired
}
```

**Test cases**: Expiration, cleanup, concurrent access

### Abstraction Interfaces

For code that MUST interact with Windows but needs testability:

```csharp
// Abstract Windows enumeration
public interface IWindowProvider
{
    IEnumerable<WindowInfo> GetAllWindows();
    IEnumerable<WindowInfo> GetWindowsByPid(int pid);
    IEnumerable<WindowInfo> GetWindowsByPids(IEnumerable<int> pids);
}

// Abstract process operations
public interface IProcessProvider
{
    int? LaunchApp(string path, string? args, string? workingDir);
    bool IsProcessAlive(int pid);
    bool CloseProcess(int pid, bool force);
}

// Abstract element operations
public interface IElementProvider
{
    ElementInfo? FindElement(FindCriteria criteria);
    IEnumerable<ElementInfo> FindAllElements(FindCriteria criteria);
    bool ClickElement(string elementId);
    bool TypeText(string elementId, string text);
}
```

## Refactoring Strategy

### Phase 1: Extract Pure Utilities (No Breaking Changes)
1. Create `Utilities/` folder
2. Extract ArgHelpers, CoordinateMath, TokenEstimator, VariableInterpolator
3. Update existing code to call utilities
4. Add comprehensive unit tests

### Phase 2: Extract Testable Services (Interface-Based)
1. Create `Services/` folder with interfaces
2. Implement ConfirmationService, ProcessTracker, ElementFilter
3. Update handlers to use DI-ready services
4. Add unit tests with mocks

### Phase 3: Thin Integration Tests
1. Integration tests only verify Windows API wiring
2. Business logic already covered by unit tests
3. E2E tests focus on end-to-end workflows only

## File Structure

```
src/Rhombus.WinFormsMcp.Server/
├── Utilities/                    # PURE, UNIT-TESTABLE
│   ├── ArgHelpers.cs
│   ├── CoordinateMath.cs
│   ├── TokenEstimator.cs
│   └── VariableInterpolator.cs
├── Services/                     # INTERFACE-BASED, MOCKABLE
│   ├── IConfirmationService.cs
│   ├── ConfirmationService.cs
│   ├── IProcessTracker.cs
│   ├── ProcessTracker.cs
│   ├── IElementCache.cs
│   └── ElementCache.cs
├── Models/                       # DATA MODELS, TESTABLE
│   ├── ElementInfo.cs
│   ├── FindCriteria.cs
│   └── TreeNode.cs
└── Automation/                   # WINDOWS-DEPENDENT (thin)
    └── ...
```

## Expected Coverage Improvement

| Area | Before | After |
|------|--------|-------|
| Variable Interpolation | 0% unit | 100% unit |
| Arg Helpers | 0% unit | 100% unit |
| Coordinate Math | 0% unit | 100% unit |
| Token Estimation | 0% unit | 100% unit |
| Element Filtering | 0% unit | 80% unit |
| Confirmation Logic | 0% unit | 100% unit |
| Process Tracking | 0% unit | 90% unit |

**Overall**: Estimate 40-50% of business logic becomes unit-testable.
