using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using C5T8fBtWY.WinFormsMcp.Server.Abstractions;
using C5T8fBtWY.WinFormsMcp.Server.Interop;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using C5T8fBtWY.WinFormsMcp.Server.Automation;

namespace C5T8fBtWY.WinFormsMcp.Server.Handlers;

/// <summary>
/// Unified handler for text input and keyboard operations.
/// Replaces: type_text, set_value, send_keys
/// All input is programmatic — UIA ValuePattern first, PostMessage WM_CHAR/WM_KEYDOWN fallback.
/// target is always required: element ID (elem_N) or raw HWND (0xHHHH).
/// This prevents keystrokes from accidentally targeting the host machine's focused window.
/// </summary>
internal class TypeHandler : HandlerBase
{
    public TypeHandler(ISessionManager session, IWindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[] { "type" };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        try
        {
            var text = GetStringArg(args, "text");
            if (string.IsNullOrEmpty(text))
                return Error("text is required. Example: {\"text\": \"Hello\", \"target\": \"elem_1\"}");

            var target = GetStringArg(args, "target");
            if (string.IsNullOrEmpty(target))
                return Error("target is required. Specify an element ID from find (e.g. \"elem_1\") " +
                             "or a raw HWND string (e.g. \"0x920972\" from the windows list in any response). " +
                             "Use find to locate the element first, then pass its ID as target.");

            var clear = GetBoolArg(args, "clear", false);
            var keys = GetBoolArg(args, "keys", false);

            // Raw HWND format (e.g., "0x007C1A7E") — bypass UIA, use Win32 directly.
            // Used for controls inside modal dialogs where UIA returns empty trees
            // (e.g., WinForms PropertyGrid inline editor during ShowDialog), or to send
            // key sequences directly to a window (e.g., {ENTER} to close a dialog).
            if (target.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(target.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out long hval))
            {
                var hwnd = new IntPtr(hval);
                if (keys)
                {
                    PostKeySequence(hwnd, text);
                    return ScopedSuccess(default, new { sent = true, target, keys = text });
                }
                return TypeIntoHwnd(hwnd, text, clear);
            }

            // Element ID (elem_N cache)
            return keys
                ? SendKeysToElement(target, text)
                : TypeIntoElement(target, text, clear);
        }
        catch (Exception ex)
        {
            return Error($"Type failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Type text into a control identified by its raw Win32 HWND.
    /// Uses WM_SETTEXT (synchronous) to set the control's text, then sends Enter to commit.
    /// Works for WinForms PropertyGrid inline editors that block UIA during ShowDialog().
    /// </summary>
    private Task<JsonElement> TypeIntoHwnd(IntPtr hwnd, string text, bool clear)
    {
        if (hwnd == IntPtr.Zero)
            return Error("Invalid HWND: zero");

        if (clear)
        {
            // WM_SETTEXT replaces the entire control text. Synchronous so the edit control
            // is updated before we send the Enter key to commit the new value.
            WindowInterop.SendMessage(hwnd, WindowInterop.WM_SETTEXT, IntPtr.Zero, text);
        }
        else
        {
            // Append: read current text, then set combined value
            var sb = new System.Text.StringBuilder(1024);
            var len = (int)WindowInterop.SendMessage(hwnd, WindowInterop.WM_GETTEXT,
                new IntPtr(sb.Capacity), sb);
            var current = sb.ToString(0, Math.Max(0, Math.Min(len, sb.Length)));
            WindowInterop.SendMessage(hwnd, WindowInterop.WM_SETTEXT, IntPtr.Zero, current + text);
        }

        // Commit: send Enter to the edit control so PropertyGrid / WinForms reads the new value
        Thread.Sleep(50);
        WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYDOWN, new IntPtr(WindowInterop.VK_RETURN), IntPtr.Zero);
        WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYUP, new IntPtr(WindowInterop.VK_RETURN), IntPtr.Zero);

        return ScopedSuccess(default, new { typed = true, hwnd = $"0x{hwnd.ToInt64():X8}", length = text.Length });
    }

    private Task<JsonElement> TypeIntoElement(string elementId, string text, bool clear)
    {
        var element = Session.GetElement(elementId);
        if (element == null)
            return Error($"Element not found: {elementId}");

        if (Session.IsElementStale(elementId))
            return Error($"Element is stale: {elementId}. Use find to locate it again.");

        // Try ValuePattern first (UIA: no focus or foreground activation required)
        try
        {
            if (element.Patterns.Value.IsSupported)
            {
                if (clear)
                {
                    element.Patterns.Value.Pattern.SetValue(text);
                }
                else
                {
                    var current = element.Patterns.Value.Pattern.Value ?? "";
                    element.Patterns.Value.Pattern.SetValue(current + text);
                }
                return ScopedSuccess(default, new { typed = true, target = elementId, length = text.Length, method = "ValuePattern" });
            }
        }
        catch { /* Fall through to PostMessage fallback */ }

        // Fallback: PostMessage WM_CHAR per character (no physical keyboard input)
        var hwnd = GetElementHwnd(element);
        if (hwnd == IntPtr.Zero)
            return Error($"Cannot send input to element: {elementId}. No window handle and ValuePattern not supported.");

        // Focus only needed for PostMessage path — some controls need focus to process WM_CHAR
        try { element.Focus(); Thread.Sleep(50); }
        catch
        {
            try { element.Click(); Thread.Sleep(50); }
            catch { /* Continue anyway */ }
        }

        if (clear)
            PostSelectAllDelete(hwnd);

        foreach (char c in text)
            WindowInterop.PostMessage(hwnd, WindowInterop.WM_CHAR, new IntPtr(c), IntPtr.Zero);

        return ScopedSuccess(default, new { typed = true, target = elementId, length = text.Length, method = "PostMessage" });
    }

    /// <summary>
    /// Send a key sequence to the element's window HWND via PostMessage WM_KEYDOWN/WM_KEYUP.
    /// Use when keys=true with a target element or window HWND.
    /// </summary>
    private Task<JsonElement> SendKeysToElement(string elementId, string keySequence)
    {
        var element = Session.GetElement(elementId);
        if (element == null)
            return Error($"Element not found: {elementId}");

        if (Session.IsElementStale(elementId))
            return Error($"Element is stale: {elementId}. Use find to locate it again.");

        var hwnd = GetElementHwnd(element);
        if (hwnd == IntPtr.Zero)
            return Error($"Cannot send keys to element: {elementId}. No window handle found.");

        PostKeySequence(hwnd, keySequence);
        return ScopedSuccess(default, new { sent = true, target = elementId, keys = keySequence });
    }

    /// <summary>Get the HWND for a UI element (for PostMessage fallback).</summary>
    private static IntPtr GetElementHwnd(AutomationElement element)
    {
        try
        {
            var handle = element.Properties.NativeWindowHandle.Value;
            if (handle != 0) return new IntPtr(handle);
        }
        catch { }
        return IntPtr.Zero;
    }

    /// <summary>PostMessage Ctrl+A, Delete to clear all text in a control.</summary>
    private static void PostSelectAllDelete(IntPtr hwnd)
    {
        // Ctrl+A
        WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYDOWN, new IntPtr(WindowInterop.VK_CONTROL), IntPtr.Zero);
        WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYDOWN, new IntPtr(WindowInterop.VK_A), IntPtr.Zero);
        WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYUP, new IntPtr(WindowInterop.VK_A), IntPtr.Zero);
        WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYUP, new IntPtr(WindowInterop.VK_CONTROL), IntPtr.Zero);
        Thread.Sleep(50);
        // Delete
        WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYDOWN, new IntPtr(WindowInterop.VK_DELETE), IntPtr.Zero);
        WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYUP, new IntPtr(WindowInterop.VK_DELETE), IntPtr.Zero);
        Thread.Sleep(50);
    }

    /// <summary>
    /// Parse SendKeys-style key sequence and post to window via WM_KEYDOWN/WM_KEYUP.
    /// Supports: ^X (Ctrl+X), +X (Shift+X), %X (Alt+X), ~ (Enter),
    ///           {TAB}, {ENTER}, {ESC}, {DELETE}, {BACK}, {UP}, {DOWN}, {LEFT}, {RIGHT},
    ///           {HOME}, {END}, {PGUP}, {PGDN}, {F1}-{F12}
    /// </summary>
    private static void PostKeySequence(IntPtr hwnd, string keySequence)
    {
        bool ctrl = false, shift = false, alt = false;
        int i = 0;
        while (i < keySequence.Length)
        {
            char c = keySequence[i];
            if (c == '^') { ctrl = true; i++; continue; }
            if (c == '+') { shift = true; i++; continue; }
            if (c == '%') { alt = true; i++; continue; }

            uint vk;
            if (c == '~')
            {
                vk = WindowInterop.VK_RETURN;
                i++;
            }
            else if (c == '{')
            {
                int end = keySequence.IndexOf('}', i);
                if (end < 0) { i++; continue; }
                var key = keySequence.Substring(i + 1, end - i - 1).ToUpperInvariant();
                vk = ParseNamedKey(key);
                i = end + 1;
            }
            else
            {
                vk = (uint)char.ToUpper(c);
                i++;
            }

            if (vk == 0) { ctrl = shift = alt = false; continue; }

            if (ctrl) WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYDOWN, new IntPtr(WindowInterop.VK_CONTROL), IntPtr.Zero);
            if (shift) WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYDOWN, new IntPtr(WindowInterop.VK_SHIFT), IntPtr.Zero);
            if (alt) WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYDOWN, new IntPtr(WindowInterop.VK_MENU), IntPtr.Zero);

            WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYDOWN, new IntPtr(vk), IntPtr.Zero);
            WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYUP, new IntPtr(vk), IntPtr.Zero);

            if (alt) WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYUP, new IntPtr(WindowInterop.VK_MENU), IntPtr.Zero);
            if (shift) WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYUP, new IntPtr(WindowInterop.VK_SHIFT), IntPtr.Zero);
            if (ctrl) WindowInterop.PostMessage(hwnd, WindowInterop.WM_KEYUP, new IntPtr(WindowInterop.VK_CONTROL), IntPtr.Zero);

            ctrl = shift = alt = false;
        }
    }

    private static uint ParseNamedKey(string name) => name switch
    {
        "TAB" => 0x09,
        "ENTER" => WindowInterop.VK_RETURN,
        "ESC" or "ESCAPE" => WindowInterop.VK_ESCAPE,
        "DELETE" or "DEL" => WindowInterop.VK_DELETE,
        "BACK" or "BACKSPACE" or "BS" => WindowInterop.VK_BACK,
        "UP" => 0x26,
        "DOWN" => 0x28,
        "LEFT" => 0x25,
        "RIGHT" => 0x27,
        "HOME" => 0x24,
        "END" => 0x23,
        "PGUP" => 0x21,
        "PGDN" => 0x22,
        "F1" => 0x70,
        "F2" => 0x71,
        "F3" => 0x72,
        "F4" => 0x73,
        "F5" => 0x74,
        "F6" => 0x75,
        "F7" => 0x76,
        "F8" => 0x77,
        "F9" => 0x78,
        "F10" => 0x79,
        "F11" => 0x7A,
        "F12" => 0x7B,
        _ => 0
    };
}
