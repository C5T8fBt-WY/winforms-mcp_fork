using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Services;

namespace Rhombus.WinFormsMcp.Tests;

/// <summary>
/// Unit tests for SnapshotCache service.
/// </summary>
public class SnapshotCacheTests
{
    private static TreeSnapshot CreateSnapshot(string hash)
    {
        return new TreeSnapshot
        {
            Hash = hash,
            Elements = new Dictionary<string, ElementInfo>()
        };
    }

    [Test]
    public void Cache_StoresSnapshot()
    {
        var cache = new SnapshotCache();
        var snapshot = CreateSnapshot("hash1");

        cache.Cache("snap1", snapshot);

        Assert.That(cache.Count, Is.EqualTo(1));
        Assert.That(cache.Get("snap1"), Is.EqualTo(snapshot));
    }

    [Test]
    public void Get_ReturnsNull_WhenNotFound()
    {
        var cache = new SnapshotCache();

        var result = cache.Get("nonexistent");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Cache_UpdatesExisting_WhenIdExists()
    {
        var cache = new SnapshotCache();
        var snapshot1 = CreateSnapshot("hash1");
        var snapshot2 = CreateSnapshot("hash2");

        cache.Cache("snap1", snapshot1);
        cache.Cache("snap1", snapshot2);

        Assert.That(cache.Count, Is.EqualTo(1));
        Assert.That(cache.Get("snap1")!.Hash, Is.EqualTo("hash2"));
    }

    [Test]
    public void Clear_RemovesSnapshot()
    {
        var cache = new SnapshotCache();
        cache.Cache("snap1", CreateSnapshot("hash1"));

        cache.Clear("snap1");

        Assert.That(cache.Get("snap1"), Is.Null);
        Assert.That(cache.Count, Is.EqualTo(0));
    }

    [Test]
    public void Clear_DoesNotThrow_WhenNotFound()
    {
        var cache = new SnapshotCache();

        Assert.DoesNotThrow(() => cache.Clear("nonexistent"));
    }

    [Test]
    public void ClearAll_RemovesAllSnapshots()
    {
        var cache = new SnapshotCache();
        cache.Cache("snap1", CreateSnapshot("hash1"));
        cache.Cache("snap2", CreateSnapshot("hash2"));
        cache.Cache("snap3", CreateSnapshot("hash3"));

        cache.ClearAll();

        Assert.That(cache.Count, Is.EqualTo(0));
    }

    [Test]
    public void LRU_Eviction_RemovesLeastRecentlyUsed()
    {
        var cache = new SnapshotCache(capacity: 3);
        cache.Cache("snap1", CreateSnapshot("hash1"));
        cache.Cache("snap2", CreateSnapshot("hash2"));
        cache.Cache("snap3", CreateSnapshot("hash3"));

        // Access snap1 to make it recently used
        cache.Get("snap1");

        // Add snap4 - should evict snap2 (LRU)
        cache.Cache("snap4", CreateSnapshot("hash4"));

        Assert.That(cache.Count, Is.EqualTo(3));
        Assert.That(cache.Get("snap1"), Is.Not.Null, "snap1 should still exist (was recently accessed)");
        Assert.That(cache.Get("snap2"), Is.Null, "snap2 should be evicted (LRU)");
        Assert.That(cache.Get("snap3"), Is.Not.Null, "snap3 should still exist");
        Assert.That(cache.Get("snap4"), Is.Not.Null, "snap4 should exist (just added)");
    }

    [Test]
    public void LRU_Eviction_RemovesOldestWhenNotAccessed()
    {
        var cache = new SnapshotCache(capacity: 2);
        cache.Cache("snap1", CreateSnapshot("hash1"));
        cache.Cache("snap2", CreateSnapshot("hash2"));

        // Add snap3 without accessing snap1 or snap2
        cache.Cache("snap3", CreateSnapshot("hash3"));

        Assert.That(cache.Count, Is.EqualTo(2));
        Assert.That(cache.Get("snap1"), Is.Null, "snap1 should be evicted (oldest)");
        Assert.That(cache.Get("snap2"), Is.Not.Null, "snap2 should still exist");
        Assert.That(cache.Get("snap3"), Is.Not.Null, "snap3 should exist (just added)");
    }

    [Test]
    public void Capacity_ReturnsConfiguredValue()
    {
        var cache = new SnapshotCache(capacity: 10);

        Assert.That(cache.Capacity, Is.EqualTo(10));
    }

    [Test]
    public void DefaultCapacity_Is50()
    {
        var cache = new SnapshotCache();

        Assert.That(cache.Capacity, Is.EqualTo(SnapshotCache.DefaultCapacity));
        Assert.That(cache.Capacity, Is.EqualTo(50));
    }

    [Test]
    public void ConcurrentAccess_DoesNotThrow()
    {
        var cache = new SnapshotCache(capacity: 10);
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                cache.Cache($"snap{index}", CreateSnapshot($"hash{index}"));
                cache.Get($"snap{index}");
                cache.Clear($"snap{index}");
            }));
        }

        Assert.DoesNotThrowAsync(async () => await Task.WhenAll(tasks));
    }

    [Test]
    public void InvalidCapacity_UsesDefault()
    {
        var cache = new SnapshotCache(capacity: 0);
        Assert.That(cache.Capacity, Is.EqualTo(SnapshotCache.DefaultCapacity));

        var cache2 = new SnapshotCache(capacity: -5);
        Assert.That(cache2.Capacity, Is.EqualTo(SnapshotCache.DefaultCapacity));
    }
}
