---
status: pending
priority: p1
issue_id: "007"
tags: [bug, hot-reload, usability]
dependencies: []
---

# Hot Reload: App Selection and Enable Flag

## Problem Statement

When multiple apps are available for hot reload, there's no prompt to select which one. Also missing per-app hot reload enable/disable option.

**Why it matters:** User confusion when multiple apps present, no way to disable hot reload for specific apps.

## Findings

**Source:** User report during MCP usage

**Expected behavior:**
1. When multiple apps detected, prompt user to select which one
2. For each app, option to enable/disable hot reload

**Current behavior:**
- Unclear which app is selected for hot reload
- No per-app hot reload toggle

## Proposed Solutions

### Solution
Add app selection prompt and per-app hot reload configuration.

**Implementation:**
1. When LazyStart detects multiple runnable apps, prompt for selection
2. Store hot reload preference per app path
3. Only watch/reload apps with hot reload enabled

## Technical Details

**Files likely affected:**
- `sandbox/bootstrap.ps1` (hot reload logic)
- `mcp-sandbox-bridge.ps1` (if app selection happens on host side)

## Acceptance Criteria

- [ ] Prompt shown when multiple apps available
- [ ] User can select which app to hot reload
- [ ] Per-app hot reload enable/disable
- [ ] Selection persisted across restarts (optional)

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-20 | Created from user bug report | Multi-app scenario unclear |
