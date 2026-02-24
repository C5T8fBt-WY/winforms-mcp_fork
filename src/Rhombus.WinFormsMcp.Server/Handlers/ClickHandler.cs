using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using Rhombus.WinFormsMcp.Server.Abstractions;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Input;
using Rhombus.WinFormsMcp.Server.Interop;
using static Rhombus.WinFormsMcp.Server.Interop.Win32Types;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Unified handler for click/tap operations with multiple input types.
///
/// Input mode selection:
///   - When a target element ID is provided and input is not explicitly set to
///     "mouse"/"touch"/"pen", the default is "uia": programmatic UIA patterns are
///     used (InvokePattern → TogglePattern → SelectionItemPattern → PostMessage).
///     This does NOT move the physical mouse cursor.
///   - When only coordinates are given, or when input is explicitly "mouse",
///     "touch", or "pen", physical input injection is used (moves cursor / injects
///     touch/pen events).
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
            var inputArg = GetStringArg(args, "input");
            var target = GetStringArg(args, "target");
            var right = GetBoolArg(args, "right", false);
            var barrel = GetBoolArg(args, "barrel", false);
            var doubleClick = GetBoolArg(args, "double", false);
            var holdMs = GetIntArg(args, "hold_ms", 0);
            var pressure = GetIntArg(args, "pressure", 512);
            var eraser = GetBoolArg(args, "eraser", false);

            // Default: use UIA when a target element is provided and input not forced
            // to a physical mode. This avoids moving the mouse cursor.
            bool hasElement = !string.IsNullOrEmpty(target);
            bool physicalForced = inputArg is "mouse" or "touch" or "pen";
            string input = inputArg ?? (hasElement ? "uia" : "mouse");

            AutomationElement? element = null;
            int x = 0, y = 0;

            if (hasElement)
            {
                element = Session.GetElement(target!);
                if (element == null)
                    return Error($"Element not found: {target}");

                if (Session.IsElementStale(target!))
                    return Error($"Element is stale: {target}. Use find to locate it again.");

                var bounds = element.BoundingRectangle;
                x = (int)(bounds.X + bounds.Width / 2);
                y = (int)(bounds.Y + bounds.Height / 2);
            }
            else
            {
                if (!args.TryGetProperty("x", out _) || !args.TryGetProperty("y", out _))
                    return Error("Either target (element ID) or x,y coordinates required.");
                x = GetIntArg(args, "x");
                y = GetIntArg(args, "y");
            }

            string method;
            bool success;

            switch (input.ToLower())
            {
                case "uia":
                    // Programmatic: UIA patterns first, PostMessage fallback.
                    // Never moves the physical mouse cursor.
                    (success, method) = ExecuteUiaClick(element!, right, doubleClick, holdMs);
                    break;

                case "mouse":
                    success = ExecuteMouseClick(x, y, right, doubleClick, holdMs);
                    method = "mouse";
                    break;

                case "touch":
                    success = InputFacade.TouchTap(x, y, holdMs);
                    method = "touch";
                    break;

                case "pen":
                    success = InputInjection.PenTap(x, y, (uint)pressure, holdMs, eraser, right || barrel);
                    method = "pen";
                    break;

                default:
                    throw new ArgumentException($"Unknown input type: {input}. Expected: uia, mouse, touch, pen");
            }

            if (!success)
                return Error($"Click failed via '{method}' at ({x}, {y}). The control may not support this interaction.");

            return ScopedSuccess(args, new
            {
                clicked = true,
                x,
                y,
                input = method,
                target
            });
        }
        catch (FlaUI.Core.Exceptions.PropertyNotSupportedException)
        {
            return Error("Element does not support click. Try using coordinates with input='mouse'.");
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

    /// <summary>
    /// Programmatic click via UIA patterns or PostMessage.
    /// Does NOT move the physical mouse cursor.
    /// Priority: InvokePattern → TogglePattern → SelectionItemPattern → PostMessage to HWND.
    /// </summary>
    private static (bool success, string method) ExecuteUiaClick(
        AutomationElement element, bool right, bool doubleClick, int holdMs)
    {
        if (!right && !doubleClick && holdMs == 0)
        {
            // InvokePattern: buttons, hyperlinks, menu items.
            // Fire on a background thread so the MCP handler is never blocked by the target
            // app's UI thread (e.g. when the button handler opens a MessageBox/dialog).
            var invoke = element.Patterns.Invoke.PatternOrDefault;
            if (invoke != null)
            {
                var invokeTask = Task.Run(() => invoke.Invoke());
                try
                {
                    bool completed = invokeTask.Wait(TimeSpan.FromMilliseconds(2000));
                    if (!completed)
                    {
                        // Timed out — button handler is blocked (e.g., opened a MessageBox).
                        // The click WAS dispatched. Observe the eventual UIA_E_TIMEOUT silently.
                        _ = invokeTask.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
                    }
                    return (true, "uia:invoke");
                }
                catch
                {
                    // InvokePattern failed immediately; fall through to next pattern.
                }
            }

            // TogglePattern: checkboxes, toggle buttons
            var toggle = element.Patterns.Toggle.PatternOrDefault;
            if (toggle != null)
            {
                toggle.Toggle();
                return (true, "uia:toggle");
            }

            // SelectionItemPattern: radio buttons, list items, tab items
            var selItem = element.Patterns.SelectionItem.PatternOrDefault;
            if (selItem != null)
            {
                selItem.Select();
                return (true, "uia:select");
            }

            // ExpandCollapsePattern: combo boxes, tree items
            var expand = element.Patterns.ExpandCollapse.PatternOrDefault;
            if (expand != null)
            {
                var state = expand.ExpandCollapseState.Value;
                if (state == FlaUI.Core.Definitions.ExpandCollapseState.Collapsed)
                    expand.Expand();
                else
                    expand.Collapse();
                return (true, "uia:expandcollapse");
            }
        }

        // Fallback: PostMessage WM_LBUTTON/WM_RBUTTON to the control's HWND.
        // This is still programmatic — does not move the mouse cursor.
        return PostMessageClick(element, right, doubleClick, holdMs);
    }

    /// <summary>
    /// PostMessage fallback: sends WM_LBUTTONDOWN/UP (or WM_RBUTTONDOWN/UP) directly
    /// to the control's HWND using coordinates local to the control's client area.
    /// Does NOT move the physical mouse cursor.
    /// </summary>
    private static (bool success, string method) PostMessageClick(
        AutomationElement element, bool right, bool doubleClick, int holdMs)
    {
        try
        {
            var hwnd = element.Properties.NativeWindowHandle.ValueOrDefault;
            if (hwnd == IntPtr.Zero)
                return (false, "postmessage");

            // Compute center in screen coords, then convert to client coords
            var bounds = element.BoundingRectangle;
            var screenPt = new POINT
            {
                x = (int)(bounds.X + bounds.Width / 2),
                y = (int)(bounds.Y + bounds.Height / 2)
            };
            WindowInterop.ScreenToClient(hwnd, ref screenPt);
            var lParam = WindowInterop.MakeLParam(screenPt.x, screenPt.y);

            if (right)
            {
                var wParam = (IntPtr)WindowInterop.MK_RBUTTON;
                WindowInterop.PostMessage(hwnd, WindowInterop.WM_RBUTTONDOWN, wParam, lParam);
                if (holdMs > 0) Thread.Sleep(holdMs);
                WindowInterop.PostMessage(hwnd, WindowInterop.WM_RBUTTONUP, IntPtr.Zero, lParam);
            }
            else
            {
                var wParam = (IntPtr)WindowInterop.MK_LBUTTON;
                WindowInterop.PostMessage(hwnd, WindowInterop.WM_LBUTTONDOWN, wParam, lParam);
                if (holdMs > 0) Thread.Sleep(holdMs);
                WindowInterop.PostMessage(hwnd, WindowInterop.WM_LBUTTONUP, IntPtr.Zero, lParam);
                if (doubleClick)
                {
                    WindowInterop.PostMessage(hwnd, WindowInterop.WM_LBUTTONDBLCLK, wParam, lParam);
                    WindowInterop.PostMessage(hwnd, WindowInterop.WM_LBUTTONUP, IntPtr.Zero, lParam);
                }
            }
            return (true, "uia:postmessage");
        }
        catch
        {
            return (false, "postmessage");
        }
    }

    /// <summary>
    /// Physical mouse click — moves the cursor. Used only when input='mouse' is explicit.
    /// </summary>
    private static bool ExecuteMouseClick(int x, int y, bool right, bool doubleClick, int holdMs)
    {
        if (holdMs > 0)
        {
            FlaUI.Core.Input.Mouse.MoveTo(x, y);
            var btn = right ? FlaUI.Core.Input.MouseButton.Right : FlaUI.Core.Input.MouseButton.Left;
            FlaUI.Core.Input.Mouse.Down(btn);
            Thread.Sleep(holdMs);
            FlaUI.Core.Input.Mouse.Up(btn);
            return true;
        }

        if (doubleClick)
            return InputFacade.MouseClick(x, y, doubleClick: true);

        if (right)
        {
            FlaUI.Core.Input.Mouse.MoveTo(x, y);
            FlaUI.Core.Input.Mouse.RightClick();
            return true;
        }

        return InputFacade.MouseClick(x, y);
    }
}
