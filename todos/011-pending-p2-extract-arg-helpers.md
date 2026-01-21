---
status: pending
priority: p2
issue_id: "011"
tags: [code-review, simplification, duplication]
dependencies: []
---

# Extract Duplicated Arg Helpers to Shared Utility

## Problem Statement

`GetStringArg`, `GetIntArg`, `GetBoolArg` are duplicated in HandlerBase and ScriptRunner (~40 lines).

**Why it matters:** DRY violation, potential for inconsistent behavior.

## Findings

**Source:** code-simplifier

**Locations:**
- `src/Rhombus.WinFormsMcp.Server/Handlers/HandlerBase.cs` (lines 50-85)
- `src/Rhombus.WinFormsMcp.Server/Script/ScriptRunner.cs` (lines 391-424)

**Also:** `GetBoolArg` uses confusing nested ternary with double `TryGetProperty` call.

## Proposed Solutions

### Option 1: Extract to ArgHelpers Static Class (Recommended)

```csharp
// New file: ArgHelpers.cs
internal static class ArgHelpers
{
    public static string? GetString(JsonElement args, string key) { ... }
    public static int GetInt(JsonElement args, string key, int defaultValue = 0) { ... }
    public static bool GetBool(JsonElement args, string key, bool defaultValue = false) { ... }
}
```

| Aspect | Assessment |
|--------|------------|
| Pros | Single source of truth, reusable |
| Cons | New file |
| Effort | Small |
| Risk | Low |

## Technical Details

**Also fix GetBoolArg:**
```csharp
// Before (confusing)
return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.True
    ? true
    : args.TryGetProperty(key, out var prop2) && prop2.ValueKind == JsonValueKind.False
        ? false
        : defaultValue;

// After (clear)
if (!args.TryGetProperty(key, out var prop))
    return defaultValue;
if (prop.ValueKind == JsonValueKind.True) return true;
if (prop.ValueKind == JsonValueKind.False) return false;
return defaultValue;
```

## Acceptance Criteria

- [ ] ArgHelpers.cs created with shared methods
- [ ] HandlerBase delegates to ArgHelpers
- [ ] ScriptRunner uses ArgHelpers
- [ ] GetBoolArg logic simplified
- [ ] ~40 lines of duplication removed

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-20 | Created from code-simplifier review | DRY + clarity |
