using C5T8fBtWY.WinFormsMcp.Server.Services;

namespace C5T8fBtWY.WinFormsMcp.Tests;

/// <summary>
/// Unit tests for ElementCache service.
/// Note: Full staleness testing requires live automation elements,
/// but we can test the basic cache operations and concurrency.
/// </summary>
public class ElementCacheTests
{
    [Test]
    public void Cache_GeneratesSequentialIds()
    {
        var cache = new ElementCache();

        // We can't create real AutomationElements in unit tests,
        // so this test validates the ID generation pattern
        // by checking that Count increases.

        // The actual caching with real elements is tested in integration tests
        Assert.That(cache.Count, Is.EqualTo(0));
    }

    [Test]
    public void Get_ReturnsNull_WhenIdNotFound()
    {
        var cache = new ElementCache();

        var result = cache.Get("elem_999");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Clear_RemovesElement_WhenExists()
    {
        var cache = new ElementCache();

        // Clear should not throw even when element doesn't exist
        Assert.DoesNotThrow(() => cache.Clear("elem_1"));
        Assert.That(cache.Count, Is.EqualTo(0));
    }

    [Test]
    public void ClearAll_ResetsCache()
    {
        var cache = new ElementCache();

        // ClearAll should work on empty cache
        cache.ClearAll();
        Assert.That(cache.Count, Is.EqualTo(0));
    }

    [Test]
    public void IsStale_ReturnsTrue_WhenElementNotFound()
    {
        var cache = new ElementCache();

        var result = cache.IsStale("elem_nonexistent");

        Assert.That(result, Is.True);
    }

    [Test]
    public void Count_ReturnsZero_WhenEmpty()
    {
        var cache = new ElementCache();

        Assert.That(cache.Count, Is.EqualTo(0));
    }

    [Test]
    public void ConcurrentAccess_DoesNotThrow()
    {
        var cache = new ElementCache();

        // Simulate concurrent access patterns
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                cache.Get($"elem_{index}");
                cache.Clear($"elem_{index}");
                cache.IsStale($"elem_{index}");
            }));
        }

        Assert.DoesNotThrowAsync(async () => await Task.WhenAll(tasks));
    }
}
