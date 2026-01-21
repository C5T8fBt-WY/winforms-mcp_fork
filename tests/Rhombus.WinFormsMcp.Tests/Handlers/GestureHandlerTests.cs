using System.Text.Json;
using NUnit.Framework;
using Rhombus.WinFormsMcp.Server.Handlers;
using Rhombus.WinFormsMcp.Tests.Mocks;

namespace Rhombus.WinFormsMcp.Tests.Handlers;

[TestFixture]
public class GestureHandlerTests
{
    private MockSessionManager _session = null!;
    private MockWindowManager _windows = null!;
    private GestureHandler _handler = null!;

    [SetUp]
    public void Setup()
    {
        _session = new MockSessionManager();
        _windows = new MockWindowManager();
        _windows.AddWindow("Test Window", "0x1234", 1234);
        _handler = new GestureHandler(_session, _windows);
    }

    [Test]
    public void SupportedTools_ReturnsGesture()
    {
        Assert.That(_handler.SupportedTools, Contains.Item("gesture"));
    }

    [Test]
    public async Task ExecuteAsync_MissingType_ReturnsError()
    {
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await _handler.ExecuteAsync("gesture", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("type is required"));
    }

    [Test]
    public async Task ExecuteAsync_UnknownType_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"type\": \"unknown\"}").RootElement;

        var result = await _handler.ExecuteAsync("gesture", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("Unknown gesture type"));
    }

    [Test]
    public async Task ExecuteAsync_PinchMissingCenter_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"type\": \"pinch\"}").RootElement;

        var result = await _handler.ExecuteAsync("gesture", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("center is required"));
    }

    [Test]
    public async Task ExecuteAsync_RotateMissingCenter_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"type\": \"rotate\"}").RootElement;

        var result = await _handler.ExecuteAsync("gesture", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("center is required"));
    }

    [Test]
    public async Task ExecuteAsync_CustomMissingFingers_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"type\": \"custom\"}").RootElement;

        var result = await _handler.ExecuteAsync("gesture", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("fingers array is required"));
    }

    [Test]
    public async Task ExecuteAsync_CustomTooFewFingers_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"type\": \"custom\", \"fingers\": [{\"path\": [{\"x\": 0, \"y\": 0}]}]}").RootElement;

        var result = await _handler.ExecuteAsync("gesture", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("at least 2 fingers"));
    }

    [Test]
    public async Task ExecuteAsync_ResponseIncludesWindows()
    {
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await _handler.ExecuteAsync("gesture", args);

        Assert.That(result.TryGetProperty("windows", out var windows), Is.True);
    }
}
