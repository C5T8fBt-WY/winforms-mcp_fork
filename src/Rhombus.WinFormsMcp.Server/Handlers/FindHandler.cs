using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Rhombus.WinFormsMcp.Server.Abstractions;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Interop;

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

    public override IEnumerable<string> SupportedTools => new[] { "find", "snapshot" };

    public override async Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        if (toolName == "snapshot")
            return await ExecuteSnapshotAsync(args);

        try
        {
            var at = GetStringArg(args, "at") ?? "root";

            var timeoutMs = GetIntArg(args, "timeout_ms", 10000);

            // window_handle: "0xABCD" → resolve to element ID so FindRecursive/FindSingle can use it as `at`.
            // Use a Task.Run timeout because GetElementFromHandle is a UIA COM call that can block
            // indefinitely if the target window's thread is stuck in MessageBox.Show/ShowDialog.
            var windowHandleStr = GetStringArg(args, "window_handle");
            if (windowHandleStr != null)
            {
                try
                {
                    var hval = Convert.ToInt64(windowHandleStr, 16);
                    var hwndPtr = new IntPtr(hval);
                    var elTask = Task.Run(() => Session.GetAutomation().GetElementFromHandle(hwndPtr));
                    if (elTask.Wait(TimeSpan.FromMilliseconds(Math.Min(timeoutMs, 2000))) && elTask.Result != null)
                        at = Session.CacheElement(elTask.Result);
                }
                catch { /* invalid handle or UIA unavailable; fall back to at=root */ }
            }

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

            return await Error($"Element not found within {waitMs}ms timeout. Try snapshot to see visible elements — modal dialogs may block UIA and appear with hwnd= refs instead.");
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

        // Win32 fallback: WindowFromPoint bypasses UIA entirely so it works even for
        // modal dialogs whose UI thread is blocked in ShowDialog / MessageBox.Show.
        if (found == null)
        {
            var pt = new Win32Types.POINT { x = x, y = y };
            var winHwnd = WindowInterop.WindowFromPoint(pt);
            if (winHwnd != IntPtr.Zero)
            {
                var elTask = Task.Run(() => Session.GetAutomation().GetElementFromHandle(winHwnd));
                if (elTask.Wait(TimeSpan.FromMilliseconds(2000)) && elTask.Result != null)
                    found = elTask.Result;
                else
                    // UIA unavailable (modal dialog) — return Win32 HWND info for direct use.
                    return Success(BuildWin32HwndInfo(winHwnd));
            }
        }

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

        // Search direct children first (fast path).
        var children = searchRoot.FindAllChildren();
        foreach (var child in children)
        {
            if (MatchesSelector(child, controlType, name, automationId, className))
            {
                found = child;
                break;
            }
        }

        // If not found in direct children, search recursively (up to 5 levels deep).
        // This covers window_handle searches where the target is nested below direct children.
        if (found == null)
        {
            found = FindInTree(searchRoot, controlType, name, automationId, className, 5);
        }

        if (found == null)
            return Error("Element not found matching criteria. Try snapshot to see all visible elements — modal dialogs may need click(window_handle:) instead.");

        var elementId = Session.CacheElement(found);
        return Success(BuildElementInfo(found, elementId));
    }

    private Task<JsonElement> FindRecursive(string at, JsonElement args, int maxDepth)
    {
        var automation = Session.GetAutomation();
        AutomationElement? searchRoot;

        if (at == "root")
        {
            var trackedPids = Session.GetTrackedProcessIds();
            List<AutomationElement> windows;
            List<IntPtr> failedHwnds;

            if (trackedPids.Count > 0)
                (windows, failedHwnds) = GetWindowsAndFailedHwndsForPids(automation, trackedPids);
            else
            {
                windows = GetAllWindows(automation);
                failedHwnds = new List<IntPtr>();
            }

            var windowTrees = new List<object>();
            foreach (var window in windows)
            {
                var windowId = Session.CacheElement(window);
                windowTrees.Add(BuildTree(window, windowId, maxDepth, 0));
            }

            // Add Win32 stubs for windows whose UIA timed out (e.g. ShowDialog modal dialogs).
            // These include the window's HWND and child controls so the agent can interact directly.
            foreach (var hwnd in failedHwnds)
                windowTrees.Add(BuildWin32WindowStub(hwnd, maxDepth));

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

    private static List<AutomationElement> GetWindowsForPids(AutomationHelper automation, IReadOnlySet<int> pids)
        => GetWindowsAndFailedHwndsForPids(automation, pids).resolved;

    /// <summary>
    /// Resolves HWNDs for tracked PIDs to UIA elements in parallel.
    /// Returns both the successfully resolved elements and the HWNDs that timed out
    /// (e.g. windows whose thread is blocked in ShowDialog / MessageBox.Show).
    /// </summary>
    private static (List<AutomationElement> resolved, List<IntPtr> failed) GetWindowsAndFailedHwndsForPids(
        AutomationHelper automation, IReadOnlySet<int> pids)
    {
        var hwndArray = GetVisibleHwndsForPids(pids).ToArray();

        // Resolve all in PARALLEL so blocked windows time out concurrently, not serially.
        var tasks = hwndArray.Select(h => Task.Run<AutomationElement?>(() =>
        {
            try { return automation.GetElementFromHandle(h); }
            catch { return null; }
        })).ToArray();

        Task.WhenAll(tasks).Wait(TimeSpan.FromMilliseconds(2000));

        var resolved = new List<AutomationElement>();
        var failed = new List<IntPtr>();
        for (int i = 0; i < hwndArray.Length; i++)
        {
            if (tasks[i].IsCompletedSuccessfully && tasks[i].Result != null)
                resolved.Add(tasks[i].Result!);
            else
                failed.Add(hwndArray[i]);
        }
        return (resolved, failed);
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowInterop.EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private static List<IntPtr> GetVisibleHwndsForPids(IReadOnlySet<int> pids)
    {
        var result = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pids.Contains((int)pid) && WindowInterop.IsWindowVisible(hwnd))
                result.Add(hwnd);
            return true; // continue enumeration
        }, IntPtr.Zero);
        return result;
    }

    private static List<AutomationElement> GetAllWindows(AutomationHelper automation)
    {
        // Enumerate all visible top-level HWNDs via Win32 (no UIA, never blocks).
        var hwndList = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (WindowInterop.IsWindowVisible(hwnd))
                hwndList.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        // Resolve all in parallel (same rationale as GetWindowsForPids).
        var tasks = hwndList
            .Select(h => Task.Run<AutomationElement?>(() =>
            {
                try { return automation.GetElementFromHandle(h); }
                catch { return null; }
            }))
            .ToList();

        Task.WhenAll(tasks).Wait(TimeSpan.FromMilliseconds(3000));

        return tasks
            .Where(t => t.IsCompletedSuccessfully && t.Result != null)
            .Select(t => t.Result!)
            .ToList();
    }

    private object BuildTree(AutomationElement element, string elementId, int maxDepth, int currentDepth)
    {
        // Wrap the entire element info + children build in a timeout-protected block
        // so a window whose thread is blocked (MessageBox, ShowDialog) appears as a leaf
        // with minimal info rather than hanging the whole tree walk.
        object? info = null;
        try
        {
            var infoTask = Task.Run(() => BuildElementInfo(element, elementId));
            if (infoTask.Wait(TimeSpan.FromMilliseconds(500)))
                info = infoTask.Result;
        }
        catch { }

        info ??= new { id = elementId, type = "Unknown", name = "", automationId = "", className = "", bounds = (object?)null, inaccessible = true };

        if (currentDepth >= maxDepth)
            return info;

        var childrenList = new List<object>();
        try
        {
            // Run FindAllChildren on a background thread with a per-element timeout.
            // A modal dialog (e.g. ShowDialog/MessageBox) blocks its own UI thread, so a raw
            // FindAllChildren() call on that window would time out and stall the whole tree walk.
            AutomationElement[]? children = null;
            var childTask = Task.Run(() => element.FindAllChildren());
            if (childTask.Wait(TimeSpan.FromMilliseconds(1000)))
                children = childTask.Result;

            if (children != null)
            {
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
            // className intentionally omitted: WinForms class strings (e.g. WindowsForms10.Button.app.0...)
            // are implementation noise with no semantic value for automation agents.
            bounds = GetBounds(element),
            enabled = SafeGetProperty(() => element.IsEnabled, true),
            visible = SafeGetProperty(() => !element.IsOffscreen, true)
        };
    }

    // ---------------------------------------------------------------------------
    // Snapshot: compact Playwright-style accessibility snapshot for LLM agents.
    // ---------------------------------------------------------------------------

    private async Task<JsonElement> ExecuteSnapshotAsync(JsonElement args)
    {
        try
        {
            var timeoutMs = GetIntArg(args, "timeout_ms", 10000);
            var depth = GetIntArg(args, "depth", 6);
            if (depth <= 0) depth = 6;
            var automation = Session.GetAutomation();
            var sb = new System.Text.StringBuilder();

            var windowHandleStr = GetStringArg(args, "window_handle");
            if (windowHandleStr != null)
            {
                var hwndPtr = new IntPtr(Convert.ToInt64(windowHandleStr, 16));
                var elTask = Task.Run(() => automation.GetElementFromHandle(hwndPtr));
                if (elTask.Wait(Math.Min(timeoutMs, 2000)) && elTask.Result != null)
                {
                    // UIA resolved — try snapshot; fall back to Win32 if tree is empty
                    // (happens when ShowDialog blocks the window's message loop).
                    var uiaSnapshot = BuildSnapshot(elTask.Result, 0, depth);
                    sb.Append(string.IsNullOrWhiteSpace(uiaSnapshot)
                        ? BuildWin32Snapshot(hwndPtr, 0, depth)
                        : uiaSnapshot);
                }
                else
                {
                    // UIA timed out — use Win32 directly.
                    sb.Append(BuildWin32Snapshot(hwndPtr, 0, depth));
                }
            }
            else
            {
                var trackedPids = Session.GetTrackedProcessIds();
                if (trackedPids.Count > 0)
                {
                    var (resolvedWindows, failedHwnds) = GetWindowsAndFailedHwndsForPids(automation, trackedPids);
                    foreach (var w in resolvedWindows)
                        sb.Append(BuildSnapshot(w, 0, depth));
                    // Include Win32 snapshots for modal dialogs that UIA couldn't resolve.
                    foreach (var hwnd in failedHwnds)
                        sb.Append(BuildWin32Snapshot(hwnd, 0, depth));
                }
                else
                {
                    var windows = GetAllWindows(automation);
                    foreach (var w in windows)
                        sb.Append(BuildSnapshot(w, 0, depth));
                }
            }

            return await ScopedSuccess(args, new { snapshot = sb.ToString() });
        }
        catch (Exception ex)
        {
            return await Error($"Snapshot failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Build a compact YAML-like accessibility snapshot of the given root element.
    /// Only interactive and meaningful elements are included; structural containers
    /// (Pane, Group, TitleBar, ScrollBar) are inlined unless they have a meaningful name.
    /// The 'ref' identifier uses automationId when available, else the elem_N cache key.
    /// </summary>
    private string BuildSnapshot(AutomationElement element, int indent = 0, int maxDepth = 6)
    {
        var sb = new System.Text.StringBuilder();
        BuildSnapshotNode(element, indent, maxDepth, 0, sb);
        return sb.ToString();
    }

    private static readonly HashSet<string> _snapshotInteractiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Button", "Edit", "ComboBox", "CheckBox", "RadioButton", "ListItem",
        "MenuItem", "Slider", "Spinner", "Tab", "TabItem", "Hyperlink", "Image"
    };

    private static readonly HashSet<string> _snapshotSkipTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TitleBar", "ScrollBar", "MenuBar", "SplitButton"
    };

    private void BuildSnapshotNode(AutomationElement element, int indent, int maxDepth, int depth, System.Text.StringBuilder sb, string? labelHint = null)
    {
        if (depth > maxDepth) return;

        string type = SafeGetProperty(() => element.ControlType.ToString(), "Unknown");
        string name = (SafeGetProperty(() => element.Name, "") ?? "").Trim();
        string autoId = (SafeGetProperty(() => element.AutomationId, "") ?? "").Trim();
        bool enabled = SafeGetProperty(() => element.IsEnabled, true);
        bool visible = SafeGetProperty(() => !element.IsOffscreen, true);

        if (!visible) return;
        if (_snapshotSkipTypes.Contains(type)) return;

        // Always cache the element so the ref resolves correctly in subsequent click/find calls.
        // (automationId for WinForms controls is the decimal HWND — not a cache key.)
        string ref_id = Session.CacheElement(element);
        bool isInteractive = _snapshotInteractiveTypes.Contains(type);
        bool isProgressBar = type.Equals("ProgressBar", StringComparison.OrdinalIgnoreCase);

        // For unnamed interactive elements: infer a display name from the adjacent text label
        // (Playwright-style accessible-name resolution) or fall back to the automationId.
        // This lets agents identify inputs by their visible label rather than an empty string.
        if (isInteractive && string.IsNullOrEmpty(name))
        {
            if (labelHint != null)
                name = labelHint;
            else if (!string.IsNullOrEmpty(autoId))
                name = $"({autoId})";
        }

        bool hasName = !string.IsNullOrEmpty(name);
        bool isText = type.Equals("Text", StringComparison.OrdinalIgnoreCase);
        bool isWindow = type.Equals("Window", StringComparison.OrdinalIgnoreCase);
        bool isContainer = type is "Pane" or "Group" or "Custom" or "Document" or "List";

        // For containers without a name: skip printing this node but recurse into children.
        bool printSelf = isInteractive || isProgressBar || isWindow ||
                         (isText && hasName) ||
                         (isContainer && hasName);

        if (printSelf)
        {
            var prefix = new string(' ', indent * 2);
            string typeLower = type.ToLowerInvariant();

            // Get value for inputs.
            string valuePart = "";
            if (type is "Edit" or "Spinner")
                valuePart = " value=\"" + (SafeGetProperty(() => element.Patterns.Value.PatternOrDefault?.Value, "") ?? "") + "\"";
            else if (type is "CheckBox" or "RadioButton")
                valuePart = SafeGetProperty(() => element.Patterns.Toggle.PatternOrDefault?.ToggleState.ToString(), "") == "On" ? " [checked]" : "";

            string disabledPart = !enabled ? " [disabled]" : "";
            string refPart = $" [ref={ref_id}]";

            sb.AppendLine($"{prefix}- {typeLower} \"{name}\"{refPart}{valuePart}{disabledPart}");
        }

        if (depth < maxDepth)
        {
            AutomationElement[]? children = null;
            try
            {
                var childTask = Task.Run(() => element.FindAllChildren());
                if (childTask.Wait(TimeSpan.FromMilliseconds(500)))
                    children = childTask.Result;
            }
            catch { }

            if (children != null)
            {
                // Infer display-name hints for unnamed interactive siblings from adjacent labels
                var labelHints = InferLabelHints(children);
                int childIndent = printSelf ? indent + 1 : indent;
                foreach (var child in children.Take(100))
                {
                    try
                    {
                        var hint = labelHints.Find(h => ReferenceEquals(h.el, child)).label;
                        BuildSnapshotNode(child, childIndent, maxDepth, depth + 1, sb, hint);
                    }
                    catch { }
                }
            }
        }
    }

    /// <summary>
    /// For a set of sibling elements, returns display-name hints for unnamed interactive
    /// controls by pairing each one with the nearest preceding text/label sibling
    /// (sorted by screen position). Mirrors Playwright's ARIA label inference.
    /// </summary>
    private List<(AutomationElement el, string label)> InferLabelHints(AutomationElement[] siblings)
    {
        var hints = new List<(AutomationElement el, string label)>();

        // Sort siblings by screen position (top-to-bottom, left-to-right)
        var ordered = siblings.Select(el =>
        {
            double y = 0, x = 0;
            try { var b = el.BoundingRectangle; y = b.Y; x = b.X; } catch { }
            return (el, y, x);
        }).OrderBy(t => t.y).ThenBy(t => t.x).ToList();

        string? pendingLabel = null;
        foreach (var (el, _, _) in ordered)
        {
            string etype = SafeGetProperty(() => el.ControlType.ToString(), "Unknown");
            string ename = (SafeGetProperty(() => el.Name, "") ?? "").Trim();

            if (etype.Equals("Text", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ename))
            {
                pendingLabel = ename;
            }
            else if (_snapshotInteractiveTypes.Contains(etype))
            {
                if (string.IsNullOrEmpty(ename) && pendingLabel != null)
                    hints.Add((el, pendingLabel));
                pendingLabel = null; // consume label after any interactive element
            }
        }

        return hints;
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

    // ── Win32 fallback helpers ───────────────────────────────────────────────────
    // Used when UIA times out for ShowDialog / MessageBox modal windows.
    // Returns HWND-based info so the agent can still interact via type/click/app(close).

    private static string GetHwndClassName(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        WindowInterop.GetClassName(hwnd, sb, 256);
        return sb.ToString();
    }

    private static string GetHwndText(IntPtr hwnd)
    {
        var len = WindowInterop.GetWindowTextLength(hwnd);
        if (len == 0) return "";
        var sb = new System.Text.StringBuilder(len + 1);
        WindowInterop.GetWindowText(hwnd, sb, len + 1);
        return sb.ToString();
    }

    /// <summary>
    /// Maps Win32 / WinForms class names to semantic type labels for agent readability.
    /// </summary>
    private static string MapClassNameToType(string className)
    {
        if (className.StartsWith("WindowsForms10.Edit", StringComparison.OrdinalIgnoreCase) || className.Equals("Edit", StringComparison.OrdinalIgnoreCase)) return "edit";
        if (className.StartsWith("WindowsForms10.Button", StringComparison.OrdinalIgnoreCase) || className.Equals("Button", StringComparison.OrdinalIgnoreCase)) return "button";
        if (className.StartsWith("WindowsForms10.ComboBox", StringComparison.OrdinalIgnoreCase) || className.Equals("ComboBox", StringComparison.OrdinalIgnoreCase)) return "combobox";
        if (className.StartsWith("WindowsForms10.ListBox", StringComparison.OrdinalIgnoreCase) || className.Equals("ListBox", StringComparison.OrdinalIgnoreCase)) return "list";
        if (className.StartsWith("WindowsForms10.ScrollBar", StringComparison.OrdinalIgnoreCase) || className.Equals("ScrollBar", StringComparison.OrdinalIgnoreCase)) return "scrollbar";
        if (className.Equals("Static", StringComparison.OrdinalIgnoreCase)) return "text";
        if (className.StartsWith("WindowsForms10.Window", StringComparison.OrdinalIgnoreCase)) return "pane";
        return "window";
    }

    /// <summary>
    /// Returns only the DIRECT child HWNDs of <paramref name="parent"/>.
    /// EnumChildWindows enumerates all descendants; this filter limits to depth=1.
    /// </summary>
    private static List<IntPtr> EnumDirectChildHwnds(IntPtr parent)
    {
        var result = new List<IntPtr>();
        WindowInterop.EnumChildWindows(parent, (hwnd, _) =>
        {
            if (WindowInterop.GetParent(hwnd) == parent)
                result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>
    /// Returns Win32-based element info for <see cref="FindAtPoint"/> when UIA is unavailable.
    /// The agent can use the returned <c>hwnd</c> with <c>type</c>, <c>click</c>, <c>app(close)</c>.
    /// </summary>
    private static object BuildWin32HwndInfo(IntPtr hwnd)
    {
        var className = GetHwndClassName(hwnd);
        var windowText = GetHwndText(hwnd);
        WindowInterop.GetWindowRect(hwnd, out var rect);
        var hwndStr = $"0x{hwnd.ToInt64():X8}";
        return new
        {
            hwnd = hwndStr,
            type = MapClassNameToType(className),
            className,
            name = windowText,
            bounds = new { x = rect.left, y = rect.top, width = rect.Width, height = rect.Height },
            win32_fallback = true,
            note = $"UIA unavailable (ShowDialog window). Interact via hwnd: type(target:\"{hwndStr}\"), app(action:\"close\", handle:\"{hwndStr}\")"
        };
    }

    /// <summary>
    /// Builds a Win32 window stub (with recursive children) for <see cref="FindRecursive"/>
    /// when UIA resolution times out for a modal dialog.
    /// </summary>
    private static object BuildWin32WindowStub(IntPtr hwnd, int maxDepth)
        => BuildWin32TreeNode(hwnd, 0, maxDepth);

    private static object BuildWin32TreeNode(IntPtr hwnd, int depth, int maxDepth)
    {
        var className = GetHwndClassName(hwnd);
        var windowText = GetHwndText(hwnd);
        WindowInterop.GetWindowRect(hwnd, out var rect);
        var hwndStr = $"0x{hwnd.ToInt64():X8}";
        var bounds = new { x = rect.left, y = rect.top, width = rect.Width, height = rect.Height };

        if (depth >= maxDepth)
            return new { hwnd = hwndStr, type = MapClassNameToType(className), className, name = windowText, bounds, win32_fallback = true };

        var children = EnumDirectChildHwnds(hwnd)
            .Where(WindowInterop.IsWindowVisible)
            .Select(h => BuildWin32TreeNode(h, depth + 1, maxDepth))
            .ToList();

        return new { hwnd = hwndStr, type = MapClassNameToType(className), className, name = windowText, bounds, win32_fallback = true, children };
    }

    /// <summary>
    /// Builds a compact snapshot string from Win32 APIs, matching the UIA snapshot format.
    /// Used when <c>snapshot(window_handle:...)</c> returns empty because UIA is blocked.
    /// </summary>
    private static string BuildWin32Snapshot(IntPtr hwnd, int indent, int maxDepth)
    {
        var sb = new System.Text.StringBuilder();
        BuildWin32SnapshotNode(hwnd, indent, maxDepth, 0, sb);
        return sb.ToString();
    }

    private static void BuildWin32SnapshotNode(IntPtr hwnd, int indent, int maxDepth, int depth, System.Text.StringBuilder sb)
    {
        if (!WindowInterop.IsWindowVisible(hwnd)) return;
        if (depth > maxDepth) return;

        var className = GetHwndClassName(hwnd);
        var type = MapClassNameToType(className);
        if (type == "scrollbar") return; // skip scrollbar noise

        var windowText = GetHwndText(hwnd);
        var hwndStr = $"0x{hwnd.ToInt64():X8}";
        var prefix = new string(' ', indent * 2);

        sb.AppendLine($"{prefix}- {type} \"{windowText}\" [hwnd={hwndStr}]");

        if (depth < maxDepth)
        {
            foreach (var child in EnumDirectChildHwnds(hwnd).Where(WindowInterop.IsWindowVisible))
                BuildWin32SnapshotNode(child, indent + 1, maxDepth, depth + 1, sb);
        }
    }
}
