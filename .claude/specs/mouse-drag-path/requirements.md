# Multi-point Mouse Drag - Requirements

## 1. Overview

### 1.1 Problem Statement

The current `mouse_drag` tool only supports linear interpolation between two points:
```
mouse_drag(x1, y1, x2, y2, steps)
```

This limitation prevents automated testing of:
- Compound shapes (rectangles, polygons)
- Curves and arcs
- Complex gestures (dimension arrows, constraint indicators)
- Any drawing operation requiring multiple connected strokes

### 1.2 Proposed Solution

Add a new `mouse_drag_path` tool that accepts an array of waypoints and moves the mouse through them sequentially while holding the mouse button down.

### 1.3 Scope

**In Scope:**
- New `mouse_drag_path` MCP tool
- Sequential movement through waypoints
- Configurable interpolation between points
- Support for both mouse and touch/pen variants

**Out of Scope:**
- Bezier curve generation (caller provides all points)
- Pressure variation for pen (use `pen_stroke` for that)
- Multi-touch gestures (handled separately)

---

## 2. Functional Requirements

### 2.1 Tool: `mouse_drag_path`

**FR-2.1.1** The tool SHALL accept an array of points, where each point has `x` and `y` coordinates.

**FR-2.1.2** The tool SHALL move the mouse cursor through all points in order while holding the left mouse button down.

**FR-2.1.3** The tool SHALL support a `stepsPerSegment` parameter to control smoothness of movement between consecutive waypoints (default: 10).

**FR-2.1.4** The tool SHALL support a `delayMs` parameter for pause between steps (default: 5ms).

**FR-2.1.5** The tool SHALL return success/failure status with the number of points processed.

### 2.2 Input Validation

**FR-2.2.1** The tool SHALL require at least 2 points in the path array.

**FR-2.2.2** The tool SHALL validate that all points have valid integer `x` and `y` coordinates.

**FR-2.2.3** The tool SHALL reject paths where any coordinate is negative.

### 2.3 Execution Behavior

**FR-2.3.1** The tool SHALL interpolate between consecutive waypoints using the specified `stepsPerSegment`.

**FR-2.3.2** The tool SHALL complete the entire path in a single mouse-down-drag-up sequence.

**FR-2.3.3** The tool SHALL release the mouse button after reaching the final point.

---

## 3. Non-Functional Requirements

### 3.1 Performance

**NFR-3.1.1** The tool SHALL complete a 10-point path in under 2 seconds with default settings.

**NFR-3.1.2** The tool SHALL support paths with up to 1000 waypoints.

### 3.2 Compatibility

**NFR-3.2.1** The tool SHALL work with any Windows application that accepts mouse input.

**NFR-3.2.2** The tool SHALL use the same underlying input injection as `mouse_drag`.

### 3.3 Reliability

**NFR-3.3.1** The tool SHALL always release the mouse button, even if an error occurs mid-path.

---

## 4. Use Cases

### 4.1 Drawing Compound Shapes

**Actor:** Automation agent testing a drawing application

**Precondition:** Drawing canvas is visible and focused

**Flow:**
1. Agent calls `mouse_drag_path` with rectangle corners:
   ```json
   {
     "points": [
       {"x": 200, "y": 200},
       {"x": 400, "y": 200},
       {"x": 400, "y": 400},
       {"x": 200, "y": 400},
       {"x": 200, "y": 200}
     ]
   }
   ```
2. Tool moves mouse through all points while dragging
3. Rectangle appears on canvas

**Postcondition:** Closed rectangle shape is drawn

### 4.2 Drawing Curved Lines

**Actor:** Automation agent testing InkCanvas

**Precondition:** InkCanvas control is visible

**Flow:**
1. Agent generates arc points (pre-computed):
   ```json
   {
     "points": [
       {"x": 100, "y": 300},
       {"x": 150, "y": 200},
       {"x": 250, "y": 150},
       {"x": 350, "y": 200},
       {"x": 400, "y": 300}
     ],
     "stepsPerSegment": 5
   }
   ```
2. Tool draws smooth curve through points

**Postcondition:** Curved line appears on canvas

### 4.3 Gesture Recognition Testing

**Actor:** Automation agent testing custom gesture control

**Precondition:** Gesture-enabled control is focused

**Flow:**
1. Agent draws a "check mark" gesture:
   ```json
   {
     "points": [
       {"x": 100, "y": 200},
       {"x": 150, "y": 300},
       {"x": 300, "y": 100}
     ],
     "stepsPerSegment": 15
   }
   ```
2. Application recognizes gesture and triggers action

**Postcondition:** Gesture recognized and handled

---

## 5. API Design

### 5.1 Tool Definition

```json
{
  "name": "mouse_drag_path",
  "description": "Drag the mouse through multiple waypoints in sequence. Useful for drawing shapes, curves, and complex gestures.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "points": {
        "type": "array",
        "description": "Array of {x, y} waypoints to drag through",
        "items": {
          "type": "object",
          "properties": {
            "x": {"type": "integer"},
            "y": {"type": "integer"}
          },
          "required": ["x", "y"]
        },
        "minItems": 2
      },
      "stepsPerSegment": {
        "type": "integer",
        "description": "Interpolation steps between each waypoint (default 10)",
        "default": 10
      },
      "delayMs": {
        "type": "integer",
        "description": "Delay in milliseconds between steps (default 5)",
        "default": 5
      }
    },
    "required": ["points"]
  }
}
```

### 5.2 Response Format

**Success:**
```json
{
  "success": true,
  "message": "Completed drag path through 5 waypoints",
  "pointsProcessed": 5,
  "totalSteps": 40
}
```

**Failure:**
```json
{
  "success": false,
  "error": "Path requires at least 2 points"
}
```

---

## 6. Future Considerations

### 6.1 Potential Extensions

- `touch_drag_path` - Same functionality for touch input
- `pen_stroke_path` - With per-point pressure values
- Closed path detection (auto-close if first/last points match)
- Named gesture library (e.g., "rectangle", "circle", "check")

### 6.2 Related Tools

| Tool | Current | After Enhancement |
|------|---------|-------------------|
| `mouse_drag` | 2 points only | Keep as-is (simple case) |
| `mouse_drag_path` | N/A | NEW - multi-point |
| `touch_drag` | 2 points only | Consider similar enhancement |
| `pen_stroke` | 2 points only | Consider similar enhancement |
