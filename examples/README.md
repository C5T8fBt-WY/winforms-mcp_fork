# Example Agent Scripts

This directory contains example agent scripts demonstrating common UI automation patterns with the WinForms MCP server.

## Examples

### [form-filling-agent.json](form-filling-agent.json)

**Simple Form Filling Agent**

Demonstrates the basic workflow for form automation:
- Application launch and wait
- UI tree exploration
- Element discovery by AutomationId
- Text input into fields
- State change detection after submit

### [test-generation-agent.json](test-generation-agent.json)

**UI Test Case Generation Agent**

Shows how to explore UI and generate test documentation:
- Progressive disclosure (shallow first, expand targets)
- Systematic element enumeration
- State change detection for each interaction
- Event subscription for async UI changes
- Test case documentation generation

### [bug-detection-agent.json](bug-detection-agent.json)

**UI Bug Detection Agent**

Scans UI for accessibility and usability issues:
- Unlabeled buttons (WCAG 1.1.1)
- Disabled controls without explanation (WCAG 3.3.1)
- Missing keyboard focus (WCAG 2.1.1)
- Inconsistent tab order (WCAG 2.4.3)
- Truncated text detection

### [ink-drawing-agent.json](ink-drawing-agent.json)

**Pen-Based Drawing Agent**

Demonstrates pen/stylus input for InkCanvas:
- Pen strokes with pressure variation
- Drawing shapes (rectangles, signatures)
- Eraser mode for corrections
- Pinch zoom gestures
- Screenshot verification

## Usage

These examples are JSON documents describing workflows. They are not directly executable but serve as:

1. **Reference documentation** for common patterns
2. **Templates** for building agent logic
3. **Test cases** for verifying MCP server functionality

### Using with run_script

The ink-drawing-agent examples include `run_script` compatible scripts:

```json
{
  "method": "tools/call",
  "params": {
    "name": "run_script",
    "arguments": {
      "script": {
        "steps": [...],
        "options": { "stop_on_error": true }
      }
    }
  }
}
```

### Variable Interpolation

Scripts can reference previous step results:

```json
{
  "id": "find_btn",
  "tool": "find_element",
  "args": { "automationId": "btnSubmit" }
},
{
  "tool": "click_element",
  "args": { "elementPath": "$find_btn.result.elementId" }
}
```

## Key Patterns

### OODA Loop

All examples follow the Observe-Orient-Decide-Act pattern:

1. **Observe**: `get_ui_tree`, `check_element_state`
2. **Orient**: Analyze tree structure, identify targets
3. **Decide**: Plan interaction sequence
4. **Act**: `click_element`, `type_text`, etc.

### State Change Detection

Verify actions succeeded:

```
capture_ui_snapshot(before) → action → compare_ui_snapshots(before)
```

### Progressive Disclosure

Start shallow, expand as needed:

```
get_ui_tree(depth=2) → mark_for_expansion(target) → get_ui_tree(depth=5)
```

### Self-Healing

Handle stale references:

```
action fails → check_element_stale → relocate_element → retry action
```

## See Also

- [MCP_TOOLS.md](../docs/MCP_TOOLS.md) - Complete tool reference
- [AGENT_EXPLORATION_GUIDE.md](../docs/AGENT_EXPLORATION_GUIDE.md) - Exploration patterns
- [TOUCH_PEN_GUIDE.md](../docs/TOUCH_PEN_GUIDE.md) - Touch/pen input details
