using System;
using System.Runtime.InteropServices;

namespace C5T8fBtWY.WinFormsMcp.Server.Interop;

/// <summary>
/// P/Invoke declarations for DPI-related APIs.
/// </summary>
public static class DpiInterop
{
    /// <summary>
    /// Get the DPI for a specific window (Windows 10 1607+).
    /// </summary>
    /// <param name="hwnd">Window handle.</param>
    /// <returns>DPI value for the window.</returns>
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// Get a device context for the entire screen.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    /// <summary>
    /// Release a device context.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    /// <summary>
    /// Get device capabilities from a device context.
    /// </summary>
    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    /// <summary>
    /// Get system metrics.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    /// <summary>
    /// Get system DPI using device context.
    /// </summary>
    /// <returns>A tuple of (dpiX, dpiY).</returns>
    public static (int dpiX, int dpiY) GetSystemDpi()
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        try
        {
            int dpiX = GetDeviceCaps(hdc, Win32Constants.LOGPIXELSX);
            int dpiY = GetDeviceCaps(hdc, Win32Constants.LOGPIXELSY);

            // Fallback to 96 if query fails
            if (dpiX <= 0) dpiX = 96;
            if (dpiY <= 0) dpiY = 96;

            return (dpiX, dpiY);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    /// <summary>
    /// Get virtual screen origin (may be negative in multi-monitor setups).
    /// </summary>
    /// <returns>A tuple of (originX, originY).</returns>
    public static (int originX, int originY) GetVirtualScreenOrigin()
    {
        int originX = GetSystemMetrics(Win32Constants.SM_XVIRTUALSCREEN);
        int originY = GetSystemMetrics(Win32Constants.SM_YVIRTUALSCREEN);
        return (originX, originY);
    }

    /// <summary>
    /// Get screen dimensions.
    /// </summary>
    /// <returns>A tuple of (width, height).</returns>
    public static (int width, int height) GetScreenSize()
    {
        int width = GetSystemMetrics(Win32Constants.SM_CXSCREEN);
        int height = GetSystemMetrics(Win32Constants.SM_CYSCREEN);
        return (width, height);
    }
}
