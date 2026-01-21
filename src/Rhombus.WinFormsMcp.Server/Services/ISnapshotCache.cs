using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server.Services;

/// <summary>
/// Interface for caching UI tree snapshots.
/// Snapshots are used for comparing UI state changes.
/// </summary>
public interface ISnapshotCache
{
    /// <summary>
    /// Cache a snapshot with the given ID.
    /// </summary>
    /// <param name="snapshotId">Unique identifier for the snapshot.</param>
    /// <param name="snapshot">The tree snapshot to cache.</param>
    void Cache(string snapshotId, TreeSnapshot snapshot);

    /// <summary>
    /// Get a cached snapshot by ID.
    /// </summary>
    /// <param name="snapshotId">The snapshot ID to retrieve.</param>
    /// <returns>The cached snapshot, or null if not found.</returns>
    TreeSnapshot? Get(string snapshotId);

    /// <summary>
    /// Remove a single cached snapshot.
    /// </summary>
    /// <param name="snapshotId">The snapshot ID to remove.</param>
    void Clear(string snapshotId);

    /// <summary>
    /// Remove all cached snapshots.
    /// </summary>
    void ClearAll();

    /// <summary>
    /// Get the current number of cached snapshots.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Get the maximum capacity of the cache.
    /// </summary>
    int Capacity { get; }
}
