# Agent Tool I/O Reference Guide

Complete input/output specifications for tools across major agent frameworks.

---

## 1. ANTHROPIC COMPUTER USE

### Tool: `computer`
**Purpose:** Visuomotor interaction with desktop GUI (pixel-level)

**Input Schema:**
```json
{
  "action": {
    "type": "string",
    "enum": ["key", "type", "mouse_move", "left_click", "left_click_drag",
             "right_click", "middle_click", "double_click", "screenshot", "cursor_position"],
    "required": true
  },
  "coordinate": {
    "type": "array",
    "items": {"type": "integer"},
    "description": "[x, y] absolute screen coordinates",
    "required_for": ["mouse_move", "left_click_drag"]
  },
  "text": {
    "type": "string",
    "description": "String to type or key name (e.g., 'Return', 'Control_L+c')",
    "required_for": ["type", "key"]
  }
}
```

**Output Schema:**
```json
{
  "output": "string (confirmation message)",
  "error": "string | null",
  "base64_image": "string (screenshot after action)"
}
```

**Example Usage:**
```json
// Input
{
  "name": "computer",
  "input": {
    "action": "left_click",
    "coordinate": [500, 300]
  }
}

// Output
{
  "output": "Clicked at (500, 300)",
  "error": null,
  "base64_image": "iVBORw0KGgoAAAANS..."
}
```

### Tool: `bash`
**Purpose:** Execute shell commands in persistent session

**Input:**
```json
{
  "command": {
    "type": "string",
    "required": true,
    "description": "Raw shell command"
  },
  "restart": {
    "type": "boolean",
    "default": false,
    "description": "Kill current session and start fresh"
  }
}
```

**Output:**
```json
{
  "stdout": "string (combined stdout + stderr)",
  "truncated": "boolean"
}
```

### Tool: `text_editor`
**Purpose:** Structured file editing (safer than sed/cat)

**Input:**
```json
{
  "command": {
    "enum": ["view", "create", "str_replace", "insert", "undo_edit"],
    "required": true
  },
  "path": {"type": "string", "required": true},
  "file_text": {"type": "string", "required_for": ["create"]},
  "old_str": {"type": "string", "required_for": ["str_replace"]},
  "new_str": {"type": "string", "required_for": ["str_replace", "insert"]},
  "view_range": {
    "type": "array",
    "items": {"type": "integer"},
    "description": "[start_line, end_line] for pagination"
  }
}
```

**Output:**
```json
{
  "content": "string (file contents or confirmation)",
  "error": "string | null (e.g., 'String not found')"
}
```

---

## 2. STAGEHAND

### Tool: `act`
**Purpose:** Semantic web interaction (natural language → selector)

**Input:**
```typescript
{
  instruction: string;           // "Click the 'Sign Up' button"
  variables?: Record<string, string>; // PII injection: {"email": "user@test.com"}
  model?: string;                // "gpt-4o" (override default)
  domSettleTimeout?: number;     // ms to wait for SPA hydration
}
```

**Output:**
```typescript
{
  success: boolean;
  message: string;                      // "Clicked button with selector #submit"
  actionDescription: string;            // Original instruction
  actions: Array<{
    selector: string;                   // XPath or CSS
    method: "click" | "type" | "scroll";
    arguments?: any;
  }>;
}
```

**Example:**
```typescript
// Input
{
  instruction: "Type %email% into the email field",
  variables: { email: "test@example.com" },
  domSettleTimeout: 2000
}

// Output
{
  success: true,
  message: "Typed 'test@example.com' into input",
  actions: [{
    selector: "input[name='email']",
    method: "type",
    arguments: ["test@example.com"]
  }]
}
```

### Tool: `extract`
**Purpose:** Structured data extraction with schema validation

**Input:**
```typescript
{
  instruction: string;           // "Find all product prices and names"
  schema: ZodSchema;             // z.object({ products: z.array(...) })
  selector?: string;             // "#product-list" (scope to subtree)
}
```

**Output:**
```typescript
{
  // Typed object matching schema
  products: Array<{
    name: string;
    price: number;
    inStock: boolean;
  }>;
}
```

**Example:**
```typescript
// Input
{
  instruction: "Extract product data from the listing",
  schema: z.object({
    products: z.array(z.object({
      name: z.string(),
      price: z.number()
    }))
  }),
  selector: "#product-grid"
}

// Output
{
  products: [
    { name: "Widget A", price: 29.99 },
    { name: "Widget B", price: 39.99 }
  ]
}
```

### Tool: `observe`
**Purpose:** Discover available interactions without acting

**Input:**
```typescript
{
  instruction?: string;          // "Find navigation elements"
  selector?: string;             // Scope to specific container
}
```

**Output:**
```typescript
Array<{
  selector: string;              // "button[aria-label='Menu']"
  description: string;           // "Opens the main navigation menu"
  method: "click" | "type";
}>
```

---

## 3. SKYVERN

### Tool: `plan` (Planner Agent)
**Purpose:** Generate DAG of steps for complex workflow

**Input:**
```json
{
  "goal": "string (high-level task)",
  "url": "string (entry point)",
  "navigation_goal": "string (optional waypoint)"
}
```

**Output:**
```json
{
  "steps": [
    {
      "id": 1,
      "action": "navigate",
      "target": "https://example.com/login"
    },
    {
      "id": 2,
      "action": "fill_form",
      "fields": [
        {"selector": "input[name='username']", "value": "user"},
        {"selector": "input[name='password']", "value": "pass"}
      ]
    },
    {
      "id": 3,
      "action": "click",
      "element_id": 42,
      "expected_outcome": "Dashboard page loads"
    }
  ]
}
```

### Tool: `click` (Actor Agent)
**Purpose:** Execute action using Set-of-Mark ID

**Input:**
```json
{
  "element_id": 42,               // From visual SoM overlay
  "current_step": {...},          // From planner
  "viewport_image": "base64...",  // Current screenshot
  "dom_snippet": "string"         // HTML near target
}
```

**Output:**
```json
{
  "status": "success | failure",
  "new_screenshot": "base64...",
  "state_changed": true
}
```

### Tool: `validate` (Validator Agent)
**Purpose:** Verify action succeeded (closes the loop)

**Input:**
```json
{
  "action_taken": "Clicked element 42 (Login button)",
  "state_before": "base64_image",
  "state_after": "base64_image",
  "expected_outcome": "Dashboard page loads"
}
```

**Output:**
```json
{
  "success": false,
  "reasoning": "Page did not change after click; likely popup blocked action",
  "correction": "Try closing the popup first"
}
```

---

## 4. MICROSOFT UFO

### HostAgent Tools

#### `launch_app`
```json
{
  "input": {
    "app_name": "PowerPoint"
  },
  "output": {
    "pid": 4402,
    "window_handle": "0x0004A3B8"
  }
}
```

#### `switch_app`
```json
{
  "input": {
    "window_title": "Excel.*",  // Regex
    "pid": 4402                   // Or use PID
  },
  "output": {
    "success": true,
    "active_window": "Excel - Book1.xlsx"
  }
}
```

### AppAgent Tools

#### `UIA_Executor`
**Purpose:** Interact via UI Automation API

```json
{
  "input": {
    "control_name": "SaveButton",      // AutomationId or Name
    "control_type": "Button",
    "action": "Click",
    "value": null
  },
  "output": {
    "status": true,
    "new_state": {
      "screenshot": "base64...",
      "ui_tree": "<Window>...</Window>"
    }
  }
}
```

#### `COM_Executor`
**Purpose:** Direct API calls to Office apps (bypass GUI)

```json
{
  "input": {
    "app": "Excel",
    "method": "set_cell_value",
    "cell_range": "A1",
    "value": "=SUM(B2:B10)"
  },
  "output": {
    "status": "success",
    "result": "Cell A1 updated"
  }
}
```

#### `UICollector`
**Purpose:** X-ray vision into application state

```json
{
  "output": {
    "screenshot": "base64...",
    "ui_tree": {
      "Window": {
        "name": "Word - Document1",
        "children": [
          {
            "type": "Button",
            "name": "Save",
            "automation_id": "SaveBtn",
            "bounding_rect": [100, 50, 200, 80],
            "is_enabled": true
          }
        ]
      }
    }
  }
}
```

---

## 5. VERCEL AI SDK

### Tool Definition Pattern

```typescript
import { tool } from 'ai';
import { z } from 'zod';

const browserTool = tool({
  description: 'Navigate web and extract content',
  parameters: z.object({
    url: z.string().url().describe('URL to navigate to'),
    action: z.enum(['read', 'click', 'type', 'screenshot'])
      .describe('Action to perform'),
    selector: z.string().optional()
      .describe('CSS selector for interaction'),
    text: z.string().optional()
      .describe('Text to type if action is type'),
  }),
  execute: async ({ url, action, selector, text }) => {
    // Implementation using Puppeteer/Playwright
    // Returns: { title, content, status }
  },
});
```

### Input/Output Flow
```typescript
// Input (LLM generates this)
{
  url: "https://example.com",
  action: "click",
  selector: "button#submit"
}

// Zod validates → schema error if URL is invalid
// Then execute() runs → returns structured result

// Output
{
  title: "Confirmation Page",
  content: "Thank you for your submission",
  status: 200
}
```

---

## 6. WINDOWS MCP EXTENDED TOOLS

### Tool: `scroll`
```json
{
  "input": {
    "selector_type": "automation_id",
    "selector_value": "ListBox1",
    "direction": "down",
    "amount": "page"
  },
  "output": {
    "status": "success",
    "scroll_percent": 45.5,
    "new_visible_items": ["Item 10", "Item 11", "Item 12"]
  }
}
```

### Tool: `expand_collapse`
```json
{
  "input": {
    "selector_type": "name",
    "selector_value": "File Menu",
    "expand": true
  },
  "output": {
    "status": "success",
    "children_revealed": 12,
    "ui_tree_updated": true
  }
}
```

### Tool: `get_process_info`
```json
{
  "input": {
    "window_handle": "0x0004A3B8"
  },
  "output": {
    "pid": 4402,
    "process_name": "notepad.exe",
    "is_responding": true,
    "window_state": "normal"
  }
}
```

### Tool: `check_element_state`
```json
{
  "input": {
    "selector_type": "automation_id",
    "selector_value": "SubmitBtn"
  },
  "output": {
    "exists": true,
    "is_enabled": true,
    "is_visible": true,
    "is_focused": false,
    "bounding_rect": [100, 200, 200, 250]
  }
}
```

---

## 7. PATTERN COMPARISON MATRIX

| Tool Pattern | Input Complexity | Output Type | Self-Healing | Best Use Case |
|-------------|------------------|-------------|--------------|---------------|
| **Anthropic `computer`** | High (coordinates) | Screenshot | Retry on error string | General OS tasks |
| **Stagehand `act`** | Low (NL instruction) | Structured JSON | Selector auto-update | Production web automation |
| **Skyvern `click`** | Medium (ID selection) | Image + status | Validator loop | Complex multi-step workflows |
| **UFO `UIA_Executor`** | High (API specs) | Hybrid (tree + image) | State machine | Windows enterprise apps |
| **Vercel `tool`** | Variable (Zod schema) | Type-safe object | Zod validation | Custom agent building |

---

## 8. KEY DESIGN PRINCIPLES

### Principle 1: Feedback Loop Closure
Every tool MUST return:
1. **Status** (success/failure boolean)
2. **State change evidence** (new screenshot, tree, or confirmation)
3. **Error details** (for self-healing prompts)

### Principle 2: Progressive Disclosure
Tools should support **lazy loading**:
- Start with shallow data (depth=2)
- Agent requests deeper data only when needed
- Prevents token explosion

### Principle 3: Type Safety
Use **schema validation** (Zod, JSON Schema):
- Prevents hallucinated arguments
- Enables compile-time safety
- Generates better LLM prompts via descriptions

### Principle 4: Hybrid Fallback
Always provide **degradation path**:
```
Structural (UIA/DOM) → Visual (Screenshots) → Coordinates (Last resort)
```

### Principle 5: Async-First
Long-running actions return **Task ID**:
```json
{
  "task_id": "task_8821a",
  "status": "running",
  "estimated_time": 10000
}
```

Agent polls with `check_task_status(task_id)`.

---

## 9. COMMON ANTI-PATTERNS

### ❌ Returning raw HTML/UIA dumps
**Problem:** 50k+ token payloads
**Fix:** Server-side filtering to 2-5k tokens

### ❌ Coordinate-only interaction
**Problem:** Brittle to resolution changes
**Fix:** Semantic selectors with coordinate fallback

### ❌ Synchronous blocking calls
**Problem:** Timeouts on slow apps
**Fix:** Task queue with polling

### ❌ Missing error details
**Problem:** Agent can't self-heal
**Fix:** Return full error context (stack, state)

### ❌ No state verification
**Problem:** False positives (click succeeded but nothing happened)
**Fix:** Always return post-action state snapshot

---

## 10. IMPLEMENTATION CHECKLIST

For each tool you implement:

- [ ] Input schema has required/optional clearly marked
- [ ] Enum types used for discrete actions (not free-form strings)
- [ ] Descriptions are semantic, not technical (teach the LLM)
- [ ] Output includes both success indicator AND evidence
- [ ] Errors return actionable messages (not just "Failed")
- [ ] Long operations (>2s) use async task pattern
- [ ] DPI scaling normalization applied to coordinates
- [ ] Window focus verified before keyboard/mouse actions
- [ ] Timeout parameters exposed and have sensible defaults
- [ ] Tool name is a verb (execute_action, get_screenshot)

**With these specifications, you can now implement a robust Windows MCP server.**
