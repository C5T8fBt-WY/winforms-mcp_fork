using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Input;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Handles touch and pen input tools: touch_tap, touch_drag, pinch_zoom, rotate,
/// multi_touch_gesture, pen_stroke, pen_tap.
/// </summary>
internal class TouchPenHandlers : HandlerBase
{
    public TouchPenHandlers(SessionManager session, WindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[]
    {
        "touch_tap",
        "touch_drag",
        "pinch_zoom",
        "rotate",
        "multi_touch_gesture",
        "pen_stroke",
        "pen_tap"
    };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        return toolName switch
        {
            "touch_tap" => TouchTap(args),
            "touch_drag" => TouchDrag(args),
            "pinch_zoom" => PinchZoom(args),
            "rotate" => RotateGesture(args),
            "multi_touch_gesture" => MultiTouchGesture(args),
            "pen_stroke" => PenStroke(args),
            "pen_tap" => PenTap(args),
            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };
    }

    private Task<JsonElement> TouchTap(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var x = GetIntArg(args, "x");
            var y = GetIntArg(args, "y");
            var holdMs = GetIntArg(args, "holdMs", 0);
            var useLegacy = GetBoolArg(args, "useLegacy", false);

            var (resolved, screenX, screenY, warning, errorResponse) = ResolveWindowCoordinates(windowHandle, windowTitle, x, y);
            if (!resolved)
                return Task.FromResult(errorResponse!.Value);

            bool success;
            string apiUsed;
            if (useLegacy)
            {
                success = InputFacade.TouchTap(screenX, screenY, holdMs, useLegacy: true);
                apiUsed = "legacy InjectTouchInput";
            }
            else
            {
                success = InputFacade.TouchTap(screenX, screenY, holdMs);
                apiUsed = "synthetic pointer";
            }

            if (success)
                return SuccessWithWarning(warning, ("message", $"Touch tap at ({screenX}, {screenY}) using {apiUsed}"));
            else
                return Error($"Touch injection failed using {apiUsed}. Check pen-debug.log for details.");
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> TouchDrag(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var x1 = GetIntArg(args, "x1");
            var y1 = GetIntArg(args, "y1");
            var x2 = GetIntArg(args, "x2");
            var y2 = GetIntArg(args, "y2");
            var steps = GetIntArg(args, "steps", 10);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var (resolved1, screenX1, screenY1, warning1, err1) = ResolveWindowCoordinates(windowHandle, windowTitle, x1, y1);
            if (!resolved1)
                return Task.FromResult(err1!.Value);

            var (resolved2, screenX2, screenY2, warning2, err2) = ResolveWindowCoordinates(windowHandle, windowTitle, x2, y2);
            if (!resolved2)
                return Task.FromResult(err2!.Value);

            var warning = warning1 ?? warning2;
            var success = InputFacade.TouchDrag(screenX1, screenY1, screenX2, screenY2, steps, delayMs);

            if (success)
                return SuccessWithWarning(warning, ("message", $"Touch drag from ({screenX1}, {screenY1}) to ({screenX2}, {screenY2})"));
            else
                return Error("Touch injection failed. Requires Windows 10 1809+ with Synthetic Pointer API support.");
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> PinchZoom(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var centerX = GetIntArg(args, "centerX");
            var centerY = GetIntArg(args, "centerY");
            var startDistance = GetIntArg(args, "startDistance");
            var endDistance = GetIntArg(args, "endDistance");
            var steps = GetIntArg(args, "steps", 20);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var (resolved, screenX, screenY, warning, err) = ResolveWindowCoordinates(windowHandle, windowTitle, centerX, centerY);
            if (!resolved)
                return Task.FromResult(err!.Value);

            var success = InputFacade.PinchZoom(screenX, screenY, startDistance, endDistance, steps, delayMs);

            if (success)
            {
                var zoomType = endDistance > startDistance ? "zoom in" : "zoom out";
                return SuccessWithWarning(warning, ("message", $"Pinch {zoomType} at ({screenX}, {screenY}) from {startDistance}px to {endDistance}px"));
            }
            else
                return Error("Touch injection failed. Requires Windows 10 1809+ with Synthetic Pointer API support.");
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> RotateGesture(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var centerX = GetIntArg(args, "centerX");
            var centerY = GetIntArg(args, "centerY");
            var radius = GetIntArg(args, "radius", 50);
            var startAngle = GetDoubleArg(args, "startAngle", 0);
            var endAngle = GetDoubleArg(args, "endAngle", 90);
            var steps = GetIntArg(args, "steps", 20);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var (resolved, screenX, screenY, warning, err) = ResolveWindowCoordinates(windowHandle, windowTitle, centerX, centerY);
            if (!resolved)
                return Task.FromResult(err!.Value);

            var success = InputFacade.Rotate(screenX, screenY, radius, startAngle, endAngle, steps, delayMs);

            if (success)
            {
                var direction = endAngle > startAngle ? "clockwise" : "counter-clockwise";
                var degrees = Math.Abs(endAngle - startAngle);
                return SuccessWithWarning(warning, ("message", $"Rotated {degrees}° {direction} at ({screenX}, {screenY}) with {radius}px radius"));
            }
            else
                return Error("Touch injection failed. Requires Windows 10 1809+ with Synthetic Pointer API support.");
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> MultiTouchGesture(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");

            if (!args.TryGetProperty("fingers", out var fingersElement) || fingersElement.ValueKind != JsonValueKind.Array)
                return Error("fingers is required and must be an array of finger paths");

            var fingersList = new List<(int x, int y, int timeMs)[]>();

            foreach (var fingerElement in fingersElement.EnumerateArray())
            {
                if (fingerElement.ValueKind != JsonValueKind.Array)
                    return Error("Each finger must be an array of waypoints");

                var waypoints = new List<(int x, int y, int timeMs)>();
                foreach (var waypointElement in fingerElement.EnumerateArray())
                {
                    if (waypointElement.ValueKind != JsonValueKind.Array)
                        return Error("Each waypoint must be an array [x, y, timeMs]");

                    var wpArray = waypointElement.EnumerateArray().ToArray();
                    if (wpArray.Length < 3)
                        return Error("Each waypoint must have at least 3 elements [x, y, timeMs]");

                    int wpX = wpArray[0].GetInt32();
                    int wpY = wpArray[1].GetInt32();
                    int wpTime = wpArray[2].GetInt32();

                    if (!string.IsNullOrEmpty(windowHandle) || !string.IsNullOrEmpty(windowTitle))
                    {
                        var (resolved, screenX, screenY, _, err) = ResolveWindowCoordinates(windowHandle, windowTitle, wpX, wpY);
                        if (!resolved)
                            return Task.FromResult(err!.Value);
                        wpX = screenX;
                        wpY = screenY;
                        // Note: Warnings from individual waypoints are not collected for multi-touch
                    }

                    waypoints.Add((wpX, wpY, wpTime));
                }

                if (waypoints.Count < 2)
                    return Error("Each finger must have at least 2 waypoints");

                fingersList.Add(waypoints.ToArray());
            }

            if (fingersList.Count == 0)
                return Error("At least one finger path is required");

            var (success, fingersProcessed, totalSteps) = InputFacade.MultiTouchGesture(fingersList.ToArray());

            if (success)
            {
                return Success(
                    ("message", $"Multi-touch gesture completed with {fingersProcessed} fingers over {totalSteps} steps"),
                    ("fingersProcessed", fingersProcessed),
                    ("totalSteps", totalSteps));
            }
            else
                return Error("Touch injection failed. Requires Windows 10 1809+ with Synthetic Pointer API support.");
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> PenStroke(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var x1 = GetIntArg(args, "x1");
            var y1 = GetIntArg(args, "y1");
            var x2 = GetIntArg(args, "x2");
            var y2 = GetIntArg(args, "y2");
            var steps = GetIntArg(args, "steps", 20);
            var pressure = (uint)GetIntArg(args, "pressure", 512);
            var eraser = GetBoolArg(args, "eraser", false);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var (resolved1, screenX1, screenY1, warning1, hwnd, err1) = ResolveWindowCoordinatesWithHandle(windowHandle, windowTitle, x1, y1);
            if (!resolved1)
                return Task.FromResult(err1!.Value);

            var (resolved2, screenX2, screenY2, warning2, _, err2) = ResolveWindowCoordinatesWithHandle(windowHandle, windowTitle, x2, y2, focusWindow: false);
            if (!resolved2)
                return Task.FromResult(err2!.Value);

            var warning = warning1 ?? warning2;
            var success = InputFacade.PenStroke(screenX1, screenY1, screenX2, screenY2, steps, pressure, eraser, delayMs, hwnd);

            if (success)
                return SuccessWithWarning(warning, ("message", $"Pen stroke from ({screenX1}, {screenY1}) to ({screenX2}, {screenY2}) with pressure {pressure}"));
            else
                return Error("Pen injection failed. Requires Windows 8+ and pen injection permissions.");
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> PenTap(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var x = GetIntArg(args, "x");
            var y = GetIntArg(args, "y");
            var pressure = (uint)GetIntArg(args, "pressure", 512);
            var holdMs = GetIntArg(args, "holdMs", 0);

            var (resolved, screenX, screenY, warning, hwnd, errorResponse) = ResolveWindowCoordinatesWithHandle(windowHandle, windowTitle, x, y);
            if (!resolved)
                return Task.FromResult(errorResponse!.Value);

            var success = InputFacade.PenTap(screenX, screenY, pressure, holdMs, hwnd);

            if (success)
                return SuccessWithWarning(warning, ("message", $"Pen tap at ({screenX}, {screenY}) with pressure {pressure}"));
            else
                return Error("Pen injection failed. Requires Windows 8+ and pen injection permissions.");
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    #region Helper Methods

    /// <summary>
    /// Resolve window-relative coordinates to screen coordinates.
    /// Returns a warning if coordinates are outside the window client area bounds.
    /// </summary>
    private (bool resolved, int screenX, int screenY, string? warning, JsonElement? errorResponse) ResolveWindowCoordinates(
        string? windowHandle, string? windowTitle, int x, int y, bool focusWindow = true)
    {
        if (string.IsNullOrEmpty(windowHandle) && string.IsNullOrEmpty(windowTitle))
        {
            return (true, x, y, null, null);
        }

        var window = Windows.FindWindow(windowHandle, windowTitle);
        if (window == null)
        {
            if (!string.IsNullOrEmpty(windowTitle))
            {
                var matches = Windows.FindWindowsByTitle(windowTitle);
                if (matches.Count > 1)
                {
                    return (false, 0, 0, null, ToolResponse.FailWithMultipleMatches(
                        $"Multiple windows match title '{windowTitle}'",
                        matches,
                        Windows).ToJsonElement());
                }
            }
            return (false, 0, 0, null, ToolResponse.Fail(
                $"Window not found: {windowHandle ?? windowTitle}",
                Windows).ToJsonElement());
        }

        if (Windows.IsWindowMinimized(windowHandle, windowTitle))
        {
            return (false, 0, 0, null, ToolResponse.Fail(
                "Window is minimized. Use focus_window first.",
                Windows).ToJsonElement());
        }

        if (focusWindow)
        {
            Windows.FocusWindowByHandle(window.Handle);
            Thread.Sleep(Constants.Timeouts.WindowFocusDelay);
        }

        // Get client area bounds for bounds checking
        var clientBounds = Windows.GetClientAreaBounds(window.Handle);
        string? warning = null;
        if (clientBounds != null)
        {
            // Check if coordinates are outside client area
            if (x < 0 || y < 0 || x >= clientBounds.Width || y >= clientBounds.Height)
            {
                warning = $"Coordinates ({x}, {y}) are outside window client area (0, 0) to ({clientBounds.Width - 1}, {clientBounds.Height - 1}). Input may miss target.";
            }
        }

        var translated = Windows.TranslateClientToScreen(window.Handle, x, y);
        if (translated == null)
        {
            return (false, 0, 0, null, ToolResponse.Fail(
                "Could not translate coordinates to screen coordinates.",
                Windows).ToJsonElement());
        }

        return (true, translated.Value.screenX, translated.Value.screenY, warning, null);
    }

    /// <summary>
    /// Resolve window-relative coordinates to screen coordinates, also returning the window handle.
    /// Returns a warning if coordinates are outside the window client area bounds.
    /// </summary>
    private (bool success, int screenX, int screenY, string? warning, IntPtr hwnd, JsonElement? errorResponse) ResolveWindowCoordinatesWithHandle(
        string? windowHandle, string? windowTitle, int x, int y, bool focusWindow = true)
    {
        if (string.IsNullOrEmpty(windowHandle) && string.IsNullOrEmpty(windowTitle))
        {
            return (true, x, y, null, IntPtr.Zero, null);
        }

        var window = Windows.FindWindow(windowHandle, windowTitle);
        if (window == null)
        {
            if (!string.IsNullOrEmpty(windowTitle))
            {
                var matches = Windows.FindWindowsByTitle(windowTitle);
                if (matches.Count > 1)
                {
                    return (false, 0, 0, null, IntPtr.Zero, ToolResponse.FailWithMultipleMatches(
                        $"Multiple windows match title '{windowTitle}'",
                        matches,
                        Windows).ToJsonElement());
                }
            }
            return (false, 0, 0, null, IntPtr.Zero, ToolResponse.Fail(
                $"Window not found: {windowHandle ?? windowTitle}",
                Windows).ToJsonElement());
        }

        if (Windows.IsWindowMinimized(windowHandle, windowTitle))
        {
            return (false, 0, 0, null, IntPtr.Zero, ToolResponse.Fail(
                "Window is minimized. Use focus_window first.",
                Windows).ToJsonElement());
        }

        if (focusWindow)
        {
            Windows.FocusWindowByHandle(window.Handle);
            Thread.Sleep(Constants.Timeouts.WindowFocusDelay);
        }

        // Get client area bounds for bounds checking
        var clientBounds = Windows.GetClientAreaBounds(window.Handle);
        string? warning = null;
        if (clientBounds != null)
        {
            // Check if coordinates are outside client area
            if (x < 0 || y < 0 || x >= clientBounds.Width || y >= clientBounds.Height)
            {
                warning = $"Coordinates ({x}, {y}) are outside window client area (0, 0) to ({clientBounds.Width - 1}, {clientBounds.Height - 1}). Input may miss target.";
            }
        }

        var translated = Windows.TranslateClientToScreen(window.Handle, x, y);
        if (translated == null)
        {
            return (false, 0, 0, null, IntPtr.Zero, ToolResponse.Fail(
                "Could not translate coordinates to screen coordinates.",
                Windows).ToJsonElement());
        }

        // Parse the handle for pen targeting
        IntPtr hwnd = IntPtr.Zero;
        if (!string.IsNullOrEmpty(window.Handle))
        {
            if (window.Handle.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hwnd = new IntPtr(Convert.ToInt64(window.Handle, 16));
            }
            else if (long.TryParse(window.Handle, out var handle))
            {
                hwnd = new IntPtr(handle);
            }
        }

        return (true, translated.Value.screenX, translated.Value.screenY, warning, hwnd, null);
    }

    #endregion
}
