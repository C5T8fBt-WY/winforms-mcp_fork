using C5T8fBtWY.WinFormsMcp.Server.Models;

namespace C5T8fBtWY.WinFormsMcp.Server.Utilities;

/// <summary>
/// Utility for filtering windows by process ID.
/// Used for window scoping in tool responses.
/// </summary>
public static class WindowFilter
{
    /// <summary>
    /// Filter windows to only those belonging to tracked process IDs.
    /// </summary>
    /// <param name="windows">Windows to filter.</param>
    /// <param name="trackedPids">Set of process IDs to include.</param>
    /// <returns>List of windows belonging to tracked processes.</returns>
    public static List<WindowInfo> FilterByPids(IEnumerable<WindowInfo> windows, IReadOnlySet<int> trackedPids)
    {
        if (trackedPids.Count == 0)
            return new List<WindowInfo>();

        return windows.Where(w => trackedPids.Contains(w.ProcessId)).ToList();
    }

    /// <summary>
    /// Filter windows to only those belonging to a specific process ID.
    /// </summary>
    /// <param name="windows">Windows to filter.</param>
    /// <param name="pid">Process ID to filter by.</param>
    /// <returns>List of windows belonging to the specified process.</returns>
    public static List<WindowInfo> FilterByPid(IEnumerable<WindowInfo> windows, int pid)
    {
        return windows.Where(w => w.ProcessId == pid).ToList();
    }

    /// <summary>
    /// Get scoped windows based on tracked PIDs.
    /// Returns all windows if no PIDs are tracked.
    /// </summary>
    /// <param name="allWindows">All available windows.</param>
    /// <param name="trackedPids">Set of tracked process IDs.</param>
    /// <returns>Scoped windows if PIDs tracked, otherwise all windows.</returns>
    public static List<WindowInfo> GetScopedWindows(IEnumerable<WindowInfo> allWindows, IReadOnlySet<int> trackedPids)
    {
        if (trackedPids.Count == 0)
            return allWindows.ToList();

        return FilterByPids(allWindows, trackedPids);
    }
}
