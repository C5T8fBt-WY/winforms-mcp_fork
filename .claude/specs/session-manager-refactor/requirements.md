# SessionManager God Object Refactoring - Requirements

## 1. Introduction

This specification defines the requirements for refactoring the `SessionManager` class in `src/Rhombus.WinFormsMcp.Server/Program.cs` to comply with the Single Responsibility Principle (SRP).

**Current State**: SessionManager has 11 distinct responsibilities in a single 256-line class, making it difficult to test, maintain, and reason about. All responsibilities are coupled through shared mutable state.

**Target State**: A set of focused, single-responsibility services that can be independently tested and injected into handlers. SessionManager becomes a thin facade or is eliminated entirely.

---

## 2. Problem Analysis

### 2.1 Current Responsibilities

The SessionManager class currently manages:

| # | Responsibility | Lines | State Fields | Thread Safety | Cohesion |
|---|---------------|-------|--------------|---------------|----------|
| 1 | Element cache (AutomationElement refs) | 234-249 | `_elementCache`, `_nextElementId` | None | High with #9 |
| 2 | Process context tracking | 251-254 | `_processContext` | None | Low |
| 3 | Snapshot cache | 289-302 | `_snapshotCache` | None | High with #9 |
| 4 | Launched apps tracking | 203, 256-288 | `_launchedAppsByPath` | None | High with #2 |
| 5 | Event queue | 204, 323-346 | `_eventQueue`, `_eventsDropped` | None | High with #6 |
| 6 | Event subscriptions | 205, 307-321, 351 | `_subscribedEventTypes` | None | High with #5 |
| 7 | Pending confirmations | 206, 353-409 | `_pendingConfirmations` | None | Low |
| 8 | Expanded elements tracking | 207, 411-448 | `_expandedElements` | None | Low |
| 9 | AutomationHelper lifecycle | 213, 217-220 | `_automation` | Lazy init | High with #1, #3 |
| 10 | SandboxManager lifecycle | 214, 222-225 | `_sandboxManager` | Lazy init | Low |
| 11 | TreeCache access | 208, 232 | `_treeCache` | Delegated | Low |

### 2.2 Code Smell Indicators

**2.2.1** WHEN a class has more than 3 related data fields, it likely violates SRP
- **Observation**: SessionManager has 12 distinct data fields across 11 responsibilities
- **Impact**: Changes to event handling require modifying a class that also manages element caching

**2.2.2** WHEN methods operate on disjoint subsets of state, they belong in separate classes
- **Observation**: `CreateConfirmation()` touches only `_pendingConfirmations`; `CacheElement()` touches only `_elementCache` and `_nextElementId`
- **Impact**: No cohesion between confirmation flow and element caching

**2.2.3** WHEN a class requires 11 test doubles to unit test one method, the class is too large
- **Observation**: Testing `ConsumeConfirmation()` requires instantiating SessionManager with all its dependencies
- **Impact**: Tests are slow, brittle, and test more than intended

---

## 3. User Stories

### 3.1 Testability

**User Story 3.1**: As a developer writing unit tests, I want each service to have focused, isolated responsibilities, so that I can test individual behaviors without mocking unrelated state.

**3.1.1** WHEN testing element caching logic, the test SHALL NOT require event queue or confirmation state
- **Acceptance Criteria**:
  - Element caching service can be instantiated without event service
  - Element caching tests mock only AutomationHelper, not confirmations
  - Test setup code is <10 lines (vs. current ~30 lines)

**3.1.2** WHEN testing confirmation workflow, the test SHALL NOT require AutomationElement references
- **Acceptance Criteria**:
  - Confirmation service can be instantiated independently
  - Time-based expiration is testable via mock clock
  - No FlaUI dependencies in confirmation tests

### 3.2 Maintainability

**User Story 3.2**: As a maintainer, I want each service to have clear boundaries, so that changes in one area don't ripple to unrelated code.

**3.2.1** WHEN adding a new event type, changes SHALL be isolated to event-related code
- **Acceptance Criteria**:
  - Event service encapsulates queue, subscriptions, and filtering
  - Adding event type requires no changes to element caching
  - Event service has its own unit test file

**3.2.2** WHEN modifying element cache eviction policy, changes SHALL NOT affect confirmation timeouts
- **Acceptance Criteria**:
  - Element cache service owns cache policy
  - Confirmation service owns timeout policy
  - No shared timeout constants between services

### 3.3 Thread Safety

**User Story 3.3**: As a developer supporting concurrent TCP clients, I want each service to declare and enforce its thread-safety guarantees.

**3.3.1** WHEN multiple clients access element cache concurrently, operations SHALL be thread-safe
- **Acceptance Criteria**:
  - Element cache uses `ConcurrentDictionary` or explicit locking
  - Thread-safety is documented in service interface
  - Race conditions in caching are eliminated

**3.3.2** WHEN event queue is drained while events are being added, operations SHALL be atomic
- **Acceptance Criteria**:
  - Event service uses `ConcurrentQueue` or lock-protected queue
  - `DrainEventQueue()` and `EnqueueEvent()` don't interleave incorrectly
  - Events dropped count is accurate under concurrent access

### 3.4 Backward Compatibility

**User Story 3.4**: As a developer maintaining handlers, I want the refactoring to be incremental, so that existing code continues to work during migration.

**3.4.1** WHEN a handler uses `Session.CacheElement()`, it SHALL continue to work during migration
- **Acceptance Criteria**:
  - SessionManager facade delegates to new services
  - Handlers compile without changes during transition
  - Deprecation warnings guide migration path

**3.4.2** WHEN all handlers are migrated, the SessionManager facade SHALL be removable
- **Acceptance Criteria**:
  - Handlers receive services via constructor injection
  - SessionManager is optional, not required
  - Clean removal leaves no orphan code

---

## 4. Requirements (EARS Format)

### 4.1 Element Cache Service

**REQ-4.1.1** WHEN the system starts, an `IElementCache` service SHALL be available for storing AutomationElement references
- **Interface Methods**:
  - `string Cache(AutomationElement element)` - returns `elem_N` ID
  - `AutomationElement? Get(string elementId)` - returns null if not found
  - `void Clear(string elementId)` - removes single element
  - `void ClearAll()` - removes all cached elements
- **Thread Safety**: All methods SHALL be thread-safe

**REQ-4.1.2** WHEN an element is cached, it SHALL receive a unique sequential ID
- **Format**: `elem_{N}` where N is monotonically increasing
- **ID Counter**: SHALL be thread-safe (Interlocked.Increment)

### 4.2 Process Context Service

**REQ-4.2.1** WHEN the system tracks launched applications, a `IProcessContext` service SHALL manage PID tracking
- **Interface Methods**:
  - `int? TrackLaunchedApp(string exePath, int pid)` - returns previous PID if any
  - `int? GetPreviousLaunchedPid(string exePath)` - lookup by path
  - `void UntrackLaunchedApp(string exePath)` - remove tracking
- **Path Normalization**: All paths SHALL be normalized to lowercase full paths
- **Thread Safety**: All methods SHALL be thread-safe

### 4.3 Snapshot Cache Service

**REQ-4.3.1** WHEN the system captures UI snapshots, an `ISnapshotCache` service SHALL store TreeSnapshot instances
- **Interface Methods**:
  - `void Cache(string snapshotId, TreeSnapshot snapshot)` - store snapshot
  - `TreeSnapshot? Get(string snapshotId)` - retrieve by ID
  - `void Clear(string snapshotId)` - remove single snapshot
  - `void ClearAll()` - remove all snapshots
- **Thread Safety**: All methods SHALL be thread-safe

### 4.4 Event Service

**REQ-4.4.1** WHEN the system monitors UI events, an `IEventService` service SHALL manage subscriptions and queue
- **Interface Methods**:
  - `void Subscribe(IEnumerable<string> eventTypes)` - add subscriptions
  - `IReadOnlyCollection<string> GetSubscribedEventTypes()` - list subscriptions
  - `bool HasSubscriptions { get; }` - check if any subscriptions active
  - `void Enqueue(UiEvent evt)` - add event if subscribed
  - `(List<UiEvent> events, int droppedCount) Drain()` - get all events and clear
- **Queue Capacity**: 10 events (FIFO eviction)
- **Thread Safety**: All methods SHALL be thread-safe

### 4.5 Confirmation Service

**REQ-4.5.1** WHEN handlers require confirmation for destructive actions, an `IConfirmationService` service SHALL manage pending confirmations
- **Interface Methods**:
  - `PendingConfirmation Create(string action, string description, string? target, JsonElement? parameters)` - create token
  - `PendingConfirmation? Consume(string token)` - get and remove if valid
- **Token Generation**: GUID-based, cryptographically unique
- **Expiration**: 60 seconds from creation
- **Cleanup**: Expired confirmations removed on Create/Consume
- **Thread Safety**: All methods SHALL be thread-safe

### 4.6 Tree Expansion Service

**REQ-4.6.1** WHEN agents drill into specific UI areas, an `ITreeExpansionService` service SHALL track elements marked for expansion
- **Interface Methods**:
  - `void Mark(string elementKey)` - mark for expansion
  - `bool IsMarked(string elementKey)` - check if marked
  - `IReadOnlyCollection<string> GetAll()` - get all marked
  - `void Clear(string elementKey)` - unmark single element
  - `void ClearAll()` - unmark all elements
- **Thread Safety**: All methods SHALL be thread-safe

### 4.7 Service Lifecycle Management

**REQ-4.7.1** WHEN services require expensive resources, an `IServiceProvider` or composed root SHALL manage lazy initialization
- **Resources**:
  - `AutomationHelper` - FlaUI automation instance
  - `SandboxManager` - Windows Sandbox manager
  - `StateChangeDetector` - Tree comparison utility
  - `TreeCache` - Cached UI tree
- **Lifetime**: Singleton per MCP server instance
- **Disposal**: All IDisposable services SHALL be disposed on server shutdown

---

## 5. Non-Functional Requirements

### 5.1 Performance

**NFR-5.1.1** WHEN `IElementCache.Get()` is called, it SHALL complete in <1ms (dictionary lookup)

**NFR-5.1.2** WHEN `IEventService.Enqueue()` is called, it SHALL complete in <1ms

**NFR-5.1.3** WHEN services are instantiated, lazy initialization SHALL defer expensive operations (FlaUI, Sandbox)

### 5.2 Thread Safety

**NFR-5.2.1** All services SHALL be safe for concurrent access from multiple TCP clients

**NFR-5.2.2** Services SHALL use `ConcurrentDictionary`, `ConcurrentQueue`, or explicit `lock` statements

**NFR-5.2.3** Thread safety SHALL be documented in interface XML comments

### 5.3 Testability

**NFR-5.3.1** All services SHALL be interface-based to support mocking

**NFR-5.3.2** Services SHALL NOT depend on concrete implementations of other services

**NFR-5.3.3** Time-dependent logic (confirmations) SHALL accept `Func<DateTime>` or `ITimeProvider` for testing

---

## 6. Constraints

### 6.1 Incremental Migration

**CON-6.1.1** The refactoring SHALL be incremental - existing handlers MUST work after each commit

**CON-6.1.2** SessionManager SHALL remain as facade during migration to prevent breaking changes

**CON-6.1.3** Each extracted service SHALL be usable before all extractions are complete

### 6.2 Dependency Direction

**CON-6.2.1** Services SHALL NOT depend on SessionManager (no circular dependencies)

**CON-6.2.2** HandlerBase MAY depend on services directly after migration

**CON-6.2.3** Services MAY depend on other services only via interfaces

### 6.3 File Organization

**CON-6.3.1** Services SHALL be placed in `src/Rhombus.WinFormsMcp.Server/Services/` directory

**CON-6.3.2** Each service SHALL have its own file: `{ServiceName}.cs`

**CON-6.3.3** Interfaces SHALL be in same file as implementation (internal interfaces)

---

## 7. Success Criteria

This refactoring is successful when:

**Testability:**
1. Each service can be unit tested with <5 mock dependencies
2. Element cache tests don't require event service mocks
3. Confirmation service tests can control time via injection
4. Test files are <200 lines each

**Maintainability:**
5. Adding new event type requires changes to only event service
6. Each service file is <150 lines
7. Service responsibilities are obvious from interface names

**Thread Safety:**
8. Concurrent TCP clients can cache elements without corruption
9. Event queue drain is atomic under concurrent enqueue
10. No race conditions detected in stress tests

**Backward Compatibility:**
11. All existing handlers compile without modification during migration
12. SessionManager facade delegates to services transparently
13. Migration can be done in multiple PRs without feature flags

**Architecture:**
14. No circular dependencies between services
15. Services folder contains 6-7 focused files
16. SessionManager is either a thin facade or removed entirely

---

## 8. Out of Scope

The following are explicitly OUT OF SCOPE for this refactoring:

1. **Handler Refactoring**: Handlers will continue using SessionManager facade initially
2. **Dependency Injection Container**: Manual composition, no DI framework
3. **New Features**: No new capabilities, only restructuring existing code
4. **Protocol Changes**: MCP tool interfaces remain unchanged
5. **Test Coverage Expansion**: Focus is structure, not adding new tests (though some tests may be added for new services)

---

## 9. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Breaking existing handlers | Medium | High | Facade pattern, incremental migration |
| Thread safety regressions | Medium | High | Use proven concurrent collections |
| Performance degradation | Low | Medium | Benchmark before/after, use same algorithms |
| Over-engineering | Low | Medium | Limit to 6-7 services, no frameworks |
