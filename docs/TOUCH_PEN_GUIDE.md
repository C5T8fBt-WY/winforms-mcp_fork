# Touch and Pen Input Guide

A guide for using touch and pen input tools for ink-based and gesture-based UI automation.

## Overview

The WinForms MCP server provides low-level input injection for:
- **Touch**: Taps, drags, pinch gestures, rotation
- **Pen/Stylus**: Pressure-sensitive strokes, eraser mode, tilt

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

All coordinate-based tools accept `windowHandle` or `windowTitle` to use window-relative coordinates:

```json
{
  "tool": "touch_tap",
  "args": {
    "x": 100,
    "y": 50,
    "windowTitle": "InkCanvas Demo"
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
  "tool": "touch_tap",
  "args": {
    "x": 200,
    "y": 300,
    "windowTitle": "MyApp"
  }
}
```

### Touch Drag

Single-finger drag gesture:

```json
{
  "tool": "touch_drag",
  "args": {
    "x1": 100,
    "y1": 300,
    "x2": 400,
    "y2": 300,
    "steps": 20,
    "windowTitle": "MyApp"
  }
}
```

**Parameters**:
- `x1, y1`: Start point
- `x2, y2`: End point
- `steps`: Intermediate points (default: 10)

More steps = smoother animation, slower execution.

### Pinch Zoom

Two-finger pinch gesture:

```json
{
  "tool": "pinch_zoom",
  "args": {
    "centerX": 300,
    "centerY": 300,
    "startDistance": 50,
    "endDistance": 200,
    "steps": 20,
    "windowTitle": "MyApp"
  }
}
```

**Parameters**:
- `centerX, centerY`: Center point of the pinch
- `startDistance`: Initial finger separation (pixels)
- `endDistance`: Final finger separation
- `steps`: Animation smoothness

**Zoom in**: `endDistance > startDistance`
**Zoom out**: `endDistance < startDistance`

### Rotation Gesture

Two-finger rotation:

```json
{
  "tool": "rotate_gesture",
  "args": {
    "centerX": 300,
    "centerY": 300,
    "radius": 100,
    "startAngle": 0,
    "endAngle": 90,
    "steps": 20,
    "windowTitle": "MyApp"
  }
}
```

**Parameters**:
- `centerX, centerY`: Center of rotation
- `radius`: Distance from center to each finger
- `startAngle, endAngle`: Rotation in degrees (clockwise)
- `steps`: Animation smoothness

### Multi-Touch Gesture

Arbitrary multi-touch with multiple contact points:

```json
{
  "tool": "multi_touch_gesture",
  "args": {
    "touches": [
      {
        "id": 0,
        "path": [
          { "x": 100, "y": 100 },
          { "x": 150, "y": 150 },
          { "x": 200, "y": 200 }
        ]
      },
      {
        "id": 1,
        "path": [
          { "x": 400, "y": 100 },
          { "x": 350, "y": 150 },
          { "x": 300, "y": 200 }
        ]
      }
    ],
    "stepDelayMs": 50,
    "windowTitle": "MyApp"
  }
}
```

**Parameters**:
- `touches`: Array of touch contact definitions
- Each touch has `id` (0-9) and `path` (array of coordinates)
- `stepDelayMs`: Delay between path points

---

## Pen/Stylus Input

### Pen Tap

Quick pen tap (like clicking):

```json
{
  "tool": "pen_tap",
  "args": {
    "x": 200,
    "y": 300,
    "pressure": 512,
    "windowTitle": "InkCanvas"
  }
}
```

**Parameters**:
- `pressure`: 0-1024 (0 = hovering, 1024 = maximum)

### Pen Stroke

Draw a line with pressure:

```json
{
  "tool": "pen_stroke",
  "args": {
    "x1": 50,
    "y1": 50,
    "x2": 200,
    "y2": 200,
    "pressure": 512,
    "steps": 20,
    "eraser": false,
    "windowTitle": "InkCanvas"
  }
}
```

**Parameters**:
- `x1, y1`: Start point
- `x2, y2`: End point
- `pressure`: Pressure level (0-1024)
- `steps`: Intermediate points for smooth strokes
- `eraser`: Use eraser end of stylus (default: false)

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
  "tool": "run_script",
  "args": {
    "script": {
      "steps": [
        { "tool": "pen_stroke", "args": { "x1": 50, "y1": 100, "x2": 100, "y2": 100, "pressure": 256 } },
        { "tool": "pen_stroke", "args": { "x1": 100, "y1": 100, "x2": 150, "y2": 100, "pressure": 768 } },
        { "tool": "pen_stroke", "args": { "x1": 150, "y1": 100, "x2": 200, "y2": 100, "pressure": 256 } }
      ]
    }
  }
}
```

### Eraser Mode

To erase ink:

```json
{
  "tool": "pen_stroke",
  "args": {
    "x1": 50,
    "y1": 50,
    "x2": 200,
    "y2": 50,
    "pressure": 512,
    "eraser": true,
    "windowTitle": "InkCanvas"
  }
}
```

---

## Common Patterns

### Drawing a Rectangle

```json
{
  "tool": "run_script",
  "args": {
    "script": {
      "steps": [
        { "tool": "pen_stroke", "args": { "x1": 100, "y1": 100, "x2": 300, "y2": 100, "pressure": 512 } },
        { "tool": "pen_stroke", "args": { "x1": 300, "y1": 100, "x2": 300, "y2": 200, "pressure": 512 } },
        { "tool": "pen_stroke", "args": { "x1": 300, "y1": 200, "x2": 100, "y2": 200, "pressure": 512 } },
        { "tool": "pen_stroke", "args": { "x1": 100, "y1": 200, "x2": 100, "y2": 100, "pressure": 512 } }
      ],
      "options": { "default_delay_ms": 50 }
    }
  }
}
```

### Drawing a Circle (Approximation)

Use multiple short strokes around the circumference:

```python
# Python pseudocode for generating circle strokes
import math

center_x, center_y = 200, 200
radius = 50
segments = 16

strokes = []
for i in range(segments):
    angle1 = (i / segments) * 2 * math.pi
    angle2 = ((i + 1) / segments) * 2 * math.pi

    x1 = center_x + radius * math.cos(angle1)
    y1 = center_y + radius * math.sin(angle1)
    x2 = center_x + radius * math.cos(angle2)
    y2 = center_y + radius * math.sin(angle2)

    strokes.append({
        "tool": "pen_stroke",
        "args": { "x1": x1, "y1": y1, "x2": x2, "y2": y2, "pressure": 512 }
    })
```

### Swipe to Scroll

```json
{
  "tool": "touch_drag",
  "args": {
    "x1": 300,
    "y1": 500,
    "x2": 300,
    "y2": 200,
    "steps": 20,
    "windowTitle": "ScrollableList"
  }
}
```

### Swipe to Dismiss

```json
{
  "tool": "touch_drag",
  "args": {
    "x1": 50,
    "y1": 300,
    "x2": 400,
    "y2": 300,
    "steps": 10,
    "windowTitle": "NotificationCard"
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
   Instead of raw coordinates, use `click_element` or `click_by_automation_id` which handle DPI internally.

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

| Steps | Use Case |
|-------|----------|
| 5-10 | Quick gestures, performance testing |
| 15-20 | Normal UI interactions |
| 30-50 | Smooth drawing, visual demos |

### Delays

Add delays between gestures if the app needs time to respond:

```json
{
  "tool": "run_script",
  "args": {
    "script": {
      "steps": [
        { "tool": "touch_tap", "args": { "x": 100, "y": 100 }, "delay_after_ms": 200 },
        { "tool": "touch_tap", "args": { "x": 200, "y": 100 }, "delay_after_ms": 200 }
      ]
    }
  }
}
```

---

## Best Practices

### 1. Use Window-Relative Coordinates

Always specify `windowTitle` or `windowHandle` to avoid issues with window positioning:

```json
{ "windowTitle": "MyApp" }
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
        { "tool": "pen_stroke", "args": { ... } },
        { "tool": "pen_stroke", "args": { ... } },
        { "tool": "pen_stroke", "args": { ... } }
      ]
    }
  }
}
```

### 5. Handle Touch Failures Gracefully

If a touch gesture fails, try:
1. Verify window is focused
2. Check if element is scrolled out of view
3. Increase step count for smoother gesture
4. Add delay before the gesture
