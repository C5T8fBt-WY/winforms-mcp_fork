using C5T8fBtWY.WinFormsMcp.Server.Abstractions;
using C5T8fBtWY.WinFormsMcp.Server.Services;
using C5T8fBtWY.WinFormsMcp.Server;

namespace C5T8fBtWY.WinFormsMcp.Tests;

/// <summary>
/// Unit tests for ConfirmationService.
/// Uses a fake time provider for testable time operations.
/// </summary>
public class ConfirmationServiceTests
{
    private class FakeTimeProvider : ITimeProvider
    {
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
    }

    [Test]
    public void Create_ReturnsConfirmation_WithToken()
    {
        var timeProvider = new FakeTimeProvider();
        var service = new ConfirmationService(timeProvider);

        var confirmation = service.Create("delete", "Delete file", "/path/to/file", null);

        Assert.That(confirmation.Token, Is.Not.Null.And.Not.Empty);
        Assert.That(confirmation.Action, Is.EqualTo("delete"));
        Assert.That(confirmation.Description, Is.EqualTo("Delete file"));
        Assert.That(confirmation.Target, Is.EqualTo("/path/to/file"));
    }

    [Test]
    public void Create_GeneratesUniqueTokens()
    {
        var service = new ConfirmationService();

        var c1 = service.Create("action1", "desc1", null, null);
        var c2 = service.Create("action2", "desc2", null, null);

        Assert.That(c1.Token, Is.Not.EqualTo(c2.Token));
    }

    [Test]
    public void Create_SetsExpiresAt()
    {
        var timeProvider = new FakeTimeProvider { UtcNow = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc) };
        var service = new ConfirmationService(timeProvider, timeoutSeconds: 30);

        var confirmation = service.Create("action", "desc", null, null);

        Assert.That(confirmation.CreatedAt, Is.EqualTo(timeProvider.UtcNow));
        Assert.That(confirmation.ExpiresAt, Is.EqualTo(timeProvider.UtcNow.AddSeconds(30)));
    }

    [Test]
    public void Consume_ReturnsConfirmation_WhenValid()
    {
        var service = new ConfirmationService();
        var created = service.Create("action", "desc", null, null);

        var consumed = service.Consume(created.Token);

        Assert.That(consumed, Is.Not.Null);
        Assert.That(consumed!.Token, Is.EqualTo(created.Token));
    }

    [Test]
    public void Consume_ReturnsNull_WhenTokenNotFound()
    {
        var service = new ConfirmationService();

        var consumed = service.Consume("nonexistent-token");

        Assert.That(consumed, Is.Null);
    }

    [Test]
    public void Consume_RemovesConfirmation()
    {
        var service = new ConfirmationService();
        var created = service.Create("action", "desc", null, null);

        service.Consume(created.Token);
        var secondAttempt = service.Consume(created.Token);

        Assert.That(secondAttempt, Is.Null);
    }

    [Test]
    public void Consume_ReturnsNull_WhenExpired()
    {
        var timeProvider = new FakeTimeProvider { UtcNow = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc) };
        var service = new ConfirmationService(timeProvider, timeoutSeconds: 30);
        var created = service.Create("action", "desc", null, null);

        // Advance time past expiration
        timeProvider.UtcNow = timeProvider.UtcNow.AddSeconds(31);

        var consumed = service.Consume(created.Token);

        Assert.That(consumed, Is.Null);
    }

    [Test]
    public void Consume_ReturnsConfirmation_JustBeforeExpiry()
    {
        var timeProvider = new FakeTimeProvider { UtcNow = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc) };
        var service = new ConfirmationService(timeProvider, timeoutSeconds: 30);
        var created = service.Create("action", "desc", null, null);

        // Advance time to just before expiration
        timeProvider.UtcNow = timeProvider.UtcNow.AddSeconds(29);

        var consumed = service.Consume(created.Token);

        Assert.That(consumed, Is.Not.Null);
    }

    [Test]
    public void Count_ReturnsNumberOfPendingConfirmations()
    {
        var service = new ConfirmationService();
        Assert.That(service.Count, Is.EqualTo(0));

        service.Create("action1", "desc1", null, null);
        Assert.That(service.Count, Is.EqualTo(1));

        service.Create("action2", "desc2", null, null);
        Assert.That(service.Count, Is.EqualTo(2));
    }

    [Test]
    public void Count_ExcludesExpiredConfirmations()
    {
        var timeProvider = new FakeTimeProvider { UtcNow = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc) };
        var service = new ConfirmationService(timeProvider, timeoutSeconds: 30);
        service.Create("action1", "desc1", null, null);
        service.Create("action2", "desc2", null, null);

        // Advance time past expiration
        timeProvider.UtcNow = timeProvider.UtcNow.AddSeconds(31);

        Assert.That(service.Count, Is.EqualTo(0));
    }

    [Test]
    public void Create_CleansUpExpiredConfirmations()
    {
        var timeProvider = new FakeTimeProvider { UtcNow = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc) };
        var service = new ConfirmationService(timeProvider, timeoutSeconds: 30);
        service.Create("action1", "desc1", null, null);

        // Advance time past expiration
        timeProvider.UtcNow = timeProvider.UtcNow.AddSeconds(31);

        // Creating a new confirmation should clean up expired ones
        service.Create("action2", "desc2", null, null);

        Assert.That(service.Count, Is.EqualTo(1));
    }

    [Test]
    public void ConcurrentAccess_DoesNotThrow()
    {
        var service = new ConfirmationService();
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var c = service.Create("action", "desc", null, null);
                _ = service.Count;
                service.Consume(c.Token);
            }));
        }

        Assert.DoesNotThrowAsync(async () => await Task.WhenAll(tasks));
    }
}
