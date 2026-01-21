# Rhombus.WinFormsMcp Usage Examples

This document provides practical examples of using Rhombus.WinFormsMcp to automate WinForms applications.

## Table of Contents

1. [Basic Application Launch](#basic-application-launch)
2. [Finding and Interacting with Controls](#finding-and-interacting-with-controls)
3. [Form Filling Workflow](#form-filling-workflow)
4. [Screenshot-Based Validation](#screenshot-based-validation)
5. [Error Handling](#error-handling)
6. [Advanced Scenarios](#advanced-scenarios)
7. [Script Execution (Batch Commands)](#script-execution-batch-commands)

## Basic Application Launch

### Launching Notepad

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "launch_app",
    "arguments": {
      "path": "notepad.exe"
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "content": [{"type": "text", "text": "{\"success\": true, \"pid\": 5432, \"processName\": \"notepad\"}"}]
  }
}
```

### Launching with Arguments

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "launch_app",
    "arguments": {
      "path": "C:\\Program Files\\MyApp\\MyApp.exe",
      "arguments": "--debug --loglevel verbose",
      "workingDirectory": "C:\\Program Files\\MyApp"
    }
  }
}
```

## Finding and Interacting with Controls

### Finding a Button by Name

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "find_element",
    "arguments": {
      "name": "OK"
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "result": {
    "content": [{"type": "text", "text": "{\"success\": true, \"elementId\": \"elem_1\", \"name\": \"OK\", \"controlType\": \"Button\"}"}]
  }
}
```

### Clicking an Element

Using the element ID from the previous response:

```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "click_element",
    "arguments": {
      "elementId": "elem_1"
    }
  }
}
```

### Double-Click Operation

```json
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "click_element",
    "arguments": {
      "elementId": "elem_1",
      "doubleClick": true
    }
  }
}
```

## Form Filling Workflow

### Complete Form Fill Example

**Scenario:** Fill a registration form with Name, Email, and Country

#### Step 1: Find the Name TextBox

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "find_element",
    "arguments": {
      "name": "txtName"
    }
  }
}
```

Response: `{"success": true, "elementId": "elem_1"}`

#### Step 2: Enter Name

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "type_text",
    "arguments": {
      "elementId": "elem_1",
      "text": "John Doe",
      "clearFirst": true
    }
  }
}
```

#### Step 3: Find Email TextBox and Enter Value

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "find_element",
    "arguments": {
      "name": "txtEmail"
    }
  }
}
```

Response: `{"success": true, "elementId": "elem_2"}`

```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "type_text",
    "arguments": {
      "elementId": "elem_2",
      "text": "john@example.com",
      "clearFirst": true
    }
  }
}
```

#### Step 4: Find ComboBox and Select Option

```json
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "find_element",
    "arguments": {
      "name": "cmbCountry"
    }
  }
}
```

Response: `{"success": true, "elementId": "elem_3"}`

Click to open dropdown:
```json
{
  "jsonrpc": "2.0",
  "id": 6,
  "method": "tools/call",
  "params": {
    "name": "click_element",
    "arguments": {
      "elementId": "elem_3"
    }
  }
}
```

Send arrow keys to select option:
```json
{
  "jsonrpc": "2.0",
  "id": 7,
  "method": "tools/call",
  "params": {
    "name": "send_keys",
    "arguments": {
      "keys": "{DOWN}{DOWN}{ENTER}"
    }
  }
}
```

#### Step 5: Find Submit Button and Click

```json
{
  "jsonrpc": "2.0",
  "id": 8,
  "method": "tools/call",
  "params": {
    "name": "find_element",
    "arguments": {
      "name": "btnSubmit"
    }
  }
}
```

Response: `{"success": true, "elementId": "elem_4"}`

```json
{
  "jsonrpc": "2.0",
  "id": 9,
  "method": "tools/call",
  "params": {
    "name": "click_element",
    "arguments": {
      "elementId": "elem_4"
    }
  }
}
```

## Screenshot-Based Validation

### Taking Full Application Screenshot

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "take_screenshot",
    "arguments": {
      "outputPath": "C:\\temp\\application_state.png"
    }
  }
}
```

### Taking Screenshot of Specific Element

First, find the element:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "find_element",
    "arguments": {
      "className": "DataGridView"
    }
  }
}
```

Response: `{"success": true, "elementId": "elem_1"}`

Then capture the element:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "take_screenshot",
    "arguments": {
      "outputPath": "C:\\temp\\datagrid_state.png",
      "elementId": "elem_1"
    }
  }
}
```

### Validation Workflow with Screenshots

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "wait_for_element",
    "arguments": {
      "automationId": "successMessage",
      "timeoutMs": 5000
    }
  }
}
```

If found, capture the result:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "take_screenshot",
    "arguments": {
      "outputPath": "C:\\temp\\success_screen.png"
    }
  }
}
```

## Error Handling

### Checking Element Existence

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "element_exists",
    "arguments": {
      "automationId": "errorMessage"
    }
  }
}
```

**Response (element exists):**
```json
{"success": true, "exists": true}
```

**Response (element not found):**
```json
{"success": true, "exists": false}
```

### Waiting for Element with Timeout

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "wait_for_element",
    "arguments": {
      "automationId": "loadingSpinner",
      "timeoutMs": 3000
    }
  }
}
```

**Response (found):**
```json
{"success": true, "found": true}
```

**Response (timeout):**
```json
{"success": true, "found": false}
```

### Error Response Example

```json
{
  "success": false,
  "error": "Element not found in session"
}
```

## Advanced Scenarios

### Multi-Window Application

Some applications have multiple windows. Example workflow:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "launch_app",
    "arguments": {
      "path": "C:\\Program Files\\MyApp\\MyApp.exe"
    }
  }
}
```

Response: `{"pid": 5432}`

Click a button that opens a child window:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "find_element",
    "arguments": {
      "name": "btnOpenSettings"
    }
  }
}
```

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "click_element",
    "arguments": {
      "elementId": "elem_1"
    }
  }
}
```

Wait for the settings window to appear:

```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "wait_for_element",
    "arguments": {
      "name": "Settings",
      "timeoutMs": 3000
    }
  }
}
```

### Data Grid Interaction

#### Navigate DataGrid

Assuming DataGrid element is identified:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "find_element",
    "arguments": {
      "name": "dataGridView1"
    }
  }
}
```

Response: `{"elementId": "elem_1"}`

Click on DataGrid to focus it:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "click_element",
    "arguments": {
      "elementId": "elem_1"
    }
  }
}
```

Navigate with arrow keys:

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "send_keys",
    "arguments": {
      "keys": "{RIGHT}{RIGHT}{DOWN}{DOWN}"
    }
  }
}
```

### Drag and Drop

#### Scenario: Drag item from list box to another

Find source element:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "find_element",
    "arguments": {
      "name": "Item1"
    }
  }
}
```

Response: `{"elementId": "elem_1"}`

Find target element:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "find_element",
    "arguments": {
      "name": "targetListBox"
    }
  }
}
```

Response: `{"elementId": "elem_2"}`

Perform drag and drop:

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "drag_drop",
    "arguments": {
      "sourceElementId": "elem_1",
      "targetElementId": "elem_2"
    }
  }
}
```

### Getting Control Properties

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_property",
    "arguments": {
      "elementId": "elem_1",
      "propertyName": "name"
    }
  }
}
```

Response:
```json
{"success": true, "propertyName": "name", "value": "buttonOK"}
```

Available properties:
- `name` - Element's Name
- `automationid` - Element's AutomationId
- `classname` - Element's ClassName
- `controltype` - Element's ControlType
- `isoffscreen` - Is element off-screen
- `isenabled` - Is element enabled

### Setting ComboBox to Specific Value

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "find_element",
    "arguments": {
      "name": "cmbStatus"
    }
  }
}
```

Response: `{"elementId": "elem_1"}`

Click to open:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "click_element",
    "arguments": {
      "elementId": "elem_1"
    }
  }
}
```

Type to search (if supported):

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "type_text",
    "arguments": {
      "elementId": "elem_1",
      "text": "Completed"
    }
  }
}
```

Or navigate with keys:

```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "send_keys",
    "arguments": {
      "keys": "{DOWN}{DOWN}{ENTER}"
    }
  }
}
```

## Script Execution (Batch Commands)

The `run_script` tool allows you to execute multiple commands in sequence without round-trip overhead between each step. This is ideal for test automation and complex workflows.

### Basic Script Execution

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "run_script",
    "arguments": {
      "script": {
        "steps": [
          { "id": "launch", "tool": "launch_app", "args": { "path": "notepad.exe" }, "delay_after_ms": 1000 },
          { "id": "find_edit", "tool": "find_element", "args": { "className": "Edit" } },
          { "tool": "type_text", "args": { "elementId": "$find_edit.result.elementId", "text": "Hello, World!" } }
        ]
      }
    }
  }
}
```

### Variable References

Steps can reference results from previous steps using `$stepId.result.path` syntax:

```json
{
  "script": {
    "steps": [
      { "id": "btn", "tool": "find_element", "args": { "name": "Submit" } },
      { "tool": "click_element", "args": { "elementId": "$btn.result.elementId" } }
    ]
  }
}
```

Use `$last` to reference the immediately previous step:

```json
{
  "script": {
    "steps": [
      { "tool": "find_element", "args": { "name": "txtName" } },
      { "tool": "type_text", "args": { "elementId": "$last.result.elementId", "text": "John Doe" } }
    ]
  }
}
```

### Script Options

```json
{
  "script": {
    "steps": [...],
    "options": {
      "stop_on_error": true,
      "default_delay_ms": 100,
      "timeout_ms": 60000
    }
  }
}
```

- **stop_on_error** (default: true) - Stop execution on first error
- **default_delay_ms** (default: 0) - Delay after each step in milliseconds
- **timeout_ms** (default: 120000) - Overall script timeout

### Complete Form Fill Script

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "run_script",
    "arguments": {
      "script": {
        "steps": [
          { "id": "name", "tool": "find_element", "args": { "automationId": "txtName" } },
          { "tool": "type_text", "args": { "elementId": "$name.result.elementId", "text": "John Doe", "clearFirst": true } },
          { "id": "email", "tool": "find_element", "args": { "automationId": "txtEmail" } },
          { "tool": "type_text", "args": { "elementId": "$email.result.elementId", "text": "john@example.com", "clearFirst": true } },
          { "id": "submit", "tool": "find_element", "args": { "name": "Submit" } },
          { "tool": "click_element", "args": { "elementId": "$submit.result.elementId" } },
          { "tool": "wait_for_element", "args": { "name": "Success", "timeoutMs": 5000 } }
        ],
        "options": {
          "stop_on_error": true,
          "default_delay_ms": 50
        }
      }
    }
  }
}
```

### Script Response Format

```json
{
  "success": true,
  "result": {
    "completed_steps": 3,
    "total_steps": 3,
    "steps": [
      { "id": "launch", "success": true, "result": { "pid": 5432 }, "execution_time_ms": 1150 },
      { "id": "find_edit", "success": true, "result": { "elementId": "elem_1" }, "execution_time_ms": 50 },
      { "id": "step_3", "success": true, "result": { "typed": true }, "execution_time_ms": 200 }
    ],
    "total_execution_time_ms": 1400
  }
}
```

### Error Handling in Scripts

When `stop_on_error: true` and a step fails:

```json
{
  "success": false,
  "result": {
    "completed_steps": 2,
    "total_steps": 5,
    "failed_at_step": "step3",
    "steps": [
      { "id": "step1", "success": true, "result": {...} },
      { "id": "step2", "success": true, "result": {...} },
      { "id": "step3", "success": false, "error": "Element not found" }
    ]
  },
  "error": "Script failed at step 'step3': Element not found"
}
```

When `stop_on_error: false`, all steps execute and failures are recorded individually.

## Tips and Best Practices

1. **Always use `wait_for_element` before interaction** - Ensures element is loaded
2. **Capture screenshots for validation** - Visual confirmation of state changes
3. **Use element_exists to branch logic** - Check for error messages or success states
4. **Set timeouts appropriately** - Longer for slow operations, shorter for quick checks
5. **Clear fields before entering data** - Use `clearFirst: true` to avoid appending
6. **Handle modal dialogs** - Use `wait_for_element` to detect dialog appearance
7. **Test with TestApp first** - Familiarize yourself with the provided test controls
8. **Close applications properly** - Always call `close_app` to clean up resources

---

For more information, see the main [README.md](../README.md).
