using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Helper class for WinForms UI automation using FlaUI with UIA2 backend
/// </summary>
public class AutomationHelper : IAutomationHelper
{
    private UIA2Automation? _automation;
    private readonly Dictionary<string, Process> _launchedProcesses = new();
    private readonly object _lock = new object();

    public AutomationHelper()
    {
        _automation = new UIA2Automation();
    }

    /// <summary>
    /// Launch a WinForms application
    /// </summary>
    /// <param name="path">Path to the executable</param>
    /// <param name="arguments">Command line arguments</param>
    /// <param name="workingDirectory">Working directory for the process</param>
    /// <param name="idleTimeoutMs">Timeout in milliseconds to wait for the app to become idle (default: 5000)</param>
    public Process LaunchApp(string path, string? arguments = null, string? workingDirectory = null, int idleTimeoutMs = 5000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false
        };

        var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to launch {path}");
        process.WaitForInputIdle(idleTimeoutMs);

        lock (_lock)
        {
            _launchedProcesses[process.Id.ToString()] = process;
        }

        return process;
    }

    /// <summary>
    /// Attach to a running process
    /// </summary>
    public Process AttachToProcess(int pid)
    {
        var process = Process.GetProcessById(pid);
        lock (_lock)
        {
            _launchedProcesses[pid.ToString()] = process;
        }
        return process;
    }

    /// <summary>
    /// Attach to a running process by name
    /// </summary>
    public Process AttachToProcessByName(string name)
    {
        var processes = Process.GetProcessesByName(name);
        if (processes.Length == 0)
            throw new InvalidOperationException($"No process found with name: {name}");

        var process = processes[0];
        lock (_lock)
        {
            _launchedProcesses[process.Id.ToString()] = process;
        }
        return process;
    }

    /// <summary>
    /// Get main window element of a process
    /// </summary>
    public AutomationElement? GetMainWindow(int pid)
    {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        try
        {
            var process = Process.GetProcessById(pid);
            return _automation.FromHandle(process.MainWindowHandle);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Find element by AutomationId
    /// </summary>
    /// <param name="automationId">The AutomationId to search for</param>
    /// <param name="parent">Parent element to search from (null for desktop)</param>
    /// <param name="timeoutMs">Total timeout for the search (default: 5000)</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    public AutomationElement? FindByAutomationId(string automationId, AutomationElement? parent = null, int timeoutMs = 5000, int pollIntervalMs = 100)
    {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var condition = new PropertyCondition(_automation.PropertyLibrary.Element.AutomationId, automationId);
        return FindElement(condition, parent, timeoutMs, pollIntervalMs);
    }

    /// <summary>
    /// Find element by Name
    /// </summary>
    /// <param name="name">The Name to search for</param>
    /// <param name="parent">Parent element to search from (null for desktop)</param>
    /// <param name="timeoutMs">Total timeout for the search (default: 5000)</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    public AutomationElement? FindByName(string name, AutomationElement? parent = null, int timeoutMs = 5000, int pollIntervalMs = 100)
    {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var condition = new PropertyCondition(_automation.PropertyLibrary.Element.Name, name);
        return FindElement(condition, parent, timeoutMs, pollIntervalMs);
    }

    /// <summary>
    /// Find element by ClassName
    /// </summary>
    /// <param name="className">The ClassName to search for</param>
    /// <param name="parent">Parent element to search from (null for desktop)</param>
    /// <param name="timeoutMs">Total timeout for the search (default: 5000)</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    public AutomationElement? FindByClassName(string className, AutomationElement? parent = null, int timeoutMs = 5000, int pollIntervalMs = 100)
    {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var condition = new PropertyCondition(_automation.PropertyLibrary.Element.ClassName, className);
        return FindElement(condition, parent, timeoutMs, pollIntervalMs);
    }

    /// <summary>
    /// Find element by ControlType
    /// </summary>
    /// <param name="controlType">The ControlType to search for</param>
    /// <param name="parent">Parent element to search from (null for desktop)</param>
    /// <param name="timeoutMs">Total timeout for the search (default: 5000)</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    public AutomationElement? FindByControlType(ControlType controlType, AutomationElement? parent = null, int timeoutMs = 5000, int pollIntervalMs = 100)
    {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var condition = new PropertyCondition(_automation.PropertyLibrary.Element.ControlType, controlType);
        return FindElement(condition, parent, timeoutMs, pollIntervalMs);
    }

    /// <summary>
    /// Find multiple elements matching condition
    /// </summary>
    /// <param name="condition">The condition to match</param>
    /// <param name="parent">Parent element to search from (null for desktop)</param>
    /// <param name="timeoutMs">Total timeout for the search (default: 5000)</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    public AutomationElement[]? FindAll(ConditionBase condition, AutomationElement? parent = null, int timeoutMs = 5000, int pollIntervalMs = 100)
    {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var root = parent ?? _automation.GetDesktop();
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                var elements = root.FindAllChildren(condition);
                if (elements.Length > 0)
                    return elements;
            }
            catch { }

            Thread.Sleep(pollIntervalMs);
        }

        return null;
    }

    /// <summary>
    /// Find element with retry/timeout - searches entire descendant tree
    /// </summary>
    /// <param name="condition">The condition to match</param>
    /// <param name="parent">Parent element to search from (null for desktop)</param>
    /// <param name="timeoutMs">Total timeout for the search</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    private AutomationElement? FindElement(ConditionBase condition, AutomationElement? parent, int timeoutMs, int pollIntervalMs = 100)
    {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var root = parent ?? _automation.GetDesktop();
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                // Use FindFirstDescendant to search the entire tree, not just immediate children
                var element = root.FindFirstDescendant(condition);
                if (element != null)
                    return element;
            }
            catch { }

            Thread.Sleep(pollIntervalMs);
        }

        return null;
    }

    /// <summary>
    /// Get element tree for debugging - returns list of elements with their properties
    /// </summary>
    public List<Dictionary<string, string>> GetElementTree(AutomationElement? parent = null, int maxDepth = 3)
    {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var root = parent ?? _automation.GetDesktop();
        var result = new List<Dictionary<string, string>>();
        CollectElements(root, result, 0, maxDepth);
        return result;
    }

    private void CollectElements(AutomationElement element, List<Dictionary<string, string>> result, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;

        try
        {
            var info = new Dictionary<string, string>
            {
                ["depth"] = depth.ToString(),
                ["name"] = element.Name ?? "",
                ["automationId"] = element.AutomationId ?? "",
                ["className"] = element.ClassName ?? "",
                ["controlType"] = element.ControlType.ToString()
            };

            // Include bounding rectangle for clickable elements
            try
            {
                var bounds = element.BoundingRectangle;
                info["bounds"] = $"{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}";
            }
            catch { }

            result.Add(info);

            // Recurse into children
            var children = element.FindAllChildren();
            foreach (var child in children)
            {
                CollectElements(child, result, depth + 1, maxDepth);
            }
        }
        catch { }
    }

    /// <summary>
    /// Find element by AutomationId and click it in one operation
    /// </summary>
    public bool ClickByAutomationId(string automationId, bool doubleClick = false, int timeoutMs = 5000)
    {
        var element = FindByAutomationId(automationId, null, timeoutMs);
        if (element == null)
            return false;

        Click(element, doubleClick);
        return true;
    }

    /// <summary>
    /// Get window by title (partial match) as automation element
    /// </summary>
    /// <param name="titleContains">Substring to search for in window title</param>
    /// <param name="timeoutMs">Total timeout for the search (default: 5000)</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    public AutomationElement? GetWindowByTitle(string titleContains, int timeoutMs = 5000, int pollIntervalMs = 100)
    {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                var desktop = _automation.GetDesktop();
                var windows = desktop.FindAllChildren();
                foreach (var window in windows)
                {
                    if (window.Name?.Contains(titleContains, StringComparison.OrdinalIgnoreCase) == true)
                        return window;
                }
            }
            catch { }

            Thread.Sleep(pollIntervalMs);
        }

        return null;
    }

    /// <summary>
    /// Get the desktop root element.
    /// </summary>
    public AutomationElement GetDesktop()
    {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        return _automation.GetDesktop();
    }

    /// <summary>
    /// Check if element exists
    /// </summary>
    public bool ElementExists(string automationId, AutomationElement? parent = null)
    {
        return FindByAutomationId(automationId, parent, 1000) != null;
    }

    /// <summary>
    /// Click element
    /// </summary>
    public void Click(AutomationElement element, bool doubleClick = false)
    {
        if (doubleClick)
        {
            element.DoubleClick();
        }
        else
        {
            element.Click();
        }
    }

    /// <summary>
    /// Type text into element
    /// </summary>
    /// <param name="element">The element to type into</param>
    /// <param name="text">The text to type</param>
    /// <param name="clearFirst">Whether to select all and clear existing text first</param>
    /// <param name="clearDelayMs">Delay after select-all before typing new text (default: 100)</param>
    public void TypeText(AutomationElement element, string text, bool clearFirst = false, int clearDelayMs = 100)
    {
        element.Focus();

        if (clearFirst)
        {
            System.Windows.Forms.SendKeys.SendWait("^a");
            Thread.Sleep(clearDelayMs);
        }

        System.Windows.Forms.SendKeys.SendWait(text);
    }

    /// <summary>
    /// Set value on element
    /// </summary>
    /// <param name="element">The element to set value on</param>
    /// <param name="value">The value to set</param>
    /// <param name="selectAllDelayMs">Delay after select-all before typing new value (default: 50)</param>
    public void SetValue(AutomationElement element, string value, int selectAllDelayMs = 50)
    {
        element.Focus();
        System.Windows.Forms.SendKeys.SendWait("^a");
        Thread.Sleep(selectAllDelayMs);
        System.Windows.Forms.SendKeys.SendWait(value);
    }

    /// <summary>
    /// Get element property
    /// </summary>
    public object? GetProperty(AutomationElement element, string propertyName)
    {
        return propertyName.ToLower() switch
        {
            "name" => element.Name,
            "automationid" => element.AutomationId,
            "classname" => element.ClassName,
            "controltype" => element.ControlType.ToString(),
            "isoffscreen" => element.IsOffscreen,
            "isenabled" => element.IsEnabled,
            _ => null
        };
    }

    /// <summary>
    /// Take screenshot of element or full desktop
    /// </summary>
    public void TakeScreenshot(string outputPath, AutomationElement? element = null)
    {
        try
        {
            Bitmap? bitmap = null;

            if (element != null)
            {
                bitmap = element.Capture();
            }
            else if (_automation != null)
            {
                var desktop = _automation.GetDesktop();
                bitmap = desktop.Capture();
            }

            if (bitmap != null)
            {
                bitmap.Save(outputPath, ImageFormat.Png);
                bitmap.Dispose();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to take screenshot: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Take screenshot of a specific screen region (window bounds)
    /// </summary>
    /// <param name="outputPath">Path to save the screenshot</param>
    /// <param name="x">Left edge of region</param>
    /// <param name="y">Top edge of region</param>
    /// <param name="width">Width of region</param>
    /// <param name="height">Height of region</param>
    public void TakeRegionScreenshot(string outputPath, int x, int y, int width, int height)
    {
        try
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Invalid region dimensions");

            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height));
            bitmap.Save(outputPath, ImageFormat.Png);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to take region screenshot: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Drag and drop
    /// </summary>
    /// <param name="source">The source element to drag from</param>
    /// <param name="target">The target element to drop onto</param>
    /// <param name="dragSetupDelayMs">Delay after positioning cursor before starting drag (default: 100)</param>
    /// <param name="dropDelayMs">Delay before releasing mouse button after drag (default: 200)</param>
    public void DragDrop(AutomationElement source, AutomationElement target, int dragSetupDelayMs = 100, int dropDelayMs = 200)
    {
        var sourceBounds = source.BoundingRectangle;
        var targetBounds = target.BoundingRectangle;

        if (sourceBounds.Width == 0 || targetBounds.Width == 0)
            throw new InvalidOperationException("Source or target element has invalid bounding rectangle");

        // Simulate drag-drop using mouse movements
        var sourceCenter = new Point(
            (int)(sourceBounds.X + sourceBounds.Width / 2),
            (int)(sourceBounds.Y + sourceBounds.Height / 2)
        );

        var targetCenter = new Point(
            (int)(targetBounds.X + targetBounds.Width / 2),
            (int)(targetBounds.Y + targetBounds.Height / 2)
        );

        source.Focus();
        System.Windows.Forms.Cursor.Position = sourceCenter;
        Thread.Sleep(dragSetupDelayMs);

        // Simulate mouse down, move, mouse up
        System.Windows.Forms.SendKeys.SendWait("{LDown}");
        System.Windows.Forms.Cursor.Position = targetCenter;
        Thread.Sleep(dropDelayMs);
        System.Windows.Forms.SendKeys.SendWait("{LUp}");
    }

    /// <summary>
    /// Send keyboard keys
    /// </summary>
    public void SendKeys(string keys)
    {
        System.Windows.Forms.SendKeys.SendWait(keys);
    }

    /// <summary>
    /// Close application
    /// </summary>
    /// <param name="pid">Process ID of the application to close</param>
    /// <param name="force">If true, immediately kills the process; if false, requests graceful close first</param>
    /// <param name="closeTimeoutMs">Timeout to wait for graceful close before force killing (default: 5000)</param>
    public void CloseApp(int pid, bool force = false, int closeTimeoutMs = 5000)
    {
        lock (_lock)
        {
            if (_launchedProcesses.TryGetValue(pid.ToString(), out var process))
            {
                try
                {
                    if (force)
                    {
                        process.Kill();
                    }
                    else
                    {
                        process.CloseMainWindow();
                        process.WaitForExit(closeTimeoutMs);
                        if (!process.HasExited)
                            process.Kill();
                    }
                }
                catch { }
                finally
                {
                    _launchedProcesses.Remove(pid.ToString());
                }
            }
        }
    }

    /// <summary>
    /// Wait for element to appear
    /// </summary>
    /// <param name="automationId">The AutomationId to wait for</param>
    /// <param name="parent">Parent element to search from (null for desktop)</param>
    /// <param name="timeoutMs">Total timeout to wait for the element (default: 10000)</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    public async Task<bool> WaitForElementAsync(string automationId, AutomationElement? parent = null, int timeoutMs = 10000, int pollIntervalMs = 100)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (FindByAutomationId(automationId, parent, 500, pollIntervalMs) != null)
                return true;

            await Task.Delay(pollIntervalMs);
        }

        return false;
    }

    /// <summary>
    /// Get all child elements
    /// </summary>
    public AutomationElement[]? GetAllChildren(AutomationElement element)
    {
        try
        {
            return element.FindAllChildren();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Build XML UI tree using TreeBuilder
    /// </summary>
    public TreeBuildResult BuildUiTree(AutomationElement? root = null, TreeBuilderOptions? options = null)
    {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var actualRoot = root ?? _automation.GetDesktop();
        var builder = new TreeBuilder(options);
        return builder.BuildTree(actualRoot);
    }

    /// <summary>
    /// Expand or collapse an element that supports ExpandCollapse pattern
    /// </summary>
    /// <param name="element">The element to expand or collapse</param>
    /// <param name="expand">True to expand, false to collapse</param>
    /// <param name="uiUpdateDelayMs">Delay to wait for UI to update after action (default: 100)</param>
    public ExpandCollapseResult ExpandCollapse(AutomationElement element, bool expand, int uiUpdateDelayMs = 100)
    {
        try
        {
            var patterns = element.Patterns;
            var expandCollapsePattern = patterns.ExpandCollapse.PatternOrDefault;

            if (expandCollapsePattern == null)
            {
                return new ExpandCollapseResult
                {
                    Success = false,
                    ErrorMessage = "Element does not support ExpandCollapse pattern",
                    CurrentState = null
                };
            }

            var previousState = expandCollapsePattern.ExpandCollapseState;

            if (expand)
            {
                expandCollapsePattern.Expand();
            }
            else
            {
                expandCollapsePattern.Collapse();
            }

            // Wait a moment for UI to update
            Thread.Sleep(uiUpdateDelayMs);

            var newState = expandCollapsePattern.ExpandCollapseState;

            return new ExpandCollapseResult
            {
                Success = true,
                PreviousState = previousState.ToString(),
                CurrentState = newState.ToString()
            };
        }
        catch (Exception ex)
        {
            return new ExpandCollapseResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                CurrentState = null
            };
        }
    }

    /// <summary>
    /// Get the expand/collapse state of an element
    /// </summary>
    public string? GetExpandCollapseState(AutomationElement element)
    {
        try
        {
            var pattern = element.Patterns.ExpandCollapse.PatternOrDefault;
            return pattern?.ExpandCollapseState.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Scroll an element that supports ScrollPattern
    /// </summary>
    /// <param name="element">The element to scroll</param>
    /// <param name="direction">Direction to scroll</param>
    /// <param name="amount">Amount to scroll</param>
    /// <param name="uiUpdateDelayMs">Delay to wait for UI to update after scrolling (default: 100)</param>
    public ScrollResult Scroll(AutomationElement element, ScrollDirection direction, ScrollAmount amount, int uiUpdateDelayMs = 100)
    {
        try
        {
            var pattern = element.Patterns.Scroll.PatternOrDefault;

            if (pattern == null)
            {
                return new ScrollResult
                {
                    Success = false,
                    ErrorMessage = "Element does not support Scroll pattern"
                };
            }

            // Get scroll info before
            var horizontalBefore = pattern.HorizontalScrollPercent;
            var verticalBefore = pattern.VerticalScrollPercent;

            // Perform scroll
            switch (direction)
            {
                case ScrollDirection.Up:
                    pattern.Scroll(FlaUI.Core.Definitions.ScrollAmount.NoAmount, ToFlaUIAmount(amount));
                    break;
                case ScrollDirection.Down:
                    pattern.Scroll(FlaUI.Core.Definitions.ScrollAmount.NoAmount, ToFlaUIAmountReverse(amount));
                    break;
                case ScrollDirection.Left:
                    pattern.Scroll(ToFlaUIAmount(amount), FlaUI.Core.Definitions.ScrollAmount.NoAmount);
                    break;
                case ScrollDirection.Right:
                    pattern.Scroll(ToFlaUIAmountReverse(amount), FlaUI.Core.Definitions.ScrollAmount.NoAmount);
                    break;
            }

            // Wait for UI update
            Thread.Sleep(uiUpdateDelayMs);

            // Get scroll info after
            var horizontalAfter = pattern.HorizontalScrollPercent;
            var verticalAfter = pattern.VerticalScrollPercent;

            return new ScrollResult
            {
                Success = true,
                HorizontalScrollPercent = horizontalAfter,
                VerticalScrollPercent = verticalAfter,
                HorizontalChanged = Math.Abs(horizontalAfter - horizontalBefore) > 0.01,
                VerticalChanged = Math.Abs(verticalAfter - verticalBefore) > 0.01
            };
        }
        catch (Exception ex)
        {
            return new ScrollResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static FlaUI.Core.Definitions.ScrollAmount ToFlaUIAmount(ScrollAmount amount)
    {
        return amount switch
        {
            ScrollAmount.SmallDecrement => FlaUI.Core.Definitions.ScrollAmount.SmallDecrement,
            ScrollAmount.LargeDecrement => FlaUI.Core.Definitions.ScrollAmount.LargeDecrement,
            _ => FlaUI.Core.Definitions.ScrollAmount.SmallDecrement
        };
    }

    private static FlaUI.Core.Definitions.ScrollAmount ToFlaUIAmountReverse(ScrollAmount amount)
    {
        return amount switch
        {
            ScrollAmount.SmallDecrement => FlaUI.Core.Definitions.ScrollAmount.SmallIncrement,
            ScrollAmount.LargeDecrement => FlaUI.Core.Definitions.ScrollAmount.LargeIncrement,
            _ => FlaUI.Core.Definitions.ScrollAmount.SmallIncrement
        };
    }

    /// <summary>
    /// Get detailed state of an element including patterns
    /// </summary>
    public ElementStateResult GetElementState(AutomationElement element)
    {
        try
        {
            var result = new ElementStateResult
            {
                Success = true,
                AutomationId = SafeGet(() => element.AutomationId),
                Name = SafeGet(() => element.Name),
                ClassName = SafeGet(() => element.ClassName),
                ControlType = SafeGet(() => element.ControlType.ToString()),
                IsEnabled = SafeGet(() => element.IsEnabled, false),
                IsOffscreen = SafeGet(() => element.IsOffscreen, true),
                IsKeyboardFocusable = SafeGet(() => element.Properties.IsKeyboardFocusable.ValueOrDefault, false),
                HasKeyboardFocus = SafeGet(() => element.Properties.HasKeyboardFocus.ValueOrDefault, false)
            };

            // Get bounding rectangle
            try
            {
                var bounds = element.BoundingRectangle;
                result.BoundingRect = new BoundingRectInfo
                {
                    X = (int)bounds.X,
                    Y = (int)bounds.Y,
                    Width = (int)bounds.Width,
                    Height = (int)bounds.Height
                };
            }
            catch { }

            // Try to get value from ValuePattern
            try
            {
                var valuePattern = element.Patterns.Value.PatternOrDefault;
                if (valuePattern != null)
                {
                    result.Value = valuePattern.Value;
                    result.IsReadOnly = valuePattern.IsReadOnly;
                }
            }
            catch { }

            // Try to get toggle state
            try
            {
                var togglePattern = element.Patterns.Toggle.PatternOrDefault;
                if (togglePattern != null)
                {
                    result.ToggleState = togglePattern.ToggleState.ToString();
                }
            }
            catch { }

            // Try to get selection state
            try
            {
                var selectionItemPattern = element.Patterns.SelectionItem.PatternOrDefault;
                if (selectionItemPattern != null)
                {
                    result.IsSelected = selectionItemPattern.IsSelected;
                }
            }
            catch { }

            // Try to get range value (slider, progress bar)
            try
            {
                var rangePattern = element.Patterns.RangeValue.PatternOrDefault;
                if (rangePattern != null)
                {
                    result.RangeValue = rangePattern.Value;
                    result.RangeMinimum = rangePattern.Minimum;
                    result.RangeMaximum = rangePattern.Maximum;
                }
            }
            catch { }

            // Get DPI scale factor
            result.DpiScaleFactor = TreeBuilder.GetDpiScaleFactor();

            return result;
        }
        catch (Exception ex)
        {
            return new ElementStateResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static T SafeGet<T>(Func<T> getter, T defaultValue = default!)
    {
        try
        {
            return getter();
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Attempt to relocate a stale element using its original search criteria.
    /// Returns true if element was found and relocated, false otherwise.
    /// </summary>
    public RelocateResult RelocateElement(ElementSearchCriteria criteria, int timeoutMs = 5000)
    {
        if (_automation == null)
            throw new ObjectDisposedException(nameof(AutomationHelper));

        var result = new RelocateResult { Success = false };

        try
        {
            AutomationElement? element = null;

            // Try AutomationId first (most stable)
            if (!string.IsNullOrEmpty(criteria.AutomationId))
            {
                element = FindByAutomationId(criteria.AutomationId, null, timeoutMs);
                result.MatchedBy = "AutomationId";
            }

            // Fall back to Name
            if (element == null && !string.IsNullOrEmpty(criteria.Name))
            {
                element = FindByName(criteria.Name, null, timeoutMs);
                result.MatchedBy = "Name";
            }

            // Fall back to ClassName
            if (element == null && !string.IsNullOrEmpty(criteria.ClassName))
            {
                element = FindByClassName(criteria.ClassName, null, timeoutMs);
                result.MatchedBy = "ClassName";
            }

            // Try control type if available
            if (element == null && !string.IsNullOrEmpty(criteria.ControlType))
            {
                // Search all elements of this control type and try to match by other criteria
                var controlType = ParseControlType(criteria.ControlType);
                if (controlType.HasValue)
                {
                    var cf = _automation.ConditionFactory;
                    var allOfType = _automation.GetDesktop().FindAllDescendants(cf.ByControlType(controlType.Value));

                    foreach (var candidate in allOfType)
                    {
                        // Match by bounds or other heuristics
                        if (criteria.LastKnownBounds.HasValue)
                        {
                            try
                            {
                                var bounds = candidate.BoundingRectangle;
                                var expected = criteria.LastKnownBounds.Value;
                                // Allow some tolerance for position changes
                                if (Math.Abs(bounds.X - expected.X) < 50 &&
                                    Math.Abs(bounds.Y - expected.Y) < 50 &&
                                    Math.Abs(bounds.Width - expected.Width) < 20 &&
                                    Math.Abs(bounds.Height - expected.Height) < 20)
                                {
                                    element = candidate;
                                    result.MatchedBy = "ControlType+Bounds";
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }
            }

            if (element != null)
            {
                result.Success = true;
                result.RelocatedElement = element;
                result.Message = $"Element relocated via {result.MatchedBy}";
            }
            else
            {
                result.Message = "Could not relocate element. Try refreshing the UI tree.";
                result.Suggestions = new[]
                {
                    "Call get_ui_tree to refresh element references",
                    "Check if element still exists in the UI",
                    "Verify the application state hasn't changed"
                };
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Relocation failed: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Check if an element reference is stale (no longer valid).
    /// </summary>
    public bool IsElementStale(AutomationElement element)
    {
        try
        {
            // Try to access a property - will throw if stale
            _ = element.ControlType;
            return false;
        }
        catch
        {
            return true;
        }
    }

    private ControlType? ParseControlType(string controlTypeName)
    {
        if (Enum.TryParse<ControlType>(controlTypeName, true, out var result))
            return result;
        return null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var process in _launchedProcesses.Values)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill();
                }
                catch { }
            }

            _launchedProcesses.Clear();
        }

        _automation?.Dispose();
        _automation = null;
    }
}
