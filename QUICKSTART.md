# Rhombus.WinFormsMcp Quick Start Guide

Get up and running with Rhombus.WinFormsMcp in 5 minutes.

## Prerequisites

- Windows 10 or later
- .NET 8.0 SDK or later
- (Optional) Visual Studio Code or Visual Studio 2022

## Installation

### 1. Clone the Repository

```bash
git clone https://github.com/yourusername/Rhombus.WinFormsMcp.git
cd Rhombus.WinFormsMcp
```

### 2. Build the Solution

```bash
dotnet build
```

### 3. Verify Installation

```bash
dotnet test
```

You should see tests passing (some may require automation setup).

## Your First Automation

### Step 1: Start the MCP Server

```bash
dotnet run --project src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj
```

The server is now listening on stdin/stdout for JSON-RPC messages.

### Step 2: Launch Notepad (in another terminal)

Send this JSON message to the server:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "app",
    "arguments": {
      "action": "launch",
      "path": "notepad.exe"
    }
  }
}
```

### Step 3: Type Some Text

First, interact with the text area (using "type" with no target for focused element):

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "type",
    "arguments": {
      "text": "Hello from Rhombus.WinFormsMcp!"
    }
  }
}
```

### Step 4: Take a Screenshot

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "screenshot",
    "arguments": {
      "file": "C:\\temp\\my_first_automation.png"
    }
  }
}
```

## Testing with TestApp

### Run the Test Application

```bash
dotnet run --project src/Rhombus.WinFormsMcp.TestApp/Rhombus.WinFormsMcp.TestApp.csproj
```

A WinForms window appears with various controls.

### Interact with It

Launch the server and run:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "find",
    "arguments": {
      "name": "textBox"
    }
  }
}
```

This will return the element ID.

Then type text:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "type",
    "arguments": {
      "target": "elem_1",
      "text": "Test input",
      "clear": true
    }
  }
}
```

## Common Tasks

### Find an Element by Name

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "find",
    "arguments": {
      "name": "buttonName"
    }
  }
}
```

### Click a Button

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "click",
    "arguments": {
      "target": "elem_1"
    }
  }
}
```

### Enter Text in a Field

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "type",
    "arguments": {
      "target": "elem_1",
      "text": "Your text here",
      "clear": true
    }
  }
}
```

### Capture Screenshot

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "screenshot",
    "arguments": {
      "file": "C:\\temp\\screen.png"
    }
  }
}
```

### Close Application

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "app",
    "arguments": {
      "action": "close",
      "pid": 5432
    }
  }
}
```

## Project Structure

```
Rhombus.WinFormsMcp/
├── src/
│   ├── Rhombus.WinFormsMcp.Server/     # MCP server (main)
│   └── Rhombus.WinFormsMcp.TestApp/    # Test WinForms app
├── tests/
│   └── Rhombus.WinFormsMcp.Tests/      # NUnit tests
├── examples/
│   └── EXAMPLES.md              # Detailed examples
├── README.md                    # Full documentation
└── QUICKSTART.md               # This file
```

## Available Tools

1. **app** - Application lifecycle (launch, attach, close, info)
2. **find** - Discovery (find element, list children, tree)
3. **click** - Interaction (mouse, touch, pen tap)
4. **type** - Input (text entry, key combos)
5. **drag** - Manipulation (mouse drag, pen stroke)
6. **gesture** - Multi-touch (pinch, rotate)
7. **screenshot** - Visual capture
8. **script** - Batch execution

## Troubleshooting

### "Element not found"

1. Use `find` with `at: "root"` to list all windows
2. Use `find` with `recursive: true` to dump the tree
3. Check that the element's Name or AutomationId property is set

### "Failed to launch application"

1. Verify the full path to executable
2. Ensure executable exists and is not corrupted
3. Try with a simple app like `notepad.exe` first

### Server not responding

1. Ensure server is running
2. Check for error messages in console
3. Verify JSON format is correct
4. Ensure each message is on a single line

## Next Steps

1. Read [EXAMPLES.md](examples/EXAMPLES.md) for detailed workflows
2. Run the [TestApp](src/Rhombus.WinFormsMcp.TestApp/) to explore controls
3. Check the [API Reference](README.md#mcp-tool-reference)
4. Run the [test suite](tests/Rhombus.WinFormsMcp.Tests/) to verify

## Performance Tips

- Use `find` to get IDs once, then reuse them
- Use `script` to batch multiple operations in one round-trip
- Close applications immediately after use with `app` tool

## Integration with Claude Code

To use Rhombus.WinFormsMcp with Claude Code, configure it as an MCP server in your workspace:

```json
{
  "mcpServers": {
    "Rhombus.WinFormsMcp": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj"]
    }
  }
}
```

Then Claude Code can call the automation tools directly!

---

Happy automating! For more help, see [README.md](README.md) or [EXAMPLES.md](examples/EXAMPLES.md).
