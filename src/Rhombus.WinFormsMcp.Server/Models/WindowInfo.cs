using System;
using System.Text.Json.Serialization;

namespace Rhombus.WinFormsMcp.Server.Models;

/// <summary>
/// Information about a visible window for agent context.
/// Returned in every tool response so agents always know available windows.
/// </summary>
public class WindowInfo
{
    /// <summary>
    /// Native HWND pointer. Use this internally to avoid string parsing overhead.
    /// </summary>
    [JsonIgnore]
    public IntPtr HandlePtr { get; set; } = IntPtr.Zero;

    /// <summary>
    /// Native HWND as hex string (e.g., "0x1A2B3C"). Stable within session.
    /// Computed from HandlePtr for JSON serialization.
    /// </summary>
    [JsonPropertyName("handle")]
    public string Handle
    {
        get => HandlePtr == IntPtr.Zero ? "" : $"0x{HandlePtr.ToInt64():X}";
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                HandlePtr = IntPtr.Zero;
                return;
            }
            try
            {
                var cleanHex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? value.Substring(2)
                    : value;
                HandlePtr = new IntPtr(long.Parse(cleanHex, System.Globalization.NumberStyles.HexNumber));
            }
            catch
            {
                HandlePtr = IntPtr.Zero;
            }
        }
    }

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

    /// <summary>
    /// Process ID that owns this window.
    /// </summary>
    [JsonPropertyName("processId")]
    public int ProcessId { get; set; }
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
