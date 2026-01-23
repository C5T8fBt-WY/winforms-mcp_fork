# Rhombus.WinFormsMcp - WinForms Automation MCP Server

[![CI Status](https://github.com/rhom6us/winforms-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/rhom6us/winforms-mcp/actions/workflows/ci.yml)
[![Publish Status](https://github.com/rhom6us/winforms-mcp/actions/workflows/publish.yml/badge.svg)](https://github.com/rhom6us/winforms-mcp/actions/workflows/publish.yml)
[![NuGet Version](https://img.shields.io/nuget/v/Rhombus.WinFormsMcp)](https://www.nuget.org/packages/Rhombus.WinFormsMcp)
[![NPM Version](https://img.shields.io/npm/v/@rhom6us/winforms-mcp)](https://www.npmjs.com/package/@rhom6us/winforms-mcp)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**Rhombus.WinFormsMcp** is a Model Context Protocol (MCP) server that provides headless automation capabilities for WinForms applications. It uses the FlaUI library with the UIA2 backend (MSAA - Microsoft Active Accessibility) to enable full Windows Forms compatibility without requiring visual interaction.

## Overview

Rhombus.WinFormsMcp bridges the gap between Claude Code and WinForms applications, enabling:

- **Automated element discovery** by AutomationId, Name, ClassName, or ControlType
- **UI interaction** including clicking, typing, value setting, and drag-drop operations
- **Process lifecycle management** (launch, attach, close applications)
- **Visual validation** through screenshot capture and analysis
- **Headless operation** compatible with CI/CD environments, remote systems, and servers
- **Full async/await support** for integration with modern .NET applications

## Prerequisites

### System Requirements

| Requirement | Details |
|-------------|---------|
| **Operating System** | Windows 10/11 Pro, Enterprise, or Education |
| **.NET SDK** | 8.0 or later |
| **Windows Sandbox** | Required for isolated testing (optional but recommended) |

> **Note:** Windows Home edition does not support Windows Sandbox. The MCP server works without sandbox, but isolated testing requires Pro/Enterprise/Education.

### Enabling Windows Sandbox (Recommended)

Windows Sandbox provides isolated environments for safe application testing. To enable it:

**PowerShell (Run as Administrator):**
```powershell
Enable-WindowsOptionalFeature -Online -FeatureName "Containers-DisposableClientVM" -All
```

**Or via Windows Features UI:**
1. Open "Turn Windows features on or off"
2. Check "Windows Sandbox"
3. Click OK

**Important:** A system restart is required after enabling Windows Sandbox.

### Verifying Setup

After reboot, verify sandbox is available:
```powershell
# Should show "Enabled" status
Get-WindowsOptionalFeature -Online -FeatureName "Containers-DisposableClientVM" | Select State
```

## Architecture

### Project Structure

```
Rhombus.WinFormsMcp/
├── src/
│   ├── Rhombus.WinFormsMcp.Server/          # MCP server implementation
│   │   ├── Program.cs                # MCP stdio transport + tool implementations
│   │   ├── Automation/
│   │   │   └── AutomationHelper.cs   # Core FlaUI wrapper (428 lines)
│   │   └── Rhombus.WinFormsMcp.Server.csproj
│   │
│   ├── Rhombus.WinFormsMcp.TestApp/         # Sample WinForms application for testing
│   │   ├── Form1.cs / Form1.Designer.cs
│   │   ├── Program.cs
│   │   └── Rhombus.WinFormsMcp.TestApp.csproj
│   │
│   └── Rhombus.WinFormsMcp.sln              # Solution file
│
├── tests/
│   └── Rhombus.WinFormsMcp.Tests/           # NUnit test suite
│       ├── UnitTest1.cs              # AutomationHelper tests
│       └── Rhombus.WinFormsMcp.Tests.csproj
│
├── README.md                         # This file
├── global.json                       # .NET 8.0 SDK version pinning
└── .gitignore                        # Standard .NET ignores + screenshots
```

### Technology Stack

| Component | Technology | Version | Purpose |
|-----------|-----------|---------|---------|
| Language | C# | - | Full type safety and modern async/await |
| Framework | .NET | 8.0 | Latest LTS, cross-platform compatible |
| Automation | FlaUI | 4.0.0 | UI element discovery and interaction |
| UI Framework | UIA2 (MSAA) | - | Maximum WinForms compatibility |
| Testing | NUnit | 3.14.0 | Comprehensive test coverage |
| Protocol | MCP | stdio transport | JSON-RPC 2.0 compatible |

## Core Components

### 1. AutomationHelper (src/Rhombus.WinFormsMcp.Server/Automation/AutomationHelper.cs)

Core automation wrapper with 25+ methods:

#### Process Management
- `LaunchApp(path, arguments, workingDirectory)` - Launch new WinForms application
- `AttachToProcess(pid)` - Attach to running process by ID
- `AttachToProcessByName(name)` - Attach to running process by name
- `GetMainWindow(pid)` - Get application's main window element
- `CloseApp(pid, force)` - Close application gracefully or forcefully

#### Element Discovery
- `FindByAutomationId(id, parent, timeoutMs)` - Find by automation ID
- `FindByName(name, parent, timeoutMs)` - Find by element name
- `FindByClassName(className, parent, timeoutMs)` - Find by class name
- `FindByControlType(controlType, parent, timeoutMs)` - Find by control type
- `FindAll(condition, parent, timeoutMs)` - Find multiple matching elements
- `ElementExists(automationId, parent)` - Check if element exists (1000ms timeout)
- `GetAllChildren(element)` - Get all child elements

#### UI Interaction
- `Click(element, doubleClick)` - Click or double-click element
- `TypeText(element, text, clearFirst)` - Type text into element
- `SetValue(element, value)` - Set element value via SendKeys
- `DragDrop(source, target)` - Simulate drag-and-drop operation
- `SendKeys(keys)` - Send keyboard input
- `GetProperty(element, propertyName)` - Get element property (name, automationId, className, controlType, isOffscreen, isEnabled)

#### Validation & Monitoring
- `TakeScreenshot(outputPath, element)` - Capture PNG screenshot
- `WaitForElementAsync(automationId, parent, timeoutMs)` - Async wait for element appearance
- `ElementExists(automationId, parent)` - Check element existence

#### Implementation Details
- **Retry mechanism**: All find operations retry every 100ms until timeout
- **Default timeout**: 5000ms for find operations, 10000ms for async wait
- **Thread safety**: Locked dictionary for process tracking
- **Resource cleanup**: IDisposable implementation with automatic process termination
- **Headless compatible**: No visual interaction required, all operations via window messages

### 2. MCP Server (src/Rhombus.WinFormsMcp.Server/Program.cs)

Implements Model Context Protocol with:

#### Session Management
- `SessionManager` class tracks:
  - Active AutomationHelper instance
  - Cached automation elements with unique IDs
  - Process contexts for lifecycle tracking

#### Tool Implementations (45+ tools)

The MCP server provides comprehensive UI automation capabilities organized into categories:

**Process Management:**
- `launch_app`, `attach_to_process`, `close_app`, `get_process_info`

**Window Management:**
- `focus_window`, `get_window_bounds`, `take_screenshot`

**Element Discovery:**
- `find_element`, `find_element_near_anchor`, `list_elements`, `wait_for_element`

**UI Interaction:**
- `click_element`, `click_by_automation_id`, `type_text`, `set_value`, `send_keys`, `drag_drop`

**Input Injection (coordinate-based):**
- **Unified Input**:
  - `click` (replaces `mouse_click`, `touch_tap`, `pen_tap`)
  - `drag` (replaces `mouse_drag`, `touch_drag`, `pen_stroke`)
  - `gesture` (replaces `pinch_zoom`, `rotate_gesture`, `multi_touch_gesture`)

**UI Tree & Observation:**
- `get_ui_tree`, `check_element_state`, `expand_collapse`, `scroll`, `get_element_at_point`

**State Change Detection:**
- `capture_ui_snapshot`, `compare_ui_snapshots`

**Self-Healing:**
- `check_element_stale`, `relocate_element`

**Progressive Disclosure:**
- `mark_for_expansion`, `clear_expansion_marks`

**Event System:**
- `subscribe_to_events`, `get_pending_events`

**Performance & Caching:**
- `get_cache_stats`, `invalidate_cache`

**DPI & Coordinates:**
- `get_dpi_info` (all coordinate tools support window-relative positioning)

**Sandbox Tools:**
- `launch_app_sandboxed`, `close_sandbox`, `list_sandbox_apps`

**Scripting:**
- `run_script` - Execute batch sequences with variable interpolation

**Capabilities:**
- `get_capabilities` - Query server features and version

For complete documentation, see **[docs/MCP_TOOLS.md](docs/MCP_TOOLS.md)**.

#### Protocol Details
- **Transport**: stdio with line-based JSON-RPC 2.0
- **Message format**: Single-line JSON objects
- **Error handling**: Comprehensive try-catch with JSON error responses
- **Session state**: Persists across multiple tool calls

### 3. Test Application (src/Rhombus.WinFormsMcp.TestApp/)

Sample WinForms application with:
- **TextBox** - Text input control
- **Button** - Clickable button with message box
- **CheckBox** - Toggle checkbox
- **ComboBox** - Dropdown selection (4 options)
- **DataGridView** - Table with 2 columns × 3 rows
- **ListBox** - Multi-select list (5 items)
- **Labels** - Status and descriptive text

All controls configured with proper names for automation discovery.

### 4. Test Suite (tests/Rhombus.WinFormsMcp.Tests/)

Comprehensive NUnit tests covering:
- AutomationHelper initialization
- Application launch and process management
- Window discovery and attachment
- Element finding and existence checking
- Screenshot generation
- Async wait operations
- Process cleanup

**Test categories:**
1. **Initialization**: Verify AutomationHelper setup
2. **Process lifecycle**: Launch, attach, close applications
3. **Element operations**: Find, click, type, get properties
4. **Validation**: Screenshots, element existence, async waits
5. **Cleanup**: Proper resource disposal

## Usage

### Quick Start with Claude Code

To use this MCP server with Claude Code, see the complete setup guide: **[Claude Code MCP Setup Guide](docs/CLAUDE_CODE_SETUP.md)**

Quick configuration for `~/.claude/mcp.json`:

```json
{
  "mcpServers": {
    "winforms-mcp": {
      "command": "dotnet",
      "args": ["path/to/Rhombus.WinFormsMcp.Server.dll"],
      "env": {}
    }
  }
}
```

Then use in Claude Code:
```claude
@mcp winforms-mcp launch_app {
  "path": "C:\\path\\to\\app.exe"
}
```

### Installation

#### Quick Install (with Windows Sandbox)

For isolated testing in Windows Sandbox with hot-reload support:

```powershell
# Clone the repository
git clone https://github.com/rhom6us/winforms-mcp.git
cd winforms-mcp

# Run as Administrator (required to enable Windows Sandbox)
.\install.ps1
```

The install script will:
1. Check/enable Windows Sandbox (reboot required if enabling)
2. Create `C:\WinFormsMcpSandboxWorkspace` with server binaries, .NET runtime, and sandbox config
3. Create the MCP bridge script for Claude Code
4. Configure Claude Code's MCP settings

After installation, launch the sandbox and restart Claude Code:
```powershell
# Launch sandbox (must be admin due to coreclr bug in Windows Sandbox 0.5.3.0)
Start-Process powershell -Verb RunAs -ArgumentList '-Command', 'Start-Process ''C:\WinFormsMcpSandboxWorkspace\sandbox-dev.wsb''; exit'

# In Claude Code, reconnect MCP
/mcp
```

See [Sandbox Development Guide](docs/SANDBOX_DEVELOPMENT.md) for hot-reload workflow details.

#### Manual Installation

```bash
cd C:\dev
git clone <repo-url> Rhombus.WinFormsMcp
cd Rhombus.WinFormsMcp
dotnet build
```

### Running the Server

```bash
dotnet run --project src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj
```

The server listens on stdin/stdout for JSON-RPC messages.

### Running Tests

```bash
dotnet test
```

### Running the Test Application

```bash
dotnet run --project src/Rhombus.WinFormsMcp.TestApp/Rhombus.WinFormsMcp.TestApp.csproj
```

## MCP Tool Reference

### find_element

Discovers a UI element by various identifiers.

**Arguments:**
- `automationId` (string, optional) - Element's AutomationId
- `name` (string, optional) - Element's Name property
- `className` (string, optional) - Element's ClassName
- `pid` (int, optional) - Process ID (for future use)

**Returns:**
```json
{
  "success": true,
  "elementId": "elem_1",
  "name": "Button1",
  "automationId": "okButton",
  "controlType": "Button"
}
```

### click_element

Clicks on an element.

**Arguments:**
- `elementId` (string, required) - Cached element ID from find_element
- `doubleClick` (boolean, optional, default: false) - Double-click if true

**Returns:**
```json
{"success": true, "message": "Element clicked"}
```

### type_text

Types text into a text field.

**Arguments:**
- `elementId` (string, required) - Target element ID
- `text` (string, required) - Text to type
- `clearFirst` (boolean, optional, default: false) - Clear field before typing

**Returns:**
```json
{"success": true, "message": "Text typed"}
```

### set_value

Sets element value (via Ctrl+A + delete + type).

**Arguments:**
- `elementId` (string, required) - Target element
- `value` (string, required) - New value

**Returns:**
```json
{"success": true, "message": "Value set"}
```

### get_property

Reads element property.

**Arguments:**
- `elementId` (string, required) - Target element
- `propertyName` (string, required) - Property name (name, automationid, classname, controltype, isoffscreen, isenabled)

**Returns:**
```json
{
  "success": true,
  "propertyName": "name",
  "value": "okButton"
}
```

### launch_app

Launches a WinForms application.

**Arguments:**
- `path` (string, required) - Path to executable
- `arguments` (string, optional) - Command-line arguments
- `workingDirectory` (string, optional) - Working directory

**Returns:**
```json
{
  "success": true,
  "pid": 12345,
  "processName": "myapp"
}
```

### attach_to_process

Attaches to a running process.

**Arguments:**
- `pid` (int, optional) - Process ID
- `processName` (string, optional) - Process name

**Returns:**
```json
{
  "success": true,
  "pid": 12345,
  "processName": "myapp"
}
```

### close_app

Closes an application.

**Arguments:**
- `pid` (int, required) - Process ID
- `force` (boolean, optional, default: false) - Force kill if true

**Returns:**
```json
{"success": true, "message": "Application closed"}
```

### take_screenshot

Captures a screenshot.

**Arguments:**
- `outputPath` (string, required) - Path to save PNG file
- `elementId` (string, optional) - Element to screenshot (omit for full screen)

**Returns:**
```json
{
  "success": true,
  "message": "Screenshot saved to C:\\temp\\screen.png"
}
```

### element_exists

Checks if element exists.

**Arguments:**
- `automationId` (string, required) - Element's AutomationId

**Returns:**
```json
{"success": true, "exists": true}
```

### wait_for_element

Waits for element to appear.

**Arguments:**
- `automationId` (string, required) - Element's AutomationId
- `timeoutMs` (int, optional, default: 10000) - Timeout in milliseconds

**Returns:**
```json
{"success": true, "found": true}
```

### drag_drop

Performs drag-and-drop operation.

**Arguments:**
- `sourceElementId` (string, required) - Element to drag
- `targetElementId` (string, required) - Drop target

**Returns:**
```json
{"success": true, "message": "Drag and drop completed"}
```

### send_keys

Sends keyboard input.

**Arguments:**
- `keys` (string, required) - Keys to send (WinForms SendKeys format)

**Returns:**
```json
{"success": true, "message": "Keys sent"}
```

### mouse_drag_path

Drags the mouse through multiple waypoints in sequence. Useful for drawing shapes, curves, and complex gestures without lifting the mouse button.

**Arguments:**
- `points` (array, required) - Array of {x, y} waypoints to drag through (minimum 2, maximum 1000)
- `stepsPerSegment` (int, optional, default: 10) - Interpolation steps between each waypoint
- `delayMs` (int, optional, default: 5) - Delay in milliseconds between steps

**Returns:**
```json
{
  "success": true,
  "message": "Completed drag path through 5 waypoints",
  "pointsProcessed": 5,
  "totalSteps": 40
}
```

**Example - Drawing a Rectangle:**
```json
{
  "points": [
    {"x": 100, "y": 100},
    {"x": 300, "y": 100},
    {"x": 300, "y": 200},
    {"x": 100, "y": 200},
    {"x": 100, "y": 100}
  ],
  "stepsPerSegment": 10
}
```

## Example Workflows

### Finding and Clicking a Button

```
1. launch_app { "path": "C:\\MyApp.exe" }
   → { "pid": 5432, "processName": "MyApp" }

2. wait_for_element { "automationId": "okButton", "timeoutMs": 5000 }
   → { "found": true }

3. find_element { "automationId": "okButton" }
   → { "elementId": "elem_1", "success": true }

4. click_element { "elementId": "elem_1" }
   → { "success": true, "message": "Element clicked" }

5. close_app { "pid": 5432 }
   → { "success": true, "message": "Application closed" }
```

### Filling a Form

```
1. find_element { "name": "textBox1" }
   → { "elementId": "elem_1" }

2. type_text { "elementId": "elem_1", "text": "John Doe", "clearFirst": true }
   → { "success": true }

3. find_element { "name": "comboBox1" }
   → { "elementId": "elem_2" }

4. click_element { "elementId": "elem_2" }
   → { "success": true }

5. send_keys { "keys": "{DOWN}{DOWN}{ENTER}" }
   → { "success": true }

6. take_screenshot { "outputPath": "C:\\temp\\form_filled.png" }
   → { "success": true, "message": "Screenshot saved..." }
```

## Configuration

### Global Settings

Edit `global.json` to change .NET SDK version:

```json
{
  "sdk": {
    "version": "8.0.0",
    "rollForward": "latestFeature"
  }
}
```

### Environment Variables

- `FNWINDOWSMCP_TIMEOUT` - Default timeout for operations (ms)
- `FNWINDOWSMCP_SCREENSHOT_DIR` - Default screenshot directory

## Documentation

Comprehensive documentation is available in the `docs/` directory:

| Document | Description |
|----------|-------------|
| [MCP_TOOLS.md](docs/MCP_TOOLS.md) | Complete reference for all 45+ MCP tools |
| [AGENT_EXPLORATION_GUIDE.md](docs/AGENT_EXPLORATION_GUIDE.md) | Patterns for AI agents: OODA loop, progressive disclosure, self-healing |
| [SANDBOX_SETUP.md](docs/SANDBOX_SETUP.md) | Windows Sandbox configuration and security |
| [HOST_SETUP.md](docs/HOST_SETUP.md) | Host machine setup for headless automation |
| [TOUCH_PEN_GUIDE.md](docs/TOUCH_PEN_GUIDE.md) | Touch and pen input with pressure sensitivity |

Example agent scripts are available in the `examples/` directory.

## Known Limitations

1. **Windows Only** - Designed for Windows 10/11; no cross-platform support
2. **UIA-Compatible Apps** - Only works with applications that support UI Automation (WinForms, WPF, most modern Windows apps)
3. **Single Monitor** - Coordinate system assumes single monitor setup
4. **Network Disabled** - Sandbox runs with network disabled for security
5. **UAC** - Applications requiring administrator privileges work in sandbox (runs as admin) but may need special handling on bare metal

## Performance Characteristics

| Operation | Typical Time | Notes |
|-----------|-------------|-------|
| Launch app | 2-5 seconds | Includes WaitForInputIdle |
| Find element | 100-500ms | With 100ms retry interval |
| Click element | <100ms | Direct window message |
| Type text | 10-50ms per character | Via SendKeys |
| Screenshot | 500-2000ms | Depends on window size |
| Close app | 1-5 seconds | Graceful close + timeout |

## Troubleshooting

### "Element not found" errors

1. Ensure element has proper Name property set
2. Verify element exists before trying to interact
3. Use `wait_for_element` before attempting interaction
4. Increase timeout value if element loads slowly

### Screenshot not saving

1. Verify output directory exists and is writable
2. Ensure full path is provided (not relative)
3. Path should use Windows paths (C:\temp\) not Unix paths

### Process attachment failures

1. Verify process is actually running (`tasklist /FI "IMAGENAME eq myapp.exe"`)
2. Check process name exactly matches (case-sensitive in some contexts)
3. Ensure no UAC elevation mismatch

### Headless operation issues

1. Some controls may require special handling
2. If using visual components, ensure they're initialized
3. Test with the included TestApp first

## Development

### Building from Source

```bash
dotnet build -c Release
```

### Running Tests

```bash
dotnet test --logger "console;verbosity=detailed"
```

### Building Release Package

```bash
dotnet publish -c Release -o publish
```

## Contributing

Contributions welcome! Areas for enhancement:

- [ ] Event raising and listening implementation
- [ ] Advanced UI patterns (ValuePattern, RangePattern, etc.)
- [ ] Performance optimization
- [ ] Cross-platform support (Linux/Mac via Wine compatibility)
- [ ] Additional control type support
- [ ] Keyboard layout detection and handling

## License

MIT License - See [LICENSE](LICENSE) file for details.

## Support

For issues, questions, or feature requests, please open an issue on GitHub.

## Version History

### v1.0.0 (Initial Release)
- Core AutomationHelper with 25+ methods
- MCP server with 14 tools
- Full FlaUI UIA2 integration
- Comprehensive test application
- NUnit test suite
- Complete documentation

---

**Rhombus.WinFormsMcp** enables headless WinForms automation with full type safety, async/await support, and MCP protocol compatibility. Perfect for test automation, CI/CD integration, and programmatic UI control.
