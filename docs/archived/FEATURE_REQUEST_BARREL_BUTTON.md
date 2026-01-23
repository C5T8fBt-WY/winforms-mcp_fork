# Feature Request: Synthetic Pen Barrel Button Support

## Summary

Add support for injecting synthetic pen input with barrel button state to enable automated testing of pen-specific features like tool cycling on barrel button press.

## Background

The current `pen_stroke` and `drag` tools use `SendInput` with mouse events, which:
- Does NOT trigger the WPF `RealTimeStylus` / `StylusPlugIn` pipeline
- Cannot simulate barrel button presses
- Cannot simulate pen hover (in-range but not in-contact)

Applications like MagpieCad use barrel button detection for tool cycling, and this feature cannot be tested with the current MCP tools.

## Technical Approach

Use the Windows 8+ `InjectSyntheticPointerInput` API instead of `SendInput` for pen input.

### Key APIs

```csharp
[DllImport("user32.dll", SetLastError = true)]
public static extern bool InjectSyntheticPointerInput(
    IntPtr device,
    [In] POINTER_TYPE_INFO[] pointerInfo,
    uint count);

[DllImport("user32.dll")]
public static extern IntPtr CreateSyntheticPointerDevice(
    int pointerType,  // PT_PEN = 3
    uint maxCount,
    int mode);

[DllImport("user32.dll")]
public static extern void DestroySyntheticPointerDevice(IntPtr device);
```

### Critical Flags

```csharp
// Pointer types
const int PT_PEN = 3;

// Pointer flags (for pointerInfo.pointerFlags)
const int POINTER_FLAG_INRANGE = 0x00000002;   // Pen is hovering
const int POINTER_FLAG_INCONTACT = 0x00000004; // Pen is touching
const int POINTER_FLAG_UPDATE = 0x00000200;    // Update event
const int POINTER_FLAG_DOWN = 0x00010000;      // Contact start
const int POINTER_FLAG_UP = 0x00020000;        // Contact end

// Pen flags (for penInfo.penFlags) - THE KEY FOR BARREL BUTTON
const int PEN_FLAG_BARREL = 0x00000001;        // Barrel button pressed
const int PEN_FLAG_INVERTED = 0x00000002;      // Eraser end
const int PEN_FLAG_ERASER = 0x00000004;        // Eraser mode
```

### Required Structs

```csharp
[StructLayout(LayoutKind.Explicit)]
public struct POINTER_TYPE_INFO
{
    [FieldOffset(0)] public int type;
    [FieldOffset(4)] public POINTER_PEN_INFO penInfo;  // For PT_PEN
}

[StructLayout(LayoutKind.Sequential)]
public struct POINTER_PEN_INFO
{
    public POINTER_INFO pointerInfo;
    public int penFlags;      // SET TO PEN_FLAG_BARREL for barrel button
    public int penMask;       // Indicates which flags are valid
    public int pressure;      // 0-1024
    public int rotation;      // 0-359 degrees
    public int tiltX;         // -90 to +90
    public int tiltY;         // -90 to +90
}

[StructLayout(LayoutKind.Sequential)]
public struct POINTER_INFO
{
    public int pointerType;
    public uint pointerId;
    public uint frameId;
    public int pointerFlags;
    public IntPtr sourceDevice;
    public IntPtr hwndTarget;
    public POINT ptPixelLocation;
    public POINT ptHimetricLocation;
    public POINT ptPixelLocationRaw;
    public POINT ptHimetricLocationRaw;
    public uint time;
    public uint historyCount;
    public int inputData;
    public uint keyStates;
    public ulong performanceCount;
    public int buttonChangeType;
}
```

## Proposed MCP Tool: `pen_input`

A new tool that provides full control over synthetic pen input.

### Schema

```json
{
  "name": "pen_input",
  "description": "Inject synthetic pen input with full control over pen state",
  "parameters": {
    "action": {
      "type": "string",
      "enum": ["enter", "move", "down", "up", "leave"],
      "description": "Pen action type"
    },
    "x": { "type": "integer", "description": "Screen X coordinate" },
    "y": { "type": "integer", "description": "Screen Y coordinate" },
    "pressure": {
      "type": "integer",
      "default": 512,
      "description": "Pen pressure 0-1024"
    },
    "barrel": {
      "type": "boolean",
      "default": false,
      "description": "Barrel button pressed"
    },
    "eraser": {
      "type": "boolean",
      "default": false,
      "description": "Eraser tip active"
    },
    "tiltX": { "type": "integer", "default": 0 },
    "tiltY": { "type": "integer", "default": 0 }
  }
}
```

### Usage Examples

**Barrel button click while hovering (air click):**
```json
// 1. Enter range
{"action": "enter", "x": 500, "y": 300}

// 2. Hover with barrel button pressed
{"action": "move", "x": 500, "y": 300, "barrel": true}

// 3. Release barrel button
{"action": "move", "x": 500, "y": 300, "barrel": false}

// 4. Leave range
{"action": "leave", "x": 500, "y": 300}
```

**Draw stroke with barrel button:**
```json
{"action": "enter", "x": 100, "y": 200}
{"action": "down", "x": 100, "y": 200, "barrel": true, "pressure": 512}
{"action": "move", "x": 200, "y": 200, "barrel": true, "pressure": 512}
{"action": "up", "x": 200, "y": 200}
{"action": "leave", "x": 200, "y": 200}
```

**Eraser mode:**
```json
{"action": "enter", "x": 300, "y": 300, "eraser": true}
{"action": "down", "x": 300, "y": 300, "eraser": true}
// ... erase strokes ...
{"action": "up", "x": 400, "y": 300}
```

## Higher-Level Convenience Tool: `pen_barrel_click`

For common use cases, a simpler tool:

```json
{
  "name": "pen_barrel_click",
  "description": "Simulate barrel button click while hovering",
  "parameters": {
    "x": { "type": "integer" },
    "y": { "type": "integer" },
    "hold_ms": {
      "type": "integer",
      "default": 100,
      "description": "How long to hold barrel button"
    }
  }
}
```

Implementation would automatically sequence: enter → move with barrel → wait → move without barrel → leave.

## State Machine Requirements

Windows requires proper sequencing or it ignores the input:

```
                    ┌─────────────────┐
                    │   Not in Range  │
                    └────────┬────────┘
                             │ ENTER (INRANGE)
                             ▼
                    ┌─────────────────┐
        ┌──────────▶│    Hovering     │◀──────────┐
        │           │  (INRANGE only) │           │
        │           └────────┬────────┘           │
        │                    │ DOWN (INCONTACT)   │ UP
        │                    ▼                    │
        │           ┌─────────────────┐           │
        │           │   In Contact    │───────────┘
        │           │ (INRANGE+CONTACT)
        │           └─────────────────┘
        │
        │ LEAVE (clear INRANGE)
        │
        └─── Back to "Not in Range"
```

Barrel button (`PEN_FLAG_BARREL`) can be set in ANY state (hovering or contact).

## Implementation Notes

1. **Device Lifetime**: Create the synthetic device once per session, reuse for all injections, destroy on cleanup.

2. **Pointer ID**: Use a consistent pointer ID (e.g., 1) for all pen events in a sequence.

3. **Frame ID**: Increment frame ID for each injection.

4. **Timing**: May need small delays (5-10ms) between injections for Windows to process.

5. **Coordinates**: Use screen coordinates (same as current tools).

6. **HIMETRIC**: Some apps may need `ptHimetricLocation` set. Calculate from pixel coords using display DPI.

## Testing Plan

Once implemented, test with MagpieCad:

1. **Barrel button hover click**: Should trigger tool cycle
2. **Barrel button during stroke**: Should block stroke and cycle tool
3. **Timer auto-return**: After barrel click, wait 1.5s, verify tool returns to start
4. **Multiple rapid clicks**: Should only cycle once per distinct press (leading edge detection)

## References

- [Pointer Input Messages and Notifications](https://docs.microsoft.com/en-us/windows/win32/inputmsg/wm-pointerupdate)
- [InjectSyntheticPointerInput](https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-injectsyntheticpointerinput)
- [POINTER_PEN_INFO structure](https://docs.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-pointer_pen_info)
