namespace Rhombus.WinFormsMcp.Server.Services;

/// <summary>
/// Interface for tracking launched application processes.
/// Maps executable paths to PIDs for process management.
/// </summary>
public interface IProcessContext
{
    /// <summary>
    /// Track a launched app by its executable path.
    /// </summary>
    /// <param name="exePath">The path to the executable.</param>
    /// <param name="pid">The process ID of the launched app.</param>
    /// <returns>The previous PID if one was already tracked for this path, null otherwise.</returns>
    int? TrackLaunchedApp(string exePath, int pid);

    /// <summary>
    /// Get the previously tracked PID for an executable path.
    /// </summary>
    /// <param name="exePath">The path to the executable.</param>
    /// <returns>The PID if tracked, null otherwise.</returns>
    int? GetPreviousLaunchedPid(string exePath);

    /// <summary>
    /// Stop tracking an app by its executable path.
    /// </summary>
    /// <param name="exePath">The path to the executable.</param>
    void UntrackLaunchedApp(string exePath);

    /// <summary>
    /// Get all tracked PIDs.
    /// </summary>
    /// <returns>A read-only collection of all tracked process IDs.</returns>
    IReadOnlyCollection<int> GetTrackedPids();

    /// <summary>
    /// Get the number of tracked processes.
    /// </summary>
    int Count { get; }
}
