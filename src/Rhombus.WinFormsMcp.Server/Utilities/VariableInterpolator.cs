using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace C5T8fBtWY.WinFormsMcp.Server.Utilities;

/// <summary>
/// Type-preserving variable interpolation for script execution.
/// Uses JSON DOM operations instead of string replacement to preserve types:
/// numbers stay numbers, booleans stay booleans.
///
/// Supports $stepId.path.to.value and $last.path.to.value syntax.
/// </summary>
public static class VariableInterpolator
{
    // Pattern: $stepId.path.to.value (captures stepId and path separately)
    private static readonly Regex VariablePattern = new(@"^\$(\w+)((?:\.\w+)+)$", RegexOptions.Compiled);

    /// <summary>
    /// Interpolate variable references in a JSON element.
    /// Variables are strings matching the pattern $stepId.path.to.value.
    /// </summary>
    /// <param name="args">The arguments containing potential variable references.</param>
    /// <param name="stepResults">Dictionary of step results indexed by step ID.</param>
    /// <param name="lastStepId">The ID of the most recently completed step (for $last alias).</param>
    /// <returns>A new JsonElement with variables replaced by their values.</returns>
    public static JsonElement Interpolate(
        JsonElement args,
        IReadOnlyDictionary<string, JsonElement> stepResults,
        string? lastStepId)
    {
        if (args.ValueKind == JsonValueKind.Undefined || args.ValueKind == JsonValueKind.Null)
        {
            return args;
        }

        // Convert to JsonNode for mutable operations
        var node = JsonNode.Parse(args.GetRawText());
        if (node == null)
        {
            return args;
        }

        // Recursively process the node
        var processed = ProcessNode(node, stepResults, lastStepId);

        // Convert back to JsonElement
        if (processed == null)
        {
            return JsonDocument.Parse("null").RootElement;
        }

        return JsonDocument.Parse(processed.ToJsonString()).RootElement;
    }

    /// <summary>
    /// Check if a string is a variable reference and extract its parts.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <param name="stepId">Output: the step ID (or "last").</param>
    /// <param name="path">Output: the property path (without leading dot).</param>
    /// <returns>True if the string is a variable reference.</returns>
    public static bool IsVariableReference(string? value, out string stepId, out string path)
    {
        stepId = string.Empty;
        path = string.Empty;

        if (string.IsNullOrEmpty(value))
            return false;

        var match = VariablePattern.Match(value);
        if (!match.Success)
            return false;

        stepId = match.Groups[1].Value;
        path = match.Groups[2].Value.TrimStart('.');
        return true;
    }

    /// <summary>
    /// Resolve a dotted path in a JsonElement.
    /// </summary>
    /// <param name="root">The root element to navigate from.</param>
    /// <param name="path">The dotted path (e.g., "result.element.bounds.x").</param>
    /// <returns>The resolved JsonElement.</returns>
    /// <exception cref="InvalidOperationException">If the path cannot be resolved.</exception>
    public static JsonElement ResolvePath(JsonElement root, string path)
    {
        var current = root;
        var pathParts = path.Split('.');

        foreach (var part in pathParts)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Cannot access '{part}' on non-object value");
            }

            if (!current.TryGetProperty(part, out var next))
            {
                throw new InvalidOperationException(
                    $"Property '{part}' not found in path '{path}'");
            }

            current = next;
        }

        return current;
    }

    /// <summary>
    /// Recursively process a JsonNode, replacing variable references with their values.
    /// </summary>
    private static JsonNode? ProcessNode(
        JsonNode? node,
        IReadOnlyDictionary<string, JsonElement> stepResults,
        string? lastStepId)
    {
        if (node == null)
            return null;

        switch (node)
        {
            case JsonObject obj:
                return ProcessObject(obj, stepResults, lastStepId);

            case JsonArray arr:
                return ProcessArray(arr, stepResults, lastStepId);

            case JsonValue val:
                return ProcessValue(val, stepResults, lastStepId);

            default:
                return node;
        }
    }

    /// <summary>
    /// Process a JSON object, replacing variable references in all values.
    /// </summary>
    private static JsonObject ProcessObject(
        JsonObject obj,
        IReadOnlyDictionary<string, JsonElement> stepResults,
        string? lastStepId)
    {
        var result = new JsonObject();

        foreach (var kvp in obj)
        {
            var processedValue = ProcessNode(kvp.Value, stepResults, lastStepId);
            result[kvp.Key] = processedValue?.DeepClone();
        }

        return result;
    }

    /// <summary>
    /// Process a JSON array, replacing variable references in all elements.
    /// </summary>
    private static JsonArray ProcessArray(
        JsonArray arr,
        IReadOnlyDictionary<string, JsonElement> stepResults,
        string? lastStepId)
    {
        var result = new JsonArray();

        foreach (var item in arr)
        {
            var processedItem = ProcessNode(item, stepResults, lastStepId);
            result.Add(processedItem?.DeepClone());
        }

        return result;
    }

    /// <summary>
    /// Process a JSON value, replacing it if it's a variable reference.
    /// </summary>
    private static JsonNode? ProcessValue(
        JsonValue val,
        IReadOnlyDictionary<string, JsonElement> stepResults,
        string? lastStepId)
    {
        // Only string values can be variable references
        if (val.TryGetValue<string>(out var stringValue))
        {
            if (IsVariableReference(stringValue, out var stepId, out var path))
            {
                // Resolve the variable
                var resolvedElement = ResolveVariable(stepId, path, stepResults, lastStepId);

                // Convert JsonElement to JsonNode
                return JsonElementToNode(resolvedElement);
            }
        }

        // Not a variable reference, return as-is
        return val.DeepClone();
    }

    /// <summary>
    /// Resolve a variable reference to its value.
    /// </summary>
    private static JsonElement ResolveVariable(
        string stepId,
        string path,
        IReadOnlyDictionary<string, JsonElement> stepResults,
        string? lastStepId)
    {
        // Handle $last alias
        if (stepId == "last")
        {
            if (lastStepId == null)
            {
                throw new InvalidOperationException(
                    "Cannot use $last in first step - no previous step exists");
            }
            stepId = lastStepId;
        }

        // Find the step result
        if (!stepResults.TryGetValue(stepId, out var stepResult))
        {
            var availableSteps = string.Join(", ", stepResults.Keys);
            throw new InvalidOperationException(
                $"Step '{stepId}' not found. Available steps: {availableSteps}");
        }

        // Resolve the path within the step result
        try
        {
            return ResolvePath(stepResult, path);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"Error resolving ${stepId}.{path}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Convert a JsonElement to a JsonNode.
    /// Preserves the original type (number, boolean, string, etc.).
    /// </summary>
    private static JsonNode? JsonElementToNode(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonNode.Parse(element.GetRawText()),
            JsonValueKind.Array => JsonNode.Parse(element.GetRawText()),
            JsonValueKind.String => JsonValue.Create(element.GetString()),
            JsonValueKind.Number => CreateNumberNode(element),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            JsonValueKind.Null => null,
            _ => null
        };
    }

    /// <summary>
    /// Create a JsonValue node for a number, preserving integer vs decimal.
    /// </summary>
    private static JsonNode CreateNumberNode(JsonElement element)
    {
        // Try to preserve integer types
        if (element.TryGetInt32(out var intValue))
        {
            return JsonValue.Create(intValue);
        }
        if (element.TryGetInt64(out var longValue))
        {
            return JsonValue.Create(longValue);
        }
        // Fall back to double for decimals
        return JsonValue.Create(element.GetDouble());
    }
}
