using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using C5T8fBtWY.WinFormsMcp.Server.Automation;
using C5T8fBtWY.WinFormsMcp.Server.Utilities;

namespace C5T8fBtWY.WinFormsMcp.Server.Script;

/// <summary>
/// Executes multi-step scripts with variable binding between steps.
/// Supports $stepId.result.path and $last.result.path syntax for inter-step references.
/// </summary>
internal class ScriptRunner
{
    private readonly Func<string, JsonElement, Task<JsonElement>> _toolDispatcher;
    private readonly WindowManager _windowManager;

    public ScriptRunner(Func<string, JsonElement, Task<JsonElement>> toolDispatcher, WindowManager windowManager)
    {
        _toolDispatcher = toolDispatcher;
        _windowManager = windowManager;
    }

    /// <summary>
    /// Execute a script with multiple steps.
    /// </summary>
    public async Task<JsonElement> RunAsync(JsonElement args)
    {
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stepResults = new List<object>();
        var stepResultsByIdOrIndex = new Dictionary<string, JsonElement>();
        string? lastStepId = null;

        try
        {
            // Parse script
            if (!args.TryGetProperty("script", out var script))
            {
                return ToolResponse.Fail("Missing required 'script' parameter", _windowManager).ToJsonElement();
            }

            if (!script.TryGetProperty("steps", out var stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
            {
                return ToolResponse.Fail("Script must contain a 'steps' array", _windowManager).ToJsonElement();
            }

            var steps = stepsElement.EnumerateArray().ToList();
            if (steps.Count == 0)
            {
                return ToolResponse.Fail("Script must contain at least one step", _windowManager).ToJsonElement();
            }

            // Parse options
            var stopOnError = true;
            var defaultDelayMs = 0;
            var timeoutMs = Constants.Timeouts.ScriptExecution;

            if (script.TryGetProperty("options", out var options))
            {
                stopOnError = ArgHelpers.GetBool(options, "stop_on_error", true);
                defaultDelayMs = ArgHelpers.GetInt(options, "default_delay_ms", 0);
                timeoutMs = ArgHelpers.GetInt(options, "timeout_ms", Constants.Timeouts.ScriptExecution);
            }

            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            var completedSteps = 0;
            string? failedAtStep = null;
            string? errorMessage = null;

            for (int i = 0; i < steps.Count; i++)
            {
                // Check timeout
                if (timeoutCts.IsCancellationRequested)
                {
                    errorMessage = $"Script timed out after {timeoutMs}ms";
                    failedAtStep = GetStepId(steps[i], i);
                    break;
                }

                var step = steps[i];
                var stepStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var stepId = GetStepId(step, i);

                // Get tool name
                var toolName = ArgHelpers.GetString(step, "tool");
                if (string.IsNullOrEmpty(toolName))
                {
                    stepStopwatch.Stop();
                    var stepError = "Step missing required 'tool' field";
                    stepResults.Add(new
                    {
                        id = stepId,
                        success = false,
                        error = stepError,
                        execution_time_ms = stepStopwatch.ElapsedMilliseconds
                    });

                    if (stopOnError)
                    {
                        failedAtStep = stepId;
                        errorMessage = stepError;
                        break;
                    }
                    continue;
                }

                // Interpolate arguments with variable references (using type-preserving VariableInterpolator)
                JsonElement interpolatedArgs;
                try
                {
                    var argsElement = step.TryGetProperty("args", out var a) ? a : default;
                    interpolatedArgs = VariableInterpolator.Interpolate(argsElement, stepResultsByIdOrIndex, lastStepId);
                }
                catch (Exception ex)
                {
                    stepStopwatch.Stop();
                    var stepError = $"Variable interpolation failed: {ex.Message}";
                    stepResults.Add(new
                    {
                        id = stepId,
                        success = false,
                        error = stepError,
                        execution_time_ms = stepStopwatch.ElapsedMilliseconds
                    });

                    if (stopOnError)
                    {
                        failedAtStep = stepId;
                        errorMessage = stepError;
                        break;
                    }
                    continue;
                }

                // Execute the tool
                JsonElement result;
                try
                {
                    result = await _toolDispatcher(toolName, interpolatedArgs);
                }
                catch (Exception ex)
                {
                    stepStopwatch.Stop();
                    var stepError = $"Tool execution failed: {ex.Message}";
                    stepResults.Add(new
                    {
                        id = stepId,
                        success = false,
                        error = stepError,
                        execution_time_ms = stepStopwatch.ElapsedMilliseconds
                    });

                    if (stopOnError)
                    {
                        failedAtStep = stepId;
                        errorMessage = stepError;
                        break;
                    }
                    continue;
                }

                stepStopwatch.Stop();

                // Store result for variable binding
                stepResultsByIdOrIndex[stepId] = result;
                stepResultsByIdOrIndex[$"step_{i}"] = result; // Also by index
                lastStepId = stepId;

                // Check if step succeeded
                var stepSuccess = result.TryGetProperty("success", out var successProp) &&
                                  successProp.ValueKind == JsonValueKind.True;

                // Extract error from failed step
                string? stepErrorMsg = null;
                if (!stepSuccess && result.TryGetProperty("error", out var errorProp) &&
                    errorProp.ValueKind == JsonValueKind.String)
                {
                    stepErrorMsg = errorProp.GetString();
                }

                // Build step result object
                object stepResultObj;
                if (stepSuccess)
                {
                    stepResultObj = new
                    {
                        id = stepId,
                        success = true,
                        result = result.TryGetProperty("result", out var r) ? (object?)r.Clone() : null,
                        execution_time_ms = stepStopwatch.ElapsedMilliseconds
                    };
                    completedSteps++;
                }
                else
                {
                    stepResultObj = new
                    {
                        id = stepId,
                        success = false,
                        error = stepErrorMsg ?? "Unknown error",
                        execution_time_ms = stepStopwatch.ElapsedMilliseconds
                    };

                    if (stopOnError)
                    {
                        stepResults.Add(stepResultObj);
                        failedAtStep = stepId;
                        errorMessage = $"Script failed at step '{stepId}': {stepErrorMsg ?? "Unknown error"}";
                        break;
                    }
                }

                stepResults.Add(stepResultObj);

                // Apply delay
                var delayMs = ArgHelpers.GetInt(step, "delay_after_ms", defaultDelayMs);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, timeoutCts.Token);
                }
            }

            totalStopwatch.Stop();

            // Build final response
            var scriptResult = new Dictionary<string, object?>
            {
                ["completed_steps"] = completedSteps,
                ["total_steps"] = steps.Count,
                ["steps"] = stepResults,
                ["total_execution_time_ms"] = totalStopwatch.ElapsedMilliseconds
            };

            if (failedAtStep != null)
            {
                scriptResult["failed_at_step"] = failedAtStep;
            }

            if (errorMessage != null)
            {
                return new ToolResponse
                {
                    Success = false,
                    Error = errorMessage,
                    Result = scriptResult,
                    Windows = _windowManager.GetAllWindows()
                }.ToJsonElement();
            }

            return new ToolResponse
            {
                Success = true,
                Result = scriptResult,
                Windows = _windowManager.GetAllWindows()
            }.ToJsonElement();
        }
        catch (OperationCanceledException)
        {
            totalStopwatch.Stop();
            return ToolResponse.Fail(
                $"Script execution timed out",
                _windowManager,
                ("completed_steps", stepResults.Count),
                ("steps", stepResults),
                ("total_execution_time_ms", totalStopwatch.ElapsedMilliseconds)
            ).ToJsonElement();
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            return ToolResponse.Fail(
                $"Script execution error: {ex.Message}",
                _windowManager,
                ("completed_steps", stepResults.Count),
                ("steps", stepResults),
                ("total_execution_time_ms", totalStopwatch.ElapsedMilliseconds)
            ).ToJsonElement();
        }
    }

    /// <summary>
    /// Get step ID from step definition, or generate one from index.
    /// </summary>
    private static string GetStepId(JsonElement step, int index)
    {
        if (step.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
        {
            var id = idProp.GetString();
            if (!string.IsNullOrEmpty(id))
                return id;
        }
        return $"step_{index}";
    }

}
