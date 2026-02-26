using System.Text.Json;
using C5T8fBtWY.WinFormsMcp.Server.Utilities;

namespace C5T8fBtWY.WinFormsMcp.Tests;

/// <summary>
/// Unit tests for VariableInterpolator utility class.
/// These tests verify type-preserving variable interpolation.
/// </summary>
public class VariableInterpolatorTests
{
    [Test]
    public void Interpolate_StringVariable_PreservesType()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["step1"] = JsonDocument.Parse(@"{""result"": {""elementId"": ""elem_42""}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""elementPath"": ""$step1.result.elementId""}").RootElement;
        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        Assert.That(interpolated.TryGetProperty("elementPath", out var prop), Is.True);
        Assert.That(prop.ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(prop.GetString(), Is.EqualTo("elem_42"));
    }

    [Test]
    public void Interpolate_IntegerVariable_PreservesType()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["launch"] = JsonDocument.Parse(@"{""result"": {""pid"": 12345}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""targetPid"": ""$launch.result.pid""}").RootElement;
        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        Assert.That(interpolated.TryGetProperty("targetPid", out var prop), Is.True);
        Assert.That(prop.ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(prop.GetInt32(), Is.EqualTo(12345));
    }

    [Test]
    public void Interpolate_BooleanVariable_PreservesType()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["check"] = JsonDocument.Parse(@"{""result"": {""visible"": true}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""isVisible"": ""$check.result.visible""}").RootElement;
        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        Assert.That(interpolated.TryGetProperty("isVisible", out var prop), Is.True);
        Assert.That(prop.ValueKind, Is.EqualTo(JsonValueKind.True));
        Assert.That(prop.GetBoolean(), Is.True);
    }

    [Test]
    public void Interpolate_FalseBooleanVariable_PreservesType()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["check"] = JsonDocument.Parse(@"{""result"": {""enabled"": false}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""isEnabled"": ""$check.result.enabled""}").RootElement;
        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        Assert.That(interpolated.TryGetProperty("isEnabled", out var prop), Is.True);
        Assert.That(prop.ValueKind, Is.EqualTo(JsonValueKind.False));
        Assert.That(prop.GetBoolean(), Is.False);
    }

    [Test]
    public void Interpolate_DoubleVariable_PreservesType()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["measure"] = JsonDocument.Parse(@"{""result"": {""value"": 3.14159}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""pi"": ""$measure.result.value""}").RootElement;
        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        Assert.That(interpolated.TryGetProperty("pi", out var prop), Is.True);
        Assert.That(prop.ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(prop.GetDouble(), Is.EqualTo(3.14159).Within(0.00001));
    }

    [Test]
    public void Interpolate_NestedPath_ResolvesCorrectly()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["find"] = JsonDocument.Parse(@"{""result"": {""element"": {""bounds"": {""x"": 100}}}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""x"": ""$find.result.element.bounds.x""}").RootElement;
        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        Assert.That(interpolated.TryGetProperty("x", out var prop), Is.True);
        Assert.That(prop.GetInt32(), Is.EqualTo(100));
    }

    [Test]
    public void Interpolate_LastAlias_ResolvesToLastStep()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["step1"] = JsonDocument.Parse(@"{""result"": {""value"": ""first""}}").RootElement,
            ["step2"] = JsonDocument.Parse(@"{""result"": {""value"": ""second""}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""prev"": ""$last.result.value""}").RootElement;
        var interpolated = VariableInterpolator.Interpolate(args, stepResults, "step2");

        Assert.That(interpolated.TryGetProperty("prev", out var prop), Is.True);
        Assert.That(prop.GetString(), Is.EqualTo("second"));
    }

    [Test]
    public void Interpolate_NoVariables_PassesThrough()
    {
        var stepResults = new Dictionary<string, JsonElement>();

        var args = JsonDocument.Parse(@"{""name"": ""static"", ""count"": 5}").RootElement;
        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        Assert.That(interpolated.TryGetProperty("name", out var nameProp), Is.True);
        Assert.That(nameProp.GetString(), Is.EqualTo("static"));
        Assert.That(interpolated.TryGetProperty("count", out var countProp), Is.True);
        Assert.That(countProp.GetInt32(), Is.EqualTo(5));
    }

    [Test]
    public void Interpolate_MixedVariablesAndLiterals_WorksCorrectly()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["step1"] = JsonDocument.Parse(@"{""result"": {""id"": ""elem_1""}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""elementId"": ""$step1.result.id"", ""action"": ""click"", ""x"": 100}").RootElement;
        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        Assert.That(interpolated.GetProperty("elementId").GetString(), Is.EqualTo("elem_1"));
        Assert.That(interpolated.GetProperty("action").GetString(), Is.EqualTo("click"));
        Assert.That(interpolated.GetProperty("x").GetInt32(), Is.EqualTo(100));
    }

    [Test]
    public void Interpolate_ArrayWithVariables_InterpolatesElements()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["step1"] = JsonDocument.Parse(@"{""result"": {""value"": 42}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""items"": [""$step1.result.value"", 100]}").RootElement;
        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        var items = interpolated.GetProperty("items").EnumerateArray().ToList();
        Assert.That(items, Has.Count.EqualTo(2));
        Assert.That(items[0].GetInt32(), Is.EqualTo(42));
        Assert.That(items[1].GetInt32(), Is.EqualTo(100));
    }

    [Test]
    public void Interpolate_MissingStep_ThrowsException()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["step1"] = JsonDocument.Parse(@"{""result"": {""id"": ""elem_1""}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""id"": ""$nonexistent.result.id""}").RootElement;

        Assert.Throws<InvalidOperationException>(() =>
            VariableInterpolator.Interpolate(args, stepResults, null));
    }

    [Test]
    public void Interpolate_MissingProperty_ThrowsException()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["step1"] = JsonDocument.Parse(@"{""result"": {""id"": ""elem_1""}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""id"": ""$step1.result.nonexistent""}").RootElement;

        Assert.Throws<InvalidOperationException>(() =>
            VariableInterpolator.Interpolate(args, stepResults, null));
    }

    [Test]
    public void Interpolate_LastAliasNoLastStep_ThrowsException()
    {
        var stepResults = new Dictionary<string, JsonElement>();

        var args = JsonDocument.Parse(@"{""id"": ""$last.result.id""}").RootElement;

        Assert.Throws<InvalidOperationException>(() =>
            VariableInterpolator.Interpolate(args, stepResults, null));
    }

    [Test]
    public void IsVariableReference_ValidReference_ReturnsTrue()
    {
        var result = VariableInterpolator.IsVariableReference("$step1.result.id", out var stepId, out var path);
        Assert.That(result, Is.True);
        Assert.That(stepId, Is.EqualTo("step1"));
        Assert.That(path, Is.EqualTo("result.id"));
    }

    [Test]
    public void IsVariableReference_LastAlias_ReturnsTrue()
    {
        var result = VariableInterpolator.IsVariableReference("$last.result.value", out var stepId, out var path);
        Assert.That(result, Is.True);
        Assert.That(stepId, Is.EqualTo("last"));
        Assert.That(path, Is.EqualTo("result.value"));
    }

    [Test]
    public void IsVariableReference_LiteralString_ReturnsFalse()
    {
        var result = VariableInterpolator.IsVariableReference("not a variable", out _, out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsVariableReference_PartialMatch_ReturnsFalse()
    {
        // Variable references must be the entire string
        var result = VariableInterpolator.IsVariableReference("prefix$step1.result.id", out _, out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsVariableReference_NullString_ReturnsFalse()
    {
        var result = VariableInterpolator.IsVariableReference(null, out _, out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsVariableReference_EmptyString_ReturnsFalse()
    {
        var result = VariableInterpolator.IsVariableReference("", out _, out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void ResolvePath_ValidPath_ReturnsValue()
    {
        var root = JsonDocument.Parse(@"{""a"": {""b"": {""c"": 42}}}").RootElement;
        var result = VariableInterpolator.ResolvePath(root, "a.b.c");
        Assert.That(result.GetInt32(), Is.EqualTo(42));
    }

    [Test]
    public void ResolvePath_SingleLevel_ReturnsValue()
    {
        var root = JsonDocument.Parse(@"{""value"": ""test""}").RootElement;
        var result = VariableInterpolator.ResolvePath(root, "value");
        Assert.That(result.GetString(), Is.EqualTo("test"));
    }

    [Test]
    public void ResolvePath_InvalidPath_ThrowsException()
    {
        var root = JsonDocument.Parse(@"{""a"": {""b"": 1}}").RootElement;
        Assert.Throws<InvalidOperationException>(() =>
            VariableInterpolator.ResolvePath(root, "a.x.y"));
    }

    [Test]
    public void Interpolate_ObjectVariable_PreservesStructure()
    {
        var stepResults = new Dictionary<string, JsonElement>
        {
            ["step1"] = JsonDocument.Parse(@"{""result"": {""bounds"": {""x"": 10, ""y"": 20}}}").RootElement
        };

        var args = JsonDocument.Parse(@"{""rect"": ""$step1.result.bounds""}").RootElement;
        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);

        var rect = interpolated.GetProperty("rect");
        Assert.That(rect.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(rect.GetProperty("x").GetInt32(), Is.EqualTo(10));
        Assert.That(rect.GetProperty("y").GetInt32(), Is.EqualTo(20));
    }

    [Test]
    public void Interpolate_NullArgs_ReturnsNull()
    {
        var stepResults = new Dictionary<string, JsonElement>();
        var args = JsonDocument.Parse("null").RootElement;
        var interpolated = VariableInterpolator.Interpolate(args, stepResults, null);
        Assert.That(interpolated.ValueKind, Is.EqualTo(JsonValueKind.Null));
    }
}
