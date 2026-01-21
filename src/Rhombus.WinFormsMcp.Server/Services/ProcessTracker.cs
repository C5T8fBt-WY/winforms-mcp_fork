using System.Collections.Concurrent;

namespace Rhombus.WinFormsMcp.Server.Services;

/// <summary>
/// Thread-safe tracker for process IDs used in window scoping.
/// Tracks which PIDs should be included in scoped window responses.
/// </summary>
public sealed class ProcessTracker : IProcessTracker
{
    // Use ConcurrentDictionary as a thread-safe HashSet (values are ignored)
    private readonly ConcurrentDictionary<int, byte> _trackedPids = new();

    /// <inheritdoc/>
    public void Track(int pid)
    {
        _trackedPids.TryAdd(pid, 0);
    }

    /// <inheritdoc/>
    public void Untrack(int pid)
    {
        _trackedPids.TryRemove(pid, out _);
    }

    /// <inheritdoc/>
    public bool IsTracked(int pid)
    {
        return _trackedPids.ContainsKey(pid);
    }

    /// <inheritdoc/>
    public IReadOnlySet<int> GetTrackedPids()
    {
        return _trackedPids.Keys.ToHashSet();
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _trackedPids.Clear();
    }

    /// <inheritdoc/>
    public int Count => _trackedPids.Count;
}
