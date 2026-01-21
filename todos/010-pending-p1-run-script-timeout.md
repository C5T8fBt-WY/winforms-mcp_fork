---
status: pending
priority: p1
issue_id: "010"
tags: [bug, run-script, timeout]
dependencies: []
---

# run_script Tool May Timeout / Not Work

## Problem Statement

The `run_script` tool seems to timeout and it's unclear if it actually works correctly.

**Why it matters:** Script execution is a key feature for multi-step automation.

## Findings

**Source:** User report during MCP usage

**Issues reported:**
1. Scripts may be timing out
2. Unclear if run_script actually works end-to-end
3. Unit tests for ScriptRunner have failures (JSON type assertions)

**Related:** 3 unit tests failing in ScriptExecutionTests.cs:
- `TestVariableInterpolation_BooleanValue`
- `TestVariableInterpolation_NestedPath`
- `TestVariableInterpolation_NumericValue`

## Proposed Solutions

### Solution
1. Fix failing unit tests to understand expected behavior
2. Add timeout configuration/feedback
3. Test run_script end-to-end via MCP

**Investigation needed:**
1. What timeout is configured? (Constants.Timeouts.ScriptExecution)
2. Is timeout being hit?
3. Are variable interpolation types working correctly?

## Technical Details

**Files:**
- `src/Rhombus.WinFormsMcp.Server/Script/ScriptRunner.cs`
- `src/Rhombus.WinFormsMcp.Server/Constants.cs` (timeout value)
- `tests/Rhombus.WinFormsMcp.Tests/ScriptExecutionTests.cs` (failing tests)

**Current timeout:**
```csharp
var timeoutMs = Constants.Timeouts.ScriptExecution;  // Check value
```

## Acceptance Criteria

- [ ] Failing unit tests fixed
- [ ] Timeout value documented and reasonable
- [ ] run_script works reliably via MCP
- [ ] Clear error when timeout occurs
- [ ] Variable interpolation handles all JSON types

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-20 | Created from user bug report | Script execution uncertain |
