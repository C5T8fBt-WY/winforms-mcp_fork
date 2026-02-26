using C5T8fBtWY.WinFormsMcp.Server.Automation;

namespace C5T8fBtWY.WinFormsMcp.Server.Input;

/// <summary>
/// Mouse input injection using FlaUI.
/// Wraps the existing InputInjection mouse methods for now.
/// Full refactoring with IWindowProvider is a future task.
/// </summary>
public sealed class MouseInput
{
    /// <summary>
    /// Simulate mouse click at coordinates.
    /// </summary>
    /// <param name="x">Screen X coordinate</param>
    /// <param name="y">Screen Y coordinate</param>
    /// <param name="doubleClick">Double-click if true (default false)</param>
    /// <param name="delayMs">Delay in milliseconds before click (default 0)</param>
    public bool Click(int x, int y, bool doubleClick = false, int delayMs = 0)
        => InputInjection.MouseClick(x, y, doubleClick, delayMs);

    /// <summary>
    /// Simulate mouse drag from one point to another.
    /// </summary>
    /// <param name="x1">Start X coordinate</param>
    /// <param name="y1">Start Y coordinate</param>
    /// <param name="x2">End X coordinate</param>
    /// <param name="y2">End Y coordinate</param>
    /// <param name="steps">Ignored - kept for API compatibility</param>
    /// <param name="delayMs">Delay in milliseconds after drag completes (default 0)</param>
    /// <param name="targetWindow">Optional window to target (not used currently)</param>
    public bool Drag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 0, string? targetWindow = null)
        => InputInjection.MouseDrag(x1, y1, x2, y2, steps, delayMs, targetWindow);

    /// <summary>
    /// Simulate mouse drag through multiple waypoints.
    /// </summary>
    /// <param name="waypoints">Array of (x, y) coordinates to drag through</param>
    /// <param name="stepsPerSegment">Ignored - kept for API compatibility</param>
    /// <param name="delayMs">Delay in milliseconds between waypoints (default 0)</param>
    public (bool success, int pointsProcessed, int totalSteps) DragPath(
        (int x, int y)[] waypoints,
        int stepsPerSegment = 1,
        int delayMs = 0)
        => InputInjection.MouseDragPath(waypoints, stepsPerSegment, delayMs);
}
