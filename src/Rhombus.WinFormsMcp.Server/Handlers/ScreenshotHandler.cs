using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using Rhombus.WinFormsMcp.Server.Abstractions;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Handler for capturing screenshots of windows or elements.
/// Replaces: take_screenshot
/// </summary>
internal class ScreenshotHandler : HandlerBase
{
    public ScreenshotHandler(ISessionManager session, IWindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[] { "screenshot" };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        try
        {
            var target = GetStringArg(args, "target");
            var handleStr = GetStringArg(args, "handle");
            var file = GetStringArg(args, "file");

            string base64;

            if (!string.IsNullOrEmpty(handleStr))
            {
                // Capture by HWND using PrintWindow — works even when behind other windows
                var hwnd = new IntPtr(Convert.ToInt64(handleStr, 16));
                if (!Interop.WindowInterop.IsWindowVisible(hwnd))
                    return Error($"Window 0x{hwnd:X} is not visible or does not exist.");
                base64 = CaptureWindowByHwndToBase64(hwnd);
            }
            else if (string.IsNullOrEmpty(target))
            {
                // Capture active window or desktop
                base64 = CaptureDesktopToBase64();
            }
            else if (target.StartsWith("elem_"))
            {
                // Capture specific element
                var element = Session.GetElement(target);
                if (element == null)
                    return Error($"Element not found: {target}");

                base64 = CaptureElementToBase64(element);
            }
            else
            {
                // target is a window title - find and capture that window
                var automation = Session.GetAutomation();
                var window = automation.GetWindowByTitle(target);
                if (window == null)
                    return Error($"Window not found: {target}");

                base64 = CaptureElementToBase64(window);
            }

            // Save to file if specified
            if (!string.IsNullOrEmpty(file))
            {
                var bytes = Convert.FromBase64String(base64);
                File.WriteAllBytes(file, bytes);
                return ScopedSuccess(args, new { saved = true, path = file });
            }

            return ScopedSuccess(args, new { image = base64, format = "png", encoding = "base64" });
        }
        catch (UnauthorizedAccessException)
        {
            return Error("Cannot write screenshot file. Access denied.");
        }
        catch (DirectoryNotFoundException)
        {
            return Error($"Cannot write screenshot - directory does not exist. Create the directory first.");
        }
        catch (FlaUI.Core.Exceptions.PropertyNotSupportedException)
        {
            return Error($"Cannot capture element - it may be minimized or not rendered.");
        }
        catch (Exception ex)
        {
            return Error($"Screenshot failed: {ex.Message}");
        }
    }
}
