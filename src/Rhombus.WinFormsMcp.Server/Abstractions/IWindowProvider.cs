using System;
using System.Collections.Generic;
using Rhombus.WinFormsMcp.Server.Models;

namespace Rhombus.WinFormsMcp.Server.Abstractions;

/// <summary>
/// Abstraction for window enumeration to enable testing.
/// Production code uses WindowManager as the implementation.
/// </summary>
public interface IWindowProvider
{
    /// <summary>
    /// Get all visible windows in the system.
    /// </summary>
    List<WindowInfo> GetAllWindows();

    /// <summary>
    /// Get windows belonging to specific process IDs.
    /// </summary>
    /// <param name="pids">Set of process IDs to filter by.</param>
    List<WindowInfo> GetWindowsByPids(IReadOnlySet<int> pids);

    /// <summary>
    /// Find a window by its handle (as hex string).
    /// </summary>
    /// <param name="handleHex">The window handle in hexadecimal format.</param>
    WindowInfo? FindByHandle(string handleHex);

    /// <summary>
    /// Find a window by partial title match.
    /// </summary>
    /// <param name="titleSubstring">Substring to search for in window titles.</param>
    WindowInfo? FindByTitle(string titleSubstring);
}
