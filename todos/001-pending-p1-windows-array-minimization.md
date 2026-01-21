---
status: pending
priority: p1
issue_id: "001"
tags: [code-review, token-efficiency, agent-native]
dependencies: []
---

# Minimize Windows Array in Responses

## Problem Statement

Every tool response includes the complete `windows` array via `windowManager.GetAllWindows()`, wasting 200-800 tokens per call. A typical automation session (15 calls) wastes 3,000-12,000 tokens on redundant window data.

**Why it matters:** 10-15% of agent context window wasted on data the agent doesn't need.

## Findings

**Source:** agent-native-reviewer

**Location:** `src/Rhombus.WinFormsMcp.Server/ToolResponse.cs` (lines 30-31, 44-52)

**Current behavior:**
```csharp
public static ToolResponse Ok(object? result, WindowManager windowManager)
{
    return new ToolResponse
    {
        Success = true,
        Result = result,
        Windows = windowManager.GetAllWindows()  // ALL windows, EVERY response
    };
}
```

**User insight:** Windows array is useful to avoid LLM asking for windows/screenshots after actions. Can minimize to only windows from the same app that was interacted with.

## Proposed Solutions

### Option 1: App-Scoped Windows (Recommended)
**Description:** Only return windows belonging to the same process/application that was interacted with.

```csharp
public static ToolResponse Ok(object? result, WindowManager windowManager, int? processId = null)
{
    var windows = processId.HasValue
        ? windowManager.GetWindowsByProcess(processId.Value)
        : new List<WindowInfo>();  // No windows if no process context
    return new ToolResponse { Success = true, Result = result, Windows = windows };
}
```

| Aspect | Assessment |
|--------|------------|
| Pros | Preserves usefulness, 80-90% token reduction, non-breaking |
| Cons | Requires tracking process context in handlers |
| Effort | Medium |
| Risk | Low |

### Option 2: Opt-in Parameter
**Description:** Add `includeWindows: boolean` parameter to handlers, default false.

| Aspect | Assessment |
|--------|------------|
| Pros | Agent controls verbosity |
| Cons | Breaking change to tool schema, requires agent updates |
| Effort | Medium |
| Risk | Medium (breaking) |

### Option 3: Verbosity Modes
**Description:** Global setting `verbosity: minimal|normal|debug` affecting all responses.

| Aspect | Assessment |
|--------|------------|
| Pros | Comprehensive control |
| Cons | Complex, requires new configuration |
| Effort | Large |
| Risk | Medium |

## Recommended Action

Option 1 (App-Scoped Windows) - preserves the value the user identified while dramatically reducing tokens.

## Technical Details

**Affected Files:**
- `src/Rhombus.WinFormsMcp.Server/ToolResponse.cs`
- `src/Rhombus.WinFormsMcp.Server/Automation/WindowManager.cs` (add GetWindowsByProcess)
- All handlers (pass process context)

**Implementation approach:**
1. Add `GetWindowsByProcess(int pid)` to WindowManager
2. Modify ToolResponse.Ok to accept optional processId
3. Update handlers to pass process context when available
4. For tools without process context, return empty windows array

## Acceptance Criteria

- [ ] Windows array only includes windows from the interacted app
- [ ] Token usage per response reduced by 80%+
- [ ] No breaking changes to tool schema
- [ ] Agent can still see relevant windows for the app it's automating

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-20 | Created from agent-native review | User insight: windows useful to avoid followup requests |

## Resources

- [Agent-native review]: Token efficiency findings
