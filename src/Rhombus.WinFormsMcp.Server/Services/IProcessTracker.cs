namespace C5T8fBtWY.WinFormsMcp.Server.Services;

/// <summary>
/// Interface for tracking process IDs for window scoping.
/// Unlike IProcessContext which maps paths to PIDs, this simply tracks
/// which PIDs should be included in scoped window responses.
/// </summary>
public interface IProcessTracker
{
    /// <summary>
    /// Start tracking a process ID.
    /// </summary>
    /// <param name="pid">The process ID to track.</param>
    void Track(int pid);

    /// <summary>
    /// Stop tracking a process ID.
    /// </summary>
    /// <param name="pid">The process ID to untrack.</param>
    void Untrack(int pid);

    /// <summary>
    /// Check if a process ID is being tracked.
    /// </summary>
    /// <param name="pid">The process ID to check.</param>
    /// <returns>True if the process is tracked, false otherwise.</returns>
    bool IsTracked(int pid);

    /// <summary>
    /// Get all tracked process IDs.
    /// </summary>
    /// <returns>A read-only set of all tracked process IDs.</returns>
    IReadOnlySet<int> GetTrackedPids();

    /// <summary>
    /// Clear all tracked process IDs.
    /// </summary>
    void Clear();

    /// <summary>
    /// Get the number of tracked processes.
    /// </summary>
    int Count { get; }
}
