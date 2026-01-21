# SessionManager Refactoring - Implementation Tasks

> **⚠️ ARCHIVED**: This spec has been superseded by `unified-refactor/`. All tasks from this spec were consolidated and completed in the unified refactoring plan.

## Overview

This task list implements the design from `design.md`. The refactoring is split across multiple PRs for incremental migration and safe rollback.

**Feedback incorporated:**
- Capacity limits added to caches (simple max count with LRU eviction)
- Handler modification is in-scope (switching to new services)
- Incremental migration across multiple PRs

---

## PR 1: Foundation - Service Extraction

### Phase 1A: Create Services Directory and Interfaces

- [ ] **Create Services directory**
  - Path: `src/Rhombus.WinFormsMcp.Server/Services/`
  - Empty directory to contain all new service files

- [ ] **Create IElementCache.cs**
  - Path: `src/Rhombus.WinFormsMcp.Server/Services/IElementCache.cs`
  - Interface with `Cache()`, `Get()`, `Clear()`, `ClearAll()`, `Count`
  - Implementation class `ElementCache` using `ConcurrentDictionary`
  - Use `Interlocked.Increment` for thread-safe ID generation
  - Add capacity limit (default: 1000 elements, LRU eviction when full)
  - ~80 lines total (interface + implementation + LRU logic)

- [ ] **Create IProcessContext.cs**
  - Path: `src/Rhombus.WinFormsMcp.Server/Services/IProcessContext.cs`
  - Interface with `TrackLaunchedApp()`, `GetPreviousLaunchedPid()`, `UntrackLaunchedApp()`
  - Implementation class `ProcessContext` using `ConcurrentDictionary`
  - Path normalization via `Path.GetFullPath().ToLowerInvariant()`
  - ~50 lines total

- [ ] **Create ISnapshotCache.cs**
  - Path: `src/Rhombus.WinFormsMcp.Server/Services/ISnapshotCache.cs`
  - Interface with `Cache()`, `Get()`, `Clear()`, `ClearAll()`
  - Implementation class `SnapshotCache` using `ConcurrentDictionary`
  - Add capacity limit (default: 50 snapshots, LRU eviction when full)
  - ~70 lines total

- [ ] **Create IEventService.cs**
  - Path: `src/Rhombus.WinFormsMcp.Server/Services/IEventService.cs`
  - Interface with `Subscribe()`, `GetSubscribedEventTypes()`, `HasSubscriptions`, `Enqueue()`, `Drain()`
  - Implementation class `EventService` with explicit `lock` for compound operations
  - Queue capacity: 10 events (FIFO eviction, already in design)
  - ~100 lines total

- [ ] **Create IConfirmationService.cs**
  - Path: `src/Rhombus.WinFormsMcp.Server/Services/IConfirmationService.cs`
  - Interface with `Create()`, `Consume()`
  - `IConfirmationServiceTestable` extends with `SetTimeProvider()`
  - Implementation class `ConfirmationService` with 60-second expiration
  - Add capacity limit (default: 100 pending confirmations, reject new if full)
  - ~120 lines total

- [ ] **Create ITreeExpansionService.cs**
  - Path: `src/Rhombus.WinFormsMcp.Server/Services/ITreeExpansionService.cs`
  - Interface with `Mark()`, `IsMarked()`, `GetAll()`, `Clear()`, `ClearAll()`
  - Implementation class `TreeExpansionService` with explicit `lock`
  - ~60 lines total

### Phase 1B: Create Supporting Types

- [ ] **Create UiEvent.cs (if not exists)**
  - Path: `src/Rhombus.WinFormsMcp.Server/Services/UiEvent.cs`
  - Record type for event data: `Type`, `Timestamp`, `Details`
  - Move/consolidate from existing location if already defined elsewhere

- [ ] **Create PendingConfirmation.cs (if not exists)**
  - Path: `src/Rhombus.WinFormsMcp.Server/Services/PendingConfirmation.cs`
  - Record type: `Token`, `Action`, `Description`, `Target`, `Parameters`, `CreatedAt`, `ExpiresAt`
  - Move/consolidate from existing location if already defined elsewhere

### PR 1 Verification

- [ ] Solution builds successfully (`dotnet build`)
- [ ] All services compile independently
- [ ] No circular dependencies between services
- [ ] Each service file is under 150 lines

---

## PR 2: Integration - SessionManager Facade

### Phase 2A: Refactor SessionManager Constructor

- [ ] **Update SessionManager to accept service interfaces**
  - Path: `src/Rhombus.WinFormsMcp.Server/Program.cs` (SessionManager class)
  - Add constructor parameters for all 6 services
  - Store services as private readonly fields
  - Keep factory delegates for lazy resources (AutomationHelper, SandboxManager, StateChangeDetector)

### Phase 2B: Delegate to Services

- [ ] **Delegate element cache operations**
  - `CacheElement()` -> `_elementCache.Cache()`
  - `GetElement()` -> `_elementCache.Get()`
  - `ClearElement()` -> `_elementCache.Clear()`
  - `ClearAllElements()` -> `_elementCache.ClearAll()`
  - Add `[Obsolete("Use IElementCache directly")]` to each

- [ ] **Delegate process context operations**
  - `TrackLaunchedApp()` -> `_processContext.TrackLaunchedApp()`
  - `GetPreviousLaunchedPid()` -> `_processContext.GetPreviousLaunchedPid()`
  - Add `[Obsolete("Use IProcessContext directly")]` to each

- [ ] **Delegate snapshot cache operations**
  - `CacheSnapshot()` -> `_snapshotCache.Cache()`
  - `GetSnapshot()` -> `_snapshotCache.Get()`
  - Add `[Obsolete("Use ISnapshotCache directly")]` to each

- [ ] **Delegate event service operations**
  - `SubscribeToEvents()` -> `_eventService.Subscribe()`
  - `EnqueueEvent()` -> `_eventService.Enqueue()`
  - `DrainEvents()` -> `_eventService.Drain()`
  - Add `[Obsolete("Use IEventService directly")]` to each

- [ ] **Delegate confirmation service operations**
  - `CreateConfirmation()` -> `_confirmationService.Create()`
  - `ConsumeConfirmation()` -> `_confirmationService.Consume()`
  - Add `[Obsolete("Use IConfirmationService directly")]` to each

- [ ] **Delegate tree expansion operations**
  - `MarkForExpansion()` -> `_treeExpansionService.Mark()`
  - `IsMarkedForExpansion()` -> `_treeExpansionService.IsMarked()`
  - `ClearExpansionMark()` -> `_treeExpansionService.Clear()`
  - Add `[Obsolete("Use ITreeExpansionService directly")]` to each

### Phase 2C: Update AutomationServer Composition

- [ ] **Instantiate services in AutomationServer**
  - Path: `src/Rhombus.WinFormsMcp.Server/Program.cs` (AutomationServer class)
  - Create instances of all 6 service implementations
  - Inject into SessionManager constructor
  - Keep services as private fields for future handler injection

### PR 2 Verification

- [ ] Solution builds with only CS0618 (obsolete) warnings
- [ ] All existing tests pass (`dotnet test`)
- [ ] No runtime behavior changes (manual smoke test)
- [ ] Handlers compile without modification

---

## PR 3: Handler Migration - Part 1 (Core Handlers)

### Phase 3A: Update HandlerBase

- [ ] **Add optional service properties to HandlerBase**
  - Path: `src/Rhombus.WinFormsMcp.Server/Handlers/HandlerBase.cs`
  - Add `protected IElementCache? ElementCache { get; init; }`
  - Add `protected IProcessContext? ProcessContext { get; init; }`
  - Add `protected ISnapshotCache? SnapshotCache { get; init; }`
  - Add `protected IEventService? EventService { get; init; }`
  - Add `protected IConfirmationService? ConfirmationService { get; init; }`
  - Add `protected ITreeExpansionService? TreeExpansionService { get; init; }`
  - Keep `Session` property for backward compatibility

### Phase 3B: Migrate ElementHandlers

- [ ] **Update ElementHandlers to use IElementCache**
  - Path: `src/Rhombus.WinFormsMcp.Server/Handlers/ElementHandlers.cs`
  - Add `IElementCache` constructor parameter
  - Replace `Session.CacheElement()` with `ElementCache.Cache()`
  - Replace `Session.GetElement()` with `ElementCache.Get()`
  - Methods affected: `FindElement`, `GetProperty`, `ClickByAutomationId`, `FindElementNearAnchor`

### Phase 3C: Migrate ProcessHandlers

- [ ] **Update ProcessHandlers to use IProcessContext**
  - Path: `src/Rhombus.WinFormsMcp.Server/Handlers/ProcessHandlers.cs`
  - Add `IProcessContext` constructor parameter
  - Replace `Session.TrackLaunchedApp()` with `ProcessContext.TrackLaunchedApp()`
  - Replace `Session.GetPreviousLaunchedPid()` with `ProcessContext.GetPreviousLaunchedPid()`
  - Methods affected: `LaunchApp`, `CloseApp`

### Phase 3D: Update AutomationServer Handler Registration

- [ ] **Inject services into migrated handlers**
  - Path: `src/Rhombus.WinFormsMcp.Server/Program.cs`
  - Update `ElementHandlers` instantiation with `IElementCache`
  - Update `ProcessHandlers` instantiation with `IProcessContext`

### PR 3 Verification

- [ ] Solution builds successfully
- [ ] No CS0618 warnings from ElementHandlers or ProcessHandlers
- [ ] All existing tests pass
- [ ] E2E tests pass (element caching, app launching)

---

## PR 4: Handler Migration - Part 2 (Observation/Advanced)

### Phase 4A: Migrate ObservationHandlers

- [ ] **Update ObservationHandlers to use ISnapshotCache and ITreeExpansionService**
  - Path: `src/Rhombus.WinFormsMcp.Server/Handlers/ObservationHandlers.cs`
  - Add `ISnapshotCache` and `ITreeExpansionService` constructor parameters
  - Replace `Session.CacheSnapshot()` with `SnapshotCache.Cache()`
  - Replace `Session.GetSnapshot()` with `SnapshotCache.Get()`
  - Replace `Session.MarkForExpansion()` with `TreeExpansionService.Mark()`
  - Replace `Session.IsMarkedForExpansion()` with `TreeExpansionService.IsMarked()`
  - Methods affected: `CaptureUiSnapshot`, `CompareUiSnapshots`, `GetUiTree`, `MarkForExpansion`, `ClearExpansionMarks`

### Phase 4B: Migrate AdvancedHandlers

- [ ] **Update AdvancedHandlers to use IEventService and IConfirmationService**
  - Path: `src/Rhombus.WinFormsMcp.Server/Handlers/AdvancedHandlers.cs`
  - Add `IEventService` and `IConfirmationService` constructor parameters
  - Replace `Session.SubscribeToEvents()` with `EventService.Subscribe()`
  - Replace `Session.DrainEvents()` with `EventService.Drain()`
  - Replace `Session.CreateConfirmation()` with `ConfirmationService.Create()`
  - Replace `Session.ConsumeConfirmation()` with `ConfirmationService.Consume()`
  - Methods affected: `SubscribeToEvents`, `GetPendingEvents`, `ConfirmAction`, `ExecuteConfirmedAction`

### Phase 4C: Update AutomationServer for Remaining Handlers

- [ ] **Inject services into ObservationHandlers and AdvancedHandlers**
  - Path: `src/Rhombus.WinFormsMcp.Server/Program.cs`
  - Update handler instantiations with appropriate services

### PR 4 Verification

- [ ] Solution builds successfully
- [ ] No CS0618 warnings from ObservationHandlers or AdvancedHandlers
- [ ] All existing tests pass
- [ ] Event subscription and confirmation flows work correctly

---

## PR 5: Cleanup - Remove SessionManager Facade

### Phase 5A: Verify All Handlers Migrated

- [ ] **Audit remaining Session usage**
  - Grep for `Session.` in all handler files
  - Ensure only resource accessors remain (GetAutomation, GetSandboxManager, etc.)

### Phase 5B: Extract Resource Accessors

- [ ] **Move resource accessors to AutomationServer or dedicated service**
  - AutomationHelper, SandboxManager, StateChangeDetector, TreeCache
  - These remain lazy-initialized in AutomationServer
  - Handlers access via injected interface or direct property

### Phase 5C: Remove SessionManager

- [ ] **Delete SessionManager class**
  - Path: `src/Rhombus.WinFormsMcp.Server/Program.cs`
  - Remove the entire SessionManager class definition
  - Remove `[Obsolete]` attributes (no longer needed)

- [ ] **Update HandlerBase**
  - Remove `Session` property
  - All handlers now use only service interfaces

- [ ] **Final handler constructor cleanup**
  - Remove `SessionManager` parameter from all handler constructors
  - Ensure all handlers receive only the services they need

### PR 5 Verification

- [ ] Solution builds without CS0618 warnings
- [ ] All tests pass
- [ ] No references to SessionManager remain
- [ ] Each handler has minimal constructor parameters

---

## Capacity Limits Summary

| Service | Limit | Eviction Strategy |
|---------|-------|-------------------|
| IElementCache | 1000 elements | LRU (track access time, evict oldest) |
| ISnapshotCache | 50 snapshots | LRU (track access time, evict oldest) |
| IConfirmationService | 100 pending | Reject new (return error) |
| IEventService | 10 events | FIFO (drop oldest on overflow) |
| IProcessContext | Unbounded | N/A (one entry per unique exe path) |
| ITreeExpansionService | Unbounded | N/A (cleared after each tree request) |

---

## Definition of Done

Each PR must satisfy:

1. **Builds clean**: No errors, only expected warnings
2. **Tests pass**: `dotnet test` succeeds
3. **No regressions**: Existing functionality unchanged
4. **Code review**: Approved by maintainer
5. **CI passes**: GitHub Actions workflow succeeds

---

## Rollback Plan

If issues arise after merging:

1. **Identify problematic PR** via git bisect or review
2. **Revert PR** using `git revert`
3. **Fix in isolation** on a new branch
4. **Re-apply** after thorough testing

Each PR is designed to be independently revertible without affecting other PRs.
