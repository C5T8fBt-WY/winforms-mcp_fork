using System.Text.Json;
using C5T8fBtWY.WinFormsMcp.Server.Utilities;

namespace C5T8fBtWY.WinFormsMcp.Tests;

/// <summary>
/// Unit tests for ArgHelpers utility class.
/// </summary>
public class ArgHelpersTests
{
    [Test]
    public void GetString_ReturnsValue_WhenPropertyExists()
    {
        var args = JsonDocument.Parse(@"{""name"": ""test""}").RootElement;
        var result = ArgHelpers.GetString(args, "name");
        Assert.That(result, Is.EqualTo("test"));
    }

    [Test]
    public void GetString_ReturnsNull_WhenPropertyMissing()
    {
        var args = JsonDocument.Parse(@"{}").RootElement;
        var result = ArgHelpers.GetString(args, "name");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetString_ReturnsNull_WhenValueIsNull()
    {
        var args = JsonDocument.Parse(@"{""name"": null}").RootElement;
        var result = ArgHelpers.GetString(args, "name");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetString_ReturnsNull_WhenArgsIsNull()
    {
        var args = JsonDocument.Parse("null").RootElement;
        var result = ArgHelpers.GetString(args, "name");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetInt_ReturnsValue_WhenPropertyExists()
    {
        var args = JsonDocument.Parse(@"{""count"": 42}").RootElement;
        var result = ArgHelpers.GetInt(args, "count");
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public void GetInt_ReturnsDefault_WhenPropertyMissing()
    {
        var args = JsonDocument.Parse(@"{}").RootElement;
        var result = ArgHelpers.GetInt(args, "count", 10);
        Assert.That(result, Is.EqualTo(10));
    }

    [Test]
    public void GetInt_ReturnsDefault_WhenValueNotNumber()
    {
        var args = JsonDocument.Parse(@"{""count"": ""not a number""}").RootElement;
        var result = ArgHelpers.GetInt(args, "count", 5);
        Assert.That(result, Is.EqualTo(5));
    }

    [Test]
    public void GetDouble_ReturnsValue_WhenPropertyExists()
    {
        var args = JsonDocument.Parse(@"{""value"": 3.14}").RootElement;
        var result = ArgHelpers.GetDouble(args, "value");
        Assert.That(result, Is.EqualTo(3.14).Within(0.001));
    }

    [Test]
    public void GetDouble_ReturnsDefault_WhenPropertyMissing()
    {
        var args = JsonDocument.Parse(@"{}").RootElement;
        var result = ArgHelpers.GetDouble(args, "value", 1.5);
        Assert.That(result, Is.EqualTo(1.5));
    }

    [Test]
    public void GetBool_ReturnsTrue_WhenValueIsTrue()
    {
        var args = JsonDocument.Parse(@"{""enabled"": true}").RootElement;
        var result = ArgHelpers.GetBool(args, "enabled");
        Assert.That(result, Is.True);
    }

    [Test]
    public void GetBool_ReturnsFalse_WhenValueIsFalse()
    {
        var args = JsonDocument.Parse(@"{""enabled"": false}").RootElement;
        var result = ArgHelpers.GetBool(args, "enabled");
        Assert.That(result, Is.False);
    }

    [Test]
    public void GetBool_ReturnsDefault_WhenPropertyMissing()
    {
        var args = JsonDocument.Parse(@"{}").RootElement;
        var result = ArgHelpers.GetBool(args, "enabled", true);
        Assert.That(result, Is.True);
    }

    [Test]
    public void GetBool_ReturnsDefault_WhenValueNotBoolean()
    {
        var args = JsonDocument.Parse(@"{""enabled"": ""yes""}").RootElement;
        var result = ArgHelpers.GetBool(args, "enabled", true);
        Assert.That(result, Is.True);
    }

    public enum TestEnum { One, Two, Three }

    [Test]
    public void GetEnum_ReturnsValue_WhenPropertyMatchesCase()
    {
        var args = JsonDocument.Parse(@"{""option"": ""Two""}").RootElement;
        var result = ArgHelpers.GetEnum<TestEnum>(args, "option");
        Assert.That(result, Is.EqualTo(TestEnum.Two));
    }

    [Test]
    public void GetEnum_ReturnsValue_WhenPropertyIgnoresCase()
    {
        var args = JsonDocument.Parse(@"{""option"": ""three""}").RootElement;
        var result = ArgHelpers.GetEnum<TestEnum>(args, "option");
        Assert.That(result, Is.EqualTo(TestEnum.Three));
    }

    [Test]
    public void GetEnum_ReturnsNull_WhenPropertyMissing()
    {
        var args = JsonDocument.Parse(@"{}").RootElement;
        var result = ArgHelpers.GetEnum<TestEnum>(args, "option");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetEnum_ReturnsNull_WhenValueInvalid()
    {
        var args = JsonDocument.Parse(@"{""option"": ""invalid""}").RootElement;
        var result = ArgHelpers.GetEnum<TestEnum>(args, "option");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetObject_ReturnsElement_WhenPropertyIsObject()
    {
        var args = JsonDocument.Parse(@"{""data"": {""x"": 1, ""y"": 2}}").RootElement;
        var result = ArgHelpers.GetObject(args, "data");
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.GetProperty("x").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public void GetObject_ReturnsNull_WhenPropertyMissing()
    {
        var args = JsonDocument.Parse(@"{}").RootElement;
        var result = ArgHelpers.GetObject(args, "data");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetObject_ReturnsNull_WhenPropertyNotObject()
    {
        var args = JsonDocument.Parse(@"{""data"": ""string""}").RootElement;
        var result = ArgHelpers.GetObject(args, "data");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetArray_ReturnsElements_WhenPropertyIsArray()
    {
        var args = JsonDocument.Parse(@"{""items"": [1, 2, 3]}").RootElement;
        var result = ArgHelpers.GetArray(args, "items");
        Assert.That(result, Is.Not.Null);
        var items = result!.ToList();
        Assert.That(items, Has.Count.EqualTo(3));
        Assert.That(items[0].GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public void GetArray_ReturnsNull_WhenPropertyMissing()
    {
        var args = JsonDocument.Parse(@"{}").RootElement;
        var result = ArgHelpers.GetArray(args, "items");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetArray_ReturnsNull_WhenPropertyNotArray()
    {
        var args = JsonDocument.Parse(@"{""items"": ""not array""}").RootElement;
        var result = ArgHelpers.GetArray(args, "items");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetUInt_ReturnsValue_WhenPropertyExists()
    {
        var args = JsonDocument.Parse(@"{""flags"": 255}").RootElement;
        var result = ArgHelpers.GetUInt(args, "flags");
        Assert.That(result, Is.EqualTo(255u));
    }

    [Test]
    public void GetLong_ReturnsValue_WhenPropertyExists()
    {
        var args = JsonDocument.Parse(@"{""timestamp"": 1234567890123}").RootElement;
        var result = ArgHelpers.GetLong(args, "timestamp");
        Assert.That(result, Is.EqualTo(1234567890123L));
    }
}
