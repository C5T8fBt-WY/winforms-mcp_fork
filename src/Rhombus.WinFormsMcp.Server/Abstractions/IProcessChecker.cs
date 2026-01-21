namespace Rhombus.WinFormsMcp.Server.Abstractions;

/// <summary>
/// Abstraction for process-related queries to enable testing.
/// Production code uses SystemProcessChecker, tests can inject mock implementations.
/// </summary>
public interface IProcessChecker
{
    /// <summary>
    /// Check if a process with the given PID is currently running.
    /// </summary>
    /// <param name="pid">The process ID to check.</param>
    /// <returns>True if the process is running, false otherwise.</returns>
    bool IsProcessRunning(int pid);

    /// <summary>
    /// Get the process ID for a process with the given name.
    /// </summary>
    /// <param name="processName">The name of the process (without .exe extension).</param>
    /// <returns>The process ID, or null if not found.</returns>
    int? GetProcessId(string processName);
}

/// <summary>
/// Default implementation that uses System.Diagnostics.Process.
/// </summary>
public class SystemProcessChecker : IProcessChecker
{
    /// <summary>
    /// Singleton instance for production use.
    /// </summary>
    public static readonly SystemProcessChecker Instance = new();

    /// <summary>
    /// Check if a process with the given PID is currently running.
    /// </summary>
    public bool IsProcessRunning(int pid)
    {
        try
        {
            var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (System.ArgumentException)
        {
            // Process doesn't exist
            return false;
        }
        catch (System.InvalidOperationException)
        {
            // Process has exited
            return false;
        }
    }

    /// <summary>
    /// Get the process ID for a process with the given name.
    /// </summary>
    public int? GetProcessId(string processName)
    {
        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(processName);
            if (processes.Length > 0)
            {
                return processes[0].Id;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
