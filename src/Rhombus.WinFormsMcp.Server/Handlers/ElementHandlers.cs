using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Handles element interaction tools: find_element, click_element, type_text, set_value, get_property,
/// click_by_automation_id, list_elements, find_element_near_anchor.
/// </summary>
internal class ElementHandlers : HandlerBase
{
    public ElementHandlers(SessionManager session, WindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[]
    {
        "find_element",
        "click_element",
        "type_text",
        "set_value",
        "get_property",
        "click_by_automation_id",
        "list_elements",
        "find_element_near_anchor"
    };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        return toolName switch
        {
            "find_element" => FindElement(args),
            "click_element" => ClickElement(args),
            "type_text" => TypeText(args),
            "set_value" => SetValue(args),
            "get_property" => GetProperty(args),
            "click_by_automation_id" => ClickByAutomationId(args),
            "list_elements" => ListElements(args),
            "find_element_near_anchor" => FindElementNearAnchor(args),
            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };
    }

    private Task<JsonElement> FindElement(JsonElement args)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var automation = Session.GetAutomation();
            var pid = GetIntArg(args, "pid");
            var automationId = GetStringArg(args, "automationId");
            var automationIdPattern = GetStringArg(args, "automationIdPattern");
            var name = GetStringArg(args, "name");
            var namePattern = GetStringArg(args, "namePattern");
            var className = GetStringArg(args, "className");

            AutomationElement? element = null;
            string? matchedBy = null;

            // Exact match first (faster)
            if (!string.IsNullOrEmpty(automationId))
            {
                element = automation.FindByAutomationId(automationId);
                matchedBy = "automationId";
            }
            else if (!string.IsNullOrEmpty(name))
            {
                element = automation.FindByName(name);
                matchedBy = "name";
            }
            else if (!string.IsNullOrEmpty(className))
            {
                element = automation.FindByClassName(className);
                matchedBy = "className";
            }
            // Pattern matching (slower - searches all elements)
            else if (!string.IsNullOrEmpty(automationIdPattern))
            {
                try
                {
                    var regex = new Regex(automationIdPattern, RegexOptions.Compiled, TimeSpan.FromSeconds(1));
                    element = FindElementByPattern(automation, e =>
                        !string.IsNullOrEmpty(e.AutomationId) && regex.IsMatch(e.AutomationId));
                    matchedBy = "automationIdPattern";
                }
                catch (RegexParseException ex)
                {
                    stopwatch.Stop();
                    return Error($"Invalid regex pattern: {ex.Message}", ("execution_time_ms", stopwatch.ElapsedMilliseconds));
                }
            }
            else if (!string.IsNullOrEmpty(namePattern))
            {
                try
                {
                    var regex = new Regex(namePattern, RegexOptions.Compiled, TimeSpan.FromSeconds(1));
                    element = FindElementByPattern(automation, e =>
                        !string.IsNullOrEmpty(e.Name) && regex.IsMatch(e.Name));
                    matchedBy = "namePattern";
                }
                catch (RegexParseException ex)
                {
                    stopwatch.Stop();
                    return Error($"Invalid regex pattern: {ex.Message}", ("execution_time_ms", stopwatch.ElapsedMilliseconds));
                }
            }

            stopwatch.Stop();

            if (element == null)
                return Error("Element not found", ("execution_time_ms", stopwatch.ElapsedMilliseconds));

            var elementId = Session.CacheElement(element);
            return Success(
                ("elementId", elementId),
                ("name", element.Name ?? ""),
                ("automationId", element.AutomationId ?? ""),
                ("controlType", element.ControlType.ToString()),
                ("matched_by", matchedBy),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> ClickElement(JsonElement args)
    {
        try
        {
            // Accept either elementId or elementPath (LLMs use both)
            var elementId = GetStringArg(args, "elementId") ?? GetStringArg(args, "elementPath")
                ?? throw new ArgumentException("elementId or elementPath is required");
            var doubleClick = GetBoolArg(args, "doubleClick", false);

            var element = Session.GetElement(elementId);
            if (element == null)
                return Error("Element not found in session");

            var automation = Session.GetAutomation();
            automation.Click(element, doubleClick);

            return Success(("message", "Element clicked"));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> TypeText(JsonElement args)
    {
        try
        {
            // Accept either elementId or elementPath (LLMs use both)
            var elementId = GetStringArg(args, "elementId") ?? GetStringArg(args, "elementPath")
                ?? throw new ArgumentException("elementId or elementPath is required");
            var text = GetStringArg(args, "text") ?? "";
            var clearFirst = GetBoolArg(args, "clearFirst", false);

            var element = Session.GetElement(elementId);
            if (element == null)
                return Error("Element not found in session");

            var automation = Session.GetAutomation();
            automation.TypeText(element, text, clearFirst);

            return Success(("message", "Text typed"));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> SetValue(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var value = GetStringArg(args, "value") ?? "";
            var selectAllDelayMs = GetIntArg(args, "selectAllDelayMs", 50);

            var element = Session.GetElement(elementId);
            if (element == null)
                return Error("Element not found in session");

            var automation = Session.GetAutomation();
            automation.SetValue(element, value, selectAllDelayMs);

            return Success(("message", "Value set"));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> GetProperty(JsonElement args)
    {
        try
        {
            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");
            var propertyName = GetStringArg(args, "propertyName") ?? "";

            var element = Session.GetElement(elementId);
            if (element == null)
                return Error("Element not found in session");

            var automation = Session.GetAutomation();
            var value = automation.GetProperty(element, propertyName);

            return Success(
                ("propertyName", propertyName),
                ("value", value?.ToString()));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> ClickByAutomationId(JsonElement args)
    {
        try
        {
            var automationId = GetStringArg(args, "automationId") ?? throw new ArgumentException("automationId is required");
            var windowTitle = GetStringArg(args, "windowTitle");
            var doubleClick = GetBoolArg(args, "doubleClick", false);

            var automation = Session.GetAutomation();

            // If window title provided, search within that window
            AutomationElement? parent = null;
            if (!string.IsNullOrEmpty(windowTitle))
            {
                parent = automation.GetWindowByTitle(windowTitle);
                if (parent == null)
                    return Error($"Window not found: {windowTitle}");
            }

            var element = automation.FindByAutomationId(automationId, parent);
            if (element == null)
                return Error($"Element not found: {automationId}");

            automation.Click(element, doubleClick);

            return Success(("message", $"Clicked element: {automationId}"));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> ListElements(JsonElement args)
    {
        try
        {
            var windowTitle = GetStringArg(args, "windowTitle") ?? throw new ArgumentException("windowTitle is required");
            var maxDepth = GetIntArg(args, "maxDepth", 3);

            var automation = Session.GetAutomation();
            var window = automation.GetWindowByTitle(windowTitle);

            if (window == null)
                return Error($"Window not found: {windowTitle}");

            var elements = automation.GetElementTree(window, maxDepth);

            return Success(("elementCount", elements.Count), ("elements", elements));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> FindElementNearAnchor(JsonElement args)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var automation = Session.GetAutomation();

            // Find anchor element
            AutomationElement? anchor = null;
            var anchorElementId = GetStringArg(args, "anchorElementId");
            var anchorAutomationId = GetStringArg(args, "anchorAutomationId");
            var anchorName = GetStringArg(args, "anchorName");

            if (!string.IsNullOrEmpty(anchorElementId))
            {
                anchor = Session.GetElement(anchorElementId);
            }
            else if (!string.IsNullOrEmpty(anchorAutomationId))
            {
                anchor = automation.FindByAutomationId(anchorAutomationId);
            }
            else if (!string.IsNullOrEmpty(anchorName))
            {
                anchor = automation.FindByName(anchorName);
            }

            if (anchor == null)
            {
                stopwatch.Stop();
                return Error("Anchor element not found", ("execution_time_ms", stopwatch.ElapsedMilliseconds));
            }

            // Get search parameters
            var targetControlType = GetStringArg(args, "targetControlType");
            var targetNamePattern = GetStringArg(args, "targetNamePattern");
            var targetAutomationIdPattern = GetStringArg(args, "targetAutomationIdPattern");
            var searchDirection = GetStringArg(args, "searchDirection") ?? "siblings";
            var maxDistance = GetIntArg(args, "maxDistance", 10);

            // Build predicate for matching
            Regex? nameRegex = null;
            Regex? automationIdRegex = null;

            if (!string.IsNullOrEmpty(targetNamePattern))
            {
                try
                {
                    nameRegex = new Regex(targetNamePattern, RegexOptions.Compiled, TimeSpan.FromSeconds(1));
                }
                catch (RegexParseException ex)
                {
                    stopwatch.Stop();
                    return Error($"Invalid name pattern: {ex.Message}", ("execution_time_ms", stopwatch.ElapsedMilliseconds));
                }
            }

            if (!string.IsNullOrEmpty(targetAutomationIdPattern))
            {
                try
                {
                    automationIdRegex = new Regex(targetAutomationIdPattern, RegexOptions.Compiled, TimeSpan.FromSeconds(1));
                }
                catch (RegexParseException ex)
                {
                    stopwatch.Stop();
                    return Error($"Invalid automationId pattern: {ex.Message}", ("execution_time_ms", stopwatch.ElapsedMilliseconds));
                }
            }

            Func<AutomationElement, bool> predicate = e =>
            {
                if (!string.IsNullOrEmpty(targetControlType) &&
                    !e.ControlType.ToString().Equals(targetControlType, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (nameRegex != null && (string.IsNullOrEmpty(e.Name) || !nameRegex.IsMatch(e.Name)))
                    return false;

                if (automationIdRegex != null && (string.IsNullOrEmpty(e.AutomationId) || !automationIdRegex.IsMatch(e.AutomationId)))
                    return false;

                return true;
            };

            // Search for element
            AutomationElement? found = null;
            int searchedCount = 0;
            var candidates = new List<AutomationElement>();

            try
            {
                switch (searchDirection.ToLowerInvariant())
                {
                    case "children":
                        candidates.AddRange(anchor.FindAllChildren().Take(maxDistance));
                        break;

                    case "parent_children":
                        var parent = anchor.Parent;
                        if (parent != null)
                        {
                            candidates.AddRange(parent.FindAllChildren().Take(maxDistance));
                        }
                        break;

                    case "siblings":
                    default:
                        // Get parent's children (siblings)
                        var siblingParent = anchor.Parent;
                        if (siblingParent != null)
                        {
                            foreach (var sibling in siblingParent.FindAllChildren().Take(maxDistance))
                            {
                                // Skip the anchor itself
                                if (sibling.Equals(anchor)) continue;
                                candidates.Add(sibling);
                            }
                        }
                        break;
                }

                // Find matching element
                foreach (var candidate in candidates)
                {
                    searchedCount++;
                    if (predicate(candidate))
                    {
                        found = candidate;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return Error($"Search failed: {ex.Message}", ("execution_time_ms", stopwatch.ElapsedMilliseconds));
            }

            stopwatch.Stop();

            if (found == null)
            {
                return Error("No matching element found near anchor",
                    ("searched_count", searchedCount),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds));
            }

            var elementId = Session.CacheElement(found);
            return Success(
                ("elementId", elementId),
                ("name", found.Name ?? ""),
                ("automationId", found.AutomationId ?? ""),
                ("controlType", found.ControlType.ToString()),
                ("searched_count", searchedCount),
                ("search_direction", searchDirection),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    #region Helper Methods

    /// <summary>
    /// Find element by predicate - searches desktop tree (limited depth for performance)
    /// </summary>
    private AutomationElement? FindElementByPattern(AutomationHelper automation, Func<AutomationElement, bool> predicate, int maxDepth = 5)
    {
        var desktop = automation.GetDesktop();
        if (desktop == null) return null;

        return SearchTreeForElement(desktop, predicate, 0, maxDepth);
    }

    private AutomationElement? SearchTreeForElement(AutomationElement parent, Func<AutomationElement, bool> predicate, int depth, int maxDepth)
    {
        if (depth > maxDepth) return null;

        try
        {
            // Check children
            var children = parent.FindAllChildren();
            foreach (var child in children)
            {
                // Check if this element matches
                if (predicate(child))
                    return child;

                // Recursively search children
                var found = SearchTreeForElement(child, predicate, depth + 1, maxDepth);
                if (found != null)
                    return found;
            }
        }
        catch
        {
            // Element may have become invalid during search
        }

        return null;
    }

    #endregion
}
