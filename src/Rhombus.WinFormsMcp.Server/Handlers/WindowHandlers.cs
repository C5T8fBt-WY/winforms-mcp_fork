using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Handles window targeting tools: get_window_bounds, focus_window.
/// </summary>
internal class WindowHandlers : HandlerBase
{
    public WindowHandlers(SessionManager session, WindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[]
    {
        "get_window_bounds",
        "focus_window"
    };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        return toolName switch
        {
            "get_window_bounds" => GetWindowBounds(args),
            "focus_window" => FocusWindow(args),
            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };
    }

    private Task<JsonElement> GetWindowBounds(JsonElement args)
    {
        try
        {
            var windowTitle = GetStringArg(args, "windowTitle") ?? throw new ArgumentException("windowTitle is required");

            var bounds = InputInjection.GetWindowBounds(windowTitle);

            if (bounds != null)
            {
                var (x, y, width, height) = bounds.Value;
                return Success(
                    ("x", x), ("y", y), ("width", width), ("height", height), ("windowTitle", windowTitle));
            }
            else
                return Error($"Window not found: {windowTitle}");
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> FocusWindow(JsonElement args)
    {
        try
        {
            var windowTitle = GetStringArg(args, "windowTitle") ?? throw new ArgumentException("windowTitle is required");

            var success = InputInjection.FocusWindow(windowTitle);

            if (success)
                return Success(("message", $"Focused window: {windowTitle}"));
            else
                return Error($"Could not focus window: {windowTitle}");
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }
}
