---
status: pending
priority: p2
issue_id: "005"
tags: [code-review, simplification, duplication]
dependencies: []
---

# Extract Duplicated ResolveWindowCoordinates

## Problem Statement

`ResolveWindowCoordinates` logic is duplicated across 3 handlers (150+ lines total). Each copy is nearly identical.

**Why it matters:** Bug fixes need to be applied in 3 places, increased maintenance burden.

## Findings

**Source:** code-simplifier, pattern-recognition-specialist

**Locations:**
- `InputHandlers.cs` lines 305-371 (67 lines)
- `TouchPenHandlers.cs` lines 332-393 (62 lines)
- `TouchPenHandlers.cs` lines 401-470 (`ResolveWindowCoordinatesWithHandle` variant)

**The pattern:**
```csharp
private (bool resolved, int screenX, int screenY, string? warning, JsonElement? errorResponse)
    ResolveWindowCoordinates(string? windowHandle, string? windowTitle, int x, int y, bool focusWindow = true)
{
    // 60+ lines of identical logic
}
```

## Proposed Solutions

### Option 1: Extract to HandlerBase (Recommended)
**Description:** Move common coordinate resolution to `HandlerBase` as protected method.

```csharp
// In HandlerBase.cs
protected (bool resolved, int screenX, int screenY, string? warning, JsonElement? errorResponse)
    ResolveWindowCoordinates(string? windowHandle, string? windowTitle, int x, int y, bool focusWindow = true)
{
    // Single implementation
}
```

| Aspect | Assessment |
|--------|------------|
| Pros | Single source of truth, ~120 lines removed |
| Cons | None significant |
| Effort | Small |
| Risk | Low |

### Option 2: Separate CoordinateResolver Class
**Description:** Create dedicated helper class.

| Aspect | Assessment |
|--------|------------|
| Pros | Better separation of concerns |
| Cons | More files, injection needed |
| Effort | Medium |
| Risk | Low |

## Recommended Action

Option 1 - Move to HandlerBase. Simple, effective, follows existing patterns.

## Technical Details

**Files to modify:**
- `src/Rhombus.WinFormsMcp.Server/Handlers/HandlerBase.cs` (add method)
- `src/Rhombus.WinFormsMcp.Server/Handlers/InputHandlers.cs` (remove, use inherited)
- `src/Rhombus.WinFormsMcp.Server/Handlers/TouchPenHandlers.cs` (remove both, use inherited)

## Acceptance Criteria

- [ ] Single ResolveWindowCoordinates in HandlerBase
- [ ] All handlers use inherited method
- [ ] ~120 lines of duplication removed
- [ ] All coordinate-based tools still work correctly

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-20 | Created from code-simplifier/pattern reviews | DRY violation |
