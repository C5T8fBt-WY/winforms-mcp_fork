# C5T8fBtWY.WinFormsMcp — WinForms Automation MCP Server

[![CI Status](https://github.com/C5T8fBt-WY/winforms-mcp_fork/actions/workflows/ci.yml/badge.svg)](https://github.com/C5T8fBt-WY/winforms-mcp_fork/actions/workflows/ci.yml)
[![NPM Version](https://img.shields.io/npm/v/@c5t8fbt-wy/winforms-mcp)](https://www.npmjs.com/package/@c5t8fbt-wy/winforms-mcp)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server that lets AI agents automate WinForms applications through FlaUI (UIA2/MSAA backend). Fork of [rhom6us/winforms-mcp](https://github.com/rhom6us/winforms-mcp).

---

## Quick Start

### 1. Add to your MCP client config

```json
{
  "servers": {
    "winforms-mcp": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "@c5t8fbt-wy/winforms-mcp@latest"]
    }
  }
}
```

> Requires Node.js >= 14 and Windows x64.

### 2. Launch and inspect an app

```
// Launch target application
app(action: "launch", path: "C:\\path\\to\\YourApp.exe", wait_ms: 2000)

// Get a structured UI snapshot (PREFERRED over screenshot for element discovery)
snapshot(window_handle: "0x1A2B3C")

// Click a button by automationId
click(automationId: "btnSubmit")

// Type into a field
type(automationId: "txtName", text: "Hello", clear: true)
```

### 3. Best practices for agents

- **Prefer `snapshot()` over `screenshot()`** for UI analysis — snapshot gives `[ref=elem_XX]` IDs and `[id=automationId]` usable with click/type/find.
- **Always specify `window_handle`** in `snapshot()` and `screenshot()` calls to avoid capturing unintended windows.
- **Use `[id=automationId]`** to reliably identify controls — WinForms label-to-control binding can produce misleading accessible names.

---

## Tools (11 total)

### Application lifecycle

| Tool | Key args | Description |
|------|----------|-------------|
| `app` | `action`, `path`/`pid`/`handle` | Launch, attach, close, or get info |

### UI discovery & interaction

| Tool | Key args | Description |
|------|----------|-------------|
| `snapshot` | `window_handle`, `depth` | **Preferred UI explorer** — Playwright-style accessibility tree with `[id=automationId]` labels |
| `find` | `automationId`/`name`/`at`, `recursive` | Find elements via UIA |
| `click` | `target`/`x,y`/`automationId`, `input` | Click with mouse, touch, or pen |
| `type` | `text`, `target`/`automationId`, `clear` | Type text or send keys |
| `drag` | `path`, `input` | Drag with path support |
| `gesture` | `type`, `center`, `start_distance` | Pinch / rotate / custom multi-touch |
| `screenshot` | `handle`, `target`, `file` | Visual capture (for inspection only) |
| `script` | `steps` | Batch operations with variable interpolation |

### Windows Sandbox

| Tool | Description |
|------|-------------|
| `launch_app_sandboxed` | Launch app in isolated Windows Sandbox |
| `close_sandbox` | Close sandbox |
| `list_sandbox_apps` | List sandbox processes |

---

## Snapshot output format

```
- window "My App" [id=MainWindow] [ref=elem_1]
  - button "Save" [id=btnSave] [ref=elem_2]
  - text "Name" [id=lblName] [ref=elem_5]
  - edit "John" [id=txtName] [ref=elem_6] value="John"
  - edit "(txtAge)" [id=txtAge] [ref=elem_7] value=""
```

- `[id=xxx]` — WinForms `Name` property (reliable code-level identifier)
- `[ref=elem_N]` — session-local element ID usable with `click(target:"elem_N")`
- Unnamed interactive elements show the automationId as display name fallback

---

## Prerequisites

- Windows 10/11 (x64)
- Node.js >= 14 (for npm usage)
- .NET 8.0 SDK (for building from source)

---

## Building from Source

```powershell
git clone https://github.com/C5T8fBt-WY/winforms-mcp_fork.git
cd winforms-mcp_fork
dotnet build C5T8fBtWY.WinFormsMcp.sln -c Release
dotnet test C5T8fBtWY.WinFormsMcp.sln -c Release
```

### Project structure

```
src/Rhombus.WinFormsMcp.Server/     # MCP server (main binary)
src/Rhombus.WinFormsMcp.TestApp/    # Sample WinForms app for testing
tests/Rhombus.WinFormsMcp.Tests/    # NUnit test suite
sandbox/                            # Windows Sandbox integration scripts
```

---

## Architecture

**Stack**: .NET 8.0-windows · FlaUI 4.0.0 (UIA2) · JSON-RPC 2.0 over stdio or TCP

The server provides a minimal 11-tool API consolidated from 52 legacy tools (~90% token reduction) covering all common UI automation needs.

All tool responses include a `windows` array scoped to tracked processes. Snapshot output carries `[ref=elem_N]` (session-local) and `[id=automationId]` (stable across sessions) for every interactive element.

---

## License

MIT — see [LICENSE](LICENSE)