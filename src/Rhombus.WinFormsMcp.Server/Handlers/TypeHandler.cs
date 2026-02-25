using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using Rhombus.WinFormsMcp.Server.Abstractions;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Interop;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Unified handler for text input and keyboard operations.
/// Replaces: type_text, set_value, send_keys
/// All input is programmatic — UIA ValuePattern first, PostMessage WM_CHAR/WM_KEYDOWN fallback.
/// Physical keyboard input (SendKeys/SendInput) is never used to prevent interfering with the user.
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
            var clear = GetBoolArg(args, "clear", false);
            var keys = GetBoolArg(args, "keys", false);

            if (!string.IsNullOrEmpty(target))
            {
                // Type into specific element
                return TypeIntoElement(target, text, clear);
            }
            else if (keys)
            {
                // Send key codes globally
                return SendKeys(text);
            }
            else
            {
                // Send text globally (less reliable, use element targeting when possible)
                return SendText(text);
            }
        }
        catch (Exception ex)
        {
            return Error($"Type failed: {ex.Message}");
        }
    }

    private Task<JsonElement> TypeIntoElement(string elementId, string text, bool clear)
    {
        var element = Session.GetElement(elementId);
        if (element == null)
            return Error($"Element not found: {elementId}");

        if (Session.IsElementStale(elementId))
            return Error($"Element is stale: {elementId}. Use find to locate it again.");

        // Focus the element first
        try
        {
            element.Focus();
            Thread.Sleep(50);
        }
        catch
        {
            // Some elements don't support Focus, try clicking instead
            try
            {
                element.Click();
                Thread.Sleep(50);
            }
            catch { /* Continue anyway */ }
        }

        // Try ValuePattern first (UIA: no physical keyboard required)
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
                return ScopedSuccess(default, new { typed = true, target = elementId, length = text.Length });
            }
        }
        catch { /* Fall through to PostMessage fallback */ }

        // Fallback: PostMessage WM_CHAR per character (no physical keyboard input)
        var hwnd = GetElementHwnd(element);
        if (hwnd == IntPtr.Zero)
            return Error($"Cannot send input to element: {elementId}. No window handle and ValuePattern not supported.");

        if (clear)
            PostSelectAllDelete(hwnd);

        foreach (char c in text)
            WindowInterop.PostMessage(hwnd, WindowInterop.WM_CHAR, new IntPtr(c), IntPtr.Zero);

        return ScopedSuccess(default, new { typed = true, target = elementId, length = text.Length });
    }

    private Task<JsonElement> SendKeys(string keySequence)
    {
        // Send key sequence to foreground window via PostMessage (no physical keyboard).
        var hwnd = WindowInterop.GetForegroundWindow();
        PostKeySequence(hwnd, keySequence);
        return ScopedSuccess(default, new { sent = true, keys = keySequence });
    }

    private Task<JsonElement> SendText(string text)
    {
        // Send text to foreground window via PostMessage WM_CHAR (no physical keyboard).
        var hwnd = WindowInterop.GetForegroundWindow();
        foreach (char c in text)
            WindowInterop.PostMessage(hwnd, WindowInterop.WM_CHAR, new IntPtr(c), IntPtr.Zero);
        return ScopedSuccess(default, new { typed = true, length = text.Length });
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
