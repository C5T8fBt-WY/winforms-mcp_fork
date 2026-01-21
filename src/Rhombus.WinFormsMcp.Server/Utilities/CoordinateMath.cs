namespace Rhombus.WinFormsMcp.Server.Utilities;

/// <summary>
/// Pure coordinate calculations for DPI scaling and HIMETRIC conversion.
/// No Windows API dependencies - all calculations are mathematical.
/// </summary>
public static class CoordinateMath
{
    /// <summary>
    /// Standard Windows DPI (100% scaling).
    /// </summary>
    public const int StandardDpi = 96;

    /// <summary>
    /// HIMETRIC units per inch (for touch/pen coordinates).
    /// HIMETRIC is a resolution-independent unit where 1 inch = 2540 HIMETRIC units.
    /// </summary>
    public const double HimetricPerInch = 2540.0;

    /// <summary>
    /// Convert pixel coordinates to HIMETRIC units.
    /// HIMETRIC = (pixel * 2540) / DPI
    /// </summary>
    /// <param name="pixelX">X coordinate in pixels.</param>
    /// <param name="pixelY">Y coordinate in pixels.</param>
    /// <param name="dpiX">Horizontal DPI of the display.</param>
    /// <param name="dpiY">Vertical DPI of the display.</param>
    /// <returns>A tuple of (himetricX, himetricY).</returns>
    public static (int himetricX, int himetricY) PixelToHimetric(int pixelX, int pixelY, int dpiX, int dpiY)
    {
        // Guard against division by zero
        if (dpiX <= 0) dpiX = StandardDpi;
        if (dpiY <= 0) dpiY = StandardDpi;

        return (
            (int)((pixelX * HimetricPerInch) / dpiX),
            (int)((pixelY * HimetricPerInch) / dpiY)
        );
    }

    /// <summary>
    /// Convert HIMETRIC units to pixel coordinates.
    /// Pixel = (HIMETRIC * DPI) / 2540
    /// </summary>
    /// <param name="himetricX">X coordinate in HIMETRIC units.</param>
    /// <param name="himetricY">Y coordinate in HIMETRIC units.</param>
    /// <param name="dpiX">Horizontal DPI of the display.</param>
    /// <param name="dpiY">Vertical DPI of the display.</param>
    /// <returns>A tuple of (pixelX, pixelY).</returns>
    public static (int pixelX, int pixelY) HimetricToPixel(int himetricX, int himetricY, int dpiX, int dpiY)
    {
        // Guard against division by zero
        if (dpiX <= 0) dpiX = StandardDpi;
        if (dpiY <= 0) dpiY = StandardDpi;

        return (
            (int)((himetricX * dpiX) / HimetricPerInch),
            (int)((himetricY * dpiY) / HimetricPerInch)
        );
    }

    /// <summary>
    /// Convert window-relative coordinates to screen coordinates.
    /// </summary>
    /// <param name="windowX">X coordinate relative to window.</param>
    /// <param name="windowY">Y coordinate relative to window.</param>
    /// <param name="windowLeft">Left edge of window in screen coordinates.</param>
    /// <param name="windowTop">Top edge of window in screen coordinates.</param>
    /// <returns>A tuple of (screenX, screenY).</returns>
    public static (int screenX, int screenY) WindowToScreen(int windowX, int windowY, int windowLeft, int windowTop)
    {
        return (windowLeft + windowX, windowTop + windowY);
    }

    /// <summary>
    /// Convert screen coordinates to window-relative coordinates.
    /// </summary>
    /// <param name="screenX">X coordinate in screen coordinates.</param>
    /// <param name="screenY">Y coordinate in screen coordinates.</param>
    /// <param name="windowLeft">Left edge of window in screen coordinates.</param>
    /// <param name="windowTop">Top edge of window in screen coordinates.</param>
    /// <returns>A tuple of (windowX, windowY).</returns>
    public static (int windowX, int windowY) ScreenToWindow(int screenX, int screenY, int windowLeft, int windowTop)
    {
        return (screenX - windowLeft, screenY - windowTop);
    }

    /// <summary>
    /// Get the scale factor for a given DPI value.
    /// Scale = DPI / 96 (e.g., 144 DPI = 1.5x scale)
    /// </summary>
    /// <param name="dpi">The DPI value.</param>
    /// <returns>The scale factor as a multiplier.</returns>
    public static double GetScaleFactor(int dpi)
    {
        if (dpi <= 0) return 1.0;
        return dpi / (double)StandardDpi;
    }

    /// <summary>
    /// Adjust a pixel value for DPI scaling.
    /// </summary>
    /// <param name="pixels">The pixel value at standard DPI.</param>
    /// <param name="dpi">The target DPI.</param>
    /// <returns>The scaled pixel value.</returns>
    public static int ScaleForDpi(int pixels, int dpi)
    {
        return (int)(pixels * GetScaleFactor(dpi));
    }

    /// <summary>
    /// Linearly interpolate between two integer values.
    /// </summary>
    /// <param name="start">The start value.</param>
    /// <param name="end">The end value.</param>
    /// <param name="t">The interpolation factor (0.0 to 1.0).</param>
    /// <returns>The interpolated value.</returns>
    public static int Lerp(int start, int end, double t)
    {
        return start + (int)((end - start) * t);
    }

    /// <summary>
    /// Calculate the distance between two points.
    /// </summary>
    /// <param name="x1">First point X coordinate.</param>
    /// <param name="y1">First point Y coordinate.</param>
    /// <param name="x2">Second point X coordinate.</param>
    /// <param name="y2">Second point Y coordinate.</param>
    /// <returns>The Euclidean distance between the points.</returns>
    public static double Distance(int x1, int y1, int x2, int y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        return System.Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Clamp a value to be within a range.
    /// </summary>
    /// <param name="value">The value to clamp.</param>
    /// <param name="min">The minimum allowed value.</param>
    /// <param name="max">The maximum allowed value.</param>
    /// <returns>The clamped value.</returns>
    public static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
