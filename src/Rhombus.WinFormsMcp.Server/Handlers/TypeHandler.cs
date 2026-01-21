using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using Rhombus.WinFormsMcp.Server.Abstractions;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Unified handler for text input and keyboard operations.
/// Replaces: type_text, set_value, send_keys
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

        // Clear existing content if requested
        if (clear)
        {
            // Select all and delete
            System.Windows.Forms.SendKeys.SendWait("^a");
            Thread.Sleep(50);
            System.Windows.Forms.SendKeys.SendWait("{DELETE}");
            Thread.Sleep(50);
        }

        // Try ValuePattern first for reliability
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
        catch { /* Fall through to keyboard input */ }

        // Fall back to keyboard simulation
        // Escape special characters for SendKeys
        var escaped = EscapeSendKeys(text);
        System.Windows.Forms.SendKeys.SendWait(escaped);

        return ScopedSuccess(default, new { typed = true, target = elementId, length = text.Length });
    }

    private Task<JsonElement> SendKeys(string keySequence)
    {
        // Parse and send key codes
        // Supports: Ctrl+S, Alt+F4, Enter, Tab, etc.
        System.Windows.Forms.SendKeys.SendWait(keySequence);
        return ScopedSuccess(default, new { sent = true, keys = keySequence });
    }

    private Task<JsonElement> SendText(string text)
    {
        var escaped = EscapeSendKeys(text);
        System.Windows.Forms.SendKeys.SendWait(escaped);
        return ScopedSuccess(default, new { typed = true, length = text.Length });
    }

    private string EscapeSendKeys(string text)
    {
        // Escape special SendKeys characters: + ^ % ~ { } [ ] ( )
        return text
            .Replace("{", "{{}")
            .Replace("}", "{}}")
            .Replace("[", "{[}")
            .Replace("]", "{]}")
            .Replace("(", "{(}")
            .Replace(")", "{)}")
            .Replace("+", "{+}")
            .Replace("^", "{^}")
            .Replace("%", "{%}")
            .Replace("~", "{~}");
    }
}
