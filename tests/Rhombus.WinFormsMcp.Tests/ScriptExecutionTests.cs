using C5T8fBtWY.WinFormsMcp.Server.Utilities;

namespace C5T8fBtWY.WinFormsMcp.Tests;

using System.Text.Json;
using C5T8fBtWY.WinFormsMcp.Server.Utilities;

/// <summary>
/// Unit tests for the run_script MCP tool functionality.
/// Tests variable interpolation, error handling, and script execution logic.
/// Now uses the VariableInterpolator utility for type-preserving interpolation.
/// </summary>
public class ScriptExecutionTests
{
    /// <summary>
    /// Test variable interpolation logic in isolation.
    /// </summary>
    [Test]
    public void TestVariableInterpolation_BasicStringValue()
    {
        // Simulate step results
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["step1"] = JsonDocument.Parse(@"{""success"": true, ""result"": {""elementId"": ""elem_42""}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""elementPath"": ""$step1.result.elementId""}").RootElement;

        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        Assert.That(interpolated.TryGetProperty("elementPath", out var elementPath), Is.True);
        Assert.That(elementPath.GetString(), Is.EqualTo("elem_42"));
    }

    [Test]
    public void TestVariableInterpolation_NumericValue()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["launch"] = JsonDocument.Parse(@"{""success"": true, ""result"": {""pid"": 12345}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""pid"": ""$launch.result.pid""}").RootElement;

        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        Assert.That(interpolated.TryGetProperty("pid", out var pid), Is.True);
        // With VariableInterpolator, types are preserved - numbers stay numbers
        Assert.That(pid.GetInt32(), Is.EqualTo(12345));
    }

    [Test]
    public void TestVariableInterpolation_LastAlias()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["step1"] = JsonDocument.Parse(@"{""success"": true, ""result"": {""value"": ""first""}}").RootElement,
            ["step2"] = JsonDocument.Parse(@"{""success"": true, ""result"": {""value"": ""second""}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""previousValue"": ""$last.result.value""}").RootElement;

        var interpolated = VariableInterpolator.Interpolate(args, stepResults, "step2");

        Assert.That(interpolated.TryGetProperty("previousValue", out var value), Is.True);
        Assert.That(value.GetString(), Is.EqualTo("second"));
    }

    [Test]
    public void TestVariableInterpolation_MissingStep_ThrowsException()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["step1"] = JsonDocument.Parse(@"{""success"": true, ""result"": {""elementId"": ""elem_42""}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""elementPath"": ""$nonexistent.result.elementId""}").RootElement;

        Assert.Throws<InvalidOperationException>(() =>
            VariableInterpolator.Interpolate(args, stepResults, null));
    }

    [Test]
    public void TestVariableInterpolation_MissingProperty_ThrowsException()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["step1"] = JsonDocument.Parse(@"{""success"": true, ""result"": {""elementId"": ""elem_42""}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""elementPath"": ""$step1.result.nonexistent""}").RootElement;

        Assert.Throws<InvalidOperationException>(() =>
            VariableInterpolator.Interpolate(args, stepResults, null));
    }

    [Test]
    public void TestVariableInterpolation_LastAliasNoLastStep_ThrowsException()
    {
        var stepResults = new Dictionary<string, JsonElement>();

        var args = JsonDocument.Parse(@"{""elementPath"": ""$last.result.elementId""}").RootElement;

        Assert.Throws<InvalidOperationException>(() =>
            VariableInterpolator.Interpolate(args, stepResults, null));
    }

    [Test]
    public void TestVariableInterpolation_NoVariables_PassesThrough()
    {
        var stepResults = new Dictionary<string, JsonElement>();

        var args = JsonDocument.Parse(@"{""elementPath"": ""elem_static"", ""text"": ""Hello World""}").RootElement;

        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        Assert.That(interpolated.TryGetProperty("elementPath", out var elementPath), Is.True);
        Assert.That(elementPath.GetString(), Is.EqualTo("elem_static"));
        Assert.That(interpolated.TryGetProperty("text", out var text), Is.True);
        Assert.That(text.GetString(), Is.EqualTo("Hello World"));
    }

    [Test]
    public void TestVariableInterpolation_BooleanValue()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["check"] = JsonDocument.Parse(@"{""success"": true, ""result"": {""visible"": true}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""isVisible"": ""$check.result.visible""}").RootElement;

        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        Assert.That(interpolated.TryGetProperty("isVisible", out var isVisible), Is.True);
        Assert.That(isVisible.GetBoolean(), Is.True);
    }

    [Test]
    public void TestVariableInterpolation_NestedPath()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["find"] = JsonDocument.Parse(@"{""success"": true, ""result"": {""element"": {""bounds"": {""x"": 100}}}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""x"": ""$find.result.element.bounds.x""}").RootElement;

        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        Assert.That(interpolated.TryGetProperty("x", out var x), Is.True);
        Assert.That(x.GetInt32(), Is.EqualTo(100));
    }

    [Test]
    public void TestScriptSchema_ValidSimpleScript()
    {
        var scriptJson = @"{
            ""script"": {
                ""steps"": [
                    { ""id"": ""step1"", ""tool"": ""find_element"", ""args"": { ""automationId"": ""Button1"" } },
                    { ""tool"": ""click_element"", ""args"": { ""elementPath"": ""$step1.result.elementId"" } }
                ]
            }
        }";

        var doc = JsonDocument.Parse(scriptJson);
        Assert.That(doc.RootElement.TryGetProperty("script", out var script), Is.True);
        Assert.That(script.TryGetProperty("steps", out var steps), Is.True);
        Assert.That(steps.GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public void TestScriptSchema_WithOptions()
    {
        var scriptJson = @"{
            ""script"": {
                ""steps"": [
                    { ""tool"": ""find_element"", ""args"": { ""automationId"": ""Button1"" } }
                ],
                ""options"": {
                    ""stop_on_error"": false,
                    ""default_delay_ms"": 100,
                    ""timeout_ms"": 30000
                }
            }
        }";

        var doc = JsonDocument.Parse(scriptJson);
        Assert.That(doc.RootElement.TryGetProperty("script", out var script), Is.True);
        Assert.That(script.TryGetProperty("options", out var options), Is.True);

        options.TryGetProperty("stop_on_error", out var stopOnError);
        Assert.That(stopOnError.GetBoolean(), Is.False);

        options.TryGetProperty("default_delay_ms", out var defaultDelay);
        Assert.That(defaultDelay.GetInt32(), Is.EqualTo(100));

        options.TryGetProperty("timeout_ms", out var timeout);
        Assert.That(timeout.GetInt32(), Is.EqualTo(30000));
    }

    [Test]
    public void TestScriptSchema_WithDelayAfterMs()
    {
        var scriptJson = @"{
            ""script"": {
                ""steps"": [
                    { ""tool"": ""launch_app"", ""args"": { ""path"": ""C:\\app.exe"" }, ""delay_after_ms"": 1000 }
                ]
            }
        }";

        var doc = JsonDocument.Parse(scriptJson);
        var steps = doc.RootElement.GetProperty("script").GetProperty("steps");
        var step = steps[0];

        Assert.That(step.TryGetProperty("delay_after_ms", out var delay), Is.True);
        Assert.That(delay.GetInt32(), Is.EqualTo(1000));
    }

    [Test]
    public void TestGetStepId_WithExplicitId()
    {
        var step = JsonDocument.Parse(@"{""id"": ""myStep"", ""tool"": ""test""}").RootElement;
        var stepId = GetStepIdForTest(step, 5);
        Assert.That(stepId, Is.EqualTo("myStep"));
    }

    [Test]
    public void TestGetStepId_WithoutId_GeneratesFromIndex()
    {
        var step = JsonDocument.Parse(@"{""tool"": ""test""}").RootElement;
        var stepId = GetStepIdForTest(step, 3);
        Assert.That(stepId, Is.EqualTo("step_3"));
    }

    // Helper for GetStepId tests (mimics ScriptRunner logic)
    private static string GetStepIdForTest(JsonElement step, int index)
    {
        if (step.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
        {
            var id = idProp.GetString();
            if (!string.IsNullOrEmpty(id))
            {
                return id;
            }
        }
        return $"step_{index}";
    }
}
