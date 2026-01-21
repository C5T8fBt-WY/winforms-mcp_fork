using System.Collections.Concurrent;

namespace Rhombus.WinFormsMcp.Server.Services;

/// <summary>
/// Thread-safe tracker for launched application processes.
/// Maps executable paths (normalized to lowercase) to PIDs.
/// </summary>
public sealed class ProcessContext : IProcessContext
{
    private readonly ConcurrentDictionary<string, int> _launchedAppsByPath = new();

    /// <inheritdoc/>
    public int? TrackLaunchedApp(string exePath, int pid)
    {
        var normalizedPath = NormalizePath(exePath);
        int? previousPid = null;

        _launchedAppsByPath.AddOrUpdate(
            normalizedPath,
            pid,
            (_, oldPid) =>
            {
                previousPid = oldPid;
                return pid;
            });

        return previousPid;
    }

    /// <inheritdoc/>
    public int? GetPreviousLaunchedPid(string exePath)
    {
        var normalizedPath = NormalizePath(exePath);
        return _launchedAppsByPath.TryGetValue(normalizedPath, out var pid) ? pid : null;
    }

    /// <inheritdoc/>
    public void UntrackLaunchedApp(string exePath)
    {
        var normalizedPath = NormalizePath(exePath);
        _launchedAppsByPath.TryRemove(normalizedPath, out _);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<int> GetTrackedPids()
    {
        return _launchedAppsByPath.Values.ToList();
    }

    /// <inheritdoc/>
    public int Count => _launchedAppsByPath.Count;

    /// <summary>
    /// Normalize path for consistent key comparison.
    /// </summary>
    private static string NormalizePath(string exePath)
    {
        return Path.GetFullPath(exePath).ToLowerInvariant();
    }
}
