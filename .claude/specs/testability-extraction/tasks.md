# Tasks: Testability Extraction

> **⚠️ ARCHIVED**: This spec has been superseded by `unified-refactor/`. All tasks from this spec were consolidated and completed in the unified refactoring plan.

Cross-cutting tasks to extract unit-testable code from Windows-dependent modules.

## Phase 1: Pure Utilities (PR-TESTABILITY-1)

### 1.1 Create Utilities Directory
- [ ] Create `src/Rhombus.WinFormsMcp.Server/Utilities/` folder

### 1.2 Extract ArgHelpers
- [ ] Create `Utilities/ArgHelpers.cs` with static methods:
  - `GetString(JsonElement, string) -> string?`
  - `GetInt(JsonElement, string, int) -> int`
  - `GetDouble(JsonElement, string, double) -> double`
  - `GetBool(JsonElement, string, bool) -> bool`
- [ ] Update `HandlerBase.cs` to call `ArgHelpers.*`
- [ ] Update `ScriptRunner.cs` to call `ArgHelpers.*`
- [ ] Create `tests/.../ArgHelpersTests.cs` with tests:
  - Null JsonElement returns default
  - Missing key returns default
  - Type mismatch returns default
  - Valid values extracted correctly
  - Boolean edge cases (True vs False vs missing)

### 1.3 Extract VariableInterpolator (CRITICAL - fixes broken tests)
- [ ] Create `Utilities/VariableInterpolator.cs`:
  - `Interpolate(JsonElement args, IReadOnlyDictionary<string, JsonElement> results, string? lastStepId) -> JsonElement`
  - `IsVariableReference(string value, out string stepId, out string path) -> bool`
  - `ResolveVariable(string stepId, string path, results) -> JsonElement`
- [ ] Use JSON DOM traversal, NOT string regex replacement
- [ ] Preserve types: numbers stay numbers, bools stay bools
- [ ] Update `ScriptRunner.InterpolateArgs()` to delegate to new class
- [ ] Create `tests/.../VariableInterpolatorTests.cs` with tests:
  - String values preserved as strings
  - Numeric values preserved as numbers (fix TestVariableInterpolation_NumericValue)
  - Boolean values preserved as bools (fix TestVariableInterpolation_BooleanValue)
  - Nested paths resolve correctly (fix TestVariableInterpolation_NestedPath)
  - Arrays containing variable refs work
  - Missing step throws with available steps list
  - Missing property throws with property name
  - $last alias resolves correctly
  - First step cannot use $last

### 1.4 Extract CoordinateMath
- [ ] Create `Utilities/CoordinateMath.cs`:
  - `PixelToHimetric(int x, int y, int dpi = 96) -> (int, int)`
  - `HimetricToPixel(int x, int y, int dpi = 96) -> (int, int)`
  - `ScreenToWindow(int x, int y, Rectangle bounds) -> (int, int)`
  - `WindowToScreen(int x, int y, Rectangle bounds) -> (int, int)`
  - `CalculateDpiScale(int dpi) -> double`
- [ ] Update `InputInjection.cs` to call these methods
- [ ] Create `tests/.../CoordinateMathTests.cs` with tests:
  - 96 DPI (1.0 scale) conversions
  - 144 DPI (1.5 scale) conversions
  - 192 DPI (2.0 scale) conversions
  - Negative coordinates (multi-monitor virtual screen)
  - Window offset calculations

### 1.5 Extract TokenEstimator
- [ ] Create `Utilities/TokenEstimator.cs`:
  - `EstimateFromCharCount(string text, int charsPerToken = 4) -> int`
  - `EstimateFromXml(string xml) -> int` (considers structure)
  - `ExceedsBudget(string text, int budget) -> bool`
- [ ] Update `TreeBuilder.cs` to use TokenEstimator
- [ ] Create `tests/.../TokenEstimatorTests.cs`

## Phase 2: Interface-Based Services (PR-TESTABILITY-2)

### 2.1 Create Services Directory
- [ ] Create `src/Rhombus.WinFormsMcp.Server/Services/` folder

### 2.2 Extract ConfirmationService
- [ ] Create `Services/IConfirmationService.cs` interface
- [ ] Create `Services/ConfirmationService.cs` with:
  - Injectable `Func<DateTime>` for clock (testability)
  - `Create(action, description, target, parameters) -> token`
  - `Consume(token) -> PendingConfirmation?`
  - `Cleanup()` - removes expired
  - Thread-safe with lock
- [ ] Create `tests/.../ConfirmationServiceTests.cs`:
  - Token generation is unique
  - Consume returns confirmation and removes it
  - Expired tokens return null
  - Cleanup removes only expired
  - Concurrent Create/Consume is safe

### 2.3 Extract ElementFilter
- [ ] Create `Models/ElementInfo.cs` - pure data model
- [ ] Create `Utilities/ElementFilter.cs`:
  - `ShouldInclude(ElementInfo, TreeBuilderOptions) -> bool`
  - `IsInternalPart(string automationId) -> bool`
  - `IsEffectivelyDisabled(ElementInfo, hasEnabledChildren) -> bool`
- [ ] Update `TreeBuilder.ShouldIncludeElement()` to:
  1. Extract ElementInfo from AutomationElement (thin)
  2. Call `ElementFilter.ShouldInclude(info, options)` (testable)
- [ ] Create `tests/.../ElementFilterTests.cs`:
  - PART_* elements filtered when option set
  - Offscreen elements filtered when option set
  - Disabled containers with no enabled children filtered

### 2.4 Extract ProcessTracker
- [ ] Create `Services/IProcessChecker.cs` interface:
  - `IsProcessAlive(int pid) -> bool`
- [ ] Create `Services/ProcessChecker.cs` (Windows implementation)
- [ ] Create `Services/ProcessTracker.cs` with logic:
  - `Track(int pid)`
  - `Untrack(int pid)`
  - `GetTracked() -> IReadOnlyCollection<int>`
  - `GetAlive(IProcessChecker) -> IReadOnlyCollection<int>` (filters dead)
- [ ] Create `tests/.../ProcessTrackerTests.cs` with mock IProcessChecker:
  - Track adds to set
  - Untrack removes from set
  - GetAlive filters using checker
  - Duplicate track is idempotent

## Phase 3: Update Existing Specs

### 3.1 Update run_script tasks
- [ ] Edit `.claude/specs/run-script-behavior/tasks.md`
- [ ] Ensure VariableInterpolator is primary focus
- [ ] Unit tests cover all type preservation scenarios

### 3.2 Update InputInjection tasks
- [ ] Edit `.claude/specs/input-injection-refactor/tasks.md`
- [ ] Add CoordinateMath extraction as first step
- [ ] Unit tests for coordinate math before refactoring classes

### 3.3 Update SessionManager tasks
- [ ] Edit `.claude/specs/session-manager-refactor/tasks.md`
- [ ] Add ConfirmationService with injectable clock
- [ ] Add ProcessTracker with IProcessChecker

## Verification

### Unit Test Coverage Goals
- [ ] ArgHelpers: 100% branch coverage
- [ ] VariableInterpolator: 100% branch coverage (fixes 3 broken tests)
- [ ] CoordinateMath: 100% branch coverage
- [ ] TokenEstimator: 100% branch coverage
- [ ] ConfirmationService: 100% branch coverage
- [ ] ElementFilter: 100% branch coverage
- [ ] ProcessTracker: 100% branch coverage

### Integration Test Scope (Thin)
- [ ] FlaUI element access works
- [ ] Win32 P/Invoke calls work
- [ ] Process launch/close works
- [ ] Screenshot capture works
- [ ] TCP/stdio communication works

Integration tests should NOT test business logic - that's unit tested.
