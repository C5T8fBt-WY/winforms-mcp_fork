# UI Agent Exploration & Testing - Distilled Guide

## Purpose
Battle-tested prompts and architectures for autonomous UI exploration agents that can:
1. Identify test cases to write
2. Identify active bugs (error messages, toasts)
3. Identify UX gaps (missing expected functionality)
4. Calculate UI complexity and needed affordances
5. Generate comprehensive documentation

---

## 1. MASTER SYSTEM PROMPT (Core Architecture)

### Role & Prime Directive
```
You are an Autonomous UX Explorer and Quality Engineer.

PRIME DIRECTIVE: Explore deeply, not just broadly. Treat the UI as a graph where:
- Nodes = UI States (URL/Window + DOM/UIA structure)
- Edges = Actions (Clicks, Inputs)
```

### Operational Loop (OODA)
```
1. OBSERVE: Use get_snapshot/get_ui_tree to understand current state. Identify interactive elements.

2. ORIENT: Check your "Visited States" map. Have you been here? Is this an error state?

3. DECIDE: Select an unvisited action or path to a new feature.
   Priority: Happy Paths first, then edge cases.

4. ACT: Use execute_action to manipulate the UI.

5. RECORD: Log the transition (State_prev, Action, State_new).
```

### Critical Constraints
```
- Do NOT submit "Contact Us" forms or trigger live payments unless authorized
- If you encounter a loop, break it by navigation (Back/Home)
- Selector Priority (Web): data-testid > id > aria-label > text content > CSS
- Selector Priority (Windows): AutomationId > Name > Anchor-based > Coordinates
```

---

## 2. FIVE ANALYSIS LENSES

### Lens 1: Test Case Identification
```
INSTRUCTION: "Based on session history and state transitions, generate E2E test cases."

OUTPUT FORMAT (JSON):
{
  "title": "User can successfully filter list by date",
  "preconditions": "User is logged in, on dashboard",
  "steps": [
    {"action": "click", "target": "filter_button"},
    {"action": "type", "target": "date_input", "value": "2025-01-01"}
  ],
  "assertions": [
    "URL changed to /dashboard?filter=date",
    "Success toast appeared"
  ]
}

STYLE: Write in Gherkin-like syntax or pseudo-Playwright code.
```

### Lens 2: Bug Detection
```
INSTRUCTION: "Analyze last N interactions for failure signals. Report immediately if detected:"

DETECT:
- Console Noise: console.error, unhandled rejections
- Visual Errors: Elements with text 'Error', 'Failed', '404', or red warning colors
- Toast/Notifications: Any transient overlays
- Dead Ends: Buttons that were clickable but resulted in no State Change

OUTPUT: Bug Report with exact inputs that triggered the state.
```

### Lens 3: UX Gap Analysis
```
INSTRUCTION: "Act as a Product Manager. Compare available functionality against industry standards."

ANALYSIS TASKS:
- CRUD Check: If this is a list, can I Create, Read, Update, Delete? What's missing?
- Feedback Loop: When I performed an action, was there immediate visual feedback?
- Navigation: Is there a clear way 'back' without browser back button?
- Empty States: If a list was empty, was there a helpful message or CTA?

OUTPUT: List 'Missing Features' ordered by severity.
```

### Lens 4: UI Complexity Calculation
```
INSTRUCTION: "Calculate 'Interaction Cost' for the primary workflow."

METRICS:
- Click Depth: How many clicks from 'Home' to 'Success' state?
- Input Friction: Count required form fields
- Cognitive Load: Text density, primary vs secondary button count

AFFORDANCE AUDIT:
Identify elements violating standard affordances:
- Text that looks like link but isn't
- Buttons that look disabled but are active

OUTPUT: Suggest UI changes to reduce Interaction Cost.
```

### Lens 5: Documentation Generation
```
INSTRUCTION: "Using the state graph discovered, write a 'User Manual' section."

STRUCTURE:
- Goal: What can the user achieve here?
- Prerequisites: What must happen before using this?
- Step-by-Step Guide: Use actual UI labels found
- Troubleshooting: Fill with edge cases/non-obvious behaviors discovered
```

---

## 3. IMPLEMENTATION: THE CONTROLLER LOOP

```python
# The Master Control Loop
while exploring:
    # 1. Get Current State
    snapshot = mcp.call_tool("get_snapshot") # or get_ui_tree for Windows

    # 2. Ask LLM what to do (Master Prompt + Current State)
    decision = llm.chat([system_prompt, f"Current State: {snapshot}"])

    # 3. Execute the chosen tool/action
    result = mcp.call_tool(decision.tool_name, decision.args)

    # 4. Post-Action Analysis (Apply Lenses)
    logs = mcp.call_tool("get_console_logs") # or check for error dialogs
    bug_report = llm.chat([bug_hunting_prompt, f"Logs: {logs}"])

    # 5. Update State Graph
    graph.add_edge(prev_state, decision.action, current_state)

    # 6. Check for terminal conditions
    if is_stuck_in_loop() or max_steps_reached():
        break

# Final Synthesis
documentation = llm.chat([documentation_prompt, f"Graph: {graph.export()}"])
test_cases = llm.chat([test_generation_prompt, f"Graph: {graph.export()}"])
```

---

## 4. CRITICAL GOTCHAS (Anti-Patterns)

### The Logout Trap
```
"If you see a button labeled 'Logout' or 'Sign Out',
mark it as a terminal node. DO NOT click it unless
specifically testing authentication flows."
```

### The Hallucinated Selector
```
"You must strictly use selectors that actually exist
in the provided DOM/UIA snapshot. Do NOT guess IDs."
```

### The State Loop
```
"If you perform the same action sequence more than 3 times,
STOP and mark this path as a 'Recursive Loop'."
```

### The False Positive Healing
```
"Self-healing agents must maintain strict State Boundaries.
If a 'Buy Now' button is missing, DO NOT hallucinate an
alternate path (like clicking a hidden admin link).
FAIL LOUDLY so regressions are not masked."
```

---

## 5. MCP TOOL SPECIFICATIONS FOR WINDOWS

### Tool 1: `get_ui_tree`
```json
{
  "name": "get_ui_tree",
  "description": "Returns simplified XML of Windows UIA tree (filtered)",
  "parameters": {
    "target_window": {
      "type": "string",
      "description": "Regex to filter window (e.g., 'Notepad.*')"
    },
    "max_depth": {
      "type": "integer",
      "default": 3,
      "description": "Tree traversal depth (2-3 for speed, 5-10 for detail)"
    },
    "include_offscreen": {
      "type": "boolean",
      "default": false,
      "description": "Include virtualized/scrolled-out elements"
    },
    "format": {
      "type": "string",
      "enum": ["xml", "json_compact"],
      "default": "xml"
    }
  }
}
```

**Output Example:**
```xml
<Window name="Calculator" id="1">
  <Group name="Keypad" id="4">
    <Button name="One" id="5" clickable="true"/>
    <Button name="Two" id="6" clickable="true"/>
  </Group>
</Window>
```

### Tool 2: `execute_action`
```json
{
  "name": "execute_action",
  "parameters": {
    "action": {
      "enum": ["click", "double_click", "right_click", "hover", "focus"]
    },
    "selector_type": {
      "enum": ["automation_id", "name", "xpath", "coordinates"]
    },
    "selector_value": {
      "type": "string",
      "description": "ID, Name, or '1920,1080' for coordinates"
    },
    "window_handle": {
      "type": "string",
      "description": "HWND hex string (optional but recommended)"
    }
  }
}
```

### Tool 3: `get_screenshot`
```json
{
  "name": "get_screenshot",
  "parameters": {
    "element_id": {
      "type": "string",
      "description": "Optional: crop to specific element"
    },
    "draw_highlights": {
      "type": "boolean",
      "description": "Draw bounding box around element"
    },
    "with_som": {
      "type": "boolean",
      "description": "Overlay Set-of-Mark IDs (numbered boxes)"
    }
  }
}
```

### Tool 4: `type_text`
```json
{
  "name": "type_text",
  "parameters": {
    "selector_type": {"enum": ["automation_id", "name"]},
    "selector_value": {"type": "string"},
    "text": {"type": "string"},
    "clear_first": {"type": "boolean", "default": true}
  }
}
```

### Tool 5: `wait_for`
```json
{
  "name": "wait_for",
  "parameters": {
    "condition": {
      "enum": ["visible", "not_visible", "enabled", "text_contains"]
    },
    "selector_type": {"enum": ["automation_id", "name"]},
    "selector_value": {"type": "string"},
    "expected_text": {"type": "string", "description": "For text_contains"},
    "timeout": {"type": "integer", "default": 10000, "description": "ms"}
  }
}
```

---

## 6. ADVANCED REASONING PATTERNS

### Pattern 1: Self-Healing Loop
```
TRY:
  execute_action(id="SubmitBtn_123")
CATCH ElementNotFoundException:
  1. Get fresh UI tree
  2. REASON: "Expected 'SubmitBtn_123', not found."
  3. SEARCH: Find elements with Name="Submit" and ControlType="Button"
  4. INFER: "ID suffix is dynamic. Use Name selector instead."
  5. RETRY: execute_action(selector_type="name", selector_value="Submit")
```

### Pattern 2: Anchor-Based Navigation (Legacy Apps)
```
PROBLEM: Text box has no AutomationId or Name.

SOLUTION:
1. Find Anchor: Search for static text "Invoice Number"
2. Get anchor_rect = element.BoundingRectangle
3. Search siblings where:
   - element.type == "Edit"
   - element.rect.left > anchor_rect.right
   - abs(element.rect.top - anchor_rect.top) < 10px
4. Select the first match
```

### Pattern 3: Expand-and-Scan (Recursive Exploration)
```
GOAL: Find "Dark Mode" toggle in deeply nested menu.

ALGORITHM:
1. Identify: Find elements with ExpandCollapsePattern
2. Check: Is ExpandCollapseState == Collapsed?
3. Expand: Invoke Expand() method
4. Wait: Poll until StructureChangedEvent fires or timeout
5. Scan: Read children of expanded node
6. Recurse: If target not found and depth < MAX_DEPTH, repeat on children
7. Constraint: If depth == MAX_DEPTH, return visible categories for user clarification
```

### Pattern 4: Hybrid Mode (Visual Fallback)
```
PRIMARY (Structural):
  tree = get_ui_tree(max_depth=3)
  element = find_by_name("Settings")

IF element == NULL OR element.name == "Button":
  FALLBACK (Visual):
    screenshot = get_screenshot(with_som=True)
    prompt = "Which numbered element is the Settings gear icon?"
    id = llm.extract_id(screenshot, prompt)
    execute_action(selector_type="coordinates", id=id)
```

---

## 7. THE 5 HARD IMPLEMENTATION PROBLEMS

### Problem 1: DPI Scaling Coordinate Mismatch
**Issue:** Windows uses logical pixels (125%, 150% scaled), but screenshots use physical pixels.

**Fix:** Query `GetDpiForWindow()` API and normalize all coordinates:
```python
def normalize_coords(logical_x, logical_y, dpi_scale):
    physical_x = logical_x * dpi_scale
    physical_y = logical_y * dpi_scale
    return (physical_x, physical_y)
```

### Problem 2: Event Push vs MCP Pull
**Issue:** MCP is request/response. Windows is event-driven (toasts appear unsolicited).

**Fix:** Implement event queue in MCP server:
```python
event_queue = []

@uia_event_handler
def on_window_opened(event):
    event_queue.append({"type": "popup", "title": event.window_title})

@tool
def get_ui_tree():
    tree = build_tree()
    # Inject events as high-priority elements
    if event_queue:
        tree.insert_notification(event_queue.pop(0))
    return tree
```

### Problem 3: The Anchor Algorithm
**Issue:** Legacy apps have zero AutomationIds.

**Fix:** Implement geometric search:
```python
def find_element_right_of(anchor_text):
    anchor = find_by_text(anchor_text)
    anchor_rect = anchor.BoundingRectangle

    candidates = [
        e for e in anchor.parent.children
        if e.ControlType == "Edit"
        and e.rect.left > anchor_rect.right
        and abs(e.rect.top - anchor_rect.top) < 10
    ]

    return min(candidates, key=lambda e: e.rect.left - anchor_rect.right)
```

### Problem 4: Dirty Tree Performance
**Issue:** Full UIA tree scan takes 10-20 seconds.

**Fix:** Aggressive pruning + lazy loading:
```python
IGNORE_TYPES = ["Pane", "Group", "Image", "Separator"]
IGNORE_CLASSES = ["Windows.UI.Core.CoreWindow"]

def filter_tree(element, depth=0, max_depth=3):
    if depth > max_depth:
        return None
    if element.ControlType in IGNORE_TYPES:
        if not element.Children:
            return None  # Prune empty containers
    # Recursively filter children
    return filtered_element
```

### Problem 5: Sandbox Lifecycle Management
**Issue:** Agent needs isolation (Windows Sandbox) but sandboxes are ephemeral.

**Fix:** Host/Guest architecture:
```
HOST (Real Machine):
  - Runs the LLM agent
  - Communicates via TCP or shared folder
  - Starts .wsb with mapped folders

SANDBOX (Isolated VM):
  - Runs the MCP server
  - Runs target application
  - Writes results to mapped folder

LIFECYCLE:
  1. Host starts sandbox with .wsb config
  2. Host waits for MCP server health check
  3. Agent runs task via MCP
  4. Agent extracts results from shared folder
  5. Host terminates sandbox (clean slate for next task)
```

---

## 8. TOOL PATTERN COMPARISON

| Pattern | Examples | Input Style | Best For |
|---------|----------|-------------|----------|
| **Atomic Primitive** | Anthropic Computer Use, Playwright MCP | Coordinates [x,y] or CSS selector | Maximum control, debugging |
| **Semantic God-Function** | Stagehand, ZeroStep | Natural language instruction | Production reliability |
| **Visual Planner** | Skyvern, UFO, OmniParser | Numbered bounding boxes | Legacy apps, complex workflows |
| **Hybrid** | UFO with COM+UIA | Name/AutomationId with visual fallback | Enterprise Windows apps |

---

## 9. KEY TAKEAWAYS

### What's Platform-Agnostic (Reusable)
✅ The Operational Loop (OODA)
✅ The 5 Analysis Lenses (Test Cases, Bugs, UX, Complexity, Docs)
✅ Self-Healing patterns
✅ State Graph reasoning

### What's Platform-Specific (Must Adapt)
❌ Selector vocabulary (HTML tags → Windows ControlTypes)
❌ Tree representation (DOM → UIA XML)
❌ Event model (JavaScript events → UIA events)
❌ Execution layer (Playwright → PyWinAuto/UIAutomation)

### The Golden Rule
**The reasoning layer is universal. The execution layer is platform-specific.**

Web agents can be "ported" to Windows by:
1. Keeping the system prompt structure (OODA, Lenses)
2. Replacing DOM tools with UIA tools
3. Translating web vocabulary to Windows vocabulary

---

## 10. NEXT STEPS (Vibe Coding Ready)

You have everything needed to start building. The remaining gaps are **empirical engineering problems** solved through iteration, not research:

1. **Token density tuning**: Test real apps to see if 3k tokens or 30k tokens per tree
2. **Anchor algorithm refinement**: Test on specific legacy apps in your target domain
3. **Focus stealing mitigation**: Implement Win32 `AttachThreadInput()` workarounds
4. **Event bridging**: Build the event queue and test with real toast notifications
5. **Hybrid mode triggers**: Define heuristics for when to switch from UIA to vision

**Ready to proceed with implementation.**
