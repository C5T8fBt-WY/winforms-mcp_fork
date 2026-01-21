using Rhombus.WinFormsMcp.Server;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Models;
using System.Diagnostics;
using System.Text.Json;

namespace Rhombus.WinFormsMcp.Tests;

/// <summary>
/// Tests for WindowManager and ToolResponse functionality.
/// </summary>
public class WindowManagerTests
{
    private WindowManager? _windowManager;

    [SetUp]
    public void Setup()
    {
        _windowManager = new WindowManager();
    }

    #region WindowManager Tests

    [Test]
    public void TestWindowManagerInitialization()
    {
        Assert.That(_windowManager, Is.Not.Null);
    }

    [Test]
    public void TestGetAllWindows_ReturnsVisibleWindows()
    {
        // There should always be at least some visible windows on Windows
        var windows = _windowManager!.GetAllWindows();

        Assert.That(windows, Is.Not.Null);
        // In a headless test environment, there may be no visible windows
        // but the list should still be valid (not null)
    }

    [Test]
    public void TestFindWindowByHandle_InvalidHandle_ReturnsNull()
    {
        var result = _windowManager!.FindWindowByHandle("0xDEADBEEF");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TestFindWindowByHandle_MalformedHandle_ReturnsNull()
    {
        var result = _windowManager!.FindWindowByHandle("not-a-handle");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TestFindWindowByTitle_NonExistent_ReturnsNull()
    {
        var result = _windowManager!.FindWindowByTitle("NonExistentWindowTitle12345");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TestFindWindow_NullParams_ReturnsNull()
    {
        var result = _windowManager!.FindWindow(null, null);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TestFindWindow_EmptyParams_ReturnsNull()
    {
        var result = _windowManager!.FindWindow("", "");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TestTranslateCoordinates_AddsOffset()
    {
        var window = new WindowInfo
        {
            Handle = "0x1234",
            Title = "Test",
            Bounds = new WindowBounds { X = 100, Y = 200, Width = 800, Height = 600 }
        };

        var (screenX, screenY) = _windowManager!.TranslateCoordinates(window, 50, 75);

        Assert.That(screenX, Is.EqualTo(150)); // 100 + 50
        Assert.That(screenY, Is.EqualTo(275)); // 200 + 75
    }

    [Test]
    public void TestTranslateCoordinates_ZeroOffset()
    {
        var window = new WindowInfo
        {
            Handle = "0x1234",
            Title = "Test",
            Bounds = new WindowBounds { X = 100, Y = 200, Width = 800, Height = 600 }
        };

        var (screenX, screenY) = _windowManager!.TranslateCoordinates(window, 0, 0);

        Assert.That(screenX, Is.EqualTo(100));
        Assert.That(screenY, Is.EqualTo(200));
    }

    [Test]
    public void TestTranslateCoordinates_NegativeOffset()
    {
        var window = new WindowInfo
        {
            Handle = "0x1234",
            Title = "Test",
            Bounds = new WindowBounds { X = 100, Y = 200, Width = 800, Height = 600 }
        };

        // Edge case: negative offsets should still work (might be useful for some scenarios)
        var (screenX, screenY) = _windowManager!.TranslateCoordinates(window, -10, -20);

        Assert.That(screenX, Is.EqualTo(90));
        Assert.That(screenY, Is.EqualTo(180));
    }

    [Test]
    public void TestTranslateCoordinates_ByTitle_NonExistent_ReturnsNull()
    {
        var result = _windowManager!.TranslateCoordinates(null, "NonExistent12345", 50, 50);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TestFindWindowsByTitle_NonExistent_ReturnsEmptyList()
    {
        var matches = _windowManager!.FindWindowsByTitle("NonExistentWindowTitle12345");
        Assert.That(matches, Is.Not.Null);
        Assert.That(matches.Count, Is.EqualTo(0));
    }

    [Test]
    public void TestIsWindowMinimized_InvalidWindow_ReturnsFalse()
    {
        var result = _windowManager!.IsWindowMinimized("0xDEADBEEF", null);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TestGetClientAreaBounds_InvalidHandle_ReturnsNull()
    {
        var result = _windowManager!.GetClientAreaBounds("0xDEADBEEF");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TestTranslateClientToScreen_InvalidHandle_ReturnsNull()
    {
        var result = _windowManager!.TranslateClientToScreen("0xDEADBEEF", 50, 50);
        Assert.That(result, Is.Null);
    }

    #endregion

    #region ToolResponse Tests

    [Test]
    public void TestToolResponse_Ok_IncludesSuccess()
    {
        var response = ToolResponse.Ok(new { message = "test" }, _windowManager!);

        Assert.That(response.Success, Is.True);
        Assert.That(response.Error, Is.Null);
        Assert.That(response.Result, Is.Not.Null);
        Assert.That(response.Windows, Is.Not.Null);
    }

    [Test]
    public void TestToolResponse_Fail_IncludesError()
    {
        var response = ToolResponse.Fail("Test error", _windowManager!);

        Assert.That(response.Success, Is.False);
        Assert.That(response.Error, Is.EqualTo("Test error"));
        Assert.That(response.Windows, Is.Not.Null);
    }

    [Test]
    public void TestToolResponse_ToJson_ValidJson()
    {
        var response = ToolResponse.Ok(new { value = 42 }, _windowManager!);
        var json = response.ToJson();

        Assert.That(json, Is.Not.Null);
        Assert.That(json.Length, Is.GreaterThan(0));

        // Verify it's valid JSON
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.GetProperty("success").GetBoolean(), Is.True);
    }

    [Test]
    public void TestToolResponse_ToJsonElement_Works()
    {
        var response = ToolResponse.Ok(new { test = "value" }, _windowManager!);
        var element = response.ToJsonElement();

        Assert.That(element.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(element.GetProperty("success").GetBoolean(), Is.True);
    }

    [Test]
    public void TestToolResponse_OkWithProperties_Works()
    {
        var response = ToolResponse.Ok(_windowManager!,
            ("pid", 1234),
            ("processName", "test.exe"));

        Assert.That(response.Success, Is.True);
        Assert.That(response.Result, Is.Not.Null);

        var json = response.ToJson();
        var doc = JsonDocument.Parse(json);
        var result = doc.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("pid").GetInt32(), Is.EqualTo(1234));
        Assert.That(result.GetProperty("processName").GetString(), Is.EqualTo("test.exe"));
    }

    [Test]
    public void TestToolResponse_FailWithPartialMatches_IncludesMatches()
    {
        var partialMatches = new List<WindowInfo>
        {
            new WindowInfo { Handle = "0x1", Title = "Similar Window 1" },
            new WindowInfo { Handle = "0x2", Title = "Similar Window 2" }
        };

        var response = ToolResponse.FailWithPartialMatches("Window not found", partialMatches, _windowManager!);

        Assert.That(response.Success, Is.False);
        Assert.That(response.Error, Is.EqualTo("Window not found"));
        Assert.That(response.PartialMatches, Is.Not.Null);
        Assert.That(response.PartialMatches!.Count, Is.EqualTo(2));
    }

    [Test]
    public void TestToolResponse_FailWithMultipleMatches_IncludesMatches()
    {
        var matches = new List<WindowInfo>
        {
            new WindowInfo { Handle = "0x1", Title = "App - Doc1" },
            new WindowInfo { Handle = "0x2", Title = "App - Doc2" }
        };

        var response = ToolResponse.FailWithMultipleMatches("Multiple windows match title 'App'", matches, _windowManager!);

        Assert.That(response.Success, Is.False);
        Assert.That(response.Matches, Is.Not.Null);
        Assert.That(response.Matches!.Count, Is.EqualTo(2));
    }

    [Test]
    public void TestToolResponse_NullResult_OmittedInJson()
    {
        var response = ToolResponse.Ok(null, _windowManager!);
        var json = response.ToJson();

        // Null result should be omitted
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.TryGetProperty("result", out _), Is.False);
    }

    [Test]
    public void TestToolResponse_EmptyPartialMatches_OmittedInJson()
    {
        var response = ToolResponse.FailWithPartialMatches("Error", new List<WindowInfo>(), _windowManager!);
        var json = response.ToJson();

        // Empty partial matches should be omitted
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.TryGetProperty("partialMatches", out _), Is.False);
    }

    [Test]
    public void TestToolResponse_Warning_IncludedInJson()
    {
        var response = ToolResponse.Ok(_windowManager!, ("message", "Test"));
        response.Warning = "Coordinates outside bounds";
        var json = response.ToJson();

        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.TryGetProperty("warning", out var warningProp), Is.True);
        Assert.That(warningProp.GetString(), Is.EqualTo("Coordinates outside bounds"));
    }

    [Test]
    public void TestToolResponse_NullWarning_OmittedInJson()
    {
        var response = ToolResponse.Ok(_windowManager!, ("message", "Test"));
        // Warning is null by default
        var json = response.ToJson();

        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.TryGetProperty("warning", out _), Is.False);
    }

    #endregion

    #region WindowInfo Tests

    [Test]
    public void TestWindowInfo_Serialization()
    {
        var windowInfo = new WindowInfo
        {
            Handle = "0x1A2B3C",
            Title = "Test Window",
            AutomationId = "MainWindow",
            Bounds = new WindowBounds { X = 100, Y = 200, Width = 800, Height = 600 },
            IsActive = true
        };

        var json = JsonSerializer.Serialize(windowInfo);
        var deserialized = JsonSerializer.Deserialize<WindowInfo>(json);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Handle, Is.EqualTo("0x1A2B3C"));
        Assert.That(deserialized.Title, Is.EqualTo("Test Window"));
        Assert.That(deserialized.AutomationId, Is.EqualTo("MainWindow"));
        Assert.That(deserialized.Bounds.X, Is.EqualTo(100));
        Assert.That(deserialized.Bounds.Y, Is.EqualTo(200));
        Assert.That(deserialized.Bounds.Width, Is.EqualTo(800));
        Assert.That(deserialized.Bounds.Height, Is.EqualTo(600));
        Assert.That(deserialized.IsActive, Is.True);
    }

    [Test]
    public void TestWindowInfo_JsonPropertyNames()
    {
        var windowInfo = new WindowInfo
        {
            Handle = "0x123",
            Title = "Test",
            AutomationId = "win1",
            Bounds = new WindowBounds { X = 0, Y = 0, Width = 100, Height = 100 },
            IsActive = false
        };

        var json = JsonSerializer.Serialize(windowInfo);

        // Verify JSON uses correct property names
        Assert.That(json, Does.Contain("\"handle\""));
        Assert.That(json, Does.Contain("\"title\""));
        Assert.That(json, Does.Contain("\"automationId\""));
        Assert.That(json, Does.Contain("\"bounds\""));
        Assert.That(json, Does.Contain("\"isActive\""));
    }

    #endregion

    #region Integration Tests (require GUI)

    [Test]
    [Ignore("Requires GUI - run manually on Windows with display")]
    public void TestGetAllWindows_WithRealWindows()
    {
        var windows = _windowManager!.GetAllWindows();

        Assert.That(windows.Count, Is.GreaterThan(0));

        foreach (var window in windows)
        {
            Assert.That(window.Handle, Is.Not.Empty);
            Assert.That(window.Handle.StartsWith("0x"), Is.True);
            Assert.That(window.Title, Is.Not.Empty);
            Assert.That(window.Bounds.Width, Is.GreaterThan(0));
            Assert.That(window.Bounds.Height, Is.GreaterThan(0));
        }
    }

    [Test]
    [Ignore("Requires GUI - run manually on Windows with display")]
    public void TestFindWindow_Notepad()
    {
        // Launch notepad
        var process = Process.Start("notepad.exe");
        try
        {
            Thread.Sleep(1000); // Wait for window

            var window = _windowManager!.FindWindowByTitle("Notepad");
            Assert.That(window, Is.Not.Null);
            Assert.That(window!.Title.Contains("Notepad"), Is.True);

            // Test translate coordinates
            var (screenX, screenY) = _windowManager.TranslateCoordinates(window, 50, 50);
            Assert.That(screenX, Is.EqualTo(window.Bounds.X + 50));
            Assert.That(screenY, Is.EqualTo(window.Bounds.Y + 50));
        }
        finally
        {
            process?.Kill();
        }
    }

    #endregion
}
