using System.Text.Json;
using C5T8fBtWY.WinFormsMcp.Server.Handlers;
using C5T8fBtWY.WinFormsMcp.Tests.Mocks;
using NUnit.Framework;

namespace C5T8fBtWY.WinFormsMcp.Tests.Handlers;

[TestFixture]
public class AppHandlerTests
{
    private MockSessionManager _session = null!;
    private MockWindowManager _windows = null!;
    private AppHandler _handler = null!;

    [SetUp]
    public void Setup()
    {
        _session = new MockSessionManager();
        _windows = new MockWindowManager();
        _windows.AddWindow("Test Window", "0x1234", 1234);
        _handler = new AppHandler(_session, _windows);
    }

    [Test]
    public void SupportedTools_ReturnsApp()
    {
        Assert.That(_handler.SupportedTools, Contains.Item("app"));
    }

    [Test]
    public async Task ExecuteAsync_UnknownAction_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"action\": \"unknown\"}").RootElement;

        var result = await _handler.ExecuteAsync("app", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("Unknown action"));
    }

    [Test]
    public async Task ExecuteAsync_LaunchMissingPath_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"action\": \"launch\"}").RootElement;

        var result = await _handler.ExecuteAsync("app", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("path is required"));
    }

    [Test]
    public async Task ExecuteAsync_AttachMissingPidAndTitle_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"action\": \"attach\"}").RootElement;

        var result = await _handler.ExecuteAsync("app", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        // Error could be about pid/title or about automation not configured
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("pid or title").Or.Contains("failed"));
    }

    [Test]
    public async Task ExecuteAsync_CloseMissingPid_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"action\": \"close\"}").RootElement;

        var result = await _handler.ExecuteAsync("app", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("pid is required"));
    }

    [Test]
    public async Task ExecuteAsync_InfoMissingPid_ReturnsError()
    {
        var args = JsonDocument.Parse("{\"action\": \"info\"}").RootElement;

        var result = await _handler.ExecuteAsync("app", args);

        Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("pid is required"));
    }

    [Test]
    public async Task ExecuteAsync_ResponseIncludesWindows()
    {
        var args = JsonDocument.Parse("{\"action\": \"unknown\"}").RootElement;

        var result = await _handler.ExecuteAsync("app", args);

        Assert.That(result.TryGetProperty("windows", out var windows), Is.True);
    }
}
