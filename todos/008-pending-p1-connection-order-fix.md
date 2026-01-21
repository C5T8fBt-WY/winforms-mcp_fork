---
status: pending
priority: p1
issue_id: "008"
tags: [bug, connection, bridge]
dependencies: []
---

# Connection: Read Signals Before Starting Server

## Problem Statement

The bridge tries to start the server before reading signals and connecting to IP. Should read signals first to know where to connect.

**Why it matters:** Race condition causes connection failures.

## Findings

**Source:** User report during MCP usage

**Expected order:**
1. Wait for sandbox to boot
2. Read ready signal with IP/port
3. Connect to the reported endpoint
4. Start proxying

**Current behavior:**
- May try to connect before signals are available
- Connection order unclear

## Proposed Solutions

### Solution
Refactor connection sequence to be signal-driven.

**Implementation:**
1. Wait for ready signal file to appear
2. Parse IP and port from signal
3. Attempt connection to reported endpoint
4. Only then report "connected" to Claude

## Technical Details

**Files likely affected:**
- `mcp-sandbox-bridge.ps1`

## Acceptance Criteria

- [ ] Bridge waits for ready signal before connecting
- [ ] IP/port read from signal file
- [ ] Connection attempts only to signaled endpoint
- [ ] Clear error if signal never appears

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-20 | Created from user bug report | Order of operations matters |
