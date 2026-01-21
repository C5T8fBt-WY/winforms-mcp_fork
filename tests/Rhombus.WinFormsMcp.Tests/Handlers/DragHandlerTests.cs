using System.Text.Json;
using NUnit.Framework;
using Rhombus.WinFormsMcp.Server.Handlers;
using Rhombus.WinFormsMcp.Tests.Mocks;

namespace Rhombus.WinFormsMcp.Tests.Handlers;

[TestFixture]
public class DragHandlerTests
{
    private MockSessionManager _session = null!;
    private MockWindowManager _windows = null!;
    private DragHandler _handler = null!;

    [SetUp]
    public void Setup()
    {
        _session = new MockSessionManager();
        _windows = new MockWindowManager();
        _windows.AddWindow("Test Window", "0x1234", 1234);
        _handler = new DragHandler(_session, _windows);
    }

    [Test]
    public void SupportedTools_ReturnsDrag()
    {
        Assert.That(_handler.SupportedTools, Contains.Item("drag"));
    }

    [Test]
    public async Task ExecuteAsync_MissingPath_ReturnsError()
    {
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await _handler.ExecuteAsync("drag", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("path array is required"));
    }

    [Test]
    public async Task ExecuteAsync_EmptyPath_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"path\": []}").RootElement;

        var result = await _handler.ExecuteAsync("drag", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("at least 2 points"));
    }

    [Test]
    public async Task ExecuteAsync_SinglePointPath_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"path\": [{\"x\": 100, \"y\": 100}]}").RootElement;

        var result = await _handler.ExecuteAsync("drag", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("at least 2 points"));
    }

    [Test]
    public async Task ExecuteAsync_PathMissingCoordinate_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"path\": [{\"x\": 100}, {\"x\": 200, \"y\": 200}]}").RootElement;

        var result = await _handler.ExecuteAsync("drag", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("coordinate"));
    }

    [Test]
    public async Task ExecuteAsync_UnknownInputType_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"path\": [{\"x\": 100, \"y\": 100}, {\"x\": 200, \"y\": 200}], \"input\": \"unknown\"}").RootElement;

        var result = await _handler.ExecuteAsync("drag", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("Unknown input type"));
    }

    [Test]
    public async Task ExecuteAsync_ResponseIncludesWindows()
    {
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await _handler.ExecuteAsync("drag", args);

        Assert.That(result.TryGetProperty("windows", out var windows), Is.True);
    }
}
