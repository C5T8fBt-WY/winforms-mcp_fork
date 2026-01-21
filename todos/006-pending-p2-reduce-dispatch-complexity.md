---
status: pending
priority: p2
issue_id: "006"
tags: [code-review, simplification, crap-score]
dependencies: []
---

# Reduce Handler Dispatch Complexity (CRAP 2970)

## Problem Statement

Handler `ExecuteAsync` methods use giant switch statements for tool dispatch, resulting in extremely high CRAP scores (2970) and cyclomatic complexity (54).

**Why it matters:** High complexity = harder to maintain, harder to test, more bugs.

## Findings

**Source:** Combined coverage report, code-simplifier

**CRAP Scores:**
| Method | CRAP | Complexity |
|--------|------|------------|
| `AdvancedHandlers.ExecuteAsync` | 2970 | 54 |
| `ElementHandlers.ExecuteAsync` | 1482 | 38 |
| `TouchPenHandlers.ExecuteAsync` | 1056 | 32 |

**Current pattern:**
```csharp
public async Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
{
    return toolName switch
    {
        "find_element" => await FindElement(args),
        "click_element" => await ClickElement(args),
        "type_text" => TypeText(args),
        // ... 30+ more cases
        _ => throw new NotSupportedException($"Unknown tool: {toolName}")
    };
}
```

## Proposed Solutions

### Option 1: Dictionary-Based Dispatch (Recommended)
**Description:** Replace switch with dictionary of delegates.

```csharp
private readonly Dictionary<string, Func<JsonElement, Task<JsonElement>>> _handlers;

public Handler()
{
    _handlers = new()
    {
        ["find_element"] = FindElement,
        ["click_element"] = ClickElement,
        // ...
    };
}

public async Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
{
    if (_handlers.TryGetValue(toolName, out var handler))
        return await handler(args);
    throw new NotSupportedException($"Unknown tool: {toolName}");
}
```

| Aspect | Assessment |
|--------|------------|
| Pros | Complexity drops from 54 to ~3, easy to extend |
| Cons | Slightly different pattern |
| Effort | Medium |
| Risk | Low |

### Option 2: Attribute-Based Registration
**Description:** Use attributes to auto-discover tool methods.

```csharp
[Tool("find_element")]
private Task<JsonElement> FindElement(JsonElement args) { ... }
```

| Aspect | Assessment |
|--------|------------|
| Pros | Self-documenting, auto-registration |
| Cons | More complex, reflection overhead |
| Effort | Large |
| Risk | Medium |

## Recommended Action

Option 1 - Dictionary dispatch. Simple, effective, dramatic CRAP reduction.

## Technical Details

**Refactoring approach (non-breaking):**
1. Add dictionary field with tool->method mappings
2. Change ExecuteAsync to use dictionary lookup
3. Keep individual tool methods unchanged
4. CRAP score drops from 2970 to ~10

**Files to modify:**
- All handler files in `src/Rhombus.WinFormsMcp.Server/Handlers/`

## Acceptance Criteria

- [ ] ExecuteAsync complexity reduced to <10
- [ ] CRAP scores under 30 for dispatch methods
- [ ] All tools still work correctly
- [ ] Easy to add new tools (just add to dictionary)

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-20 | Created from combined coverage analysis | Switch = complexity explosion |
