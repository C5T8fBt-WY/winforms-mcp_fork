# run_script Tool Behavior Tasks

> **⚠️ ARCHIVED**: This spec has been superseded by `unified-refactor/`. All tasks from this spec were consolidated and completed in the unified refactoring plan.

## Overview

This checklist implements the AST-based variable interpolation system to replace the broken regex-based approach. The primary goal is to fix the type-preservation problem where numeric, boolean, and other non-string values are incorrectly interpolated as strings.

## Key Design Decisions

### Mixed Interpolation (Embedded Variables)
Embedded variable references within strings (e.g., `"prefix_$step.result.id_suffix"`) are **NOT supported**. Only exact matches where the entire string value is a variable reference are interpolated. Strings with embedded references are passed through unchanged.

### Variable References in Arrays
Variable references in arrays are fully supported. Each array element is processed recursively, allowing:
```json
["$step1.result.a", "$step2.result.b", "static"]
```

### Timeout Semantics
The `timeout_ms` option represents **wall-clock time** for the entire script execution, not per-step timeout. The timeout check occurs before each step begins, not during tool execution.

---

## Phase 1: Create VariableInterpolator Class

### Task 1.1: Create VariableInterpolator.cs skeleton
- [ ] **File**: `src/Rhombus.WinFormsMcp.Server/Script/VariableInterpolator.cs`
- [ ] Create internal static class with XML documentation
- [ ] Add required using statements: `System.Text.Json`, `System.Text.RegularExpressions`, `System.IO`
- [ ] Define compiled regex pattern: `^\$(\w+)((?:\.\w+)+)$` for exact match detection

### Task 1.2: Implement IsVariableReference method
- [ ] **File**: `src/Rhombus.WinFormsMcp.Server/Script/VariableInterpolator.cs`
- [ ] Method signature: `private static bool IsVariableReference(string value, out string stepId, out string path)`
- [ ] Return true only for exact matches (entire string is a variable reference)
- [ ] Extract stepId and path segments from regex groups

### Task 1.3: Implement ResolveVariable method
- [ ] **File**: `src/Rhombus.WinFormsMcp.Server/Script/VariableInterpolator.cs`
- [ ] Method signature: `private static JsonElement ResolveVariable(string stepId, string path, IReadOnlyDictionary<string, JsonElement> stepResults, string? lastStepId)`
- [ ] Handle `$last` alias: resolve to lastStepId or throw if null
- [ ] Navigate path segments through nested objects
- [ ] Throw `InvalidOperationException` with specific error messages per REQ-6.1:
  - Step not found: `"Step '{stepId}' not found. Available steps: {list}"`
  - Property not found: `"Property '{segment}' not found in ${stepId}.{navigatedPath}"`
  - Non-object navigation: `"Cannot access '{segment}' on non-object value at ${stepId}.{navigatedPath}"`
  - $last without previous: `"Cannot use $last in first step - no previous step exists"`

### Task 1.4: Implement Interpolate entry point
- [ ] **File**: `src/Rhombus.WinFormsMcp.Server/Script/VariableInterpolator.cs`
- [ ] Method signature: `public static JsonElement Interpolate(JsonElement args, IReadOnlyDictionary<string, JsonElement> stepResults, string? lastStepId)`
- [ ] Handle Undefined/Null ValueKind by returning input unchanged
- [ ] Switch on ValueKind to dispatch to Object/Array/String handlers
- [ ] Pass through Number, True, False, Null unchanged with `.Clone()`

### Task 1.5: Implement InterpolateObject method
- [ ] **File**: `src/Rhombus.WinFormsMcp.Server/Script/VariableInterpolator.cs`
- [ ] Create new JSON object with interpolated property values
- [ ] Use `MemoryStream` + `Utf8JsonWriter` pattern from design
- [ ] Recursively call `Interpolate` for each property value
- [ ] Return parsed result detached from source document

### Task 1.6: Implement InterpolateArray method
- [ ] **File**: `src/Rhombus.WinFormsMcp.Server/Script/VariableInterpolator.cs`
- [ ] Create new JSON array with interpolated elements
- [ ] Use `MemoryStream` + `Utf8JsonWriter` pattern
- [ ] Recursively call `Interpolate` for each array element
- [ ] Return parsed result detached from source document

### Task 1.7: Implement InterpolateString method
- [ ] **File**: `src/Rhombus.WinFormsMcp.Server/Script/VariableInterpolator.cs`
- [ ] Check if entire string is a variable reference using `IsVariableReference`
- [ ] If not a variable reference, return input unchanged with `.Clone()`
- [ ] If variable reference, call `ResolveVariable` and return the result (preserves original type)

---

## Phase 2: Update ScriptRunner Integration

### Task 2.1: Replace InterpolateArgs implementation
- [ ] **File**: `src/Rhombus.WinFormsMcp.Server/Script/ScriptRunner.cs`
- [ ] Remove existing regex-based `InterpolateArgs` method body
- [ ] Replace with single call to `VariableInterpolator.Interpolate()`
- [ ] Keep method signature unchanged for backward compatibility

### Task 2.2: Remove obsolete helper methods
- [ ] **File**: `src/Rhombus.WinFormsMcp.Server/Script/ScriptRunner.cs`
- [ ] Remove `EscapeJsonString` method (no longer needed)
- [ ] Verify no other callers depend on removed methods

---

## Phase 3: Create Unit Tests for VariableInterpolator

### Task 3.1: Create VariableInterpolatorTests.cs
- [ ] **File**: `tests/Rhombus.WinFormsMcp.Tests/VariableInterpolatorTests.cs`
- [ ] Add required using statements
- [ ] Create test fixture with NUnit attributes

### Task 3.2: Implement type preservation tests
- [ ] **File**: `tests/Rhombus.WinFormsMcp.Tests/VariableInterpolatorTests.cs`
- [ ] Test: String value -> `JsonValueKind.String`
- [ ] Test: Numeric value -> `JsonValueKind.Number`, verify `GetInt32()` works
- [ ] Test: Boolean true -> `JsonValueKind.True`
- [ ] Test: Boolean false -> `JsonValueKind.False`
- [ ] Test: Null value -> `JsonValueKind.Null`
- [ ] Test: Object value -> `JsonValueKind.Object`, verify nested properties
- [ ] Test: Array value -> `JsonValueKind.Array`, verify elements

### Task 3.3: Implement path navigation tests
- [ ] **File**: `tests/Rhombus.WinFormsMcp.Tests/VariableInterpolatorTests.cs`
- [ ] Test: Single segment path (`$step.result`)
- [ ] Test: Multi-segment path (`$step.result.a.b.c.d`)
- [ ] Test: `$last` alias resolves to last step result

### Task 3.4: Implement error condition tests
- [ ] **File**: `tests/Rhombus.WinFormsMcp.Tests/VariableInterpolatorTests.cs`
- [ ] Test: Missing step throws with correct message
- [ ] Test: Missing property throws with correct message
- [ ] Test: Non-object navigation throws with correct message
- [ ] Test: `$last` in first step throws with correct message

### Task 3.5: Implement array interpolation tests
- [ ] **File**: `tests/Rhombus.WinFormsMcp.Tests/VariableInterpolatorTests.cs`
- [ ] Test: Variable reference as array element
- [ ] Test: Mixed array with variables and static values
- [ ] Test: Nested arrays with variables

### Task 3.6: Implement pass-through tests
- [ ] **File**: `tests/Rhombus.WinFormsMcp.Tests/VariableInterpolatorTests.cs`
- [ ] Test: Static string without $ passes through unchanged
- [ ] Test: String starting with $ but not matching pattern passes through
- [ ] Test: Embedded variable references pass through unchanged (e.g., `"prefix_$step.result.id"`)

---

## Phase 4: Fix Existing ScriptExecutionTests

### Task 4.1: Update test helper to use VariableInterpolator
- [ ] **File**: `tests/Rhombus.WinFormsMcp.Tests/ScriptExecutionTests.cs`
- [ ] Replace `InterpolateArgsForTest` method body with call to `VariableInterpolator.Interpolate()`
- [ ] Alternatively, make tests call `VariableInterpolator` directly and remove helper

### Task 4.2: Verify TestVariableInterpolation_NumericValue passes
- [ ] **File**: `tests/Rhombus.WinFormsMcp.Tests/ScriptExecutionTests.cs`
- [ ] Ensure `pid.GetInt32()` returns `12345` (not a string parse)
- [ ] Verify `pid.ValueKind == JsonValueKind.Number`

### Task 4.3: Verify TestVariableInterpolation_BooleanValue passes
- [ ] **File**: `tests/Rhombus.WinFormsMcp.Tests/ScriptExecutionTests.cs`
- [ ] Ensure `isVisible.GetBoolean()` returns `true`
- [ ] Verify `isVisible.ValueKind == JsonValueKind.True`

### Task 4.4: Verify TestVariableInterpolation_NestedPath passes
- [ ] **File**: `tests/Rhombus.WinFormsMcp.Tests/ScriptExecutionTests.cs`
- [ ] Ensure `x.GetInt32()` returns `100`
- [ ] Verify deep path navigation works correctly

### Task 4.5: Remove obsolete test helpers
- [ ] **File**: `tests/Rhombus.WinFormsMcp.Tests/ScriptExecutionTests.cs`
- [ ] Remove `EscapeJsonStringForTest` method if no longer needed
- [ ] Remove any regex-based helper code

---

## Phase 5: Validation and Cleanup

### Task 5.1: Run full test suite
- [ ] **Command**: `dotnet test Rhombus.WinFormsMcp.sln`
- [ ] Verify all ScriptExecutionTests pass
- [ ] Verify all VariableInterpolatorTests pass
- [ ] Verify no regressions in other test files

### Task 5.2: Verify timeout behavior
- [ ] Confirm `timeout_ms` is wall-clock time for entire script
- [ ] Test that timeout check occurs before step execution, not during
- [ ] Verify partial results are returned on timeout

### Task 5.3: Code review checklist
- [ ] VariableInterpolator uses `JsonElement.Clone()` to prevent memory leaks
- [ ] VariableInterpolator is `internal static` (not public)
- [ ] Error messages match REQ-6.1 exactly
- [ ] No regex used in production interpolation code

### Task 5.4: Documentation
- [ ] Update any inline comments in ScriptRunner.cs
- [ ] Ensure XML doc comments are complete on VariableInterpolator public methods

---

## Dependency Graph

```
Phase 1 (VariableInterpolator)
    |
    v
Phase 2 (ScriptRunner Integration)
    |
    v
Phase 3 (New Unit Tests) -----> Phase 4 (Fix Existing Tests)
                          \     /
                           \   /
                            v v
                      Phase 5 (Validation)
```

**Critical Path**: Tasks 1.1-1.7 -> 2.1 -> 4.1-4.4 -> 5.1

---

## Test Matrix

| Test Case | Input Type | Expected Output Type | Verifies |
|-----------|-----------|---------------------|----------|
| String variable | `"$s.result.id"` where id="elem" | `"elem"` (string) | REQ-2.1 |
| Numeric variable | `"$s.result.pid"` where pid=12345 | `12345` (number) | REQ-2.2 |
| Boolean true | `"$s.result.ok"` where ok=true | `true` (boolean) | REQ-2.3 |
| Boolean false | `"$s.result.ok"` where ok=false | `false` (boolean) | REQ-2.3 |
| Null variable | `"$s.result.data"` where data=null | `null` | REQ-2.4 |
| Object variable | `"$s.result.cfg"` where cfg={...} | `{...}` (object) | REQ-2.5 |
| Array variable | `"$s.result.items"` where items=[...] | `[...]` (array) | REQ-2.6 |
| Deep path | `"$s.result.a.b.c"` | Value at path | REQ-1.3 |
| $last alias | `"$last.result.x"` | Value from last step | REQ-1.2 |
| Static string | `"static_value"` | `"static_value"` | Pass-through |
| Embedded var | `"pre_$s.result.id_suf"` | `"pre_$s.result.id_suf"` | Not interpolated |
| Array element | `["$s.result.a", "b"]` | `[resolved_a, "b"]` | Array support |
