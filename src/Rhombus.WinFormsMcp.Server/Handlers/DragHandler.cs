using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Rhombus.WinFormsMcp.Server.Abstractions;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Input;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Unified handler for drag/stroke operations with multiple input types.
/// Replaces: drag_drop, mouse_drag, mouse_drag_path, touch_drag, pen_stroke
/// </summary>
internal class DragHandler : HandlerBase
{
    public DragHandler(ISessionManager session, IWindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[] { "drag" };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        try
        {
            var input = GetStringArg(args, "input") ?? "mouse";
            var button = GetStringArg(args, "button") ?? "left";
            var eraser = GetBoolArg(args, "eraser", false);
            var right = GetBoolArg(args, "right", false);
            var barrel = GetBoolArg(args, "barrel", false);
            var durationMs = GetIntArg(args, "duration_ms", 0);

            // Parse path array
            if (!args.TryGetProperty("path", out var pathElement))
                return Error("path array is required. Example: {\"path\": [{\"x\": 100, \"y\": 100}, {\"x\": 200, \"y\": 200}]}");

            var path = ParsePath(pathElement);
            if (path.Count < 2)
                return Error("path must contain at least 2 points with x,y coordinates");

            // Calculate steps based on duration or use reasonable default
            int totalSteps = path.Count > 2 ? path.Count : 10;
            int delayMs = durationMs > 0 ? durationMs / totalSteps : 5;

            bool success = input.ToLower() switch
            {
                "mouse" => ExecuteMouseDrag(path, button, delayMs),
                "touch" => ExecuteTouchDrag(path, delayMs),
                "pen" => ExecutePenStroke(path, eraser, right || barrel, delayMs),
                _ => throw new ArgumentException($"Unknown input type: {input}")
            };

            if (!success)
                return Error($"Drag injection failed. The window may not be accepting {input} input or the coordinates may be outside the visible screen.");

            return ScopedSuccess(args, new
            {
                dragged = true,
                input,
                points = path.Count
            });
        }
        catch (KeyNotFoundException ex)
        {
            return Error($"Missing coordinate in path: {ex.Message}. Each point must have 'x' and 'y' properties.");
        }
        catch (Exception ex)
        {
            return Error($"Drag failed: {ex.Message}");
        }
    }

    private List<PathPoint> ParsePath(JsonElement pathElement)
    {
        var points = new List<PathPoint>();

        foreach (var pointEl in pathElement.EnumerateArray())
        {
            var x = pointEl.GetProperty("x").GetInt32();
            var y = pointEl.GetProperty("y").GetInt32();
            var pressure = pointEl.TryGetProperty("pressure", out var pressureProp)
                ? pressureProp.GetInt32()
                : 512;

            points.Add(new PathPoint(x, y, pressure));
        }

        return points;
    }

    private bool ExecuteMouseDrag(List<PathPoint> path, string button, int delayMs)
    {
        if (path.Count == 2)
        {
            // Simple two-point drag
            var start = path[0];
            var end = path[1];
            return InputFacade.MouseDrag(start.X, start.Y, end.X, end.Y, delayMs: delayMs);
        }

        // Multi-point path drag
        var waypoints = path.Select(p => (p.X, p.Y)).ToArray();
        var (success, _, _) = InputFacade.MouseDragPath(waypoints, stepsPerSegment: 1, delayMs: delayMs);
        return success;
    }

    private bool ExecuteTouchDrag(List<PathPoint> path, int delayMs)
    {
        if (path.Count == 2)
        {
            var start = path[0];
            var end = path[1];
            return InputFacade.TouchDrag(start.X, start.Y, end.X, end.Y, delayMs: delayMs);
        }

        // For multi-point touch drag, we'd need to implement path support
        // For now, chain two-point drags
        for (int i = 0; i < path.Count - 1; i++)
        {
            var start = path[i];
            var end = path[i + 1];
            if (!InputFacade.TouchDrag(start.X, start.Y, end.X, end.Y, steps: 5, delayMs: delayMs))
                return false;
        }
        return true;
    }

    private bool ExecutePenStroke(List<PathPoint> path, bool eraser, bool barrel, int delayMs)
    {
        if (path.Count == 2)
        {
            var start = path[0];
            var end = path[1];
            return InputInjection.PenStroke(start.X, start.Y, end.X, end.Y,
                steps: 20, pressure: (uint)start.Pressure, eraser: eraser, barrel: barrel, delayMs: delayMs);
        }

        // Multi-point pen stroke - chain segments
        for (int i = 0; i < path.Count - 1; i++)
        {
            var start = path[i];
            var end = path[i + 1];
            if (!InputInjection.PenStroke(start.X, start.Y, end.X, end.Y,
                steps: 5, pressure: (uint)start.Pressure, eraser: eraser, barrel: barrel, delayMs: delayMs))
                return false;
        }
        return true;
    }

    private record PathPoint(int X, int Y, int Pressure);
}
