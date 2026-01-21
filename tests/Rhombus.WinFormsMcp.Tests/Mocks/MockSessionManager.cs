using System.Collections.Generic;
using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using Rhombus.WinFormsMcp.Server.Abstractions;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Sandbox;

namespace Rhombus.WinFormsMcp.Tests.Mocks;

/// <summary>
/// Mock implementation of ISessionManager for unit testing handlers.
/// </summary>
public class MockSessionManager : ISessionManager
{
    private readonly Dictionary<string, AutomationElement> _elements = new();
    private readonly HashSet<string> _staleElements = new();
    private readonly Dictionary<string, int> _launchedApps = new();
    private readonly HashSet<int> _trackedPids = new();
    private int _elementCounter = 0;

    // Configurable behaviors for testing
    public AutomationHelper? MockAutomation { get; set; }
    public SandboxManager? MockSandboxManager { get; set; }

    public AutomationHelper GetAutomation()
    {
        if (MockAutomation == null)
            throw new System.InvalidOperationException("MockAutomation not configured");
        return MockAutomation;
    }

    public string CacheElement(AutomationElement element)
    {
        var id = $"elem_{++_elementCounter}";
        _elements[id] = element;
        return id;
    }

    public AutomationElement? GetElement(string elementId)
    {
        return _elements.TryGetValue(elementId, out var element) ? element : null;
    }

    public void ClearElement(string elementId)
    {
        _elements.Remove(elementId);
    }

    public bool IsElementStale(string elementId)
    {
        return _staleElements.Contains(elementId);
    }

    /// <summary>
    /// Mark an element as stale for testing.
    /// </summary>
    public void MarkElementStale(string elementId)
    {
        _staleElements.Add(elementId);
    }

    /// <summary>
    /// Add a mock element to the cache.
    /// </summary>
    public string AddMockElement(AutomationElement element)
    {
        return CacheElement(element);
    }

    public void CacheProcess(int pid, object context)
    {
        // No-op for testing
    }

    public int? TrackLaunchedApp(string exePath, int pid)
    {
        int? previous = _launchedApps.TryGetValue(exePath, out var prevPid) ? prevPid : null;
        _launchedApps[exePath] = pid;
        return previous;
    }

    public int? GetPreviousLaunchedPid(string exePath)
    {
        return _launchedApps.TryGetValue(exePath, out var pid) ? pid : null;
    }

    public void UntrackLaunchedApp(string exePath)
    {
        _launchedApps.Remove(exePath);
    }

    public IReadOnlyCollection<int> GetTrackedPids()
    {
        return _launchedApps.Values.ToList();
    }

    public void TrackProcess(int pid)
    {
        _trackedPids.Add(pid);
    }

    public void UntrackProcess(int pid)
    {
        _trackedPids.Remove(pid);
    }

    public IReadOnlySet<int> GetTrackedProcessIds()
    {
        return _trackedPids;
    }

    public SandboxManager GetSandboxManager()
    {
        if (MockSandboxManager == null)
            throw new System.InvalidOperationException("MockSandboxManager not configured");
        return MockSandboxManager;
    }

    /// <summary>
    /// Reset all mock state.
    /// </summary>
    public void Reset()
    {
        _elements.Clear();
        _staleElements.Clear();
        _launchedApps.Clear();
        _trackedPids.Clear();
        _elementCounter = 0;
    }
}
