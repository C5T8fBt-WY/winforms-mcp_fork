using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Interface for WinForms UI automation
/// </summary>
public interface IAutomationHelper : IDisposable
{
    /// <summary>
    /// Launch a WinForms application
    /// </summary>
    /// <param name="path">Path to the executable</param>
    /// <param name="arguments">Command line arguments</param>
    /// <param name="workingDirectory">Working directory for the process</param>
    /// <param name="idleTimeoutMs">Timeout in milliseconds to wait for the app to become idle (default: 5000)</param>
    Process LaunchApp(string path, string? arguments = null, string? workingDirectory = null, int idleTimeoutMs = 5000);

    /// <summary>
    /// Attach to a running process
    /// </summary>
    Process AttachToProcess(int pid);

    /// <summary>
    /// Attach to a running process by name
    /// </summary>
    Process AttachToProcessByName(string name);

    /// <summary>
    /// Get main window element of a process
    /// </summary>
    AutomationElement? GetMainWindow(int pid);

    /// <summary>
    /// Get an AutomationElement from a native window handle (HWND).
    /// </summary>
    AutomationElement? GetElementFromHandle(IntPtr hwnd);

    /// <summary>
    /// Find element by AutomationId
    /// </summary>
    /// <param name="automationId">The AutomationId to search for</param>
    /// <param name="parent">Parent element to search from (null for desktop)</param>
    /// <param name="timeoutMs">Total timeout for the search (default: 5000)</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    AutomationElement? FindByAutomationId(string automationId, AutomationElement? parent = null, int timeoutMs = 5000, int pollIntervalMs = 100);

    /// <summary>
    /// Find element by Name
    /// </summary>
    /// <param name="name">The Name to search for</param>
    /// <param name="parent">Parent element to search from (null for desktop)</param>
    /// <param name="timeoutMs">Total timeout for the search (default: 5000)</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    AutomationElement? FindByName(string name, AutomationElement? parent = null, int timeoutMs = 5000, int pollIntervalMs = 100);

    /// <summary>
    /// Find element by ClassName
    /// </summary>
    /// <param name="className">The ClassName to search for</param>
    /// <param name="parent">Parent element to search from (null for desktop)</param>
    /// <param name="timeoutMs">Total timeout for the search (default: 5000)</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    AutomationElement? FindByClassName(string className, AutomationElement? parent = null, int timeoutMs = 5000, int pollIntervalMs = 100);

    /// <summary>
    /// Find element by ControlType
    /// </summary>
    /// <param name="controlType">The ControlType to search for</param>
    /// <param name="parent">Parent element to search from (null for desktop)</param>
    /// <param name="timeoutMs">Total timeout for the search (default: 5000)</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    AutomationElement? FindByControlType(ControlType controlType, AutomationElement? parent = null, int timeoutMs = 5000, int pollIntervalMs = 100);

    /// <summary>
    /// Find multiple elements matching condition
    /// </summary>
    /// <param name="condition">The condition to match</param>
    /// <param name="parent">Parent element to search from (null for desktop)</param>
    /// <param name="timeoutMs">Total timeout for the search (default: 5000)</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    AutomationElement[]? FindAll(ConditionBase condition, AutomationElement? parent = null, int timeoutMs = 5000, int pollIntervalMs = 100);

    /// <summary>
    /// Check if element exists
    /// </summary>
    bool ElementExists(string automationId, AutomationElement? parent = null);

    /// <summary>
    /// Click element
    /// </summary>
    void Click(AutomationElement element, bool doubleClick = false);

    /// <summary>
    /// Type text into element
    /// </summary>
    /// <param name="element">The element to type into</param>
    /// <param name="text">The text to type</param>
    /// <param name="clearFirst">Whether to select all and clear existing text first</param>
    /// <param name="clearDelayMs">Delay after select-all before typing new text (default: 100)</param>
    void TypeText(AutomationElement element, string text, bool clearFirst = false, int clearDelayMs = 100);

    /// <summary>
    /// Set value on element
    /// </summary>
    /// <param name="element">The element to set value on</param>
    /// <param name="value">The value to set</param>
    /// <param name="selectAllDelayMs">Delay after select-all before typing new value (default: 50)</param>
    void SetValue(AutomationElement element, string value, int selectAllDelayMs = 50);

    /// <summary>
    /// Get element property
    /// </summary>
    object? GetProperty(AutomationElement element, string propertyName);

    /// <summary>
    /// Take screenshot of element or full desktop
    /// </summary>
    void TakeScreenshot(string outputPath, AutomationElement? element = null);

    /// <summary>
    /// Take screenshot of a specific screen region (window bounds)
    /// </summary>
    void TakeRegionScreenshot(string outputPath, int x, int y, int width, int height);

    /// <summary>
    /// Drag and drop
    /// </summary>
    /// <param name="source">The source element to drag from</param>
    /// <param name="target">The target element to drop onto</param>
    /// <param name="dragSetupDelayMs">Delay after positioning cursor before starting drag (default: 100)</param>
    /// <param name="dropDelayMs">Delay before releasing mouse button after drag (default: 200)</param>
    void DragDrop(AutomationElement source, AutomationElement target, int dragSetupDelayMs = 100, int dropDelayMs = 200);

    /// <summary>
    /// Send keyboard keys
    /// </summary>
    void SendKeys(string keys);

    /// <summary>
    /// Close application
    /// </summary>
    /// <param name="pid">Process ID of the application to close</param>
    /// <param name="force">If true, immediately kills the process; if false, requests graceful close first</param>
    /// <param name="closeTimeoutMs">Timeout to wait for graceful close before force killing (default: 5000)</param>
    void CloseApp(int pid, bool force = false, int closeTimeoutMs = 5000);

    /// <summary>
    /// Wait for element to appear
    /// </summary>
    /// <param name="automationId">The AutomationId to wait for</param>
    /// <param name="parent">Parent element to search from (null for desktop)</param>
    /// <param name="timeoutMs">Total timeout to wait for the element (default: 10000)</param>
    /// <param name="pollIntervalMs">Interval between search attempts (default: 100)</param>
    Task<bool> WaitForElementAsync(string automationId, AutomationElement? parent = null, int timeoutMs = 10000, int pollIntervalMs = 100);

    /// <summary>
    /// Get all child elements
    /// </summary>
    AutomationElement[]? GetAllChildren(AutomationElement element);
}
