---
status: pending
priority: p1
issue_id: "004"
tags: [code-review, data-integrity, memory]
dependencies: []
---

# Unbounded Element Cache Causes Memory Leak

## Problem Statement

The `SessionManager._elementCache` grows indefinitely without any eviction policy. Long-running sessions accumulate stale COM references.

**Why it matters:** Memory bloat, stale UIA references cause errors, potential crashes.

## Findings

**Source:** performance-oracle, data-integrity-guardian

**Location:** `src/Rhombus.WinFormsMcp.Server/Program.cs` (SessionManager class)

```csharp
private readonly Dictionary<string, AutomationElement> _elementCache = new();
private int _nextElementId = 1;

public string CacheElement(AutomationElement element)
{
    var id = $"elem_{_nextElementId++}";
    _elementCache[id] = element;  // Never expires, never evicted
    return id;
}
```

**Impact projection:**
- 10,000 tool calls → 10,000 cached elements → ~500KB + stale COM references
- COM `IUIAutomationElement` references held until explicitly released

## Proposed Solutions

### Option 1: LRU Cache with Max Size (Recommended)
**Description:** Implement LRU eviction with max 500 elements.

| Aspect | Assessment |
|--------|------------|
| Pros | Bounded memory, keeps frequently-used elements |
| Cons | May evict elements still needed |
| Effort | Medium |
| Risk | Low |

### Option 2: TTL-Based Expiration
**Description:** Elements expire after 5 minutes of non-access.

| Aspect | Assessment |
|--------|------------|
| Pros | Simple time-based cleanup |
| Cons | Doesn't bound peak memory |
| Effort | Small |
| Risk | Low |

### Option 3: Hybrid LRU + TTL
**Description:** Evict when over max size OR when stale for 5 minutes.

| Aspect | Assessment |
|--------|------------|
| Pros | Best of both approaches |
| Cons | More complex |
| Effort | Medium |
| Risk | Low |

## Recommended Action

Option 3 (Hybrid) - Both size bounds AND time expiration provide defense in depth.

## Technical Details

```csharp
public class ElementCacheEntry
{
    public AutomationElement Element { get; set; }
    public DateTime LastAccessed { get; set; }
}

// Configuration
private const int MaxCacheSize = 500;
private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
```

## Acceptance Criteria

- [ ] Element cache bounded to 500 elements
- [ ] LRU eviction when full
- [ ] Elements expire after 5 minutes of non-use
- [ ] get_cache_stats reports cache size and eviction count
- [ ] Memory stable in long-running sessions

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-20 | Created from performance/data-integrity reviews | COM refs prevent GC |
