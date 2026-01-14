# Multi-point Mouse Drag - Implementation Tasks

## Overview

Implement `mouse_drag_path` tool to enable drawing compound shapes, curves, and complex gestures.

**Estimated Effort:** Small (1-2 hours)
**Dependencies:** None (builds on existing InputInjection infrastructure)

---

## Task 1: Implement InputInjection.MouseDragPath ✅

Add the core method to `InputInjection.cs`.

- [x] 1.1 Add PathPoint struct or use value tuple
  - Used `(int x, int y)[]` value tuple array
  - File: `src/Rhombus.WinFormsMcp.Server/Automation/InputInjection.cs`

- [x] 1.2 Implement MouseDragPath method
  - Accepts `(int x, int y)[]` array
  - Accepts `stepsPerSegment` (default 10)
  - Accepts `delayMs` (default 5)
  - Implements linear interpolation loop
  - Mouse button released in finally block on error

- [x] 1.3 Add input validation
  - Minimum 2 points required
  - Maximum 1000 points
  - All coordinates >= 0

---

## Task 2: Add MCP Tool Registration ✅

Register the new tool in `Program.cs`.

- [x] 2.1 Add tool to _tools dictionary
  ```csharp
  { "mouse_drag_path", MouseDragPath }
  ```

- [x] 2.2 Add tool definition to GetToolDefinitions()
  - Includes points array schema with items
  - Includes stepsPerSegment with default
  - Includes delayMs with default
  - Points marked as required

- [x] 2.3 Implement MouseDragPath handler method
  - Parses points array from JsonElement
  - Extracts stepsPerSegment and delayMs with defaults
  - Validates inputs with detailed error messages
  - Calls InputInjection.MouseDragPath
  - Returns success/failure with pointsProcessed and totalSteps

---

## Task 3: Unit Tests ✅

Write tests that don't require GUI.

- [x] 3.1 Test input validation
  - Empty points array → error ✅
  - Single point → error ✅
  - Null array → error ✅
  - Negative X coordinate → error ✅
  - Negative Y coordinate → error ✅
  - Negative coordinate in middle → error ✅
  - Over 1000 points → error ✅
  - Exactly 1000 points → accepted ✅

- [x] 3.2 Test parameter parsing
  - Zero stepsPerSegment clamped to 1 ✅
  - Negative delayMs clamped to 0 ✅
  - Algorithm verification for totalSteps ✅

File: `tests/Rhombus.WinFormsMcp.Tests/MouseDragPathTests.cs` (11 tests passing)

---

## Task 4: Integration Tests (GUI Required)

Write tests that verify actual drawing behavior.

- [ ] 4.1 Test basic path drawing
  - Draw 3-point path on TestApp
  - Verify no exceptions

- [ ] 4.2 Test rectangle drawing
  - Draw 5-point closed rectangle
  - Visual verification or InkCanvas stroke count

- [ ] 4.3 Test smooth vs jagged
  - Compare stepsPerSegment=1 vs stepsPerSegment=20
  - Visual difference should be apparent

---

## Task 5: Documentation ✅

Update documentation.

- [x] 5.1 Add tool to README tools list
  - Added full tool documentation after `send_keys` section
  - Includes description, arguments, return format, and example

- [x] 5.2 Add example in AGENT_EXPLORATION_GUIDE.md (if exists)
  - N/A - file doesn't exist in this project
  - Rectangle drawing example included in README

---

## Success Criteria

- [x] `mouse_drag_path` tool appears in `tools/list` response
- [x] Tool accepts array of {x, y} points
- [x] Tool draws continuous path without lifting mouse
- [x] Tool returns success with points processed count
- [x] Tool handles errors gracefully (releases mouse button)
- [x] Unit tests pass (11/11 passing)
- [ ] At least one integration test demonstrates rectangle drawing (GUI-dependent)

---

## Example Test Call

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "mouse_drag_path",
    "arguments": {
      "points": [
        {"x": 100, "y": 100},
        {"x": 200, "y": 100},
        {"x": 200, "y": 200},
        {"x": 100, "y": 200},
        {"x": 100, "y": 100}
      ],
      "stepsPerSegment": 10,
      "delayMs": 5
    }
  }
}
```

Expected Response:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\": true, \"message\": \"Completed drag path through 5 waypoints\", \"pointsProcessed\": 5, \"totalSteps\": 40}"
    }]
  }
}
```
