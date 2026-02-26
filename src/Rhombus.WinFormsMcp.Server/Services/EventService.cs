namespace C5T8fBtWY.WinFormsMcp.Server.Services;

/// <summary>
/// Thread-safe service for managing UI event subscriptions and queuing.
/// Events are filtered by subscription and queued with a maximum capacity.
/// </summary>
public sealed class EventService : IEventService
{
    private readonly object _lock = new();
    private readonly Queue<UiEvent> _eventQueue = new();
    private readonly HashSet<string> _subscribedEventTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxQueueSize;
    private int _eventsDropped;

    /// <summary>
    /// Creates a new EventService with the default queue size.
    /// </summary>
    public EventService() : this(Constants.Queues.MaxEventQueueSize)
    {
    }

    /// <summary>
    /// Creates a new EventService with a specified queue size.
    /// </summary>
    /// <param name="maxQueueSize">Maximum number of events to queue.</param>
    public EventService(int maxQueueSize)
    {
        _maxQueueSize = maxQueueSize > 0 ? maxQueueSize : Constants.Queues.MaxEventQueueSize;
    }

    /// <inheritdoc/>
    public void Subscribe(IEnumerable<string> eventTypes)
    {
        lock (_lock)
        {
            foreach (var eventType in eventTypes)
            {
                if (!string.IsNullOrWhiteSpace(eventType))
                {
                    _subscribedEventTypes.Add(eventType.ToLowerInvariant());
                }
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetSubscribedEventTypes()
    {
        lock (_lock)
        {
            return _subscribedEventTypes.ToList();
        }
    }

    /// <inheritdoc/>
    public bool HasSubscriptions
    {
        get
        {
            lock (_lock)
            {
                return _subscribedEventTypes.Count > 0;
            }
        }
    }

    /// <inheritdoc/>
    public void Enqueue(UiEvent evt)
    {
        lock (_lock)
        {
            // Only queue if type is subscribed
            if (!_subscribedEventTypes.Contains(evt.Type.ToLowerInvariant()))
            {
                return;
            }

            // Evict oldest if at capacity
            if (_eventQueue.Count >= _maxQueueSize)
            {
                _eventQueue.Dequeue();
                _eventsDropped++;
            }

            _eventQueue.Enqueue(evt);
        }
    }

    /// <inheritdoc/>
    public (List<UiEvent> events, int droppedCount) Drain()
    {
        lock (_lock)
        {
            var events = _eventQueue.ToList();
            _eventQueue.Clear();
            var dropped = _eventsDropped;
            _eventsDropped = 0;
            return (events, dropped);
        }
    }

    /// <inheritdoc/>
    public int QueueCount
    {
        get
        {
            lock (_lock)
            {
                return _eventQueue.Count;
            }
        }
    }
}
