---
status: pending
priority: p1
issue_id: "003"
tags: [code-review, token-efficiency, agent-native]
dependencies: []
---

# Remove execution_time_ms from Action Tools

## Problem Statement

Many tools include `execution_time_ms` in responses, which agents rarely use. This adds 5-10 tokens per response.

**Why it matters:** Cumulative token waste, clutters responses with debugging data.

## Findings

**Source:** agent-native-reviewer

**Locations:** ElementHandlers.cs, ProcessHandlers.cs, AdvancedHandlers.cs, ObservationHandlers.cs

**Current:**
```csharp
return Success(
    ("elementId", elementId),
    ("name", element.Name ?? ""),
    ("execution_time_ms", stopwatch.ElapsedMilliseconds));  // Rarely useful
```

## Proposed Solutions

### Option 1: Remove from Action Tools (Recommended)
**Description:** Keep execution_time_ms only in diagnostic tools like `get_capabilities`, `get_cache_stats`.

| Aspect | Assessment |
|--------|------------|
| Pros | Cleaner responses, token savings |
| Cons | Lose timing data for debugging |
| Effort | Small |
| Risk | Low |

### Option 2: Debug Mode Only
**Description:** Only include when verbosity=debug.

| Aspect | Assessment |
|--------|------------|
| Pros | Available when needed |
| Cons | More complex |
| Effort | Medium |
| Risk | Low |

## Recommended Action

Option 1 - Remove timing from action tools. Keep in diagnostic tools.

## Technical Details

**Keep timing in:**
- `get_capabilities`
- `get_cache_stats`
- `run_script` (total execution time is useful)
- `wait_for_element` (indicates how long wait took)

**Remove timing from:**
- `find_element`
- `click_element`
- `type_text`
- All other action tools

## Acceptance Criteria

- [ ] Action tools don't include execution_time_ms
- [ ] Diagnostic tools still include timing
- [ ] run_script still reports total_execution_time_ms

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-20 | Created from agent-native review | Timing rarely useful to agents |
