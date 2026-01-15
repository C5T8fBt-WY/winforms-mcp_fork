using System.Text.Json.Serialization;

namespace Rhombus.WinFormsMcp.Server.Models;

/// <summary>
/// Information about a visible window for agent context.
/// Returned in every tool response so agents always know available windows.
/// </summary>
public class WindowInfo
{
    /// <summary>
    /// Native HWND as hex string (e.g., "0x1A2B3C"). Stable within session.
    /// </summary>
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = "";

    /// <summary>
    /// Current window title. May change dynamically.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>
    /// Static AutomationId if set by developer. May be empty.
    /// </summary>
    [JsonPropertyName("automationId")]
    public string AutomationId { get; set; } = "";

    /// <summary>
    /// Window position and size on screen.
    /// </summary>
    [JsonPropertyName("bounds")]
    public WindowBounds Bounds { get; set; } = new();

    /// <summary>
    /// Whether this window currently has focus.
    /// </summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}

/// <summary>
/// Window position and size.
/// </summary>
public class WindowBounds
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}
