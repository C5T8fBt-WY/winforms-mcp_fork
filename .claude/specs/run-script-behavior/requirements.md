# run_script Tool Behavior Requirements

## Overview

The `run_script` MCP tool executes multiple tool calls in sequence with variable binding between steps. This spec defines the behavior for variable interpolation across JSON types, timeout handling, and error reporting.

## Problem Statement

The current implementation has issues with variable interpolation when non-string values are referenced from within quoted JSON strings:

- **TestVariableInterpolation_BooleanValue**: `"$check.result.visible"` where `visible` is `true` produces invalid JSON
- **TestVariableInterpolation_NestedPath**: `"$find.result.element.bounds.x"` where `x` is `100` produces invalid JSON
- **TestVariableInterpolation_NumericValue**: `"$launch.result.pid"` where `pid` is `12345` produces invalid JSON

The regex replacement produces constructs like `"true"` or `""12345""` (double-quoted) which are invalid or semantically incorrect.

---

## REQ-1: Variable Reference Syntax

**Ubiquitous**: The system shall support variable references using the syntax `$stepId.path.to.value`.

### REQ-1.1: Step ID References

**When** a variable reference uses a step ID, **the system shall** resolve it to that step's result.

- Custom IDs via `"id": "launch"` take precedence
- Auto-generated IDs follow format `step_N` (0-indexed)

### REQ-1.2: Last Step Alias

**When** a variable reference uses `$last`, **the system shall** resolve it to the most recently executed step's result.

**If** `$last` is used in the first step, **then the system shall** fail with error: "Cannot use $last in first step - no previous step exists"

### REQ-1.3: Path Navigation

**When** a path contains multiple segments (e.g., `.result.element.bounds.x`), **the system shall** navigate nested objects to retrieve the value.

**If** any segment in the path does not exist, **then the system shall** fail with error: "Property '{segment}' not found in ${stepId}.{path}"

**If** a segment attempts to access a property on a non-object, **then the system shall** fail with error: "Cannot access '{segment}' on non-object value at ${stepId}.{path}"

---

## REQ-2: Type-Aware Variable Interpolation

**Ubiquitous**: The system shall preserve JSON type semantics when interpolating variables.

### REQ-2.1: String Values

**When** the referenced value is a JSON string, **the system shall** substitute it as a string in the target position.

- If target position is a quoted string: `"elementPath": "$step1.result.id"` -> `"elementPath": "elem_42"`
- If target position is unquoted (invalid JSON anyway): Not applicable

### REQ-2.2: Numeric Values

**When** the referenced value is a JSON number, **the system shall** substitute it as a number in the target position.

- Source: `{"pid": 12345}`
- Reference: `"pid": "$launch.result.pid"`
- Result: `"pid": 12345` (number, not string)

**Acceptance Criteria:**
```json
// Given step result: {"result": {"pid": 12345}}
// Input args: {"pid": "$launch.result.pid"}
// Output args: {"pid": 12345}
// Type: JsonValueKind.Number
```

### REQ-2.3: Boolean Values

**When** the referenced value is a JSON boolean, **the system shall** substitute it as a boolean in the target position.

- Source: `{"visible": true}`
- Reference: `"isVisible": "$check.result.visible"`
- Result: `"isVisible": true` (boolean, not string)

**Acceptance Criteria:**
```json
// Given step result: {"result": {"visible": true}}
// Input args: {"isVisible": "$check.result.visible"}
// Output args: {"isVisible": true}
// Type: JsonValueKind.True
```

### REQ-2.4: Null Values

**When** the referenced value is JSON null, **the system shall** substitute it as null in the target position.

### REQ-2.5: Object Values

**When** the referenced value is a JSON object, **the system shall** substitute the entire object structure.

### REQ-2.6: Array Values

**When** the referenced value is a JSON array, **the system shall** substitute the entire array structure.

---

## REQ-3: Script Timeout Handling

### REQ-3.1: Default Timeout

**Ubiquitous**: The system shall use a default script timeout of 120,000ms (2 minutes).

### REQ-3.2: Custom Timeout

**When** the script options include `timeout_ms`, **the system shall** use that value as the timeout.

### REQ-3.3: Timeout Detection

**When** the total script execution exceeds the timeout, **the system shall** stop execution at the current step.

### REQ-3.4: Timeout Error Reporting

**When** a timeout occurs, **the system shall** return a response with:
- `success: false`
- `error: "Script timed out after {timeout_ms}ms"`
- `result.completed_steps`: Number of steps that completed successfully
- `result.total_steps`: Total number of steps in script
- `result.failed_at_step`: ID of the step that was interrupted
- `result.steps`: Array of results for all attempted steps
- `result.total_execution_time_ms`: Actual elapsed time

**Acceptance Criteria:**
```json
{
  "success": false,
  "error": "Script timed out after 5000ms",
  "result": {
    "completed_steps": 2,
    "total_steps": 5,
    "failed_at_step": "step_2",
    "steps": [
      {"id": "step_0", "success": true, "result": {...}},
      {"id": "step_1", "success": true, "result": {...}}
    ],
    "total_execution_time_ms": 5003
  }
}
```

---

## REQ-4: Partial Results on Failure

### REQ-4.1: Step Results Preservation

**Ubiquitous**: The system shall preserve results from all completed steps, regardless of subsequent failures.

### REQ-4.2: Continue on Error Option

**When** `stop_on_error: false` is set in options, **the system shall** continue executing subsequent steps after a step failure.

**When** `stop_on_error: true` (default) is set, **the system shall** halt execution after the first failed step.

### REQ-4.3: Partial Result Structure

**When** a script fails mid-execution, **the system shall** return:
- Results for all steps that executed (successful or not)
- Clear indication of which step failed
- The specific error message for the failed step

---

## REQ-5: Variable Binding Scope

### REQ-5.1: Forward References Only

**Ubiquitous**: Steps may only reference results from previously executed steps.

**If** a step references a future step ID, **then the system shall** fail with error: "Step '{stepId}' not found. Available steps: {list}"

### REQ-5.2: Failed Step Results

**When** a step fails but `stop_on_error: false`, **the system shall** still store its result for potential reference by later steps.

**Note:** Failed steps have `success: false` and `error` field instead of `result` field.

---

## REQ-6: Error Messages

### REQ-6.1: Interpolation Errors

**When** variable interpolation fails, **the system shall** provide specific error messages:

| Scenario | Error Message |
|----------|---------------|
| Missing step | "Step '{stepId}' not found. Available steps: {list}" |
| Missing property | "Property '{prop}' not found in ${stepId}.{path}" |
| Type navigation error | "Cannot access '{prop}' on non-object value at ${stepId}.{path}" |
| $last with no previous | "Cannot use $last in first step - no previous step exists" |

### REQ-6.2: Execution Errors

**When** tool execution fails, **the system shall** include the underlying error in the step result.

---

## Non-Functional Requirements

### NFR-1: Performance

**Ubiquitous**: Variable interpolation overhead shall be negligible (<1ms per step for typical use cases).

### NFR-2: Memory

**Ubiquitous**: Step results shall be stored efficiently; large result objects should not be duplicated unnecessarily.

---

## Out of Scope

- Array index access in paths (e.g., `$step.result.items[0]`) - future enhancement
- Conditional logic within scripts
- Loops or iteration constructs
- Variable assignment outside of step results
