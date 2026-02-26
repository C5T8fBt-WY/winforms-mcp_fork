using C5T8fBtWY.WinFormsMcp.Server.Automation;

namespace C5T8fBtWY.WinFormsMcp.Server.Input;

/// <summary>
/// Touch input injection using Windows Synthetic Pointer API.
/// Wraps the existing InputInjection touch methods for now.
/// Full refactoring with Interop/ classes is a future task.
/// </summary>
public sealed class TouchInput : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Ensure touch device is initialized.
    /// </summary>
    public bool EnsureInitialized(uint maxContacts = 10)
        => InputInjection.EnsureTouchInitialized(maxContacts);

    /// <summary>
    /// Simulate a touch tap at a location.
    /// </summary>
    /// <param name="x">Screen X coordinate</param>
    /// <param name="y">Screen Y coordinate</param>
    /// <param name="holdMs">Milliseconds to hold before release (default 0)</param>
    /// <param name="useLegacy">Use legacy touch API instead of synthetic (default false)</param>
    public bool Tap(int x, int y, int holdMs = 0, bool useLegacy = false)
    {
        if (useLegacy)
            return InputInjection.LegacyTouchTap(x, y, holdMs);
        return InputInjection.TouchTap(x, y, holdMs);
    }

    /// <summary>
    /// Simulate a touch drag from one point to another.
    /// </summary>
    public bool Drag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 5)
        => InputInjection.TouchDrag(x1, y1, x2, y2, steps, delayMs);

    /// <summary>
    /// Simulate pinch-to-zoom gesture.
    /// </summary>
    public bool PinchZoom(int centerX, int centerY, int startDistance, int endDistance, int steps = 20, int delayMs = 0)
        => InputInjection.PinchZoom(centerX, centerY, startDistance, endDistance, steps, delayMs);

    /// <summary>
    /// Simulate two-finger rotate gesture.
    /// </summary>
    public bool Rotate(int centerX, int centerY, int radius, double startAngle, double endAngle, int steps = 20, int delayMs = 0)
        => InputInjection.Rotate(centerX, centerY, radius, startAngle, endAngle, steps, delayMs);

    /// <summary>
    /// Execute a multi-finger gesture with time-synchronized waypoints.
    /// </summary>
    public (bool success, int fingersProcessed, int totalSteps) MultiTouchGesture(
        (int x, int y, int timeMs)[][] fingers,
        int interpolationSteps = 5)
        => InputInjection.MultiTouchGesture(fingers, interpolationSteps);

    /// <summary>
    /// Cleanup touch device.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            InputInjection.CleanupTouchDevice();
            _disposed = true;
        }
    }
}
