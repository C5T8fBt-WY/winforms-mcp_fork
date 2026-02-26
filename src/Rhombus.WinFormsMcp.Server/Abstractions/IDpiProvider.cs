using System;

namespace C5T8fBtWY.WinFormsMcp.Server.Abstractions;

/// <summary>
/// Abstraction for DPI-related queries to enable testing.
/// Production code uses Win32DpiProvider, tests can inject mock implementations.
/// </summary>
public interface IDpiProvider
{
    /// <summary>
    /// Get the system DPI (typically from the primary monitor).
    /// </summary>
    /// <returns>A tuple of (dpiX, dpiY).</returns>
    (int dpiX, int dpiY) GetSystemDpi();

    /// <summary>
    /// Get the DPI for a specific window.
    /// </summary>
    /// <param name="hwnd">The window handle.</param>
    /// <returns>A tuple of (dpiX, dpiY).</returns>
    (int dpiX, int dpiY) GetWindowDpi(IntPtr hwnd);

    /// <summary>
    /// Get the X origin of the virtual screen (may be negative in multi-monitor setups).
    /// </summary>
    int GetVirtualScreenOriginX();

    /// <summary>
    /// Get the Y origin of the virtual screen (may be negative in multi-monitor setups).
    /// </summary>
    int GetVirtualScreenOriginY();
}

/// <summary>
/// Mock DPI provider for testing (returns standard 96 DPI).
/// </summary>
public class StandardDpiProvider : IDpiProvider
{
    /// <summary>
    /// Singleton instance for testing.
    /// </summary>
    public static readonly StandardDpiProvider Instance = new();

    private const int StandardDpi = 96;

    public (int dpiX, int dpiY) GetSystemDpi() => (StandardDpi, StandardDpi);

    public (int dpiX, int dpiY) GetWindowDpi(IntPtr hwnd) => (StandardDpi, StandardDpi);

    public int GetVirtualScreenOriginX() => 0;

    public int GetVirtualScreenOriginY() => 0;
}
