using C5T8fBtWY.WinFormsMcp.Server.Automation;

namespace C5T8fBtWY.WinFormsMcp.Server.Services;

/// <summary>
/// Thread-safe cache for UI tree snapshots with LRU eviction.
/// Default capacity is 50 snapshots.
/// </summary>
public sealed class SnapshotCache : ISnapshotCache
{
    private readonly object _lock = new();
    private readonly Dictionary<string, TreeSnapshot> _cache = new();
    private readonly LinkedList<string> _accessOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _nodeMap = new();

    /// <summary>
    /// Default maximum number of snapshots to cache.
    /// </summary>
    public const int DefaultCapacity = 50;

    /// <inheritdoc/>
    public int Capacity { get; }

    /// <summary>
    /// Creates a new SnapshotCache with the default capacity.
    /// </summary>
    public SnapshotCache() : this(DefaultCapacity)
    {
    }

    /// <summary>
    /// Creates a new SnapshotCache with the specified capacity.
    /// </summary>
    /// <param name="capacity">Maximum number of snapshots to cache.</param>
    public SnapshotCache(int capacity)
    {
        Capacity = capacity > 0 ? capacity : DefaultCapacity;
    }

    /// <inheritdoc/>
    public void Cache(string snapshotId, TreeSnapshot snapshot)
    {
        lock (_lock)
        {
            // If already exists, update and move to front
            if (_cache.ContainsKey(snapshotId))
            {
                _cache[snapshotId] = snapshot;
                MoveToFront(snapshotId);
                return;
            }

            // Evict LRU if at capacity
            while (_cache.Count >= Capacity && _accessOrder.Count > 0)
            {
                var lruId = _accessOrder.Last!.Value;
                _accessOrder.RemoveLast();
                _nodeMap.Remove(lruId);
                _cache.Remove(lruId);
            }

            // Add new entry
            _cache[snapshotId] = snapshot;
            var node = _accessOrder.AddFirst(snapshotId);
            _nodeMap[snapshotId] = node;
        }
    }

    /// <inheritdoc/>
    public TreeSnapshot? Get(string snapshotId)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(snapshotId, out var snapshot))
            {
                MoveToFront(snapshotId);
                return snapshot;
            }
            return null;
        }
    }

    /// <inheritdoc/>
    public void Clear(string snapshotId)
    {
        lock (_lock)
        {
            if (_cache.Remove(snapshotId) && _nodeMap.TryGetValue(snapshotId, out var node))
            {
                _accessOrder.Remove(node);
                _nodeMap.Remove(snapshotId);
            }
        }
    }

    /// <inheritdoc/>
    public void ClearAll()
    {
        lock (_lock)
        {
            _cache.Clear();
            _accessOrder.Clear();
            _nodeMap.Clear();
        }
    }

    /// <inheritdoc/>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _cache.Count;
            }
        }
    }

    private void MoveToFront(string snapshotId)
    {
        if (_nodeMap.TryGetValue(snapshotId, out var node))
        {
            _accessOrder.Remove(node);
            var newNode = _accessOrder.AddFirst(snapshotId);
            _nodeMap[snapshotId] = newNode;
        }
    }
}
