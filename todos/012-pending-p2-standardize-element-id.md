---
status: pending
priority: p2
issue_id: "012"
tags: [code-review, agent-native, consistency]
dependencies: []
---

# Standardize Parameter Naming (elementId vs elementPath)

## Problem Statement

Inconsistent parameter naming between tools confuses agents.

**Why it matters:** Agents learn patterns; inconsistency causes errors.

## Findings

**Source:** agent-native-reviewer

**Current inconsistency:**
| Parameter | Used By |
|-----------|---------|
| `elementPath` | `click_element`, `type_text`, `set_value` |
| `elementId` | `expand_collapse`, `check_element_state` |
| Both | `drag_drop` (sourceElementId/sourceElementPath) |

**From ElementHandlers.cs:**
```csharp
var elementId = GetStringArg(args, "elementId") ?? GetStringArg(args, "elementPath")
    ?? throw new ArgumentException("elementId or elementPath is required");
```

## Proposed Solutions

### Option 1: Standardize on elementId (Recommended)
**Description:** Use `elementId` everywhere, keep `elementPath` as alias for backwards compatibility.

| Aspect | Assessment |
|--------|------------|
| Pros | Consistent, non-breaking |
| Cons | Old parameter still works (could be confusing) |
| Effort | Small |
| Risk | Low |

### Option 2: Hard Cutover to elementId
**Description:** Remove `elementPath` entirely.

| Aspect | Assessment |
|--------|------------|
| Pros | Clean, no confusion |
| Cons | Breaking change |
| Effort | Small |
| Risk | Medium (breaking) |

## Recommended Action

Option 1 - Standardize docs/descriptions on `elementId`, keep `elementPath` as silent alias.

## Technical Details

**Files:**
- `src/Rhombus.WinFormsMcp.Server/Protocol/ToolDefinitions.cs` (update descriptions)
- All handlers (already support both via fallback)

## Acceptance Criteria

- [ ] Tool definitions use `elementId` as primary
- [ ] Descriptions mention `elementId` only
- [ ] Handlers continue to accept both (backwards compat)
- [ ] Documentation updated

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-20 | Created from agent-native review | Consistency helps agents |
