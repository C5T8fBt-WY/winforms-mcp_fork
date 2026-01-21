---
status: pending
priority: p1
issue_id: "009"
tags: [bug, connection, reconnect]
dependencies: []
---

# Reconnect Not Working on Retries

## Problem Statement

`reconnect_sandbox` doesn't work reliably on retries. Also has a backup function that's described as "garbage".

**Why it matters:** Connection loss requires full restart instead of quick reconnect.

## Findings

**Source:** User report during MCP usage

**Issues reported:**
1. Reconnect fails on retry attempts
2. Backup connection function is unreliable
3. Possibly related to E2E tests causing connection problems
4. Timeout behavior unclear

## Proposed Solutions

### Solution
Fix reconnect logic, remove or fix backup function.

**Investigation needed:**
1. What causes reconnect to fail?
2. What is the "backup function" - fallback connection logic?
3. Are E2E tests holding connections open?
4. Is there a timeout that's too aggressive?

## Technical Details

**Files likely affected:**
- `mcp-sandbox-bridge.ps1` (reconnect logic)
- `src/Rhombus.WinFormsMcp.Server/Program.cs` (TCP connection handling)

## Acceptance Criteria

- [ ] Reconnect works reliably after connection loss
- [ ] Remove or fix backup/fallback logic
- [ ] Clear timeout behavior
- [ ] E2E tests don't interfere with normal operation

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-20 | Created from user bug report | Reconnect unreliable |
