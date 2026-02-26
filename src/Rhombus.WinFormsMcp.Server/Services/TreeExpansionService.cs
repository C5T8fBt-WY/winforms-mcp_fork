using System.Collections.Concurrent;

namespace C5T8fBtWY.WinFormsMcp.Server.Services;

/// <summary>
/// Thread-safe service for tracking which elements are marked for expansion in the UI tree.
/// The tree builder will expand marked elements regardless of depth limit.
/// </summary>
public sealed class TreeExpansionService : ITreeExpansionService
{
    // Using ConcurrentDictionary as a thread-safe HashSet
    private readonly ConcurrentDictionary<string, byte> _markedElements = new();

    /// <inheritdoc/>
    public void Mark(string elementKey)
    {
        if (!string.IsNullOrEmpty(elementKey))
        {
            _markedElements.TryAdd(elementKey, 0);
        }
    }

    /// <inheritdoc/>
    public bool IsMarked(string elementKey)
    {
        return !string.IsNullOrEmpty(elementKey) && _markedElements.ContainsKey(elementKey);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetAll()
    {
        return _markedElements.Keys.ToList();
    }

    /// <inheritdoc/>
    public void Clear(string elementKey)
    {
        _markedElements.TryRemove(elementKey, out _);
    }

    /// <inheritdoc/>
    public void ClearAll()
    {
        _markedElements.Clear();
    }

    /// <inheritdoc/>
    public int Count => _markedElements.Count;
}
