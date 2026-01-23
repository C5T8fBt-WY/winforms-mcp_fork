using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using Rhombus.WinFormsMcp.Server.Abstractions;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Input;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Unified handler for click/tap operations with multiple input types.
/// Replaces: click_element, click_by_automation_id, mouse_click, touch_tap, pen_tap
/// </summary>
internal class ClickHandler : HandlerBase
{
    public ClickHandler(ISessionManager session, IWindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[] { "click" };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        try
        {
            var input = GetStringArg(args, "input") ?? "mouse";
            var target = GetStringArg(args, "target");
            var right = GetBoolArg(args, "right", false);
            var doubleClick = GetBoolArg(args, "double", false);
            var holdMs = GetIntArg(args, "hold_ms", 0);
            var pressure = GetIntArg(args, "pressure", 512);
            var eraser = GetBoolArg(args, "eraser", false);

            // Determine click coordinates
            int x, y;
            AutomationElement? element = null;

            if (!string.IsNullOrEmpty(target))
            {
                element = Session.GetElement(target);
                if (element == null)
                    return Error($"Element not found: {target}");

                // Check for stale element
                if (Session.IsElementStale(target))
                {
                    return Error($"Element is stale: {target}. Use find to locate it again.");
                }

                var bounds = element.BoundingRectangle;
                x = (int)(bounds.X + bounds.Width / 2);
                y = (int)(bounds.Y + bounds.Height / 2);
            }
            else
            {
                if (!args.TryGetProperty("x", out _) || !args.TryGetProperty("y", out _))
                    return Error("Either target (element ID) or x,y coordinates required. Example: {\"target\": \"elem_1\"} or {\"x\": 100, \"y\": 200}");
                x = GetIntArg(args, "x");
                y = GetIntArg(args, "y");
            }

            // Execute click based on input type
            bool success = input.ToLower() switch
            {
                "mouse" => ExecuteMouseClick(x, y, right, doubleClick, holdMs),
                "touch" => ExecuteTouchTap(x, y, holdMs),
                "pen" => ExecutePenTap(x, y, (uint)pressure, holdMs, eraser, right),
                _ => throw new ArgumentException($"Unknown input type: {input}")
            };

            if (!success)
                return Error($"Click injection failed at ({x}, {y}). The window may not be accepting {input} input or the coordinates may be outside the visible screen.");

            return ScopedSuccess(args, new
            {
                clicked = true,
                x,
                y,
                input,
                target
            });
        }
        catch (FlaUI.Core.Exceptions.PropertyNotSupportedException)
        {
            return Error("Element does not support click. Try using coordinates instead.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("offscreen"))
        {
            return Error("Element is not visible on screen. Scroll it into view first.");
        }
        catch (Exception ex)
        {
            return Error($"Click failed: {ex.Message}");
        }
    }

    private bool ExecuteMouseClick(int x, int y, bool right, bool doubleClick, int holdMs)
    {
        if (holdMs > 0)
        {
            // Long press: Use FlaUI's mouse API directly
            FlaUI.Core.Input.Mouse.MoveTo(x, y);
            if (right)
                FlaUI.Core.Input.Mouse.Down(FlaUI.Core.Input.MouseButton.Right);
            else
                FlaUI.Core.Input.Mouse.Down(FlaUI.Core.Input.MouseButton.Left);
            Thread.Sleep(holdMs);
            if (right)
                FlaUI.Core.Input.Mouse.Up(FlaUI.Core.Input.MouseButton.Right);
            else
                FlaUI.Core.Input.Mouse.Up(FlaUI.Core.Input.MouseButton.Left);
            return true;
        }

        if (doubleClick)
        {
            return InputFacade.MouseClick(x, y, doubleClick: true);
        }

        if (right)
        {
            FlaUI.Core.Input.Mouse.MoveTo(x, y);
            FlaUI.Core.Input.Mouse.RightClick();
            return true;
        }

        return InputFacade.MouseClick(x, y);
    }

    private bool ExecuteTouchTap(int x, int y, int holdMs)
    {
        return InputFacade.TouchTap(x, y, holdMs);
    }

    private bool ExecutePenTap(int x, int y, uint pressure, int holdMs, bool eraser, bool barrel)
    {
        return InputInjection.PenTap(x, y, pressure, holdMs, eraser, barrel);
    }
}
