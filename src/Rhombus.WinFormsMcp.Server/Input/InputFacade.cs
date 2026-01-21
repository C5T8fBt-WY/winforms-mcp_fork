namespace Rhombus.WinFormsMcp.Server.Input;

/// <summary>
/// Static facade for input injection that uses the new Input classes.
/// Provides backwards-compatible static methods during the migration period.
/// </summary>
public static class InputFacade
{
    private static readonly Lazy<TouchInput> _touch = new(() => new TouchInput());
    private static readonly Lazy<PenInput> _pen = new(() => new PenInput());
    private static readonly Lazy<MouseInput> _mouse = new(() => new MouseInput());

    /// <summary>
    /// Get the TouchInput instance.
    /// </summary>
    public static TouchInput Touch => _touch.Value;

    /// <summary>
    /// Get the PenInput instance.
    /// </summary>
    public static PenInput Pen => _pen.Value;

    /// <summary>
    /// Get the MouseInput instance.
    /// </summary>
    public static MouseInput Mouse => _mouse.Value;

    // Touch convenience methods
    public static bool TouchTap(int x, int y, int holdMs = 0, bool useLegacy = false)
        => Touch.Tap(x, y, holdMs, useLegacy);

    public static bool TouchDrag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 5)
        => Touch.Drag(x1, y1, x2, y2, steps, delayMs);

    public static bool PinchZoom(int centerX, int centerY, int startDistance, int endDistance, int steps = 20, int delayMs = 0)
        => Touch.PinchZoom(centerX, centerY, startDistance, endDistance, steps, delayMs);

    public static bool Rotate(int centerX, int centerY, int radius, double startAngle, double endAngle, int steps = 20, int delayMs = 0)
        => Touch.Rotate(centerX, centerY, radius, startAngle, endAngle, steps, delayMs);

    public static (bool success, int fingersProcessed, int totalSteps) MultiTouchGesture(
        (int x, int y, int timeMs)[][] fingers, int interpolationSteps = 5)
        => Touch.MultiTouchGesture(fingers, interpolationSteps);

    // Pen convenience methods
    public static bool PenTap(int x, int y, uint pressure = 512, int holdMs = 0, IntPtr hwndTarget = default)
        => Pen.Tap(x, y, pressure, holdMs, hwndTarget);

    public static bool PenStroke(int x1, int y1, int x2, int y2, int steps = 20, uint pressure = 512, bool eraser = false, int delayMs = 2, IntPtr hwndTarget = default)
        => Pen.Stroke(x1, y1, x2, y2, steps, pressure, eraser, delayMs, hwndTarget);

    // Mouse convenience methods
    public static bool MouseClick(int x, int y, bool doubleClick = false, int delayMs = 0)
        => Mouse.Click(x, y, doubleClick, delayMs);

    public static bool MouseDrag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 0, string? targetWindow = null)
        => Mouse.Drag(x1, y1, x2, y2, steps, delayMs, targetWindow);

    public static (bool success, int pointsProcessed, int totalSteps) MouseDragPath(
        (int x, int y)[] waypoints, int stepsPerSegment = 1, int delayMs = 0)
        => Mouse.DragPath(waypoints, stepsPerSegment, delayMs);
}
