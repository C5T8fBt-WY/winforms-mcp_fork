using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using C5T8fBtWY.WinFormsMcp.Server.Abstractions;
using C5T8fBtWY.WinFormsMcp.Server.Automation;
using C5T8fBtWY.WinFormsMcp.Server.Utilities;

namespace C5T8fBtWY.WinFormsMcp.Server.Handlers;

/// <summary>
/// Handler for batch script execution with variable interpolation.
/// Replaces: run_script (with simplified API)
/// </summary>
internal class ScriptHandler : HandlerBase
{
    private readonly Func<string, JsonElement, Task<JsonElement>> _dispatcher;

    public ScriptHandler(ISessionManager session, IWindowManager windows, Func<string, JsonElement, Task<JsonElement>> dispatcher)
        : base(session, windows)
    {
        _dispatcher = dispatcher;
    }

    public override IEnumerable<string> SupportedTools => new[] { "script" };

    public override async Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        try
        {
            if (!args.TryGetProperty("steps", out var stepsEl))
                return await Error("steps array is required. Example: {\"steps\": [{\"tool\": \"find\", \"args\": {...}}]}");

            var stopOnError = GetBoolArg(args, "stop_on_error", true);

            var results = new Dictionary<string, JsonElement>();
            var stepResults = new List<object>();
            int stepIndex = 0;

            foreach (var step in stepsEl.EnumerateArray())
            {
                stepIndex++;
                var stepId = step.TryGetProperty("id", out var idProp)
                    ? idProp.GetString() ?? $"step_{stepIndex}"
                    : $"step_{stepIndex}";

                if (!step.TryGetProperty("tool", out var toolProp))
                {
                    var errorResult = new { step = stepId, success = false, error = "Step missing 'tool' property. Each step needs: {\"tool\": \"<tool_name>\", \"args\": {...}}" };
                    stepResults.Add(errorResult);
                    if (stopOnError)
                        return await Success(new { completed = false, results = stepResults, failedStep = stepId });
                    continue;
                }

                var tool = toolProp.GetString()!;
                var stepArgs = step.TryGetProperty("args", out var argsProp)
                    ? argsProp
                    : default;

                // Interpolate variables from previous results
                var interpolatedArgs = InterpolateVariables(stepArgs, results);

                // Execute the tool
                var result = await _dispatcher(tool, interpolatedArgs);
                results[stepId] = result;

                // Check for success
                bool success = true;
                if (result.TryGetProperty("success", out var successProp))
                {
                    success = successProp.GetBoolean();
                }

                stepResults.Add(new
                {
                    step = stepId,
                    tool,
                    success,
                    result
                });

                if (!success && stopOnError)
                {
                    return await Success(new { completed = false, results = stepResults, failedStep = stepId });
                }
            }

            return await Success(new { completed = true, results = stepResults });
        }
        catch (JsonException ex)
        {
            return await Error($"Invalid JSON in script args: {ex.Message}");
        }
        catch (Exception ex)
        {
            return await Error($"Script execution failed: {ex.Message}");
        }
    }

    private JsonElement InterpolateVariables(JsonElement args, Dictionary<string, JsonElement> results)
    {
        if (args.ValueKind == JsonValueKind.Undefined)
            return args;

        var json = args.GetRawText();

        // Pattern: "$stepId.path" (inside a JSON string) or $stepId.path (raw)
        // We need to handle both cases:
        // 1. "target": "$cb.id" -> "target": "elem_39" (don't add quotes, already in string)
        // 2. "count": $cb.count -> "count": 5 (raw value replacement)

        // First handle quoted references: "$var.path"
        var quotedPattern = @"""(\$(\w+)\.([.\w]+))""";
        var interpolated = Regex.Replace(json, quotedPattern, match =>
        {
            var stepId = match.Groups[2].Value;
            var path = match.Groups[3].Value;

            if (!results.TryGetValue(stepId, out var stepResult))
                return match.Value; // Keep original if step not found

            try
            {
                var value = NavigateJsonPath(stepResult, path);
                if (value.ValueKind == JsonValueKind.String)
                    return $"\"{EscapeJson(value.GetString()!)}\"";
                // For non-strings inside a quoted context, stringify them
                return $"\"{EscapeJson(value.GetRawText())}\"";
            }
            catch
            {
                return match.Value; // Keep original on error
            }
        });

        // Then handle unquoted references (for numeric/boolean values)
        var unquotedPattern = @"(?<!"")(\$(\w+)\.([.\w]+))(?!"")";
        interpolated = Regex.Replace(interpolated, unquotedPattern, match =>
        {
            var stepId = match.Groups[2].Value;
            var path = match.Groups[3].Value;

            if (!results.TryGetValue(stepId, out var stepResult))
                return match.Value;

            try
            {
                var value = NavigateJsonPath(stepResult, path);
                return value.GetRawText();
            }
            catch
            {
                return match.Value;
            }
        });

        return JsonDocument.Parse(interpolated).RootElement;
    }

    private JsonElement NavigateJsonPath(JsonElement root, string path)
    {
        var current = root;
        var parts = path.Split('.');

        foreach (var part in parts)
        {
            if (current.TryGetProperty(part, out var next))
            {
                current = next;
            }
            // Shorthand: if path is "id", "type", "name" etc and not found at root,
            // try looking in "result" first (common pattern for tool responses)
            else if (current.TryGetProperty("result", out var resultEl) &&
                     resultEl.TryGetProperty(part, out next))
            {
                current = next;
            }
            else
            {
                throw new InvalidOperationException($"Path not found: {part}");
            }
        }

        return current;
    }

    private new string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
