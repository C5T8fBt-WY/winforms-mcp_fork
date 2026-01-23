# Touch and Pen Input Guide

A guide for using touch and pen input tools for ink-based and gesture-based UI automation.

## Overview

The WinForms MCP server provides low-level input injection for:
- **Touch**: Taps, drags, pinch gestures, rotation
- **Pen/Stylus**: Pressure-sensitive strokes, eraser mode, tilt, barrel button

These tools are useful for testing InkCanvas applications, touch-optimized UIs, and pen-based workflows.

---

## Coordinate Systems

### Logical vs Physical Coordinates

Windows uses DPI scaling, which means coordinates can be:

- **Logical coordinates**: What the application sees (96 DPI baseline)
- **Physical coordinates**: Actual screen pixels

At 150% scaling (144 DPI):
- Logical (100, 100) → Physical (150, 150)

### Getting DPI Information

```json
{
  "tool": "get_dpi_info",
  "args": { "windowTitle": "MyApp" }
}
```

Returns:
```json
{
  "system_dpi": 144,
  "system_scale_factor": 1.5,
  "window_dpi": 144,
  "window_scale_factor": 1.5,
  "is_per_monitor_aware": true,
  "standard_dpi": 96
}
```

### Window-Relative Coordinates

All coordinate-based tools accept `windowHandle` or `windowTitle` to use window-relative coordinates (Not yet fully implemented in unified tools):

```json
{
  "tool": "click",
  "args": {
    "x": 100,
    "y": 50,
    "input": "touch"
  }
}
```

**Coordinates are relative to the window's client area** (excludes title bar).

Without window parameters, coordinates are screen-absolute.

---

## Touch Input

### Touch Tap

Single touch tap at a point:

```json
{
  "tool": "click",
  "args": {
    "x": 200,
    "y": 300,
    "input": "touch"
  }
}
```

### Touch Drag

Single-finger drag gesture:

```json
{
  "tool": "drag",
  "args": {
    "path": [
      { "x": 100, "y": 300 },
      { "x": 400, "y": 300 }
    ],
    "input": "touch",
    "duration_ms": 1000
  }
}
```

**Parameters**:
- `path`: Array of points (x, y)
- `input`: "touch"
- `duration_ms`: Total duration of drag

### Pinch Zoom

Two-finger pinch gesture:

```json
{
  "tool": "gesture",
  "args": {
    "type": "pinch",
    "center": { "x": 300, "y": 300 },
    "start_distance": 50,
    "end_distance": 200,
    "duration_ms": 1000
  }
}
```

**Parameters**:
- `center`: Center point of the pinch (x, y)
- `start_distance`: Initial finger separation (pixels)
- `end_distance`: Final finger separation
- `duration_ms`: Animation duration

**Zoom in**: `end_distance > start_distance`
**Zoom out**: `end_distance < start_distance`

### Rotation Gesture

Two-finger rotation:

```json
{
  "tool": "gesture",
  "args": {
    "type": "rotate",
    "center": { "x": 300, "y": 300 },
    "radius": 100,
    "start_angle": 0,
    "end_angle": 90,
    "duration_ms": 1000
  }
}
```

**Parameters**:
- `center`: Center of rotation
- `radius`: Distance from center to each finger
- `start_angle, end_angle`: Rotation in degrees (clockwise)
- `duration_ms`: Animation duration

### Multi-Touch Gesture

Arbitrary multi-touch with multiple contact points:

```json
{
  "tool": "gesture",
  "args": {
    "type": "custom",
    "fingers": [
      {
        "path": [
          { "x": 100, "y": 100, "time_ms": 0 },
          { "x": 150, "y": 150, "time_ms": 500 },
          { "x": 200, "y": 200, "time_ms": 1000 }
        ]
      },
      {
        "path": [
          { "x": 400, "y": 100, "time_ms": 0 },
          { "x": 350, "y": 150, "time_ms": 500 },
          { "x": 300, "y": 200, "time_ms": 1000 }
        ]
      }
    ]
  }
}
```

**Parameters**:
- `fingers`: Array of finger paths
- Each finger has a `path` array of points with `x`, `y`, and `time_ms`

---

## Pen/Stylus Input

### Pen Tap

Quick pen tap (like clicking):

```json
{
  "tool": "click",
  "args": {
    "x": 200,
    "y": 300,
    "input": "pen",
    "pressure": 512
  }
}
```

**Parameters**:
- `input`: "pen"
- `pressure`: 0-1024 (0 = hovering, 1024 = maximum)

### Pen Stroke

Draw a line with pressure:

```json
{
  "tool": "drag",
  "args": {
    "path": [
      { "x": 50, "y": 50, "pressure": 512 },
      { "x": 200, "y": 200, "pressure": 512 }
    ],
    "input": "pen",
    "duration_ms": 1000,
    "eraser": false
  }
}
```

**Parameters**:
- `path`: Array of points with optional `pressure`
- `input`: "pen"
- `duration_ms`: Total duration
- `eraser`: Use eraser end of stylus (default: false)
- `barrel`: Use barrel button (default: false)

### Barrel Button

Simulate barrel button press (often right-click or tool cycle):

```json
{
  "tool": "click",
  "args": {
    "x": 200,
    "y": 300,
    "input": "pen",
    "barrel": true
  }
}
```

Or during a stroke:

```json
{
  "tool": "drag",
  "args": {
    "path": [...],
    "input": "pen",
    "barrel": true
  }
}
```

### Pressure Sensitivity

| Value | Meaning |
|-------|---------|
| 0 | Hovering (not touching) |
| 256 | Light touch |
| 512 | Normal pressure |
| 768 | Firm pressure |
| 1024 | Maximum pressure |

**Tip**: Vary pressure along a stroke for calligraphy effects:

```json
{
  "tool": "drag",
  "args": {
    "path": [
      { "x": 50, "y": 100, "pressure": 256 },
      { "x": 100, "y": 100, "pressure": 768 },
      { "x": 150, "y": 100, "pressure": 256 }
    ],
    "input": "pen",
    "duration_ms": 1000
  }
}
```

### Eraser Mode

To erase ink:

```json
{
  "tool": "drag",
  "args": {
    "path": [
      { "x": 50, "y": 50 },
      { "x": 200, "y": 50 }
    ],
    "input": "pen",
    "eraser": true
  }
}
```

---

## Common Patterns

### Drawing a Rectangle

```json
{
  "tool": "drag",
  "args": {
    "path": [
      { "x": 100, "y": 100 },
      { "x": 300, "y": 100 },
      { "x": 300, "y": 200 },
      { "x": 100, "y": 200 },
      { "x": 100, "y": 100 }
    ],
    "input": "pen",
    "duration_ms": 2000
  }
}
```

### Drawing a Circle (Approximation)

Use multiple short strokes or a high-resolution path:

```python
# Python pseudocode for generating circle path
import math

center_x, center_y = 200, 200
radius = 50
segments = 32
path = []

for i in range(segments + 1):
    angle = (i / segments) * 2 * math.pi
    x = center_x + radius * math.cos(angle)
    y = center_y + radius * math.sin(angle)
    path.append({ "x": int(x), "y": int(y), "pressure": 512 })

# Send as one drag command
```

### Swipe to Scroll

```json
{
  "tool": "drag",
  "args": {
    "path": [
      { "x": 300, "y": 500 },
      { "x": 300, "y": 200 }
    ],
    "input": "touch",
    "duration_ms": 500
  }
}
```

### Swipe to Dismiss

```json
{
  "tool": "drag",
  "args": {
    "path": [
      { "x": 50, "y": 300 },
      { "x": 400, "y": 300 }
    ],
    "input": "touch",
    "duration_ms": 300
  }
}
```

---

## Sandbox Scaling Issues

### DPI Mismatch in Windows Sandbox

Windows Sandbox may have a different DPI setting than the host machine. This can cause coordinate misalignment for ink and touch input.

**Symptoms:**
- Pen strokes appear offset from where they should be
- Touch taps don't hit the intended target
- Drawing appears scaled incorrectly

**Root Cause:**
The sandbox window inherits RDP scaling behavior. If your host runs at 150% DPI but the sandbox uses 100%, coordinates need manual adjustment.

### In-App Rescaling Required

For accurate ink/touch input in sandbox, applications may need to handle DPI scaling explicitly:

**WPF Applications:**
```csharp
// In App.xaml.cs - Enable per-monitor DPI awareness
protected override void OnStartup(StartupEventArgs e)
{
    // Force per-monitor DPI awareness
    SetProcessDpiAwareness(PROCESS_DPI_AWARENESS.Process_Per_Monitor_DPI_Aware);
    base.OnStartup(e);
}
```

**WinForms Applications:**
```csharp
// In Program.cs before Application.Run
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
```

### Workarounds

1. **Query DPI before input:**
   ```json
   { "tool": "get_dpi_info", "args": { "windowTitle": "MyApp" } }
   ```
   Check `window_scale_factor` and adjust coordinates accordingly.

2. **Use element-based interactions when possible:**
   Instead of raw coordinates, use `click` with `target` (element ID) which handles DPI internally.

3. **Match host DPI in sandbox:**
   Launch sandbox with display settings matching host DPI (not always possible).

4. **Manual coordinate scaling:**
   ```
   scaled_x = x * (sandbox_dpi / host_dpi)
   scaled_y = y * (sandbox_dpi / host_dpi)
   ```

---

## Permissions

### Admin Privileges

Touch and pen input injection (`InjectTouchInput` API) requires elevated privileges on Windows.

**In Windows Sandbox**: This is solved automatically - sandbox runs as Admin.

**On bare metal**: The application must run as Administrator, or you'll get "Access Denied" errors.

### Troubleshooting Permission Errors

**Error**: `InjectTouchInput failed: Access Denied`

**Solutions**:
1. Run MCP server as Administrator
2. Use Windows Sandbox (recommended)
3. Check if UAC is blocking the operation

---

## Performance Considerations

### Step Count

More steps = smoother animation but slower execution.

| Duration | Use Case |
|-------|----------|
| 100-300ms | Quick gestures |
| 500-1000ms | Normal UI interactions |
| 2000ms+ | Smooth drawing, visual demos |

### Delays

Add delays between gestures if the app needs time to respond:

```json
{
  "tool": "run_script",
  "args": {
    "script": {
      "steps": [
        { "tool": "click", "args": { "x": 100, "y": 100, "input": "touch" }, "delay_after_ms": 200 },
        { "tool": "click", "args": { "x": 200, "y": 100, "input": "touch" }, "delay_after_ms": 200 }
      ]
    }
  }
}
```

---

## Best Practices

### 1. Use Element Targeting When Possible

Use `target` (element ID) instead of coordinates to avoid DPI/resolution issues:

```json
{ "tool": "click", "args": { "target": "elem_123", "input": "touch" } }
```

### 2. Verify DPI Scaling

Check DPI before coordinate-heavy operations:

```json
{ "tool": "get_dpi_info", "args": { "windowTitle": "MyApp" } }
```

### 3. Take Screenshots for Debugging

Capture before/after screenshots to verify gestures:

```json
{
  "tool": "take_screenshot",
  "args": { "windowTitle": "InkCanvas", "outputPath": "C:\\Output\\before.png" }
}
```

### 4. Use Scripts for Complex Drawings

Batch multiple strokes together to reduce round-trip overhead:

```json
{
  "tool": "run_script",
  "args": {
    "script": {
      "steps": [
        { "tool": "drag", "args": { ... } },
        { "tool": "drag", "args": { ... } }
      ]
    }
  }
}
```

### 5. Handle Touch Failures Gracefully

If a touch gesture fails, try:
1. Verify window is focused
2. Check if element is scrolled out of view
3. Increase duration for smoother gesture
4. Add delay before the gesture
