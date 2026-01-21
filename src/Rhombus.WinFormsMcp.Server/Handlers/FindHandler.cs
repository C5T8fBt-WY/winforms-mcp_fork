using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Rhombus.WinFormsMcp.Server.Abstractions;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Unified handler for element discovery with recursive tree support.
/// Replaces: find_element, get_ui_tree, list_elements, element_exists, wait_for_element,
///           check_element_state, get_property, find_element_near_anchor, mark_for_expansion,
///           clear_expansion_marks, get_element_at_point
/// </summary>
internal class FindHandler : HandlerBase
{
    public FindHandler(ISessionManager session, IWindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[] { "find" };

    public override async Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        try
        {
            var at = GetStringArg(args, "at") ?? "root";
            var recursive = GetBoolArg(args, "recursive", false);
            var depth = GetIntArg(args, "depth", 3);
            var waitMs = GetIntArg(args, "wait_ms", 0);

            // Point-based search
            if (args.TryGetProperty("point", out var pointEl))
            {
                return await FindAtPoint(pointEl);
            }

            // Near-anchor search
            if (args.TryGetProperty("near", out var nearEl))
            {
                return await FindNearAnchor(nearEl, args);
            }

            // Standard find with optional wait
            var startTime = DateTime.UtcNow;
            do
            {
                var result = recursive
                    ? await FindRecursive(at, args, depth)
                    : await FindSingle(at, args);

                if (result.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
                    return result;

                if (waitMs <= 0)
                    return result;

                await Task.Delay(100);
            }
            while ((DateTime.UtcNow - startTime).TotalMilliseconds < waitMs);

            return await Error($"Element not found within {waitMs}ms timeout. Try increasing wait_ms or verify the element exists.");
        }
        catch (FlaUI.Core.Exceptions.PropertyNotSupportedException)
        {
            return await Error("Unable to access element properties - the element may be unavailable or the application unresponsive.");
        }
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80070005))
        {
            return await Error("Access denied to UI element - the target window may be elevated or protected.");
        }
        catch (Exception ex)
        {
            return await Error($"Find failed: {ex.Message}");
        }
    }

    private Task<JsonElement> FindAtPoint(JsonElement pointEl)
    {
        var x = pointEl.GetProperty("x").GetInt32();
        var y = pointEl.GetProperty("y").GetInt32();

        var automation = Session.GetAutomation();
        var desktop = automation.GetDesktop();

        // Search for element at point by checking bounds
        AutomationElement? found = null;
        try
        {
            // Use FlaUI's point-based lookup through the desktop
            var children = desktop.FindAllChildren();
            foreach (var window in children)
            {
                try
                {
                    var bounds = window.BoundingRectangle;
                    if (x >= bounds.X && x <= bounds.X + bounds.Width &&
                        y >= bounds.Y && y <= bounds.Y + bounds.Height)
                    {
                        // Found a window containing the point - search its children
                        found = FindElementAtPointRecursive(window, x, y) ?? window;
                        break;
                    }
                }
                catch { /* Skip inaccessible windows */ }
            }
        }
        catch { }

        if (found == null)
            return Error($"No element at point ({x}, {y})");

        var elementId = Session.CacheElement(found);
        return Success(BuildElementInfo(found, elementId));
    }

    private AutomationElement? FindElementAtPointRecursive(AutomationElement parent, int x, int y, int depth = 0)
    {
        if (depth > 10) return null; // Prevent infinite recursion

        try
        {
            var children = parent.FindAllChildren();
            foreach (var child in children)
            {
                try
                {
                    var bounds = child.BoundingRectangle;
                    if (bounds.Width > 0 && bounds.Height > 0 &&
                        x >= bounds.X && x <= bounds.X + bounds.Width &&
                        y >= bounds.Y && y <= bounds.Y + bounds.Height)
                    {
                        // Found a child containing the point - check deeper
                        var deeper = FindElementAtPointRecursive(child, x, y, depth + 1);
                        return deeper ?? child;
                    }
                }
                catch { /* Skip inaccessible children */ }
            }
        }
        catch { }

        return null;
    }

    private Task<JsonElement> FindNearAnchor(JsonElement nearEl, JsonElement args)
    {
        var anchorId = nearEl.GetProperty("element").GetString();
        var direction = nearEl.TryGetProperty("direction", out var dirProp)
            ? dirProp.GetString()
            : "siblings";

        var anchor = Session.GetElement(anchorId!);
        if (anchor == null)
            return Error($"Anchor element not found: {anchorId}");

        var targetType = GetStringArg(args, "controlType");
        var targetName = GetStringArg(args, "name");
        var targetAutoId = GetStringArg(args, "automationId");

        // Get parent's children for sibling search
        var parent = anchor.Parent;
        if (parent == null)
            return Error("Anchor has no parent for sibling search");

        var siblings = parent.FindAllChildren();
        AutomationElement? found = null;
        var anchorBounds = anchor.BoundingRectangle;

        foreach (var sibling in siblings)
        {
            if (sibling.Equals(anchor)) continue;

            var siblingBounds = sibling.BoundingRectangle;
            bool matchesDirection = direction switch
            {
                "above" => siblingBounds.Bottom <= anchorBounds.Top,
                "below" => siblingBounds.Top >= anchorBounds.Bottom,
                "left" => siblingBounds.Right <= anchorBounds.Left,
                "right" => siblingBounds.Left >= anchorBounds.Right,
                _ => true // siblings - no direction filter
            };

            if (!matchesDirection) continue;

            if (MatchesSelector(sibling, targetType, targetName, targetAutoId))
            {
                found = sibling;
                break;
            }
        }

        if (found == null)
            return Error("No matching element found near anchor");

        var elementId = Session.CacheElement(found);
        return Success(BuildElementInfo(found, elementId));
    }

    private Task<JsonElement> FindSingle(string at, JsonElement args)
    {
        var automation = Session.GetAutomation();
        AutomationElement? searchRoot;

        if (at == "root")
        {
            searchRoot = automation.GetDesktop();
        }
        else
        {
            searchRoot = Session.GetElement(at);
            if (searchRoot == null)
                return Error($"Element not found: {at}");
        }

        var name = GetStringArg(args, "name");
        var automationId = GetStringArg(args, "automationId");
        var className = GetStringArg(args, "className");
        var controlType = GetStringArg(args, "controlType");

        AutomationElement? found = null;

        // Search children
        var children = searchRoot.FindAllChildren();
        foreach (var child in children)
        {
            if (MatchesSelector(child, controlType, name, automationId, className))
            {
                found = child;
                break;
            }
        }

        // If not found in direct children and at root, search recursively
        if (found == null && at == "root")
        {
            found = FindInTree(searchRoot, controlType, name, automationId, className, 5);
        }

        if (found == null)
            return Error("Element not found matching criteria");

        var elementId = Session.CacheElement(found);
        return Success(BuildElementInfo(found, elementId));
    }

    private Task<JsonElement> FindRecursive(string at, JsonElement args, int maxDepth)
    {
        var automation = Session.GetAutomation();
        AutomationElement? searchRoot;

        if (at == "root")
        {
            // Get all tracked windows or all windows
            var trackedPids = Session.GetTrackedProcessIds();
            var windows = trackedPids.Count > 0
                ? GetWindowsForPids(automation, trackedPids)
                : GetAllWindows(automation);

            var windowTrees = new List<object>();
            foreach (var window in windows)
            {
                var windowId = Session.CacheElement(window);
                var tree = BuildTree(window, windowId, maxDepth, 0);
                windowTrees.Add(tree);
            }

            return Success(new { windows = windowTrees });
        }
        else
        {
            searchRoot = Session.GetElement(at);
            if (searchRoot == null)
                return Error($"Element not found: {at}");

            var rootId = at;
            var tree = BuildTree(searchRoot, rootId, maxDepth, 0);
            return Success(tree);
        }
    }

    private List<AutomationElement> GetWindowsForPids(AutomationHelper automation, IReadOnlySet<int> pids)
    {
        var desktop = automation.GetDesktop();
        var windows = new List<AutomationElement>();

        foreach (var child in desktop.FindAllChildren())
        {
            try
            {
                var pid = child.Properties.ProcessId.ValueOrDefault;
                if (pids.Contains(pid) && child.ControlType == ControlType.Window)
                {
                    windows.Add(child);
                }
            }
            catch { /* Skip inaccessible elements */ }
        }

        return windows;
    }

    private List<AutomationElement> GetAllWindows(AutomationHelper automation)
    {
        var desktop = automation.GetDesktop();
        var windows = new List<AutomationElement>();

        foreach (var child in desktop.FindAllChildren())
        {
            try
            {
                if (child.ControlType == ControlType.Window)
                {
                    windows.Add(child);
                }
            }
            catch { /* Skip inaccessible elements */ }
        }

        return windows;
    }

    private object BuildTree(AutomationElement element, string elementId, int maxDepth, int currentDepth)
    {
        var info = BuildElementInfo(element, elementId);

        if (currentDepth >= maxDepth)
            return info;

        var childrenList = new List<object>();
        try
        {
            var children = element.FindAllChildren();
            foreach (var child in children.Take(50)) // Limit children
            {
                try
                {
                    var childId = Session.CacheElement(child);
                    childrenList.Add(BuildTree(child, childId, maxDepth, currentDepth + 1));
                }
                catch { /* Skip inaccessible children */ }
            }
        }
        catch { /* Element might not support children */ }

        if (childrenList.Count > 0)
        {
            return new
            {
                id = elementId,
                type = SafeGetProperty(() => element.ControlType.ToString(), "Unknown"),
                name = SafeGetProperty(() => element.Name, "") ?? "",
                automationId = SafeGetProperty(() => element.AutomationId, "") ?? "",
                bounds = GetBounds(element),
                children = childrenList
            };
        }

        return info;
    }

    private AutomationElement? FindInTree(AutomationElement root, string? controlType, string? name, string? automationId, string? className, int maxDepth, int currentDepth = 0)
    {
        if (currentDepth >= maxDepth) return null;

        try
        {
            var children = root.FindAllChildren();
            foreach (var child in children)
            {
                if (MatchesSelector(child, controlType, name, automationId, className))
                    return child;

                var found = FindInTree(child, controlType, name, automationId, className, maxDepth, currentDepth + 1);
                if (found != null) return found;
            }
        }
        catch { /* Skip inaccessible */ }

        return null;
    }

    private bool MatchesSelector(AutomationElement element, string? controlType, string? name, string? automationId, string? className = null)
    {
        try
        {
            if (controlType != null)
            {
                var actualType = element.ControlType.ToString();
                if (!string.Equals(actualType, controlType, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (name != null)
            {
                var actualName = element.Name ?? "";
                // Support regex patterns
                if (name.Contains("*") || name.Contains("^") || name.Contains("$"))
                {
                    if (!Regex.IsMatch(actualName, name, RegexOptions.IgnoreCase))
                        return false;
                }
                else if (!actualName.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (automationId != null)
            {
                var actualId = element.AutomationId ?? "";
                if (!string.Equals(actualId, automationId, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (className != null)
            {
                var actualClass = element.ClassName ?? "";
                if (!string.Equals(actualClass, className, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private object BuildElementInfo(AutomationElement element, string elementId)
    {
        return new
        {
            id = elementId,
            type = SafeGetProperty(() => element.ControlType.ToString(), "Unknown"),
            name = SafeGetProperty(() => element.Name, "") ?? "",
            automationId = SafeGetProperty(() => element.AutomationId, "") ?? "",
            className = SafeGetProperty(() => element.ClassName, "") ?? "",
            bounds = GetBounds(element),
            enabled = SafeGetProperty(() => element.IsEnabled, true),
            visible = SafeGetProperty(() => !element.IsOffscreen, true)
        };
    }

    private T SafeGetProperty<T>(Func<T> getter, T defaultValue)
    {
        try
        {
            return getter() ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private object? GetBounds(AutomationElement element)
    {
        try
        {
            var rect = element.BoundingRectangle;
            return new { x = (int)rect.X, y = (int)rect.Y, width = (int)rect.Width, height = (int)rect.Height };
        }
        catch
        {
            return null;
        }
    }
}
