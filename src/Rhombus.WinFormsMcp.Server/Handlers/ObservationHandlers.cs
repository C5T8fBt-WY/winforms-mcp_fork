using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Handles observation tools: get_ui_tree, expand_collapse, scroll, get_element_at_point,
/// capture_ui_snapshot, compare_ui_snapshots.
/// </summary>
internal class ObservationHandlers : HandlerBase
{
    public ObservationHandlers(SessionManager session, WindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[]
    {
        "get_ui_tree",
        "expand_collapse",
        "scroll",
        "get_element_at_point",
        "capture_ui_snapshot",
        "compare_ui_snapshots"
    };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        return toolName switch
        {
            "get_ui_tree" => GetUiTree(args),
            "expand_collapse" => ExpandCollapse(args),
            "scroll" => Scroll(args),
            "get_element_at_point" => GetElementAtPoint(args),
            "capture_ui_snapshot" => CaptureUiSnapshot(args),
            "compare_ui_snapshots" => CompareUiSnapshots(args),
            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };
    }

    private Task<JsonElement> GetUiTree(JsonElement args)
    {
        try
        {
            var windowTitle = GetStringArg(args, "windowTitle");
            var maxDepth = GetIntArg(args, "maxDepth", 3);
            var maxTokenBudget = GetIntArg(args, "maxTokenBudget", Constants.Limits.DefaultTokenBudget);
            var includeInvisible = GetBoolArg(args, "includeInvisible", false);
            var skipInternalParts = GetBoolArg(args, "skipInternalParts", true);

            var automation = Session.GetAutomation();

            // Get root element (window or desktop)
            AutomationElement? root = null;
            if (!string.IsNullOrEmpty(windowTitle))
            {
                root = automation.GetWindowByTitle(windowTitle);
                if (root == null)
                    return Error($"Window not found: {windowTitle}");
            }

            // Build tree with options
            var options = new TreeBuilderOptions
            {
                MaxDepth = maxDepth,
                MaxTokenBudget = maxTokenBudget,
                IncludeInvisible = includeInvisible,
                SkipInternalParts = skipInternalParts
            };

            var result = automation.BuildUiTree(root, options);

            return Success(
                ("xml", result.Xml),
                ("tokenCount", result.TokenCount),
                ("elementCount", result.ElementCount),
                ("dpiScaleFactor", result.DpiScaleFactor),
                ("timestamp", result.Timestamp),
                ("truncated", result.Truncated));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> ExpandCollapse(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId");
            var automationId = GetStringArg(args, "automationId");
            var windowTitle = GetStringArg(args, "windowTitle");
            var expand = GetBoolArg(args, "expand", true);
            var uiUpdateDelayMs = GetIntArg(args, "uiUpdateDelayMs", Constants.Timeouts.UiUpdateDelay);

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

            var result = automation.ExpandCollapse(element, expand, uiUpdateDelayMs);

            if (result.Success)
            {
                return Success(
                    ("previousState", result.PreviousState),
                    ("currentState", result.CurrentState));
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

    private Task<JsonElement> Scroll(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId");
            var automationId = GetStringArg(args, "automationId");
            var windowTitle = GetStringArg(args, "windowTitle");
            var directionStr = GetStringArg(args, "direction") ?? throw new ArgumentException("direction is required");
            var amountStr = GetStringArg(args, "amount") ?? "SmallDecrement";
            var uiUpdateDelayMs = GetIntArg(args, "uiUpdateDelayMs", Constants.Timeouts.UiUpdateDelay);

            // Parse direction
            if (!Enum.TryParse<ScrollDirection>(directionStr, true, out var direction))
                return Error($"Invalid direction: {directionStr}. Valid values: Up, Down, Left, Right");

            // Parse amount
            if (!Enum.TryParse<ScrollAmount>(amountStr, true, out var amount))
                return Error($"Invalid amount: {amountStr}. Valid values: SmallDecrement, LargeDecrement");

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

            var result = automation.Scroll(element, direction, amount, uiUpdateDelayMs);

            if (result.Success)
            {
                return Success(
                    ("horizontalScrollPercent", result.HorizontalScrollPercent),
                    ("verticalScrollPercent", result.VerticalScrollPercent),
                    ("horizontalChanged", result.HorizontalChanged),
                    ("verticalChanged", result.VerticalChanged));
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

    private Task<JsonElement> GetElementAtPoint(JsonElement args)
    {
        try
        {
            var x = GetIntArg(args, "x");
            var y = GetIntArg(args, "y");

            var automation = Session.GetAutomation();
            var result = automation.GetElementAtPoint(x, y);

            if (result.Success)
            {
                var props = new List<(string, object?)>
                {
                    ("automationId", result.AutomationId ?? ""),
                    ("name", result.Name ?? ""),
                    ("controlType", result.ControlType ?? ""),
                    ("runtimeId", result.RuntimeId ?? ""),
                    ("pid", result.Pid),
                    ("processName", result.ProcessName ?? ""),
                    ("className", result.ClassName ?? ""),
                    ("nativeWindowHandle", result.NativeWindowHandle ?? "")
                };

                if (result.BoundingRect != null)
                {
                    props.Add(("boundingRect", new {
                        x = result.BoundingRect.X,
                        y = result.BoundingRect.Y,
                        width = result.BoundingRect.Width,
                        height = result.BoundingRect.Height
                    }));
                }

                return Task.FromResult(ToolResponse.Ok(Windows, props.ToArray()).ToJsonElement());
            }
            else
            {
                return Error(result.ErrorMessage ?? "No element at point");
            }
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> CaptureUiSnapshot(JsonElement args)
    {
        try
        {
            var windowTitle = GetStringArg(args, "windowTitle");
            var snapshotId = GetStringArg(args, "snapshotId") ?? throw new ArgumentException("snapshotId is required");

            var automation = Session.GetAutomation();
            var detector = Session.GetStateChangeDetector();

            // Get root element (window or desktop)
            AutomationElement? root = null;
            if (!string.IsNullOrEmpty(windowTitle))
            {
                root = automation.GetWindowByTitle(windowTitle);
                if (root == null)
                    return Error($"Window not found: {windowTitle}");
            }
            else
            {
                root = automation.GetDesktop();
            }

            // Capture snapshot
            var snapshot = detector.CaptureSnapshot(root);
            Session.CacheSnapshot(snapshotId, snapshot);

            return Success(
                ("snapshotId", snapshotId),
                ("hash", snapshot.Hash),
                ("elementCount", snapshot.Elements.Count),
                ("capturedAt", snapshot.CapturedAt.ToString("o")));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> CompareUiSnapshots(JsonElement args)
    {
        try
        {
            var beforeSnapshotId = GetStringArg(args, "beforeSnapshotId") ?? throw new ArgumentException("beforeSnapshotId is required");
            var afterSnapshotId = GetStringArg(args, "afterSnapshotId");
            var windowTitle = GetStringArg(args, "windowTitle");

            var automation = Session.GetAutomation();
            var detector = Session.GetStateChangeDetector();

            // Get before snapshot
            var beforeSnapshot = Session.GetSnapshot(beforeSnapshotId);
            if (beforeSnapshot == null)
                return Error($"Snapshot not found: {beforeSnapshotId}");

            // Get or capture after snapshot
            TreeSnapshot afterSnapshot;
            if (!string.IsNullOrEmpty(afterSnapshotId))
            {
                var cached = Session.GetSnapshot(afterSnapshotId);
                if (cached == null)
                    return Error($"Snapshot not found: {afterSnapshotId}");
                afterSnapshot = cached;
            }
            else
            {
                // Auto-capture current state
                AutomationElement? root = null;
                if (!string.IsNullOrEmpty(windowTitle))
                {
                    root = automation.GetWindowByTitle(windowTitle);
                    if (root == null)
                        return Error($"Window not found: {windowTitle}");
                }
                else
                {
                    root = automation.GetDesktop();
                }
                afterSnapshot = detector.CaptureSnapshot(root);
            }

            // Compare snapshots
            var result = detector.CompareSnapshots(beforeSnapshot, afterSnapshot);

            return Success(
                ("stateChanged", result.StateChanged),
                ("addedCount", result.AddedCount),
                ("removedCount", result.RemovedCount),
                ("modifiedCount", result.ModifiedCount),
                ("diffSummary", result.DiffSummary));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }
}
