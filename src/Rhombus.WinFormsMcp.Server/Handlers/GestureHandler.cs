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
/// Handler for multi-touch gestures: pinch, rotate, and custom finger paths.
/// Replaces: pinch_zoom, rotate_gesture, multi_touch_gesture
/// </summary>
internal class GestureHandler : HandlerBase
{
    public GestureHandler(ISessionManager session, IWindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[] { "gesture" };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        try
        {
            var type = GetStringArg(args, "type");
            if (string.IsNullOrEmpty(type))
                return Error("type is required: 'pinch', 'rotate', or 'custom'");

            var durationMs = GetIntArg(args, "duration_ms", 0);

            bool success = type.ToLower() switch
            {
                "pinch" => ExecutePinch(args, durationMs),
                "rotate" => ExecuteRotate(args, durationMs),
                "custom" => ExecuteCustom(args),
                _ => throw new ArgumentException($"Unknown gesture type: '{type}'. Expected: pinch, rotate, or custom")
            };

            if (!success)
                return Error($"Gesture injection failed. Touch input may not be supported on this device.");

            return ScopedSuccess(args, new { gesture = type, completed = true });
        }
        catch (ArgumentException ex)
        {
            return Error(ex.Message);
        }
        catch (Exception ex)
        {
            return Error($"Gesture failed: {ex.Message}");
        }
    }

    private bool ExecutePinch(JsonElement args, int durationMs)
    {
        if (!args.TryGetProperty("center", out var centerEl))
            throw new ArgumentException("center is required for pinch");

        var centerX = centerEl.GetProperty("x").GetInt32();
        var centerY = centerEl.GetProperty("y").GetInt32();
        var startDistance = GetIntArg(args, "start_distance", 100);
        var endDistance = GetIntArg(args, "end_distance", 50);

        int steps = durationMs > 0 ? durationMs / 10 : 20;
        int delayMs = durationMs > 0 ? durationMs / steps : 0;

        return InputFacade.PinchZoom(centerX, centerY, startDistance, endDistance, steps, delayMs);
    }

    private bool ExecuteRotate(JsonElement args, int durationMs)
    {
        if (!args.TryGetProperty("center", out var centerEl))
            throw new ArgumentException("center is required for rotate");

        var centerX = centerEl.GetProperty("x").GetInt32();
        var centerY = centerEl.GetProperty("y").GetInt32();
        var radius = GetIntArg(args, "radius", 50);
        var startAngle = GetDoubleArg(args, "start_angle", 0);
        var endAngle = GetDoubleArg(args, "end_angle", 90);

        int steps = durationMs > 0 ? durationMs / 10 : 20;
        int delayMs = durationMs > 0 ? durationMs / steps : 0;

        return InputFacade.Rotate(centerX, centerY, radius, startAngle, endAngle, steps, delayMs);
    }

    private bool ExecuteCustom(JsonElement args)
    {
        if (!args.TryGetProperty("fingers", out var fingersEl))
            throw new ArgumentException("fingers array is required for custom gesture");

        var fingers = ParseFingers(fingersEl);
        if (fingers.Length < 2)
            throw new ArgumentException("custom gesture requires at least 2 fingers");

        var (success, fingersProcessed, totalSteps) = InputFacade.MultiTouchGesture(fingers);
        return success;
    }

    private (int x, int y, int timeMs)[][] ParseFingers(JsonElement fingersEl)
    {
        var fingers = new List<(int x, int y, int timeMs)[]>();

        foreach (var fingerEl in fingersEl.EnumerateArray())
        {
            if (!fingerEl.TryGetProperty("path", out var pathEl))
                throw new ArgumentException("Each finger must have a path array");

            var points = new List<(int x, int y, int timeMs)>();
            int defaultTime = 0;

            foreach (var pointEl in pathEl.EnumerateArray())
            {
                var x = pointEl.GetProperty("x").GetInt32();
                var y = pointEl.GetProperty("y").GetInt32();
                var timeMs = pointEl.TryGetProperty("time_ms", out var timeProp)
                    ? timeProp.GetInt32()
                    : (defaultTime += 50);

                points.Add((x, y, timeMs));
            }

            fingers.Add(points.ToArray());
        }

        return fingers.ToArray();
    }
}
