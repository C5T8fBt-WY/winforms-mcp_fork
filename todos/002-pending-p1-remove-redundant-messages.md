---
status: pending
priority: p1
issue_id: "002"
tags: [code-review, token-efficiency, agent-native]
dependencies: []
---

# Remove Redundant Confirmation Messages

## Problem Statement

Action tools return verbose confirmation messages that waste tokens. The agent already knows what action it requested - confirming "Element clicked" adds no value.

**Why it matters:** 10-50 tokens wasted per action call, adds up across sessions.

## Findings

**Source:** agent-native-reviewer, code-simplifier

**Locations:**
| Tool | Redundant Response | File:Line |
|------|-------------------|-----------|
| `click_element` | `{"message": "Element clicked"}` | ElementHandlers.cs:151 |
| `type_text` | `{"message": "Text typed"}` | ElementHandlers.cs:176 |
| `set_value` | `{"message": "Value set"}` | ElementHandlers.cs:221 |
| `drag_drop` | `{"message": "Drag and drop completed"}` | InputHandlers.cs |
| `mouse_click` | `{"message": "Mouse click at (x, y)"}` | InputHandlers.cs |

**Current:**
```csharp
return Success(("message", "Element clicked"));
```

**Should be:**
```csharp
return Success();  // Just success=true, no message
```

## Proposed Solutions

### Option 1: Minimal Success Responses (Recommended)
**Description:** Return only `{"success": true}` for simple actions. Only include data when returning new IDs or state changes.

| Aspect | Assessment |
|--------|------------|
| Pros | Maximum token savings, cleaner responses |
| Cons | Less debugging info in logs |
| Effort | Small |
| Risk | Low |

### Option 2: Keep Messages in Debug Mode Only
**Description:** Add verbosity flag, only include messages when debugging.

| Aspect | Assessment |
|--------|------------|
| Pros | Preserves debug capability |
| Cons | More complex implementation |
| Effort | Medium |
| Risk | Low |

## Recommended Action

Option 1 - Simple is better. Agents don't need confirmation of what they just asked for.

## Technical Details

**Affected Files:**
- `src/Rhombus.WinFormsMcp.Server/Handlers/ElementHandlers.cs`
- `src/Rhombus.WinFormsMcp.Server/Handlers/InputHandlers.cs`
- `src/Rhombus.WinFormsMcp.Server/Handlers/TouchPenHandlers.cs`

**Pattern to apply:**
```csharp
// Before
return Success(("message", "Element clicked"));

// After
return Success();

// Or when returning new data
return Success(("elementId", newId));  // Only meaningful data
```

## Acceptance Criteria

- [ ] Action tools return minimal responses
- [ ] New element IDs still returned when created
- [ ] State changes still returned when relevant
- [ ] No "message" field in success responses

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-20 | Created from agent-native review | Agents know what they asked for |

## Resources

- [Agent-native review]: Token efficiency findings
