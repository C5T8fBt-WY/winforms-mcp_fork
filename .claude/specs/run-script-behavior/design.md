# run_script Tool Behavior Design

## Design Overview

This design addresses the type-preservation problem in variable interpolation by replacing the current regex-based string replacement with an AST-based approach that operates directly on JSON structures.

### Core Problem

The current implementation uses regex replacement on raw JSON text:

```csharp
// Current approach (problematic)
var regex = new Regex(@"\$(\w+)((?:\.\w+)+)");
var interpolated = regex.Replace(json, match => {
    // Returns raw JSON values like: "true", "12345", ""elem_1""
});
```

This produces invalid JSON when non-string values are substituted into quoted string positions:
- `"pid": "$launch.result.pid"` becomes `"pid": "12345"` (string instead of number)
- `"isVisible": "$check.result.visible"` produces parse errors with `"true"` inside quotes

### Solution: AST-Based Interpolation

Replace text manipulation with JSON DOM traversal and reconstruction. This preserves types naturally because we operate on parsed values, not strings.

---

## Component Design

### 1. VariableInterpolator Class

**Responsibility**: Resolve variable references in JSON arguments to their actual values with correct types.

```csharp
internal static class VariableInterpolator
{
    /// <summary>
    /// Interpolate variable references in a JSON element.
    /// Returns a new JsonElement with all $stepId.path references resolved.
    /// </summary>
    public static JsonElement Interpolate(
        JsonElement args,
        IReadOnlyDictionary<string, JsonElement> stepResults,
        string? lastStepId)
    {
        return args.ValueKind switch
        {
            JsonValueKind.Object => InterpolateObject(args, stepResults, lastStepId),
            JsonValueKind.Array => InterpolateArray(args, stepResults, lastStepId),
            JsonValueKind.String => InterpolateString(args, stepResults, lastStepId),
            _ => args.Clone() // Numbers, booleans, null pass through unchanged
        };
    }
}
```

### 2. Variable Reference Detection

A string value is a variable reference if and only if:
1. It starts with `$`
2. It matches pattern `$stepId.path.to.value` (one or more path segments after stepId)

```csharp
private static readonly Regex VariablePattern = new(
    @"^\$(\w+)((?:\.\w+)+)$",
    RegexOptions.Compiled);

private static bool IsVariableReference(string value, out string stepId, out string path)
{
    var match = VariablePattern.Match(value);
    if (match.Success)
    {
        stepId = match.Groups[1].Value;
        path = match.Groups[2].Value.TrimStart('.');
        return true;
    }
    stepId = path = "";
    return false;
}
```

**Design Decision**: Only exact matches are variable references. Embedded references like `"prefix_$step.result.id_suffix"` are NOT supported (out of scope per requirements).

### 3. Value Resolution Algorithm

```
FUNCTION ResolveVariable(stepId, path, stepResults, lastStepId)
    1. If stepId == "last":
        a. If lastStepId is null: THROW "Cannot use $last in first step"
        b. stepId = lastStepId

    2. If stepId not in stepResults:
        THROW "Step '{stepId}' not found. Available steps: {list}"

    3. current = stepResults[stepId]

    4. For each segment in path.split('.'):
        a. If current is not Object:
            THROW "Cannot access '{segment}' on non-object at ${stepId}.{path}"
        b. If current does not have property segment:
            THROW "Property '{segment}' not found in ${stepId}.{path}"
        c. current = current[segment]

    5. RETURN current (preserves original type)
END FUNCTION
```

### 4. AST Transformation Functions

#### InterpolateObject

```csharp
private static JsonElement InterpolateObject(
    JsonElement obj,
    IReadOnlyDictionary<string, JsonElement> stepResults,
    string? lastStepId)
{
    using var stream = new MemoryStream();
    using var writer = new Utf8JsonWriter(stream);

    writer.WriteStartObject();
    foreach (var property in obj.EnumerateObject())
    {
        writer.WritePropertyName(property.Name);
        var interpolated = Interpolate(property.Value, stepResults, lastStepId);
        interpolated.WriteTo(writer);
    }
    writer.WriteEndObject();
    writer.Flush();

    return JsonDocument.Parse(stream.ToArray()).RootElement;
}
```

#### InterpolateArray

```csharp
private static JsonElement InterpolateArray(
    JsonElement arr,
    IReadOnlyDictionary<string, JsonElement> stepResults,
    string? lastStepId)
{
    using var stream = new MemoryStream();
    using var writer = new Utf8JsonWriter(stream);

    writer.WriteStartArray();
    foreach (var item in arr.EnumerateArray())
    {
        var interpolated = Interpolate(item, stepResults, lastStepId);
        interpolated.WriteTo(writer);
    }
    writer.WriteEndArray();
    writer.Flush();

    return JsonDocument.Parse(stream.ToArray()).RootElement;
}
```

#### InterpolateString

This is the key function that handles type preservation:

```csharp
private static JsonElement InterpolateString(
    JsonElement str,
    IReadOnlyDictionary<string, JsonElement> stepResults,
    string? lastStepId)
{
    var value = str.GetString() ?? "";

    if (!IsVariableReference(value, out var stepId, out var path))
    {
        // Not a variable reference, return as-is
        return str.Clone();
    }

    // Resolve the variable - returns the actual typed value
    var resolved = ResolveVariable(stepId, path, stepResults, lastStepId);

    // Clone to detach from source document
    return resolved.Clone();
}
```

---

## Step Execution Flow

```
PROCEDURE ExecuteScript(script)
    stepResults = {}  // Dictionary<string, JsonElement>
    lastStepId = null
    completedSteps = 0
    totalStopwatch.Start()

    FOR i = 0 TO script.steps.Count - 1:
        step = script.steps[i]
        stepId = step.id ?? "step_{i}"

        // 1. Check timeout
        IF totalStopwatch.Elapsed > script.options.timeout_ms:
            RETURN TimeoutResult(completedSteps, i, stepResults)

        // 2. Validate tool name
        IF step.tool is empty:
            HANDLE_ERROR("Step missing required 'tool' field")
            CONTINUE or BREAK based on stop_on_error

        // 3. Interpolate arguments (type-preserving)
        TRY:
            interpolatedArgs = VariableInterpolator.Interpolate(
                step.args, stepResults, lastStepId)
        CATCH InterpolationError as e:
            HANDLE_ERROR(e.Message)
            CONTINUE or BREAK based on stop_on_error

        // 4. Execute tool
        TRY:
            result = toolDispatcher(step.tool, interpolatedArgs)
        CATCH ToolError as e:
            HANDLE_ERROR(e.Message)
            CONTINUE or BREAK based on stop_on_error

        // 5. Store result for future references
        stepResults[stepId] = result
        stepResults["step_{i}"] = result  // Also by index
        lastStepId = stepId

        // 6. Update completion count if successful
        IF result.success:
            completedSteps++
        ELSE IF stop_on_error:
            RETURN FailureResult(step, result, completedSteps, stepResults)

        // 7. Apply delay
        IF step.delay_after_ms > 0 OR options.default_delay_ms > 0:
            delay = step.delay_after_ms ?? options.default_delay_ms
            Task.Delay(delay)

    RETURN SuccessResult(completedSteps, stepResults)
END PROCEDURE
```

---

## Error Handling Strategy

### Error Categories

| Category | When | Behavior |
|----------|------|----------|
| Validation Error | Script structure invalid | Immediate failure, no steps executed |
| Interpolation Error | Variable resolution fails | Depends on `stop_on_error` |
| Tool Execution Error | Tool returns error | Depends on `stop_on_error` |
| Timeout | Total time exceeded | Stop at current step, return partial |

### Error Response Structure

All errors follow a consistent structure:

```json
{
  "success": false,
  "error": "Human-readable error message",
  "result": {
    "completed_steps": 2,
    "total_steps": 5,
    "failed_at_step": "step_2",
    "steps": [
      {"id": "step_0", "success": true, "result": {...}, "execution_time_ms": 45},
      {"id": "step_1", "success": true, "result": {...}, "execution_time_ms": 120}
    ],
    "total_execution_time_ms": 5023
  }
}
```

### Interpolation Error Messages

Per REQ-6.1, error messages are specific and actionable:

```csharp
// Step not found
$"Step '{stepId}' not found. Available steps: {string.Join(", ", stepResults.Keys)}"

// Property not found
$"Property '{segment}' not found in ${stepId}.{navigatedPath}"

// Type navigation error
$"Cannot access '{segment}' on non-object value at ${stepId}.{navigatedPath}"

// $last without previous step
"Cannot use $last in first step - no previous step exists"
```

---

## Timeout Handling

### Implementation

```csharp
public async Task<JsonElement> RunAsync(JsonElement args)
{
    var timeoutMs = GetTimeoutFromOptions(args, Constants.Timeouts.ScriptExecution);
    using var timeoutCts = new CancellationTokenSource(timeoutMs);

    try
    {
        // Check before each step
        for (int i = 0; i < steps.Count; i++)
        {
            if (timeoutCts.IsCancellationRequested)
            {
                return BuildTimeoutResponse(completedSteps, steps.Count, GetStepId(steps[i], i));
            }

            // Execute step...

            // Pass token to delay
            await Task.Delay(delayMs, timeoutCts.Token);
        }
    }
    catch (OperationCanceledException)
    {
        return BuildTimeoutResponse(completedSteps, steps.Count, currentStepId);
    }
}
```

### Timeout Response Format

Per REQ-3.4:

```json
{
  "success": false,
  "error": "Script timed out after 5000ms",
  "result": {
    "completed_steps": 2,
    "total_steps": 5,
    "failed_at_step": "step_2",
    "steps": [...partial results...],
    "total_execution_time_ms": 5003
  }
}
```

---

## Result Aggregation

### Step Result Structure

Each step produces a result object:

```csharp
// Successful step
{
    "id": "launch",
    "success": true,
    "result": { /* tool-specific result */ },
    "execution_time_ms": 1234
}

// Failed step
{
    "id": "click",
    "success": false,
    "error": "Element not found",
    "execution_time_ms": 567
}
```

### Final Result Structure

```csharp
{
    "success": true|false,
    "error": "...", // Only if success=false
    "result": {
        "completed_steps": N,
        "total_steps": M,
        "failed_at_step": "stepId", // Only if failed
        "steps": [...],
        "total_execution_time_ms": T
    },
    "windows": [...] // Standard window list
}
```

### Storage for Variable Binding

Results are stored with dual indexing:

```csharp
// Store by custom ID
stepResultsByIdOrIndex[stepId] = result;  // e.g., "launch"

// Also store by index for positional reference
stepResultsByIdOrIndex[$"step_{i}"] = result;  // e.g., "step_0"
```

This allows both `$launch.result.pid` and `$step_0.result.pid` to work.

---

## Memory Efficiency (NFR-2)

### JsonElement Cloning Strategy

- Use `JsonElement.Clone()` when storing results to detach from source `JsonDocument`
- Avoid deep-copying entire results when only specific paths are accessed
- The step results dictionary holds cloned elements, not references to mutable documents

### Stream Reuse

For high-volume scripts, consider pooling `MemoryStream` instances:

```csharp
// Future optimization: ArrayPool-backed streams
private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;
```

---

## File Structure

```
src/Rhombus.WinFormsMcp.Server/Script/
├── ScriptRunner.cs           # Main orchestration (modified)
└── VariableInterpolator.cs   # New: AST-based interpolation

tests/Rhombus.WinFormsMcp.Tests/
├── ScriptExecutionTests.cs   # Existing tests (update)
└── VariableInterpolatorTests.cs  # New: focused interpolation tests
```

---

## Migration Path

1. **Create `VariableInterpolator.cs`** with AST-based implementation
2. **Update `ScriptRunner.InterpolateArgs`** to delegate to new class
3. **Update tests** to verify type preservation
4. **Remove old regex-based code** after verification

### Backward Compatibility

The new implementation is fully backward compatible:
- Same variable reference syntax (`$stepId.path`)
- Same error messages
- String interpolations continue to work identically
- Non-string types now work correctly (new capability)

---

## Test Plan

### Unit Tests (VariableInterpolator)

| Test | Input | Expected |
|------|-------|----------|
| String value | `"$step.result.id"` where id="elem_1" | `"elem_1"` (string) |
| Numeric value | `"$step.result.pid"` where pid=12345 | `12345` (number) |
| Boolean true | `"$step.result.visible"` where visible=true | `true` (boolean) |
| Boolean false | `"$step.result.enabled"` where enabled=false | `false` (boolean) |
| Null value | `"$step.result.data"` where data=null | `null` |
| Nested object | `"$step.result.config"` where config={...} | `{...}` (object) |
| Array value | `"$step.result.items"` where items=[...] | `[...]` (array) |
| Deep path | `"$step.result.a.b.c.d"` | Value at path |
| $last alias | `"$last.result.x"` after step_1 | Value from step_1 |
| No interpolation | `"static_value"` | `"static_value"` |

### Error Tests

| Test | Input | Expected Error |
|------|-------|----------------|
| Missing step | `"$unknown.result.x"` | "Step 'unknown' not found..." |
| Missing property | `"$step.result.missing"` | "Property 'missing' not found..." |
| Non-object navigation | `"$step.result.str.x"` (str is string) | "Cannot access 'x' on non-object..." |
| $last in first step | `"$last.result.x"` with no prior | "Cannot use $last in first step..." |

### Integration Tests (ScriptRunner)

- Full script with mixed types
- Timeout behavior
- Partial results on failure
- `stop_on_error: false` continuation

---

## Performance Considerations (NFR-1)

The AST approach has comparable performance to regex for typical use cases:

- **Regex approach**: Parse JSON -> Serialize to string -> Regex replace -> Parse JSON
- **AST approach**: Traverse JSON -> Rebuild with substitutions

For a typical 5-step script with 3 arguments per step:
- Regex: ~15 operations (serialize/deserialize per step)
- AST: ~15 operations (traverse/write per step)

Both approaches are dominated by I/O and tool execution time, making interpolation overhead negligible (<1ms per step).

---

## Summary

This design replaces text-based regex substitution with AST-based JSON transformation:

1. **Type Preservation**: Values maintain their JSON types naturally
2. **Clean Architecture**: Separates interpolation logic into dedicated class
3. **Same API**: No changes to script syntax or error message format
4. **Testable**: Pure functions enable comprehensive unit testing
5. **Maintainable**: Clear separation of concerns, no regex edge cases
