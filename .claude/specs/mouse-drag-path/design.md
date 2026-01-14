# Multi-point Mouse Drag - Design

## 1. Architecture Overview

### 1.1 Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        MCP Server (Program.cs)                  │
├─────────────────────────────────────────────────────────────────┤
│  ┌──────────────────┐      ┌──────────────────────────────────┐ │
│  │ mouse_drag_path  │─────▶│     InputInjection.cs            │ │
│  │ Tool Handler     │      │  ┌────────────────────────────┐  │ │
│  └──────────────────┘      │  │ MouseDragPath()            │  │ │
│                            │  │  - Point[] waypoints       │  │ │
│                            │  │  - int stepsPerSegment     │  │ │
│                            │  │  - int delayMs             │  │ │
│                            │  └────────────────────────────┘  │ │
│                            │              │                    │ │
│                            │              ▼                    │ │
│                            │  ┌────────────────────────────┐  │ │
│                            │  │ Interpolate & Send Events  │  │ │
│                            │  │  - MOUSEEVENTF_MOVE       │  │ │
│                            │  │  - MOUSEEVENTF_LEFTDOWN   │  │ │
│                            │  │  - MOUSEEVENTF_LEFTUP     │  │ │
│                            │  └────────────────────────────┘  │ │
│                            └──────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 Key Design Decisions

**D1: Separate tool vs. extending existing**
- **Decision:** Create new `mouse_drag_path` tool
- **Rationale:** Keeps `mouse_drag` simple for the common 2-point case; different parameter structure

**D2: Interpolation approach**
- **Decision:** Linear interpolation between consecutive waypoints
- **Rationale:** Simple, predictable; caller can provide curve points if needed (splines computed externally)

**D3: Input injection method**
- **Decision:** Reuse existing `SendInput` infrastructure from `mouse_drag`
- **Rationale:** Proven to work; consistent behavior

---

## 2. Detailed Design

### 2.1 Data Structures

```csharp
/// <summary>
/// Represents a 2D point for mouse path operations.
/// </summary>
public readonly struct PathPoint
{
    public int X { get; }
    public int Y { get; }

    public PathPoint(int x, int y)
    {
        X = x;
        Y = y;
    }
}
```

### 2.2 InputInjection.MouseDragPath Method

```csharp
/// <summary>
/// Drags the mouse through multiple waypoints in sequence.
/// </summary>
/// <param name="waypoints">Array of points to drag through (minimum 2)</param>
/// <param name="stepsPerSegment">Interpolation steps between each pair of points</param>
/// <param name="delayMs">Delay between each step in milliseconds</param>
/// <returns>True if successful, false otherwise</returns>
public static bool MouseDragPath(
    PathPoint[] waypoints,
    int stepsPerSegment = 10,
    int delayMs = 5)
{
    if (waypoints.Length < 2)
        return false;

    try
    {
        // Move to first point
        SetCursorPos(waypoints[0].X, waypoints[0].Y);
        Thread.Sleep(10);

        // Press mouse button
        MouseEvent(MOUSEEVENTF_LEFTDOWN, waypoints[0].X, waypoints[0].Y);
        Thread.Sleep(10);

        // Iterate through segments
        for (int i = 0; i < waypoints.Length - 1; i++)
        {
            var start = waypoints[i];
            var end = waypoints[i + 1];

            // Interpolate this segment
            for (int step = 1; step <= stepsPerSegment; step++)
            {
                float t = (float)step / stepsPerSegment;
                int x = (int)(start.X + (end.X - start.X) * t);
                int y = (int)(start.Y + (end.Y - start.Y) * t);

                SetCursorPos(x, y);
                MouseEvent(MOUSEEVENTF_MOVE, x, y);
                Thread.Sleep(delayMs);
            }
        }

        // Release mouse button
        var lastPoint = waypoints[waypoints.Length - 1];
        MouseEvent(MOUSEEVENTF_LEFTUP, lastPoint.X, lastPoint.Y);

        return true;
    }
    catch
    {
        // Ensure mouse is released on error
        try { MouseEvent(MOUSEEVENTF_LEFTUP, 0, 0); } catch { }
        return false;
    }
}
```

### 2.3 Algorithm: Path Interpolation

```
Input: waypoints = [(x0,y0), (x1,y1), ..., (xn,yn)]
       stepsPerSegment = S
       delayMs = D

1. MOVE cursor to (x0, y0)
2. PRESS left mouse button

3. FOR i = 0 to n-1:
     start = waypoints[i]
     end = waypoints[i+1]

     FOR step = 1 to S:
       t = step / S                    // 0.0 to 1.0
       x = start.x + (end.x - start.x) * t
       y = start.y + (end.y - start.y) * t

       MOVE cursor to (x, y)
       SEND mouse move event
       SLEEP(D ms)

4. RELEASE left mouse button
```

**Total steps:** `(n-1) * stepsPerSegment` where n = number of waypoints

**Example:** 5 waypoints, 10 steps/segment = 40 mouse move events

---

## 3. Integration Points

### 3.1 Program.cs Changes

1. Add tool registration in `_tools` dictionary
2. Add tool definition in `GetToolDefinitions()`
3. Add tool handler method `MouseDragPath(JsonElement args)`

### 3.2 InputInjection.cs Changes

1. Add `PathPoint` struct (or use tuple)
2. Add `MouseDragPath()` method
3. Reuse existing Win32 imports (`SetCursorPos`, `mouse_event`)

---

## 4. Error Handling

### 4.1 Validation Errors

| Condition | Error Message |
|-----------|---------------|
| Less than 2 points | "Path requires at least 2 points" |
| Missing x or y in point | "Point at index {i} missing required x or y coordinate" |
| Negative coordinate | "Point at index {i} has invalid coordinate (must be >= 0)" |
| Too many points (>1000) | "Path exceeds maximum of 1000 waypoints" |

### 4.2 Runtime Errors

| Condition | Behavior |
|-----------|----------|
| SetCursorPos fails | Return false, release mouse button |
| SendInput fails | Return false, release mouse button |
| Exception during drag | Catch, release mouse button, return false |

---

## 5. Testing Strategy

### 5.1 Unit Tests

- Validate input parsing (points array)
- Validate parameter defaults
- Test error conditions (empty array, single point)

### 5.2 Integration Tests (require GUI)

- Draw rectangle on InkCanvas, verify stroke count
- Draw diagonal line, compare to `mouse_drag` result
- Test with high step count (smooth curves)
- Test with low step count (jagged lines)

### 5.3 Performance Tests

- Benchmark 100-point path
- Benchmark 1000-point path
- Measure timing accuracy of delayMs

---

## 6. Example Usage

### 6.1 Drawing a Rectangle

```json
{
  "method": "tools/call",
  "params": {
    "name": "mouse_drag_path",
    "arguments": {
      "points": [
        {"x": 100, "y": 100},
        {"x": 300, "y": 100},
        {"x": 300, "y": 200},
        {"x": 100, "y": 200},
        {"x": 100, "y": 100}
      ],
      "stepsPerSegment": 10
    }
  }
}
```

### 6.2 Drawing an Arc (Pre-computed Points)

```json
{
  "points": [
    {"x": 200, "y": 300},
    {"x": 220, "y": 260},
    {"x": 260, "y": 230},
    {"x": 300, "y": 220},
    {"x": 340, "y": 230},
    {"x": 380, "y": 260},
    {"x": 400, "y": 300}
  ],
  "stepsPerSegment": 5,
  "delayMs": 3
}
```

### 6.3 Simple Triangle

```json
{
  "points": [
    {"x": 200, "y": 400},
    {"x": 300, "y": 200},
    {"x": 400, "y": 400},
    {"x": 200, "y": 400}
  ]
}
```

---

## 7. Future Extensions

### 7.1 Touch Path (`touch_drag_path`)

Same structure, uses `InjectTouchInput` instead of mouse events.

### 7.2 Pen Path with Pressure (`pen_stroke_path`)

Extended point structure:
```json
{
  "points": [
    {"x": 100, "y": 100, "pressure": 256},
    {"x": 200, "y": 150, "pressure": 512},
    {"x": 300, "y": 100, "pressure": 768}
  ]
}
```

### 7.3 Bezier Curve Helper

Utility function to generate interpolated points from control points:
```csharp
PathPoint[] GenerateBezierCurve(
    PathPoint p0, PathPoint p1, PathPoint p2, PathPoint p3,
    int segments);
```

This would be a client-side helper, not part of the MCP tool itself.
