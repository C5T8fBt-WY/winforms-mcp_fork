using System.Text.Json;
using C5T8fBtWY.WinFormsMcp.Server.Abstractions;

namespace C5T8fBtWY.WinFormsMcp.Server.Services;

/// <summary>
/// Thread-safe service for managing pending confirmations for destructive actions.
/// Confirmations have a timeout and can only be used once.
/// Uses ITimeProvider for testable time operations.
/// </summary>
public sealed class ConfirmationService : IConfirmationService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, PendingConfirmation> _pendingConfirmations = new();
    private readonly ITimeProvider _timeProvider;
    private readonly int _timeoutSeconds;

    /// <summary>
    /// Creates a new ConfirmationService with the default time provider.
    /// </summary>
    public ConfirmationService() : this(new SystemTimeProvider())
    {
    }

    /// <summary>
    /// Creates a new ConfirmationService with a custom time provider (for testing).
    /// </summary>
    /// <param name="timeProvider">The time provider to use.</param>
    public ConfirmationService(ITimeProvider timeProvider)
        : this(timeProvider, Constants.Queues.ConfirmationTimeoutSeconds)
    {
    }

    /// <summary>
    /// Creates a new ConfirmationService with a custom time provider and timeout.
    /// </summary>
    /// <param name="timeProvider">The time provider to use.</param>
    /// <param name="timeoutSeconds">Timeout in seconds for confirmations.</param>
    public ConfirmationService(ITimeProvider timeProvider, int timeoutSeconds)
    {
        _timeProvider = timeProvider;
        _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : Constants.Queues.ConfirmationTimeoutSeconds;
    }

    /// <inheritdoc/>
    public PendingConfirmation Create(string action, string description, string? target, JsonElement? parameters)
    {
        lock (_lock)
        {
            CleanupExpired();

            var now = _timeProvider.UtcNow;
            var confirmation = new PendingConfirmation
            {
                Token = Guid.NewGuid().ToString("N"),
                Action = action,
                Description = description,
                Target = target,
                Parameters = parameters,
                CreatedAt = now,
                ExpiresAt = now.AddSeconds(_timeoutSeconds)
            };

            _pendingConfirmations[confirmation.Token] = confirmation;
            return confirmation;
        }
    }

    /// <inheritdoc/>
    public PendingConfirmation? Consume(string token)
    {
        lock (_lock)
        {
            CleanupExpired();

            if (_pendingConfirmations.TryGetValue(token, out var confirmation))
            {
                _pendingConfirmations.Remove(token);

                if (confirmation.ExpiresAt < _timeProvider.UtcNow)
                {
                    return null; // Expired
                }

                return confirmation;
            }

            return null;
        }
    }

    /// <inheritdoc/>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                CleanupExpired();
                return _pendingConfirmations.Count;
            }
        }
    }

    private void CleanupExpired()
    {
        var now = _timeProvider.UtcNow;
        var expired = _pendingConfirmations
            .Where(kvp => kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var token in expired)
        {
            _pendingConfirmations.Remove(token);
        }
    }
}
