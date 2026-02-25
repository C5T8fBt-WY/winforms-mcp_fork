using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using Rhombus.WinFormsMcp.Server.Abstractions;
using Rhombus.WinFormsMcp.Server.Interop;
using static Rhombus.WinFormsMcp.Server.Interop.Win32Types;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Unified handler for click/tap operations.
///
/// All clicks are programmatic — UIA patterns first (InvokePattern → TogglePattern →
/// SelectionItemPattern → ExpandCollapsePattern → LegacyIAccessiblePattern), then
/// PostMessage WM_LBUTTON/WM_RBUTTON to the control's HWND.
/// Physical mouse/cursor movement is never used to prevent interfering with the user.
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
            var target = GetStringArg(args, "target");
            var right = GetBoolArg(args, "right", false);
            var doubleClick = GetBoolArg(args, "double", false);
            var holdMs = GetIntArg(args, "hold_ms", 0);

            // Validate input type when explicit; all types are unified under PostMessage now.
            var input = GetStringArg(args, "input");
            if (input != null && input is not ("mouse" or "touch" or "pen"))
                return Error($"Unknown input type: {input}. Supported values: mouse, touch, pen");

            // window_handle path: accept/cancel a dialog by HWND.
            // Works even during MessageBox.Show() because it uses Win32 message queue, not UIA COM.
            // Modern WinForms MessageBox (TaskDialog) assigns OK=id2, so we enumerate buttons by text.
            var windowHandleStr = GetStringArg(args, "window_handle");
            if (windowHandleStr != null)
            {
                var hwnd = new IntPtr(Convert.ToInt64(windowHandleStr, 16));
                var cancel = GetBoolArg(args, "cancel", false);

                // Find the button child by text (most reliable across Win32 and TaskDialog).
                var btnHwnd = FindDialogButton(hwnd, cancel);
                if (btnHwnd != IntPtr.Zero)
                {
                    // BM_CLICK simulates a button press: highlights + fires WM_COMMAND + dismisses dialog.
                    WindowInterop.PostMessage(btnHwnd, WindowInterop.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                }
                else
                {
                    // VK_ESCAPE/VK_RETURN first, then WM_CLOSE as final fallback for dialogs
                    // without cancel buttons (e.g. WinForms PropertyGrid shown via ShowDialog).
                    uint vKey = cancel ? WindowInterop.VK_ESCAPE : WindowInterop.VK_RETURN;
                    WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYDOWN, new IntPtr(vKey), IntPtr.Zero);
                    WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYUP, new IntPtr(vKey), IntPtr.Zero);
                    if (cancel)
                    {
                        Thread.Sleep(50);
                        WindowInterop.PostMessage(hwnd, WindowInterop.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }
                }

                return ScopedSuccess(args, new
                {
                    clicked = true,
                    input = "postmessage:dialog",
                    window_handle = windowHandleStr,
                    button = cancel ? "cancel" : "ok"
                });
            }

            // Direct automation targeting: click(automationId: "btnOK") or click(name: "OK")
            // without a prior find step.
            var directAutoId = GetStringArg(args, "automationId");
            var directName = GetStringArg(args, "name");
            if (directAutoId != null || directName != null)
            {
                var automation = Session.GetAutomation();
                string refStr = directAutoId ?? directName!;

                // Search from desktop root (short timeout — element must already exist).
                var directElement = directAutoId != null
                    ? automation.FindByAutomationId(directAutoId, timeoutMs: 500)
                    : automation.FindByName(directName!, timeoutMs: 500);

                if (directElement == null)
                    return Error($"Element not found with {(directAutoId != null ? "automationId" : "name")}='{refStr}'. If targeting a native dialog (MessageBox), use click(window_handle:\"0xHWND\") instead — run snapshot to find the dialog's hwnd.");

                var (clicked, clickMethod) = ExecuteUiaClick(directElement, right, doubleClick, holdMs);
                return ScopedSuccess(args, new
                {
                    clicked,
                    input = clickMethod,
                    @ref = refStr
                });
            }

            bool hasElement = !string.IsNullOrEmpty(target);

            AutomationElement? element = null;
            int x = 0, y = 0;

            if (hasElement)
            {
                element = Session.GetElement(target!);
                if (element == null)
                    return Error($"Element not found: {target}. If a modal dialog is blocking, use snapshot to find hwnd= refs, then click(window_handle:).");

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

            // All clicks are programmatic: UIA patterns first, PostMessage fallback.
            var (success, method) = hasElement
                ? ExecuteUiaClick(element!, right, doubleClick, holdMs)
                : PostMessageClickAtScreen(x, y, right, doubleClick, holdMs);

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
            return Error("Element does not support click. Try using coordinates (x, y) with PostMessage.");
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
    /// Find the accept or cancel button in a dialog by enumerating child Button windows and
    /// matching by text first (most reliable across Win32 and WinForms/TaskDialog which may
    /// assign non-standard control IDs). Falls back to well-known dialog control IDs.
    /// </summary>
    private static IntPtr FindDialogButton(IntPtr hwnd, bool cancel)
    {
        // Text patterns for accept/cancel direction (case-insensitive).
        var acceptTexts = new[] { "ok", "yes", "はい", "확인", "oui", "ja", "sì", "sim", "да" };
        var cancelTexts = new[] { "cancel", "no", "いいえ", "キャンセル", "취소", "non", "nein", "não", "нет" };
        var targetTexts = cancel ? cancelTexts : acceptTexts;

        // Collect all Button children and score them by text match.
        var buttons = new List<IntPtr>();
        WindowInterop.EnumChildWindows(hwnd, (child, _) =>
        {
            var cls = new System.Text.StringBuilder(64);
            WindowInterop.GetClassName(child, cls, cls.Capacity);
            if (cls.ToString().Equals("Button", StringComparison.OrdinalIgnoreCase))
                buttons.Add(child);
            return true; // continue enumeration
        }, IntPtr.Zero);

        // First pass: match by button text.
        foreach (var btn in buttons)
        {
            var sb = new System.Text.StringBuilder(256);
            WindowInterop.GetWindowText(btn, sb, sb.Capacity);
            var text = sb.ToString().Trim();
            if (targetTexts.Any(t => text.Equals(t, StringComparison.OrdinalIgnoreCase)))
                return btn;
        }

        // Second pass: no text match; use fallback control IDs.
        // Classic Win32 MessageBox: IDOK=1, IDCANCEL=2, IDYES=6, IDNO=7.
        // WinForms/TaskDialog maps OK-only dialogs to ID=2 instead of 1.
        int[] fallbackIds = cancel
            ? new[] { WindowInterop.IDCANCEL, 7 }  // IDCANCEL=2, IDNO=7
            : new[] { WindowInterop.IDOK, 2, 6 };   // IDOK=1, then ID=2 (WinForms OK), IDYES=6
        foreach (var id in fallbackIds)
        {
            var btn = WindowInterop.GetDlgItem(hwnd, id);
            if (btn != IntPtr.Zero)
                return btn;
        }

        // Last resort: for a single-button OK dialog, return the only button found.
        if (!cancel && buttons.Count == 1)
            return buttons[0];

        return IntPtr.Zero;
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

            // LegacyIAccessiblePattern (MSAA/IAccessible): WinForms buttons in UIA2 are
            // often reported as ControlType.Pane without InvokePattern, but they DO expose
            // LegacyIAccessiblePattern wrapping IAccessible::accDoDefaultAction().
            // Critically, this is a COM out-of-process call that Windows routes through
            // UIAutomationCore, so it crosses UAC elevation boundaries unlike PostMessage.
            try
            {
                var legacyIa = element.Patterns.LegacyIAccessible.PatternOrDefault;
                if (legacyIa != null)
                {
                    var laTask = Task.Run(() => legacyIa.DoDefaultAction());
                    bool completed = laTask.Wait(TimeSpan.FromMilliseconds(2000));
                    if (!completed)
                        _ = laTask.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
                    return (true, "uia:msaa");
                }
            }
            catch
            {
                // LegacyIAccessiblePattern not supported by this element or framework version.
                // Fall through to PostMessage / physical mouse.
            }
        }

        // Fallback: PostMessage WM_LBUTTON/WM_RBUTTON to the control's HWND.
        // Programmatic — does not move the mouse cursor.
        // Note: UIPI blocks PostMessage when the target runs at higher integrity (elevated).
        // In that case the click will fail and an error is returned.
        return PostMessageClick(element, right, doubleClick, holdMs);
    }

    /// <summary>
    /// PostMessage fallback: sends WM_LBUTTONDOWN/UP (or WM_RBUTTONDOWN/UP) directly
    /// to the control's HWND using coordinates local to the control's client area.
    /// Uses DeepChildFromClientPoint to resolve the actual child HWND from the container
    /// window, fixing WinForms buttons that UIA2 reports as Pane with parent-window HWND.
    /// Does NOT move the physical mouse cursor.
    /// </summary>
    private static (bool success, string method) PostMessageClick(
        AutomationElement element, bool right, bool doubleClick, int holdMs)
    {
        try
        {
            var containerHwnd = element.Properties.NativeWindowHandle.ValueOrDefault;
            if (containerHwnd == IntPtr.Zero)
                return (false, "postmessage");

            // Compute center in screen coords
            var bounds = element.BoundingRectangle;
            var screenCenter = new POINT
            {
                x = (int)(bounds.X + bounds.Width / 2),
                y = (int)(bounds.Y + bounds.Height / 2)
            };

            // Convert to client coords of the container and drill down to actual child.
            // This handles WinForms controls (e.g. Button) that UIA2 reports as "Pane"
            // with the parent Form's HWND as NativeWindowHandle.
            var clientPt = screenCenter;
            WindowInterop.ScreenToClient(containerHwnd, ref clientPt);
            var hwnd = WindowInterop.DeepChildFromClientPoint(containerHwnd, clientPt);

            // Recompute client coords for the resolved HWND
            var localPt = screenCenter;
            WindowInterop.ScreenToClient(hwnd, ref localPt);
            var lParam = WindowInterop.MakeLParam(localPt.x, localPt.y);

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
    /// PostMessage click at screen coordinates (no element). Resolves the HWND at the
    /// given screen point using WindowFromPoint and DeepChildFromClientPoint.
    /// Does NOT move the physical mouse cursor.
    /// </summary>
    private static (bool success, string method) PostMessageClickAtScreen(
        int screenX, int screenY, bool right, bool doubleClick, int holdMs)
    {
        try
        {
            var screenPt = new POINT { x = screenX, y = screenY };
            var hwnd = WindowInterop.WindowFromPoint(screenPt);
            if (hwnd == IntPtr.Zero)
                return (false, "postmessage");

            // Drill down to actual child HWND
            var clientPt = screenPt;
            WindowInterop.ScreenToClient(hwnd, ref clientPt);
            hwnd = WindowInterop.DeepChildFromClientPoint(hwnd, clientPt);

            // Recompute client coords for the resolved HWND
            var localPt = screenPt;
            WindowInterop.ScreenToClient(hwnd, ref localPt);
            var lParam = WindowInterop.MakeLParam(localPt.x, localPt.y);

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
            return (true, "postmessage");
        }
        catch
        {
            return (false, "postmessage");
        }
    }
}
