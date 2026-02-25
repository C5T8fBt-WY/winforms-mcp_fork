# @c5t8fbt-wy/winforms-mcp

[![CI Status](https://github.com/C5T8fBt-WY/winforms-mcp_fork/actions/workflows/ci.yml/badge.svg)](https://github.com/C5T8fBt-WY/winforms-mcp_fork/actions/workflows/ci.yml)
[![npm version](https://img.shields.io/npm/v/@c5t8fbt-wy/winforms-mcp)](https://www.npmjs.com/package/@c5t8fbt-wy/winforms-mcp)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A Model Context Protocol (MCP) server for headless WinForms automation using FlaUI (UIA2/MSAA backend). Enables AI agents to discover, interact with, and automate Windows Forms applications — including modal dialogs — via Win32 fallback.

> **Windows only.** Requires .NET 8 runtime.

## Installation

```bash
npm install -g @c5t8fbt-wy/winforms-mcp
```

## Usage with Claude Desktop / VS Code

Add to your MCP config (e.g. `claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "winforms-mcp": {
      "command": "npx",
      "args": ["@c5t8fbt-wy/winforms-mcp"]
    }
  }
}
```

Or run directly:

```bash
npx @c5t8fbt-wy/winforms-mcp
```

## Available Tools (11)

| Tool | Description |
|------|-------------|
| `app` | Launch, attach, close, or inspect applications |
| `find` | Discover UI elements by name, automationId, type, or point |
| `click` | Click with mouse, touch, or pen input |
| `type` | Type text or send keyboard keys |
| `drag` | Drag with path support |
| `gesture` | Multi-touch: pinch, rotate, custom |
| `snapshot` | Capture UI tree as text — supports modal dialogs via Win32 fallback |
| `screenshot` | Capture window or element screenshot |
| `script` | Batch steps with variable interpolation |
| `launch_app_sandboxed` | Launch app in Windows Sandbox |
| `close_sandbox` / `list_sandbox_apps` | Manage sandbox |

## Key Features

- **Win32 fallback**: `snapshot`, `find(point:...)`, and `find(recursive:true)` automatically fall back to Win32 APIs when UIA is blocked by modal dialogs (e.g. `ShowDialog`)
- **Close modal dialogs**: `app(action:"close", handle:"0xHWND")` closes dialogs without PowerShell
- **All standard UIA operations**: click, type, drag, gesture, screenshot

## Source

Fork of [jhedin/winforms-mcp](https://github.com/jhedin/winforms-mcp) with Win32 modal dialog improvements.  
Repository: [C5T8fBt-WY/winforms-mcp_fork](https://github.com/C5T8fBt-WY/winforms-mcp_fork)
