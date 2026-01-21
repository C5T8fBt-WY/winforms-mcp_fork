using System.Text.Json;
using NUnit.Framework;
using Rhombus.WinFormsMcp.Server.Handlers;
using Rhombus.WinFormsMcp.Tests.Mocks;

namespace Rhombus.WinFormsMcp.Tests.Handlers;

[TestFixture]
public class ClickHandlerTests
{
    private MockSessionManager _session = null!;
    private MockWindowManager _windows = null!;
    private ClickHandler _handler = null!;

    [SetUp]
    public void Setup()
    {
        _session = new MockSessionManager();
        _windows = new MockWindowManager();
        _windows.AddWindow("Test Window", "0x1234", 1234);
        _handler = new ClickHandler(_session, _windows);
    }

    [Test]
    public void SupportedTools_ReturnsClick()
    {
        Assert.That(_handler.SupportedTools, Contains.Item("click"));
    }

    [Test]
    public async Task ExecuteAsync_NoTargetNoCoordinates_ReturnsError()
    {
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await _handler.ExecuteAsync("click", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("target").Or.Contains("coordinates"));
    }

    [Test]
    public async Task ExecuteAsync_PartialCoordinates_ReturnsError()
    {
        // Only x, missing y
        var args = JsonDocument.Parse("{\"x\": 100}").RootElement;

        var result = await _handler.ExecuteAsync("click", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
    }

    [Test]
    public async Task ExecuteAsync_ElementNotFound_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"target\": \"elem_999\"}").RootElement;

        var result = await _handler.ExecuteAsync("click", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("Element not found"));
    }

    // Note: StaleElement test requires real AutomationElement objects
    // which can't be easily mocked. This is tested in integration tests.

    [Test]
    public async Task ExecuteAsync_UnknownInputType_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"x\": 100, \"y\": 200, \"input\": \"unknown\"}").RootElement;

        var result = await _handler.ExecuteAsync("click", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("Unknown input type"));
    }

    [Test]
    public async Task ExecuteAsync_ResponseIncludesWindows()
    {
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await _handler.ExecuteAsync("click", args);

        Assert.That(result.TryGetProperty("windows", out var windows), Is.True);
        Assert.That(windows.GetArrayLength(), Is.EqualTo(1));
    }
}
