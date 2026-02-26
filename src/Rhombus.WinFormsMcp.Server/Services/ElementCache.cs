using System.Collections.Concurrent;
using FlaUI.Core.AutomationElements;

namespace C5T8fBtWY.WinFormsMcp.Server.Services;

/// <summary>
/// Thread-safe cache for UI Automation elements.
/// Elements are cached with auto-generated IDs (elem_1, elem_2, etc.).
/// </summary>
public sealed class ElementCache : IElementCache
{
    private readonly ConcurrentDictionary<string, AutomationElement> _cache = new();
    private int _nextId;

    /// <inheritdoc/>
    public string Cache(AutomationElement element)
    {
        var id = $"elem_{Interlocked.Increment(ref _nextId)}";
        _cache[id] = element;
        return id;
    }

    /// <inheritdoc/>
    public AutomationElement? Get(string elementId)
    {
        return _cache.TryGetValue(elementId, out var element) ? element : null;
    }

    /// <inheritdoc/>
    public void Clear(string elementId)
    {
        _cache.TryRemove(elementId, out _);
    }

    /// <inheritdoc/>
    public void ClearAll()
    {
        _cache.Clear();
        // Reset ID counter on full clear for predictable test behavior
        Interlocked.Exchange(ref _nextId, 0);
    }

    /// <inheritdoc/>
    public bool IsStale(string elementId)
    {
        if (!_cache.TryGetValue(elementId, out var element))
        {
            return true; // Not found = stale
        }

        try
        {
            // Try to access a property - will throw if element is invalid
            _ = element.Properties.ProcessId.Value;
            return false;
        }
        catch
        {
            // Element is stale - remove it from cache
            _cache.TryRemove(elementId, out _);
            return true;
        }
    }

    /// <inheritdoc/>
    public int Count => _cache.Count;
}
