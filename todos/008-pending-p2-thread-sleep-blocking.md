---
status: pending
priority: p2
issue_id: "008"
tags: [code-review, performance, async]
dependencies: []
---

# Thread.Sleep Used in Async Context (Blocking)

## Problem Statement

Multiple methods use `Thread.Sleep` instead of `await Task.Delay()`, blocking threads and reducing throughput in concurrent scenarios.

**Why it matters:** Blocks thread pool threads, prevents other work during waits, especially problematic with the UIA lock serialization.

## Findings

**Source:** performance-oracle agent

**Locations:**
- `AutomationHelper.cs` line 377-396: `TypeText` - `Thread.Sleep(clearDelayMs)`
- `AutomationHelper.cs` line 500, 505: `DragDrop` - `Thread.Sleep(dragSetupDelayMs)`, `Thread.Sleep(dropDelayMs)`
- `AutomationHelper.cs` line 636: `ExpandCollapse` - `Thread.Sleep(uiUpdateDelayMs)`
- `AutomationHelper.cs` line 718: `Scroll` - `Thread.Sleep(uiUpdateDelayMs)`
- `AutomationHelper.cs` line 230: `FindElement` - `Thread.Sleep(pollIntervalMs)`

**Evidence:**
```csharp
public void TypeText(AutomationElement element, string text, bool clearFirst = false, int clearDelayMs = 100)
{
    element.Focus();
    if (clearFirst)
    {
        System.Windows.Forms.SendKeys.SendWait("^a");
        Thread.Sleep(clearDelayMs);  // BLOCKING!
    }
    System.Windows.Forms.SendKeys.SendWait(text);
}
```

## Proposed Solutions

### Option 1: Convert to Async Methods (Recommended)
**Description:** Make blocking methods async and use `await Task.Delay()`.

| Aspect | Assessment |
|--------|------------|
| Pros | Proper async, better thread utilization |
| Cons | Requires making callers async |
| Effort | Medium |
| Risk | Low |

### Option 2: Use Task.Delay().Wait() (Quick Fix)
**Description:** Replace `Thread.Sleep` with `Task.Delay().Wait()`.

| Aspect | Assessment |
|--------|------------|
| Pros | Minimal code change |
| Cons | Still blocks (but configures SynchronizationContext better) |
| Effort | Small |
| Risk | Low |

## Recommended Action

<!-- To be filled during triage -->

## Technical Details

**Affected Files:**
- `src/Rhombus.WinFormsMcp.Server/Automation/AutomationHelper.cs`

## Acceptance Criteria

- [ ] No Thread.Sleep calls in async code paths
- [ ] Delays use await Task.Delay()
- [ ] Concurrent request handling improved
- [ ] No regression in UI automation timing

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-20 | Created from performance-oracle review | Thread.Sleep is a code smell in async code |

## Resources

- [PR/Issue]: Current uncommitted changes on master
