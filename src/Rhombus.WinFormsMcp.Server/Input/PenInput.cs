using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server.Input;

/// <summary>
/// Pen input injection using Windows Synthetic Pointer API.
/// Wraps the existing InputInjection pen methods for now.
/// Full refactoring with Interop/ classes is a future task.
/// </summary>
public sealed class PenInput : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Ensure pen device is initialized.
    /// </summary>
    public bool EnsureInitialized()
        => InputInjection.EnsurePenInitialized();

    /// <summary>
    /// Simulate pen tap (like clicking with pen tip).
    /// </summary>
    /// <param name="x">Screen X coordinate</param>
    /// <param name="y">Screen Y coordinate</param>
    /// <param name="pressure">Pen pressure 0-1024 (default 512)</param>
    /// <param name="holdMs">Milliseconds to hold before release (default 0)</param>
    /// <param name="hwndTarget">Target window handle (if IntPtr.Zero, uses window at coordinates)</param>
    public bool Tap(int x, int y, uint pressure = 512, int holdMs = 0, IntPtr hwndTarget = default)
        => InputInjection.PenTap(x, y, pressure, holdMs, hwndTarget);

    /// <summary>
    /// Simulate a pen stroke from one point to another.
    /// </summary>
    /// <param name="x1">Start X screen coordinate</param>
    /// <param name="y1">Start Y screen coordinate</param>
    /// <param name="x2">End X screen coordinate</param>
    /// <param name="y2">End Y screen coordinate</param>
    /// <param name="steps">Number of interpolation steps</param>
    /// <param name="pressure">Pen pressure 0-1024</param>
    /// <param name="eraser">Use eraser end of pen</param>
    /// <param name="delayMs">Delay between steps in ms</param>
    /// <param name="hwndTarget">Target window handle (if IntPtr.Zero, uses window at coordinates)</param>
    public bool Stroke(int x1, int y1, int x2, int y2, int steps = 20, uint pressure = 512, bool eraser = false, int delayMs = 2, IntPtr hwndTarget = default)
        => InputInjection.PenStroke(x1, y1, x2, y2, steps, pressure, eraser, delayMs, hwndTarget);

    /// <summary>
    /// Cleanup pen device.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            InputInjection.CleanupPenDevice();
            _disposed = true;
        }
    }
}
