using System.Collections.Generic;
using System.Diagnostics;
using C5T8fBtWY.WinFormsMcp.Server.Automation;
using C5T8fBtWY.WinFormsMcp.Server.Sandbox;
using FlaUI.Core.AutomationElements;

namespace C5T8fBtWY.WinFormsMcp.Server.Abstractions;

/// <summary>
/// Interface for session management, enabling testability of handlers.
/// </summary>
public interface ISessionManager
{
    // Automation access
    AutomationHelper GetAutomation();

    // Element cache
    string CacheElement(AutomationElement element);
    AutomationElement? GetElement(string elementId);
    void ClearElement(string elementId);
    bool IsElementStale(string elementId);

    // Process tracking
    void CacheProcess(int pid, object context);
    int? TrackLaunchedApp(string exePath, int pid);
    int? GetPreviousLaunchedPid(string exePath);
    void UntrackLaunchedApp(string exePath);
    IReadOnlyCollection<int> GetTrackedPids();

    // Process tracker (for window scoping)
    void TrackProcess(int pid);
    void UntrackProcess(int pid);
    IReadOnlySet<int> GetTrackedProcessIds();

    // Sandbox management
    SandboxManager GetSandboxManager();
}
