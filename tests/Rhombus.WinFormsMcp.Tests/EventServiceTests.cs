using C5T8fBtWY.WinFormsMcp.Server;
using C5T8fBtWY.WinFormsMcp.Server.Services;

namespace C5T8fBtWY.WinFormsMcp.Tests;

/// <summary>
/// Unit tests for EventService.
/// </summary>
public class EventServiceTests
{
    private static UiEvent CreateEvent(string type, string? details = null)
    {
        return new UiEvent
        {
            Type = type,
            Timestamp = DateTime.UtcNow,
            Details = details
        };
    }

    [Test]
    public void Subscribe_AddsEventTypes()
    {
        var service = new EventService();

        service.Subscribe(new[] { "click", "focus" });

        var types = service.GetSubscribedEventTypes();
        Assert.That(types, Has.Count.EqualTo(2));
        Assert.That(types, Does.Contain("click"));
        Assert.That(types, Does.Contain("focus"));
    }

    [Test]
    public void Subscribe_NormalizesToLowercase()
    {
        var service = new EventService();

        service.Subscribe(new[] { "CLICK", "Focus", "hover" });

        var types = service.GetSubscribedEventTypes();
        Assert.That(types, Does.Contain("click"));
        Assert.That(types, Does.Contain("focus"));
        Assert.That(types, Does.Contain("hover"));
    }

    [Test]
    public void Subscribe_IgnoresNullAndWhitespace()
    {
        var service = new EventService();

        service.Subscribe(new[] { "click", "", "  ", "focus" });

        var types = service.GetSubscribedEventTypes();
        Assert.That(types, Has.Count.EqualTo(2));
    }

    [Test]
    public void HasSubscriptions_ReturnsFalse_WhenEmpty()
    {
        var service = new EventService();

        Assert.That(service.HasSubscriptions, Is.False);
    }

    [Test]
    public void HasSubscriptions_ReturnsTrue_WhenSubscribed()
    {
        var service = new EventService();
        service.Subscribe(new[] { "click" });

        Assert.That(service.HasSubscriptions, Is.True);
    }

    [Test]
    public void Enqueue_AddsEvent_WhenSubscribed()
    {
        var service = new EventService();
        service.Subscribe(new[] { "click" });

        service.Enqueue(CreateEvent("click"));

        Assert.That(service.QueueCount, Is.EqualTo(1));
    }

    [Test]
    public void Enqueue_IgnoresEvent_WhenNotSubscribed()
    {
        var service = new EventService();
        service.Subscribe(new[] { "click" });

        service.Enqueue(CreateEvent("focus"));

        Assert.That(service.QueueCount, Is.EqualTo(0));
    }

    [Test]
    public void Enqueue_MatchesCaseInsensitive()
    {
        var service = new EventService();
        service.Subscribe(new[] { "click" });

        service.Enqueue(CreateEvent("CLICK"));

        Assert.That(service.QueueCount, Is.EqualTo(1));
    }

    [Test]
    public void Drain_ReturnsAllEvents()
    {
        var service = new EventService();
        service.Subscribe(new[] { "click", "focus" });
        service.Enqueue(CreateEvent("click", "event1"));
        service.Enqueue(CreateEvent("focus", "event2"));

        var (events, dropped) = service.Drain();

        Assert.That(events, Has.Count.EqualTo(2));
        Assert.That(events[0].Details, Is.EqualTo("event1"));
        Assert.That(events[1].Details, Is.EqualTo("event2"));
        Assert.That(dropped, Is.EqualTo(0));
    }

    [Test]
    public void Drain_ClearsQueue()
    {
        var service = new EventService();
        service.Subscribe(new[] { "click" });
        service.Enqueue(CreateEvent("click"));

        service.Drain();

        Assert.That(service.QueueCount, Is.EqualTo(0));
    }

    [Test]
    public void Enqueue_DropsOldest_WhenAtCapacity()
    {
        var service = new EventService(maxQueueSize: 2);
        service.Subscribe(new[] { "click" });
        service.Enqueue(CreateEvent("click", "first"));
        service.Enqueue(CreateEvent("click", "second"));
        service.Enqueue(CreateEvent("click", "third"));

        var (events, dropped) = service.Drain();

        Assert.That(events, Has.Count.EqualTo(2));
        Assert.That(events[0].Details, Is.EqualTo("second"), "First event should be dropped");
        Assert.That(events[1].Details, Is.EqualTo("third"));
        Assert.That(dropped, Is.EqualTo(1));
    }

    [Test]
    public void Drain_ResetsDroppedCount()
    {
        var service = new EventService(maxQueueSize: 1);
        service.Subscribe(new[] { "click" });
        service.Enqueue(CreateEvent("click", "first"));
        service.Enqueue(CreateEvent("click", "second"));

        var (_, dropped1) = service.Drain();
        service.Enqueue(CreateEvent("click", "third"));
        var (_, dropped2) = service.Drain();

        Assert.That(dropped1, Is.EqualTo(1));
        Assert.That(dropped2, Is.EqualTo(0));
    }

    [Test]
    public void ConcurrentAccess_DoesNotThrow()
    {
        var service = new EventService();
        service.Subscribe(new[] { "click", "focus", "hover" });
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                service.Enqueue(CreateEvent("click", $"event{index}"));
                service.GetSubscribedEventTypes();
                _ = service.HasSubscriptions;
                _ = service.QueueCount;
            }));
        }

        Assert.DoesNotThrowAsync(async () => await Task.WhenAll(tasks));
    }

    [Test]
    public void Constructor_InvalidMaxQueueSize_UsesDefault()
    {
        var service1 = new EventService(maxQueueSize: 0);
        var service2 = new EventService(maxQueueSize: -5);

        // These should not throw and should use a reasonable default
        service1.Subscribe(new[] { "click" });
        service2.Subscribe(new[] { "click" });
        Assert.That(service1.HasSubscriptions, Is.True);
        Assert.That(service2.HasSubscriptions, Is.True);
    }
}
