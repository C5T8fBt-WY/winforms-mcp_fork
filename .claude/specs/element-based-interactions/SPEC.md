# Element-Based Interactions Spec

## Goal

Add optional element-based targeting to all coordinate-based interaction tools. Instead of requiring x/y coordinates, allow tools to target elements by their session ID and automatically resolve to element center coordinates.

## Current State

### Element-based tools (already work with elements):
- `click_element` - elementId/elementPath
- `type_text` - elementId/elementPath
- `drag_drop` - sourceElementId/targetElementId

### Coordinate-only tools (need element support):
| Tool | Current Params | Proposed Element Params |
|------|---------------|------------------------|
| `touch_tap` | x, y | elementId |
| `touch_drag` | x1, y1, x2, y2 | sourceElementId, targetElementId |
| `pen_tap` | x, y | elementId |
| `pen_stroke` | x1, y1, x2, y2 | sourceElementId, targetElementId |
| `mouse_click` | x, y | elementId |
| `mouse_drag` | x1, y1, x2, y2 | sourceElementId, targetElementId |
| `pinch_zoom` | centerX, centerY | elementId (center on element) |
| `rotate` | centerX, centerY | elementId (center on element) |

## Design

### Resolution Priority

1. If `elementId` provided → resolve element center, ignore x/y
2. If `sourceElementId`/`targetElementId` provided → resolve both centers
3. Fall back to x/y coordinates if no element specified
4. Error if neither element nor coordinates provided

### Helper Method

```csharp
private (int x, int y)? ResolveElementCenter(string? elementId)
{
    if (string.IsNullOrEmpty(elementId))
        return null;

    var element = _session.GetElement(elementId);
    if (element == null)
        throw new ArgumentException($"Element '{elementId}' not found in session");

    var bounds = element.BoundingRectangle;
    if (bounds.Width == 0 || bounds.Height == 0)
        throw new InvalidOperationException($"Element '{elementId}' has invalid bounds");

    return (
        (int)(bounds.X + bounds.Width / 2),
        (int)(bounds.Y + bounds.Height / 2)
    );
}
```

### Tool Updates

#### Single-point tools (touch_tap, pen_tap, mouse_click)

```csharp
// Before
var x = GetIntArg(args, "x");
var y = GetIntArg(args, "y");

// After
var elementId = GetStringArg(args, "elementId");
int x, y;

if (!string.IsNullOrEmpty(elementId))
{
    var center = ResolveElementCenter(elementId);
    (x, y) = center.Value;
}
else
{
    x = GetIntArg(args, "x");
    y = GetIntArg(args, "y");
}
```

#### Drag tools (touch_drag, pen_stroke, mouse_drag)

```csharp
var sourceElementId = GetStringArg(args, "sourceElementId");
var targetElementId = GetStringArg(args, "targetElementId");
int x1, y1, x2, y2;

if (!string.IsNullOrEmpty(sourceElementId) && !string.IsNullOrEmpty(targetElementId))
{
    var source = ResolveElementCenter(sourceElementId);
    var target = ResolveElementCenter(targetElementId);
    (x1, y1) = source.Value;
    (x2, y2) = target.Value;
}
else
{
    x1 = GetIntArg(args, "x1");
    y1 = GetIntArg(args, "y1");
    x2 = GetIntArg(args, "x2");
    y2 = GetIntArg(args, "y2");
}
```

#### Center-based tools (pinch_zoom, rotate)

```csharp
var elementId = GetStringArg(args, "elementId");
int centerX, centerY;

if (!string.IsNullOrEmpty(elementId))
{
    var center = ResolveElementCenter(elementId);
    (centerX, centerY) = center.Value;
}
else
{
    centerX = GetIntArg(args, "centerX");
    centerY = GetIntArg(args, "centerY");
}
```

## Schema Updates

Each tool's inputSchema needs the new optional parameters:

```csharp
// For single-point tools
elementId = new { type = "string", description = "Element ID from session. If provided, taps center of element (ignores x/y)." }

// For drag tools
sourceElementId = new { type = "string", description = "Source element ID. If provided with targetElementId, drags from source center to target center." }
targetElementId = new { type = "string", description = "Target element ID. Required if sourceElementId is provided." }
```

## Examples

```json
// Touch tap on button element
{"name": "touch_tap", "arguments": {"elementId": "elem_5"}}

// Pen stroke between two elements
{"name": "pen_stroke", "arguments": {"sourceElementId": "elem_1", "targetElementId": "elem_2", "pressure": 512}}

// Mouse drag with element targeting
{"name": "mouse_drag", "arguments": {"sourceElementId": "slider_thumb", "targetElementId": "slider_end"}}

// Pinch zoom centered on canvas element
{"name": "pinch_zoom", "arguments": {"elementId": "canvas", "startDistance": 100, "endDistance": 200}}

// Falls back to coordinates if no elementId
{"name": "touch_tap", "arguments": {"x": 100, "y": 200}}
```

## Validation

- If elementId provided but not in session → error with helpful message
- If element has zero-size bounds → error (element not visible/rendered)
- If both elementId and x/y provided → elementId wins (log warning?)
- If sourceElementId provided without targetElementId → error

## Testing

1. Tap element by ID
2. Drag from element to element
3. Mixed: element source, coordinate target (if we support it)
4. Invalid element ID handling
5. Zero-bounds element handling
