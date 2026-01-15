using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Helper class for DPI scaling and coordinate normalization.
/// Ensures consistent coordinate handling across different DPI settings.
/// </summary>
public static class DpiHelper
{
    private const int StandardDpi = 96;

    /// <summary>
    /// Get the system DPI scale factor.
    /// </summary>
    /// <returns>Scale factor (1.0 = 100%, 1.5 = 150%, 2.0 = 200%)</returns>
    public static double GetScaleFactor()
    {
        try
        {
            var dpi = GetDpiForSystem();
            return dpi / (double)StandardDpi;
        }
        catch
        {
            return 1.0; // Default to 100% if DPI detection fails
        }
    }

    /// <summary>
    /// Get the system DPI value.
    /// </summary>
    /// <returns>DPI value (96 = 100%, 144 = 150%, 192 = 200%)</returns>
    public static int GetSystemDpi()
    {
        try
        {
            return (int)GetDpiForSystem();
        }
        catch
        {
            return StandardDpi;
        }
    }

    /// <summary>
    /// Convert logical coordinates to physical (screen) coordinates.
    /// Use when sending input to the screen.
    /// </summary>
    /// <param name="logicalX">Logical X coordinate</param>
    /// <param name="logicalY">Logical Y coordinate</param>
    /// <returns>Physical coordinates</returns>
    public static (int x, int y) LogicalToPhysical(int logicalX, int logicalY)
    {
        var scale = GetScaleFactor();
        return (
            (int)Math.Round(logicalX * scale),
            (int)Math.Round(logicalY * scale)
        );
    }

    /// <summary>
    /// Convert logical coordinates to physical (screen) coordinates.
    /// </summary>
    public static Point LogicalToPhysical(Point logical)
    {
        var (x, y) = LogicalToPhysical(logical.X, logical.Y);
        return new Point(x, y);
    }

    /// <summary>
    /// Convert physical (screen) coordinates to logical coordinates.
    /// Use when reading coordinates from the screen.
    /// </summary>
    /// <param name="physicalX">Physical X coordinate</param>
    /// <param name="physicalY">Physical Y coordinate</param>
    /// <returns>Logical coordinates</returns>
    public static (int x, int y) PhysicalToLogical(int physicalX, int physicalY)
    {
        var scale = GetScaleFactor();
        if (scale <= 0) scale = 1.0;
        return (
            (int)Math.Round(physicalX / scale),
            (int)Math.Round(physicalY / scale)
        );
    }

    /// <summary>
    /// Convert physical (screen) coordinates to logical coordinates.
    /// </summary>
    public static Point PhysicalToLogical(Point physical)
    {
        var (x, y) = PhysicalToLogical(physical.X, physical.Y);
        return new Point(x, y);
    }

    /// <summary>
    /// Convert a rectangle from logical to physical coordinates.
    /// </summary>
    public static Rectangle LogicalToPhysical(Rectangle logical)
    {
        var scale = GetScaleFactor();
        return new Rectangle(
            (int)Math.Round(logical.X * scale),
            (int)Math.Round(logical.Y * scale),
            (int)Math.Round(logical.Width * scale),
            (int)Math.Round(logical.Height * scale)
        );
    }

    /// <summary>
    /// Convert a rectangle from physical to logical coordinates.
    /// </summary>
    public static Rectangle PhysicalToLogical(Rectangle physical)
    {
        var scale = GetScaleFactor();
        if (scale <= 0) scale = 1.0;
        return new Rectangle(
            (int)Math.Round(physical.X / scale),
            (int)Math.Round(physical.Y / scale),
            (int)Math.Round(physical.Width / scale),
            (int)Math.Round(physical.Height / scale)
        );
    }

    /// <summary>
    /// Get DPI information for a specific window.
    /// </summary>
    /// <param name="hwnd">Window handle</param>
    /// <returns>DPI value for the window's monitor</returns>
    public static int GetDpiForWindow(IntPtr hwnd)
    {
        try
        {
            return (int)GetDpiForWindowApi(hwnd);
        }
        catch
        {
            return GetSystemDpi();
        }
    }

    /// <summary>
    /// Get the scale factor for a specific window.
    /// </summary>
    public static double GetScaleFactorForWindow(IntPtr hwnd)
    {
        return GetDpiForWindow(hwnd) / (double)StandardDpi;
    }

    /// <summary>
    /// Check if DPI awareness is per-monitor (different monitors can have different DPI).
    /// </summary>
    public static bool IsPerMonitorDpiAware()
    {
        try
        {
            var result = GetProcessDpiAwareness(IntPtr.Zero, out var awareness);
            return result == 0 && (awareness == PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE ||
                                   awareness == PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE_V2);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get comprehensive DPI information.
    /// </summary>
    public static DpiInfo GetDpiInfo(IntPtr? windowHandle = null)
    {
        var systemDpi = GetSystemDpi();
        var systemScale = GetScaleFactor();

        var windowDpi = systemDpi;
        var windowScale = systemScale;

        if (windowHandle.HasValue && windowHandle.Value != IntPtr.Zero)
        {
            windowDpi = GetDpiForWindow(windowHandle.Value);
            windowScale = windowDpi / (double)StandardDpi;
        }

        return new DpiInfo
        {
            SystemDpi = systemDpi,
            SystemScaleFactor = systemScale,
            WindowDpi = windowDpi,
            WindowScaleFactor = windowScale,
            IsPerMonitorAware = IsPerMonitorDpiAware(),
            StandardDpi = StandardDpi
        };
    }

    #region Native APIs

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindowApi(IntPtr hwnd);

    [DllImport("shcore.dll")]
    private static extern int GetProcessDpiAwareness(IntPtr hprocess, out PROCESS_DPI_AWARENESS awareness);

    private enum PROCESS_DPI_AWARENESS
    {
        PROCESS_DPI_UNAWARE = 0,
        PROCESS_SYSTEM_DPI_AWARE = 1,
        PROCESS_PER_MONITOR_DPI_AWARE = 2,
        PROCESS_PER_MONITOR_DPI_AWARE_V2 = 3
    }

    #endregion
}

/// <summary>
/// DPI information container.
/// </summary>
public class DpiInfo
{
    /// <summary>
    /// System-wide DPI value.
    /// </summary>
    public int SystemDpi { get; init; }

    /// <summary>
    /// System-wide scale factor.
    /// </summary>
    public double SystemScaleFactor { get; init; }

    /// <summary>
    /// DPI for the target window (may differ in per-monitor setups).
    /// </summary>
    public int WindowDpi { get; init; }

    /// <summary>
    /// Scale factor for the target window.
    /// </summary>
    public double WindowScaleFactor { get; init; }

    /// <summary>
    /// Whether the process is per-monitor DPI aware.
    /// </summary>
    public bool IsPerMonitorAware { get; init; }

    /// <summary>
    /// Standard DPI value (96 = 100%).
    /// </summary>
    public int StandardDpi { get; init; }
}
