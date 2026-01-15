using System;
using System.Threading;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Caches UI tree results to reduce repeated expensive tree traversals.
/// Uses time-based invalidation and explicit dirty marking.
/// </summary>
public class TreeCache
{
    private readonly object _lock = new();
    private TreeBuildResult? _cachedResult;
    private DateTime _cachedAt;
    private bool _isDirty;
    private int _cacheHits;
    private int _cacheMisses;

    /// <summary>
    /// Maximum age of cached tree before automatic invalidation (default: 5 seconds).
    /// </summary>
    public TimeSpan MaxCacheAge { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Get the cached tree if valid, or null if cache is stale/dirty.
    /// </summary>
    /// <returns>Cached result if valid, null otherwise</returns>
    public TreeBuildResult? GetCached()
    {
        lock (_lock)
        {
            if (_cachedResult == null || _isDirty)
            {
                _cacheMisses++;
                return null;
            }

            if (DateTime.UtcNow - _cachedAt > MaxCacheAge)
            {
                _cacheMisses++;
                return null;
            }

            _cacheHits++;

            // Return with cache metadata
            return _cachedResult with
            {
                CacheHit = true,
                CacheAgeMs = (int)(DateTime.UtcNow - _cachedAt).TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Store a new tree result in the cache.
    /// </summary>
    public void Store(TreeBuildResult result)
    {
        lock (_lock)
        {
            _cachedResult = result;
            _cachedAt = DateTime.UtcNow;
            _isDirty = false;
        }
    }

    /// <summary>
    /// Mark the cache as dirty (e.g., after a UI interaction that may have changed structure).
    /// </summary>
    public void MarkDirty()
    {
        lock (_lock)
        {
            _isDirty = true;
        }
    }

    /// <summary>
    /// Clear the cache completely.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _cachedResult = null;
            _isDirty = true;
        }
    }

    /// <summary>
    /// Get cache statistics.
    /// </summary>
    public CacheStats GetStats()
    {
        lock (_lock)
        {
            var total = _cacheHits + _cacheMisses;
            return new CacheStats
            {
                CacheHits = _cacheHits,
                CacheMisses = _cacheMisses,
                HitRate = total > 0 ? (double)_cacheHits / total : 0,
                IsDirty = _isDirty,
                HasCachedData = _cachedResult != null,
                CacheAgeMs = _cachedResult != null ? (int)(DateTime.UtcNow - _cachedAt).TotalMilliseconds : -1,
                MaxCacheAgeMs = (int)MaxCacheAge.TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Reset cache statistics.
    /// </summary>
    public void ResetStats()
    {
        lock (_lock)
        {
            _cacheHits = 0;
            _cacheMisses = 0;
        }
    }
}

/// <summary>
/// Cache statistics for monitoring.
/// </summary>
public class CacheStats
{
    public int CacheHits { get; init; }
    public int CacheMisses { get; init; }
    public double HitRate { get; init; }
    public bool IsDirty { get; init; }
    public bool HasCachedData { get; init; }
    public int CacheAgeMs { get; init; }
    public int MaxCacheAgeMs { get; init; }
}
