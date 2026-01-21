using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Handles screenshot tools: take_screenshot.
/// </summary>
internal class ScreenshotHandlers : HandlerBase
{
    public ScreenshotHandlers(SessionManager session, WindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[]
    {
        "take_screenshot"
    };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        return toolName switch
        {
            "take_screenshot" => TakeScreenshot(args),
            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };
    }

    private Task<JsonElement> TakeScreenshot(JsonElement args)
    {
        try
        {
            var outputPath = GetStringArg(args, "outputPath");
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");
            var elementId = GetStringArg(args, "elementId") ?? GetStringArg(args, "elementPath");
            var returnBase64 = GetBoolArg(args, "returnBase64", false);

            // Validate: need either outputPath or returnBase64
            if (!returnBase64 && string.IsNullOrEmpty(outputPath))
            {
                return Error("Either outputPath or returnBase64=true is required");
            }

            var automation = Session.GetAutomation();

            // Priority: element > window > desktop
            if (!string.IsNullOrEmpty(elementId))
            {
                // Element screenshot
                var element = Session.GetElement(elementId!);
                if (element == null)
                    return Error($"Element '{elementId}' not found in session");

                if (returnBase64)
                {
                    var base64 = CaptureElementToBase64(element);
                    return Success(
                        ("base64", base64),
                        ("format", "png"));
                }
                else
                {
                    automation.TakeScreenshot(outputPath!, element);
                    return Success(("message", $"Screenshot of element saved to {outputPath}"));
                }
            }
            else if (!string.IsNullOrEmpty(windowHandle) || !string.IsNullOrEmpty(windowTitle))
            {
                // Window screenshot
                var window = Windows.FindWindow(windowHandle, windowTitle);
                if (window == null)
                {
                    // Check for multiple matches
                    if (!string.IsNullOrEmpty(windowTitle))
                    {
                        var matches = Windows.FindWindowsByTitle(windowTitle);
                        if (matches.Count > 1)
                            return Error($"Multiple windows match '{windowTitle}': {string.Join(", ", matches.ConvertAll(w => w.Title))}");
                    }
                    return Error($"Window not found: {windowHandle ?? windowTitle}");
                }

                // Focus the window to bring it to front (saves previous for restore)
                var previousForeground = Windows.GetCurrentForegroundHandle();
                Windows.FocusWindowByHandle(window.Handle);
                Thread.Sleep(Constants.Timeouts.WindowFocusDelay);

                // Get client area bounds (excludes title bar and borders)
                var clientBounds = Windows.GetClientAreaBounds(window.Handle);
                if (clientBounds == null)
                {
                    return Error("Could not get client area bounds for window");
                }

                if (returnBase64)
                {
                    var base64 = CaptureRegionToBase64(clientBounds.X, clientBounds.Y, clientBounds.Width, clientBounds.Height);

                    // Restore previous foreground window
                    if (!string.IsNullOrEmpty(previousForeground) && previousForeground != window.Handle)
                    {
                        Windows.FocusWindowByHandle(previousForeground);
                    }

                    return Success(
                        ("base64", base64),
                        ("format", "png"),
                        ("window", new {
                            handle = window.Handle,
                            title = window.Title,
                            clientBounds = new {
                                x = clientBounds.X,
                                y = clientBounds.Y,
                                width = clientBounds.Width,
                                height = clientBounds.Height
                            }
                        }));
                }
                else
                {
                    // Capture only the client area (no title bar)
                    automation.TakeRegionScreenshot(outputPath!,
                        clientBounds.X, clientBounds.Y,
                        clientBounds.Width, clientBounds.Height);

                    // Restore previous foreground window
                    if (!string.IsNullOrEmpty(previousForeground) && previousForeground != window.Handle)
                    {
                        Windows.FocusWindowByHandle(previousForeground);
                    }

                    // Return with client area bounds - coordinates in screenshot match client coordinates directly
                    return Success(
                        ("message", $"Screenshot of window '{window.Title}' (client area) saved to {outputPath}"),
                        ("window", new {
                            handle = window.Handle,
                            title = window.Title,
                            clientBounds = new {
                                x = clientBounds.X,
                                y = clientBounds.Y,
                                width = clientBounds.Width,
                                height = clientBounds.Height
                            },
                            note = "Screenshot shows client area only (no title bar). Coordinates in screenshot match client coordinates. Add clientBounds.x/y to get screen coordinates for clicking."
                        }));
                }
            }
            else
            {
                // Full desktop screenshot
                if (returnBase64)
                {
                    var base64 = CaptureDesktopToBase64();
                    return Success(
                        ("base64", base64),
                        ("format", "png"));
                }
                else
                {
                    automation.TakeScreenshot(outputPath!, null);
                    return Success(("message", $"Desktop screenshot saved to {outputPath}"));
                }
            }
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }
}
