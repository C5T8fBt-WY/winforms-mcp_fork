namespace Rhombus.WinFormsMcp.Server.Services;

/// <summary>
/// Interface for managing UI event subscriptions and queuing.
/// Events are filtered by subscription and queued for retrieval.
/// </summary>
public interface IEventService
{
    /// <summary>
    /// Subscribe to specific event types.
    /// Events of these types will be queued for later retrieval.
    /// </summary>
    /// <param name="eventTypes">Event types to subscribe to.</param>
    void Subscribe(IEnumerable<string> eventTypes);

    /// <summary>
    /// Get the list of currently subscribed event types.
    /// </summary>
    IReadOnlyCollection<string> GetSubscribedEventTypes();

    /// <summary>
    /// Check if any events are subscribed.
    /// </summary>
    bool HasSubscriptions { get; }

    /// <summary>
    /// Add an event to the queue. Event is only queued if its type is subscribed.
    /// If queue is full, oldest event is dropped.
    /// </summary>
    /// <param name="evt">The event to enqueue.</param>
    void Enqueue(UiEvent evt);

    /// <summary>
    /// Get all queued events and clear the queue.
    /// </summary>
    /// <returns>Tuple of (events list, number of events dropped since last drain).</returns>
    (List<UiEvent> events, int droppedCount) Drain();

    /// <summary>
    /// Get the current number of queued events.
    /// </summary>
    int QueueCount { get; }
}
