using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Input;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Handles keyboard and mouse input tools: send_keys, drag_drop, mouse_drag, mouse_drag_path, mouse_click.
/// </summary>
internal class InputHandlers : HandlerBase
{
    public InputHandlers(SessionManager session, WindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[]
    {
        "send_keys",
        "drag_drop",
        "mouse_drag",
        "mouse_drag_path",
        "mouse_click"
    };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        return toolName switch
        {
            "send_keys" => SendKeys(args),
            "drag_drop" => DragDrop(args),
            "mouse_drag" => MouseDrag(args),
            "mouse_drag_path" => MouseDragPath(args),
            "mouse_click" => MouseClick(args),
            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };
    }

    private Task<JsonElement> SendKeys(JsonElement args)
    {
        try
        {
            var keys = GetStringArg(args, "keys") ?? throw new ArgumentException("keys is required");
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");

            // If window targeting specified, focus that window first
            if (!string.IsNullOrEmpty(windowHandle) || !string.IsNullOrEmpty(windowTitle))
            {
                var window = Windows.FindWindow(windowHandle, windowTitle);
                if (window == null)
                {
                    if (!string.IsNullOrEmpty(windowTitle))
                    {
                        var matches = Windows.FindWindowsByTitle(windowTitle);
                        if (matches.Count > 1)
                        {
                            return Task.FromResult(ToolResponse.FailWithMultipleMatches(
                                $"Multiple windows match title '{windowTitle}'",
                                matches,
                                Windows).ToJsonElement());
                        }
                    }
                    return Error($"Window not found: {windowHandle ?? windowTitle}");
                }

                if (Windows.IsWindowMinimized(windowHandle, windowTitle))
                {
                    return Error("Window is minimized. Cannot send keys to minimized window.");
                }

                // Focus the window before sending keys
                Windows.FocusWindowByHandle(window.Handle);
                Thread.Sleep(50); // Brief delay for window to come to front
            }

            var automation = Session.GetAutomation();
            automation.SendKeys(keys);

            return Success(("message", $"Keys sent{(string.IsNullOrEmpty(windowTitle) ? "" : $" to '{windowTitle}'")}"));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> DragDrop(JsonElement args)
    {
        try
        {
            var sourceElementId = GetStringArg(args, "sourceElementId") ?? throw new ArgumentException("sourceElementId is required");
            var targetElementId = GetStringArg(args, "targetElementId") ?? throw new ArgumentException("targetElementId is required");
            var dragSetupDelayMs = GetIntArg(args, "dragSetupDelayMs", Constants.Timeouts.DragSetupDelay);
            var dropDelayMs = GetIntArg(args, "dropDelayMs", 200);

            var sourceElement = Session.GetElement(sourceElementId);
            var targetElement = Session.GetElement(targetElementId);

            if (sourceElement == null || targetElement == null)
                return Error("Source or target element not found in session");

            var automation = Session.GetAutomation();
            automation.DragDrop(sourceElement, targetElement, dragSetupDelayMs, dropDelayMs);

            return Success(("message", "Drag and drop completed"));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> MouseDrag(JsonElement args)
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

            // Resolve first point (this will focus the window)
            var (resolved1, screenX1, screenY1, warning1, err1) = ResolveWindowCoordinates(windowHandle, windowTitle, x1, y1);
            if (!resolved1)
                return Task.FromResult(err1!.Value);

            // Resolve second point (skip focus since already done)
            var (resolved2, screenX2, screenY2, warning2, err2) = ResolveWindowCoordinates(windowHandle, windowTitle, x2, y2, focusWindow: false);
            if (!resolved2)
                return Task.FromResult(err2!.Value);

            // Combine warnings if any
            var warning = warning1 ?? warning2;

            var success = InputFacade.MouseDrag(screenX1, screenY1, screenX2, screenY2, steps, delayMs);

            if (success)
            {
                return SuccessWithWarning(warning, ("message", $"Mouse drag from ({screenX1}, {screenY1}) to ({screenX2}, {screenY2})"));
            }
            else
            {
                return Error("Mouse drag failed.");
            }
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> MouseDragPath(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");

            // Parse points array
            if (!args.TryGetProperty("points", out var pointsElement) || pointsElement.ValueKind != JsonValueKind.Array)
            {
                return Error("points array is required");
            }

            var pointsList = new List<(int x, int y)>();
            int index = 0;
            foreach (var point in pointsElement.EnumerateArray())
            {
                if (!point.TryGetProperty("x", out var xProp) || !point.TryGetProperty("y", out var yProp))
                {
                    return Error($"Point at index {index} missing required x or y coordinate");
                }

                var x = xProp.GetInt32();
                var y = yProp.GetInt32();

                if (x < 0 || y < 0)
                {
                    return Error($"Point at index {index} has invalid coordinate (must be >= 0)");
                }

                pointsList.Add((x, y));
                index++;
            }

            if (pointsList.Count < 2)
            {
                return Error("Path requires at least 2 points");
            }

            if (pointsList.Count > Constants.Limits.MaxDragPathWaypoints)
            {
                return Error($"Path exceeds maximum of {Constants.Limits.MaxDragPathWaypoints} waypoints");
            }

            string? warning = null;

            // If window targeting is specified, translate all points to screen coordinates
            if (!string.IsNullOrEmpty(windowHandle) || !string.IsNullOrEmpty(windowTitle))
            {
                // Use ResolveWindowCoordinates for first point to handle validation and focus
                var (firstPt, firstY) = pointsList[0];
                var (resolved, _, _, firstWarning, err) = ResolveWindowCoordinates(windowHandle, windowTitle, firstPt, firstY);
                if (!resolved)
                    return Task.FromResult(err!.Value);
                warning = firstWarning;

                // Now get window for translating remaining points (already focused)
                var window = Windows.FindWindow(windowHandle, windowTitle);
                if (window == null)
                {
                    return Error($"Window not found after focus: {windowHandle ?? windowTitle}");
                }

                // Translate all points using client coordinates
                var translatedPoints = new List<(int x, int y)>();
                foreach (var (x, y) in pointsList)
                {
                    var translated = Windows.TranslateClientToScreen(window.Handle, x, y);
                    if (translated == null)
                    {
                        return Error("Could not translate coordinates to screen coordinates.");
                    }
                    translatedPoints.Add((translated.Value.screenX, translated.Value.screenY));
                }
                pointsList = translatedPoints;
            }

            var stepsPerSegment = GetIntArg(args, "stepsPerSegment", 1);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var (success, pointsProcessed, totalSteps) = InputFacade.MouseDragPath(
                pointsList.ToArray(),
                stepsPerSegment,
                delayMs);

            if (success)
            {
                return SuccessWithWarning(warning,
                    ("message", $"Completed drag path through {pointsProcessed} waypoints"),
                    ("pointsProcessed", pointsProcessed),
                    ("totalSteps", totalSteps));
            }
            else
            {
                return Error("Mouse drag path failed");
            }
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> MouseClick(JsonElement args)
    {
        try
        {
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var x = GetIntArg(args, "x");
            var y = GetIntArg(args, "y");
            var doubleClick = GetBoolArg(args, "doubleClick", false);
            var delayMs = GetIntArg(args, "delayMs", 0);

            var (resolved, screenX, screenY, warning, err) = ResolveWindowCoordinates(windowHandle, windowTitle, x, y);
            if (!resolved)
                return Task.FromResult(err!.Value);

            var success = InputFacade.MouseClick(screenX, screenY, doubleClick);
            if (delayMs > 0)
            {
                Thread.Sleep(delayMs);
            }

            if (success)
            {
                return SuccessWithWarning(warning, ("message", $"Mouse {(doubleClick ? "double-" : "")}click at ({screenX}, {screenY})"));
            }
            else
            {
                return Error("Mouse click failed.");
            }
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
            // No window targeting - use coordinates as-is (screen coordinates)
            return (true, x, y, null, null);
        }

        var window = Windows.FindWindow(windowHandle, windowTitle);
        if (window == null)
        {
            // Check for multiple matches
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

        // Check if minimized
        if (Windows.IsWindowMinimized(windowHandle, windowTitle))
        {
            return (false, 0, 0, null, ToolResponse.Fail(
                "Window is minimized. Use focus_window first.",
                Windows).ToJsonElement());
        }

        // Focus window if requested
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
                warning = $"Coordinates ({x}, {y}) are outside window client area (0, 0) to ({clientBounds.Width - 1}, {clientBounds.Height - 1}). Click may miss target.";
            }
        }

        // Translate client coordinates to screen coordinates
        var translated = Windows.TranslateClientToScreen(window.Handle, x, y);
        if (translated == null)
        {
            return (false, 0, 0, null, ToolResponse.Fail(
                "Could not translate coordinates to screen coordinates.",
                Windows).ToJsonElement());
        }

        return (true, translated.Value.screenX, translated.Value.screenY, warning, null);
    }

    #endregion
}
