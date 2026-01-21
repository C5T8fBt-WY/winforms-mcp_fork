using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Handles validation tools: element_exists, wait_for_element, check_element_state.
/// </summary>
internal class ValidationHandlers : HandlerBase
{
    public ValidationHandlers(SessionManager session, WindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[]
    {
        "element_exists",
        "wait_for_element",
        "check_element_state"
    };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        return toolName switch
        {
            "element_exists" => ElementExists(args),
            "wait_for_element" => WaitForElement(args),
            "check_element_state" => CheckElementState(args),
            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };
    }

    private Task<JsonElement> ElementExists(JsonElement args)
    {
        try
        {
            var automationId = GetStringArg(args, "automationId") ?? throw new ArgumentException("automationId is required");

            var automation = Session.GetAutomation();
            var exists = automation.ElementExists(automationId);

            return Success(("exists", exists));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private async Task<JsonElement> WaitForElement(JsonElement args)
    {
        try
        {
            var automationId = GetStringArg(args, "automationId") ?? throw new ArgumentException("automationId is required");
            var timeoutMs = GetIntArg(args, "timeoutMs", Constants.Timeouts.DefaultWait);
            var pollIntervalMs = GetIntArg(args, "pollIntervalMs", Constants.Timeouts.DefaultPollInterval);

            var automation = Session.GetAutomation();
            var found = await automation.WaitForElementAsync(automationId, null, timeoutMs, pollIntervalMs);

            return ToolResponse.Ok(Windows, ("found", found)).ToJsonElement();
        }
        catch (Exception ex)
        {
            return ToolResponse.Fail(ex.Message, Windows).ToJsonElement();
        }
    }

    private Task<JsonElement> CheckElementState(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId");
            var automationId = GetStringArg(args, "automationId");
            var windowTitle = GetStringArg(args, "windowTitle");

            var automation = Session.GetAutomation();
            AutomationElement? element = null;

            // Get element by ID or find by automationId
            if (!string.IsNullOrEmpty(elementId))
            {
                element = Session.GetElement(elementId);
                if (element == null)
                    return Error($"Element not found in cache: {elementId}");
            }
            else if (!string.IsNullOrEmpty(automationId))
            {
                if (string.IsNullOrEmpty(windowTitle))
                    return Error("windowTitle is required when using automationId");

                var window = automation.GetWindowByTitle(windowTitle);
                if (window == null)
                    return Error($"Window not found: {windowTitle}");

                element = automation.FindByAutomationId(automationId, window);
                if (element == null)
                    return Error($"Element not found by AutomationId: {automationId}");
            }
            else
            {
                return Error("Either elementId or automationId is required");
            }

            var result = automation.GetElementState(element);

            if (result.Success)
            {
                // Build properties list for ToolResponse
                var props = new List<(string, object?)>
                {
                    ("automationId", result.AutomationId ?? ""),
                    ("name", result.Name ?? ""),
                    ("className", result.ClassName ?? ""),
                    ("controlType", result.ControlType ?? ""),
                    ("isEnabled", result.IsEnabled),
                    ("isOffscreen", result.IsOffscreen),
                    ("isKeyboardFocusable", result.IsKeyboardFocusable),
                    ("hasKeyboardFocus", result.HasKeyboardFocus),
                    ("dpiScaleFactor", result.DpiScaleFactor)
                };

                if (result.BoundingRect != null)
                {
                    props.Add(("boundingRect", new { x = result.BoundingRect.X, y = result.BoundingRect.Y, width = result.BoundingRect.Width, height = result.BoundingRect.Height }));
                }

                if (result.Value != null) props.Add(("value", result.Value));
                if (result.IsReadOnly.HasValue) props.Add(("isReadOnly", result.IsReadOnly.Value));
                if (result.ToggleState != null) props.Add(("toggleState", result.ToggleState));
                if (result.IsSelected.HasValue) props.Add(("isSelected", result.IsSelected.Value));
                if (result.RangeValue.HasValue)
                {
                    props.Add(("rangeValue", result.RangeValue.Value));
                    if (result.RangeMinimum.HasValue) props.Add(("rangeMinimum", result.RangeMinimum.Value));
                    if (result.RangeMaximum.HasValue) props.Add(("rangeMaximum", result.RangeMaximum.Value));
                }

                return Task.FromResult(ToolResponse.Ok(Windows, props.ToArray()).ToJsonElement());
            }
            else
            {
                return Error(result.ErrorMessage ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }
}
