# SessionManager God Object Refactoring - Design

## 1. Overview

This design document specifies the architectural solution for refactoring the `SessionManager` class into focused, single-responsibility services. The design follows the Interface Segregation Principle (ISP) and enables incremental migration via a facade pattern.

**Design Goals:**
1. Extract 6 interface-based services from SessionManager
2. Enable independent testing of each service
3. Maintain backward compatibility during migration
4. Add thread-safety where required for concurrent TCP clients

---

## 2. Service Architecture

### 2.1 Service Dependency Graph

```
                    ┌─────────────────────┐
                    │   HandlerBase       │
                    │   (abstract)        │
                    └─────────┬───────────┘
                              │ uses
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
        ▼                     ▼                     ▼
┌───────────────┐   ┌─────────────────┐   ┌─────────────────┐
│ IElementCache │   │ IProcessContext │   │ ISnapshotCache  │
└───────────────┘   └─────────────────┘   └─────────────────┘
        │                     │                     │
        ├─────────────────────┼─────────────────────┤
        │                     │                     │
        ▼                     ▼                     ▼
┌───────────────┐   ┌─────────────────┐   ┌─────────────────┐
│ IEventService │   │IConfirmationSvc │   │ITreeExpansionSvc│
└───────────────┘   └─────────────────┘   └─────────────────┘
        │                     │                     │
        └─────────────────────┼─────────────────────┘
                              │
                              ▼
                    ┌─────────────────────┐
                    │   SessionManager    │
                    │   (Facade/Composed) │
                    └─────────────────────┘
                              │ owns
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
        ▼                     ▼                     ▼
┌───────────────┐   ┌─────────────────┐   ┌─────────────────┐
│AutomationHelper│  │ SandboxManager  │   │   TreeCache     │
│   (lazy)      │   │    (lazy)       │   │                 │
└───────────────┘   └─────────────────┘   └─────────────────┘
```

### 2.2 Service Responsibilities Matrix

| Service | Responsibility | State | Thread-Safe |
|---------|---------------|-------|-------------|
| `IElementCache` | Cache AutomationElement references | `_elementCache`, `_nextElementId` | Yes |
| `IProcessContext` | Track launched app PIDs by path | `_launchedAppsByPath` | Yes |
| `ISnapshotCache` | Store UI tree snapshots | `_snapshotCache` | Yes |
| `IEventService` | Manage event subscriptions and queue | `_eventQueue`, `_subscribedEventTypes`, `_eventsDropped` | Yes |
| `IConfirmationService` | Manage pending confirmations | `_pendingConfirmations` | Yes |
| `ITreeExpansionService` | Track elements marked for expansion | `_expandedElements` | Yes |

---

## 3. Interface Definitions

### 3.1 IElementCache

```csharp
namespace Rhombus.WinFormsMcp.Server.Services;

using FlaUI.Core.AutomationElements;

/// <summary>
/// Thread-safe cache for AutomationElement references.
/// Elements are assigned sequential IDs (elem_1, elem_2, ...).
/// </summary>
public interface IElementCache
{
    /// <summary>
    /// Cache an element and return its unique ID.
    /// Thread-safe: Uses Interlocked.Increment for ID generation.
    /// </summary>
    string Cache(AutomationElement element);

    /// <summary>
    /// Retrieve a cached element by ID.
    /// Thread-safe: ConcurrentDictionary lookup.
    /// </summary>
    AutomationElement? Get(string elementId);

    /// <summary>
    /// Remove a specific element from the cache.
    /// Thread-safe: ConcurrentDictionary removal.
    /// </summary>
    void Clear(string elementId);

    /// <summary>
    /// Remove all cached elements.
    /// Thread-safe: ConcurrentDictionary.Clear().
    /// </summary>
    void ClearAll();

    /// <summary>
    /// Get the current count of cached elements.
    /// </summary>
    int Count { get; }
}
```

### 3.2 IProcessContext

```csharp
namespace Rhombus.WinFormsMcp.Server.Services;

/// <summary>
/// Thread-safe tracking of launched applications by executable path.
/// Used for hot-reload detection (closing previous instance before launching new).
/// </summary>
public interface IProcessContext
{
    /// <summary>
    /// Track a launched app. Returns the previous PID if one was tracked for this path.
    /// Paths are normalized to lowercase full paths.
    /// Thread-safe: ConcurrentDictionary operations.
    /// </summary>
    int? TrackLaunchedApp(string exePath, int pid);

    /// <summary>
    /// Get the previously tracked PID for an executable path.
    /// Thread-safe: ConcurrentDictionary lookup.
    /// </summary>
    int? GetPreviousLaunchedPid(string exePath);

    /// <summary>
    /// Remove tracking for a launched app.
    /// Thread-safe: ConcurrentDictionary removal.
    /// </summary>
    void UntrackLaunchedApp(string exePath);
}
```

### 3.3 ISnapshotCache

```csharp
namespace Rhombus.WinFormsMcp.Server.Services;

using Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Thread-safe cache for UI tree snapshots used in state change detection.
/// </summary>
public interface ISnapshotCache
{
    /// <summary>
    /// Store a snapshot with the given ID.
    /// Thread-safe: ConcurrentDictionary operations.
    /// </summary>
    void Cache(string snapshotId, TreeSnapshot snapshot);

    /// <summary>
    /// Retrieve a snapshot by ID.
    /// Thread-safe: ConcurrentDictionary lookup.
    /// </summary>
    TreeSnapshot? Get(string snapshotId);

    /// <summary>
    /// Remove a specific snapshot.
    /// Thread-safe: ConcurrentDictionary removal.
    /// </summary>
    void Clear(string snapshotId);

    /// <summary>
    /// Remove all snapshots.
    /// Thread-safe: ConcurrentDictionary.Clear().
    /// </summary>
    void ClearAll();
}
```

### 3.4 IEventService

```csharp
namespace Rhombus.WinFormsMcp.Server.Services;

/// <summary>
/// Thread-safe event subscription and queue management.
/// Queue capacity is 10 events with FIFO eviction.
/// </summary>
public interface IEventService
{
    /// <summary>
    /// Subscribe to event types. Types are normalized to lowercase.
    /// Thread-safe: Lock-protected HashSet operations.
    /// </summary>
    void Subscribe(IEnumerable<string> eventTypes);

    /// <summary>
    /// Get currently subscribed event types.
    /// Thread-safe: Returns a snapshot copy.
    /// </summary>
    IReadOnlyCollection<string> GetSubscribedEventTypes();

    /// <summary>
    /// Check if any event subscriptions are active.
    /// Thread-safe: Atomic read.
    /// </summary>
    bool HasSubscriptions { get; }

    /// <summary>
    /// Enqueue an event if its type is subscribed.
    /// If queue is at capacity (10), oldest event is dropped.
    /// Thread-safe: Lock-protected queue operations.
    /// </summary>
    void Enqueue(UiEvent evt);

    /// <summary>
    /// Drain all events from the queue and return with dropped count.
    /// Resets the dropped count to zero.
    /// Thread-safe: Lock-protected drain operation.
    /// </summary>
    (List<UiEvent> events, int droppedCount) Drain();
}
```

### 3.5 IConfirmationService

```csharp
namespace Rhombus.WinFormsMcp.Server.Services;

using System.Text.Json;

/// <summary>
/// Thread-safe management of pending confirmations for destructive actions.
/// Confirmations expire after 60 seconds.
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// Create a new pending confirmation with a GUID token.
    /// Automatically cleans up expired confirmations.
    /// Thread-safe: Lock-protected dictionary operations.
    /// </summary>
    PendingConfirmation Create(string action, string description, string? target, JsonElement? parameters);

    /// <summary>
    /// Consume a confirmation by token. Returns null if not found or expired.
    /// Removes the confirmation from pending state.
    /// Thread-safe: Lock-protected dictionary operations.
    /// </summary>
    PendingConfirmation? Consume(string token);
}

/// <summary>
/// Overload for testing with injectable time provider.
/// </summary>
public interface IConfirmationServiceTestable : IConfirmationService
{
    /// <summary>
    /// Inject a time provider for testing expiration logic.
    /// </summary>
    void SetTimeProvider(Func<DateTime> utcNowProvider);
}
```

### 3.6 ITreeExpansionService

```csharp
namespace Rhombus.WinFormsMcp.Server.Services;

/// <summary>
/// Thread-safe tracking of elements marked for progressive disclosure expansion.
/// Marked elements have their children expanded on next get_ui_tree regardless of depth.
/// </summary>
public interface ITreeExpansionService
{
    /// <summary>
    /// Mark an element for expansion by its AutomationId or Name.
    /// Thread-safe: Lock-protected HashSet operations.
    /// </summary>
    void Mark(string elementKey);

    /// <summary>
    /// Check if an element is marked for expansion.
    /// Thread-safe: Lock-protected lookup.
    /// </summary>
    bool IsMarked(string elementKey);

    /// <summary>
    /// Get all marked element keys.
    /// Thread-safe: Returns a snapshot copy.
    /// </summary>
    IReadOnlyCollection<string> GetAll();

    /// <summary>
    /// Remove expansion mark from a specific element.
    /// Thread-safe: Lock-protected HashSet operations.
    /// </summary>
    void Clear(string elementKey);

    /// <summary>
    /// Remove all expansion marks.
    /// Thread-safe: Lock-protected HashSet.Clear().
    /// </summary>
    void ClearAll();
}
```

---

## 4. Implementation Classes

### 4.1 ElementCache

```csharp
namespace Rhombus.WinFormsMcp.Server.Services;

using System.Collections.Concurrent;
using FlaUI.Core.AutomationElements;

/// <summary>
/// Thread-safe element cache using ConcurrentDictionary.
/// </summary>
internal sealed class ElementCache : IElementCache
{
    private readonly ConcurrentDictionary<string, AutomationElement> _cache = new();
    private int _nextId;

    public string Cache(AutomationElement element)
    {
        var id = $"elem_{Interlocked.Increment(ref _nextId)}";
        _cache[id] = element;
        return id;
    }

    public AutomationElement? Get(string elementId)
        => _cache.TryGetValue(elementId, out var elem) ? elem : null;

    public void Clear(string elementId)
        => _cache.TryRemove(elementId, out _);

    public void ClearAll()
        => _cache.Clear();

    public int Count => _cache.Count;
}
```

### 4.2 ProcessContext

```csharp
namespace Rhombus.WinFormsMcp.Server.Services;

using System.Collections.Concurrent;
using System.IO;

/// <summary>
/// Thread-safe launched app tracking using ConcurrentDictionary.
/// </summary>
internal sealed class ProcessContext : IProcessContext
{
    private readonly ConcurrentDictionary<string, int> _launchedApps = new();

    public int? TrackLaunchedApp(string exePath, int pid)
    {
        var normalized = NormalizePath(exePath);
        int? previous = _launchedApps.TryGetValue(normalized, out var old) ? old : null;
        _launchedApps[normalized] = pid;
        return previous;
    }

    public int? GetPreviousLaunchedPid(string exePath)
    {
        var normalized = NormalizePath(exePath);
        return _launchedApps.TryGetValue(normalized, out var pid) ? pid : null;
    }

    public void UntrackLaunchedApp(string exePath)
    {
        var normalized = NormalizePath(exePath);
        _launchedApps.TryRemove(normalized, out _);
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).ToLowerInvariant();
}
```

### 4.3 SnapshotCache

```csharp
namespace Rhombus.WinFormsMcp.Server.Services;

using System.Collections.Concurrent;
using Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Thread-safe snapshot cache using ConcurrentDictionary.
/// </summary>
internal sealed class SnapshotCache : ISnapshotCache
{
    private readonly ConcurrentDictionary<string, TreeSnapshot> _cache = new();

    public void Cache(string snapshotId, TreeSnapshot snapshot)
        => _cache[snapshotId] = snapshot;

    public TreeSnapshot? Get(string snapshotId)
        => _cache.TryGetValue(snapshotId, out var snapshot) ? snapshot : null;

    public void Clear(string snapshotId)
        => _cache.TryRemove(snapshotId, out _);

    public void ClearAll()
        => _cache.Clear();
}
```

### 4.4 EventService

```csharp
namespace Rhombus.WinFormsMcp.Server.Services;

/// <summary>
/// Thread-safe event service using explicit locking for compound operations.
/// </summary>
internal sealed class EventService : IEventService
{
    private const int MaxQueueSize = 10;
    private readonly HashSet<string> _subscriptions = new();
    private readonly Queue<UiEvent> _queue = new();
    private readonly object _lock = new();
    private int _droppedCount;

    public void Subscribe(IEnumerable<string> eventTypes)
    {
        lock (_lock)
        {
            foreach (var type in eventTypes)
                _subscriptions.Add(type.ToLowerInvariant());
        }
    }

    public IReadOnlyCollection<string> GetSubscribedEventTypes()
    {
        lock (_lock)
        {
            return _subscriptions.ToArray();
        }
    }

    public bool HasSubscriptions
    {
        get
        {
            lock (_lock)
            {
                return _subscriptions.Count > 0;
            }
        }
    }

    public void Enqueue(UiEvent evt)
    {
        lock (_lock)
        {
            if (!_subscriptions.Contains(evt.Type.ToLowerInvariant()))
                return;

            if (_queue.Count >= MaxQueueSize)
            {
                _queue.Dequeue();
                _droppedCount++;
            }
            _queue.Enqueue(evt);
        }
    }

    public (List<UiEvent> events, int droppedCount) Drain()
    {
        lock (_lock)
        {
            var events = _queue.ToList();
            var dropped = _droppedCount;
            _queue.Clear();
            _droppedCount = 0;
            return (events, dropped);
        }
    }
}
```

### 4.5 ConfirmationService

```csharp
namespace Rhombus.WinFormsMcp.Server.Services;

using System.Text.Json;

/// <summary>
/// Thread-safe confirmation service with testable time injection.
/// </summary>
internal sealed class ConfirmationService : IConfirmationServiceTestable
{
    private const int TimeoutSeconds = 60;
    private readonly Dictionary<string, PendingConfirmation> _pending = new();
    private readonly object _lock = new();
    private Func<DateTime> _utcNow = () => DateTime.UtcNow;

    public void SetTimeProvider(Func<DateTime> utcNowProvider)
    {
        _utcNow = utcNowProvider;
    }

    public PendingConfirmation Create(string action, string description, string? target, JsonElement? parameters)
    {
        lock (_lock)
        {
            CleanupExpired();

            var now = _utcNow();
            var confirmation = new PendingConfirmation
            {
                Token = Guid.NewGuid().ToString("N"),
                Action = action,
                Description = description,
                Target = target,
                Parameters = parameters,
                CreatedAt = now,
                ExpiresAt = now.AddSeconds(TimeoutSeconds)
            };

            _pending[confirmation.Token] = confirmation;
            return confirmation;
        }
    }

    public PendingConfirmation? Consume(string token)
    {
        lock (_lock)
        {
            CleanupExpired();

            if (!_pending.TryGetValue(token, out var confirmation))
                return null;

            _pending.Remove(token);

            if (confirmation.ExpiresAt < _utcNow())
                return null;

            return confirmation;
        }
    }

    private void CleanupExpired()
    {
        var now = _utcNow();
        var expired = _pending.Where(kvp => kvp.Value.ExpiresAt < now)
                              .Select(kvp => kvp.Key)
                              .ToList();

        foreach (var token in expired)
            _pending.Remove(token);
    }
}
```

### 4.6 TreeExpansionService

```csharp
namespace Rhombus.WinFormsMcp.Server.Services;

/// <summary>
/// Thread-safe tree expansion tracking using explicit locking.
/// </summary>
internal sealed class TreeExpansionService : ITreeExpansionService
{
    private readonly HashSet<string> _marked = new();
    private readonly object _lock = new();

    public void Mark(string elementKey)
    {
        lock (_lock)
        {
            _marked.Add(elementKey);
        }
    }

    public bool IsMarked(string elementKey)
    {
        lock (_lock)
        {
            return _marked.Contains(elementKey);
        }
    }

    public IReadOnlyCollection<string> GetAll()
    {
        lock (_lock)
        {
            return _marked.ToArray();
        }
    }

    public void Clear(string elementKey)
    {
        lock (_lock)
        {
            _marked.Remove(elementKey);
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _marked.Clear();
        }
    }
}
```

---

## 5. Dependency Injection Approach

### 5.1 Manual Composition (No DI Framework)

Per constraints, we use manual composition in `AutomationServer`:

```csharp
class AutomationServer
{
    // Services (owned by server)
    private readonly IElementCache _elementCache;
    private readonly IProcessContext _processContext;
    private readonly ISnapshotCache _snapshotCache;
    private readonly IEventService _eventService;
    private readonly IConfirmationService _confirmationService;
    private readonly ITreeExpansionService _treeExpansionService;

    // Legacy resources (lazy-initialized)
    private AutomationHelper? _automation;
    private SandboxManager? _sandboxManager;
    private StateChangeDetector? _stateChangeDetector;
    private readonly TreeCache _treeCache;

    // Facade for backward compatibility
    private readonly SessionManager _session;

    public AutomationServer()
    {
        // Create services
        _elementCache = new ElementCache();
        _processContext = new ProcessContext();
        _snapshotCache = new SnapshotCache();
        _eventService = new EventService();
        _confirmationService = new ConfirmationService();
        _treeExpansionService = new TreeExpansionService();
        _treeCache = new TreeCache();

        // Create facade wrapping services (for backward compatibility)
        _session = new SessionManager(
            _elementCache,
            _processContext,
            _snapshotCache,
            _eventService,
            _confirmationService,
            _treeExpansionService,
            () => GetAutomation(),
            () => GetSandboxManager(),
            () => GetStateChangeDetector(),
            _treeCache
        );

        // Register handlers (unchanged during migration)
        RegisterHandler(new ProcessHandlers(_session, _windowManager));
        // ... other handlers
    }

    private AutomationHelper GetAutomation()
        => _automation ??= new AutomationHelper();

    private SandboxManager GetSandboxManager()
        => _sandboxManager ??= new SandboxManager();

    private StateChangeDetector GetStateChangeDetector()
        => _stateChangeDetector ??= new StateChangeDetector();
}
```

### 5.2 SessionManager as Facade

During migration, SessionManager delegates to injected services:

```csharp
class SessionManager
{
    private readonly IElementCache _elementCache;
    private readonly IProcessContext _processContext;
    private readonly ISnapshotCache _snapshotCache;
    private readonly IEventService _eventService;
    private readonly IConfirmationService _confirmationService;
    private readonly ITreeExpansionService _treeExpansionService;
    private readonly Func<AutomationHelper> _automationFactory;
    private readonly Func<SandboxManager> _sandboxFactory;
    private readonly Func<StateChangeDetector> _detectorFactory;
    private readonly TreeCache _treeCache;

    public SessionManager(
        IElementCache elementCache,
        IProcessContext processContext,
        ISnapshotCache snapshotCache,
        IEventService eventService,
        IConfirmationService confirmationService,
        ITreeExpansionService treeExpansionService,
        Func<AutomationHelper> automationFactory,
        Func<SandboxManager> sandboxFactory,
        Func<StateChangeDetector> detectorFactory,
        TreeCache treeCache)
    {
        _elementCache = elementCache;
        _processContext = processContext;
        _snapshotCache = snapshotCache;
        _eventService = eventService;
        _confirmationService = confirmationService;
        _treeExpansionService = treeExpansionService;
        _automationFactory = automationFactory;
        _sandboxFactory = sandboxFactory;
        _detectorFactory = detectorFactory;
        _treeCache = treeCache;
    }

    // Delegate to services (backward-compatible API)
    [Obsolete("Use IElementCache.Cache directly")]
    public string CacheElement(AutomationElement element) => _elementCache.Cache(element);

    [Obsolete("Use IElementCache.Get directly")]
    public AutomationElement? GetElement(string id) => _elementCache.Get(id);

    // ... similar delegating methods for all existing SessionManager methods

    // Resource accessors (unchanged)
    public AutomationHelper GetAutomation() => _automationFactory();
    public SandboxManager GetSandboxManager() => _sandboxFactory();
    public StateChangeDetector GetStateChangeDetector() => _detectorFactory();
    public TreeCache GetTreeCache() => _treeCache;
}
```

---

## 6. Handler Integration

### 6.1 Phase 1: Facade Pattern (No Handler Changes)

Handlers continue using `Session.CacheElement()` etc. The `[Obsolete]` attribute produces compiler warnings guiding migration:

```csharp
// ElementHandlers.cs - unchanged during Phase 1
internal class ElementHandlers : HandlerBase
{
    private Task<JsonElement> FindElement(JsonElement args)
    {
        // Produces CS0618 warning: 'CacheElement is obsolete. Use IElementCache.Cache directly'
        var elementId = Session.CacheElement(element);
        return Success(("elementId", elementId), ...);
    }
}
```

### 6.2 Phase 2: Direct Service Injection (Optional)

After services are stable, handlers can receive services directly:

```csharp
// Updated HandlerBase with optional services
internal abstract class HandlerBase : IToolHandler
{
    protected readonly SessionManager Session;  // Legacy (deprecated)
    protected readonly WindowManager Windows;

    // New service accessors (populated during handler construction)
    protected IElementCache? ElementCache { get; init; }
    protected IEventService? EventService { get; init; }
    // ... other services

    protected HandlerBase(SessionManager session, WindowManager windows)
    {
        Session = session;
        Windows = windows;
    }
}

// Updated ElementHandlers after migration
internal class ElementHandlers : HandlerBase
{
    public ElementHandlers(
        SessionManager session,
        WindowManager windows,
        IElementCache elementCache)
        : base(session, windows)
    {
        ElementCache = elementCache;
    }

    private Task<JsonElement> FindElement(JsonElement args)
    {
        // Use service directly (no deprecation warning)
        var elementId = ElementCache!.Cache(element);
        return Success(("elementId", elementId), ...);
    }
}
```

### 6.3 Constructor Signature Evolution

| Phase | Handler Constructor | Notes |
|-------|---------------------|-------|
| Current | `(SessionManager, WindowManager)` | All handlers |
| Phase 1 | `(SessionManager, WindowManager)` | SessionManager wraps services |
| Phase 2 | `(SessionManager, WindowManager, ...services)` | Optional service injection |
| Phase 3 | `(...services, WindowManager)` | SessionManager removed |

---

## 7. Migration Path

### 7.1 Migration Phases

```
┌──────────────────────────────────────────────────────────────────┐
│                         Phase 1: Foundation                       │
│  - Create Services/ directory                                     │
│  - Implement 6 service interfaces and classes                     │
│  - Unit tests for each service                                    │
│  Duration: 1 PR                                                   │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│                       Phase 2: Integration                        │
│  - Refactor SessionManager to facade pattern                      │
│  - Inject services into SessionManager                            │
│  - Add [Obsolete] attributes to facade methods                    │
│  - Handlers unchanged (compile with warnings)                     │
│  Duration: 1 PR                                                   │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│                     Phase 3: Handler Migration                    │
│  - Update HandlerBase with optional service properties            │
│  - Migrate handlers one-by-one to use services directly           │
│  - Each handler migration is a separate commit                    │
│  Duration: 2-3 PRs (grouped by handler complexity)               │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│                        Phase 4: Cleanup                          │
│  - Remove SessionManager facade (all handlers migrated)           │
│  - Remove [Obsolete] methods                                      │
│  - Final documentation update                                     │
│  Duration: 1 PR                                                   │
└──────────────────────────────────────────────────────────────────┘
```

### 7.2 Detailed Task Breakdown

#### Phase 1 Tasks (Foundation)
1. Create `src/Rhombus.WinFormsMcp.Server/Services/` directory
2. Create `IElementCache.cs` with interface and `ElementCache` implementation
3. Create `IProcessContext.cs` with interface and `ProcessContext` implementation
4. Create `ISnapshotCache.cs` with interface and `SnapshotCache` implementation
5. Create `IEventService.cs` with interface and `EventService` implementation
6. Create `IConfirmationService.cs` with interface and `ConfirmationService` implementation
7. Create `ITreeExpansionService.cs` with interface and `TreeExpansionService` implementation
8. Create `tests/Rhombus.WinFormsMcp.Tests/Services/` directory
9. Create unit tests for each service (6 test files)

#### Phase 2 Tasks (Integration)
1. Update SessionManager constructor to accept service interfaces
2. Modify SessionManager methods to delegate to services
3. Add `[Obsolete]` attributes to all delegating methods
4. Update AutomationServer to compose services and inject into SessionManager
5. Verify all existing tests pass

#### Phase 3 Tasks (Handler Migration)
1. Add optional service properties to HandlerBase
2. Migrate ElementHandlers (uses IElementCache)
3. Migrate AdvancedHandlers (uses IEventService, IConfirmationService, ITreeExpansionService)
4. Migrate ObservationHandlers (uses ISnapshotCache)
5. Migrate ProcessHandlers (uses IProcessContext)
6. Migrate remaining handlers (ValidationHandlers, InputHandlers, etc.)

#### Phase 4 Tasks (Cleanup)
1. Remove SessionManager class
2. Update AutomationServer to inject services directly to handlers
3. Remove HandlerBase.Session property
4. Update documentation

### 7.3 Backward Compatibility Guarantees

| Guarantee | Mechanism |
|-----------|-----------|
| Handlers compile without changes | SessionManager facade delegates to services |
| No runtime behavior change | Services implement identical logic |
| Gradual migration | [Obsolete] warnings, not errors |
| Rollback possible | Each phase is a separate PR |

---

## 8. File Organization

### 8.1 New Directory Structure

```
src/Rhombus.WinFormsMcp.Server/
├── Services/
│   ├── IElementCache.cs          # Interface + ElementCache implementation
│   ├── IProcessContext.cs        # Interface + ProcessContext implementation
│   ├── ISnapshotCache.cs         # Interface + SnapshotCache implementation
│   ├── IEventService.cs          # Interface + EventService implementation
│   ├── IConfirmationService.cs   # Interface + ConfirmationService implementation
│   └── ITreeExpansionService.cs  # Interface + TreeExpansionService implementation
├── Handlers/
│   └── (unchanged)
├── Program.cs                    # SessionManager becomes facade, then removed
└── ...

tests/Rhombus.WinFormsMcp.Tests/
├── Services/
│   ├── ElementCacheTests.cs
│   ├── ProcessContextTests.cs
│   ├── SnapshotCacheTests.cs
│   ├── EventServiceTests.cs
│   ├── ConfirmationServiceTests.cs
│   └── TreeExpansionServiceTests.cs
└── ...
```

### 8.2 File Size Targets

| File | Target Lines | Notes |
|------|--------------|-------|
| `IElementCache.cs` | ~50 | Interface + simple impl |
| `IProcessContext.cs` | ~40 | Interface + path normalization |
| `ISnapshotCache.cs` | ~40 | Interface + simple impl |
| `IEventService.cs` | ~80 | Interface + queue logic |
| `IConfirmationService.cs` | ~100 | Interface + expiration logic |
| `ITreeExpansionService.cs` | ~50 | Interface + simple impl |

---

## 9. Testing Strategy

### 9.1 Unit Test Structure

Each service test file follows this pattern:

```csharp
namespace Rhombus.WinFormsMcp.Tests.Services;

[TestFixture]
public class ElementCacheTests
{
    private IElementCache _cache = null!;

    [SetUp]
    public void Setup()
    {
        _cache = new ElementCache();
    }

    [Test]
    public void Cache_ReturnsSequentialIds()
    {
        // Arrange
        var element1 = CreateMockElement();
        var element2 = CreateMockElement();

        // Act
        var id1 = _cache.Cache(element1);
        var id2 = _cache.Cache(element2);

        // Assert
        Assert.That(id1, Is.EqualTo("elem_1"));
        Assert.That(id2, Is.EqualTo("elem_2"));
    }

    [Test]
    public void Get_ReturnsNull_WhenElementNotCached()
    {
        Assert.That(_cache.Get("elem_999"), Is.Null);
    }

    // ... more tests
}
```

### 9.2 Thread Safety Tests

```csharp
[Test]
public void Cache_IsThreadSafe_UnderConcurrentAccess()
{
    var cache = new ElementCache();
    var ids = new ConcurrentBag<string>();
    var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
    {
        var element = CreateMockElement();
        var id = cache.Cache(element);
        ids.Add(id);
    })).ToArray();

    Task.WaitAll(tasks);

    // All IDs should be unique
    Assert.That(ids.Distinct().Count(), Is.EqualTo(100));
    Assert.That(cache.Count, Is.EqualTo(100));
}
```

### 9.3 Confirmation Service Time Testing

```csharp
[Test]
public void Consume_ReturnsNull_WhenExpired()
{
    var service = new ConfirmationService();
    var currentTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    // Inject mock time
    service.SetTimeProvider(() => currentTime);

    var confirmation = service.Create("close_app", "Test", null, null);

    // Advance time past expiration
    currentTime = currentTime.AddSeconds(61);

    // Should return null (expired)
    Assert.That(service.Consume(confirmation.Token), Is.Null);
}
```

---

## 10. Risk Mitigation

### 10.1 Thread Safety Risks

| Risk | Mitigation |
|------|------------|
| Race condition in ID generation | Use `Interlocked.Increment` |
| Queue corruption during concurrent enqueue/drain | Use explicit `lock` around compound operations |
| Dictionary corruption during concurrent access | Use `ConcurrentDictionary` |

### 10.2 Migration Risks

| Risk | Mitigation |
|------|------------|
| Breaking handler compilation | Facade pattern preserves API |
| Introducing bugs during refactor | Comprehensive unit tests before migration |
| Performance regression | Benchmark critical paths (CacheElement, Get) |

### 10.3 Rollback Plan

Each phase is a separate PR. If issues arise:
1. Revert the problematic PR
2. Fix issues in isolation
3. Re-apply with fixes

---

## 11. Success Criteria Verification

| Criterion | Verification Method |
|-----------|---------------------|
| Each service testable with <5 mocks | Count mock objects in test setup |
| Service files <150 lines | `wc -l Services/*.cs` |
| No circular dependencies | Build succeeds, no CS0246 errors |
| Thread-safe under concurrent access | Thread safety unit tests pass |
| Handlers compile without modification | Build succeeds with only CS0618 warnings |
| Migration reversible | Each phase is separate PR |

---

## 12. Approval Checklist

Before proceeding to Phase 3 (Tasks), please confirm:

- [ ] Service interfaces accurately capture current SessionManager responsibilities
- [ ] Thread-safety approach (ConcurrentDictionary + locks) is acceptable
- [ ] Facade pattern for backward compatibility is acceptable
- [ ] File organization (interfaces + implementations in same file) is acceptable
- [ ] Migration phases are correctly scoped
- [ ] Testing strategy is sufficient

---

## Appendix A: Current vs. New Code Mapping

| Current Location | New Location |
|-----------------|--------------|
| `SessionManager._elementCache` | `ElementCache._cache` |
| `SessionManager._nextElementId` | `ElementCache._nextId` |
| `SessionManager._launchedAppsByPath` | `ProcessContext._launchedApps` |
| `SessionManager._snapshotCache` | `SnapshotCache._cache` |
| `SessionManager._eventQueue` | `EventService._queue` |
| `SessionManager._subscribedEventTypes` | `EventService._subscriptions` |
| `SessionManager._eventsDropped` | `EventService._droppedCount` |
| `SessionManager._pendingConfirmations` | `ConfirmationService._pending` |
| `SessionManager._expandedElements` | `TreeExpansionService._marked` |
