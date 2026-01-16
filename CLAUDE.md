# CLAUDE.md

## Project Overview

**Rhombus.WinFormsMcp** - MCP server for headless WinForms automation using FlaUI (UIA2 backend).

## Commands

```bash
dotnet build Rhombus.WinFormsMcp.sln                    # Build
dotnet test Rhombus.WinFormsMcp.sln                     # Test
dotnet run --project src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj  # Run server
dotnet run --project src/Rhombus.WinFormsMcp.TestApp/Rhombus.WinFormsMcp.TestApp.csproj # Run test app
dotnet publish src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj -c Release -o publish
```

## Architecture

| Component | Path | Purpose |
|-----------|------|---------|
| Server | `src/Rhombus.WinFormsMcp.Server/` | MCP server with JSON-RPC 2.0 over stdio |
| TestApp | `src/Rhombus.WinFormsMcp.TestApp/` | Sample WinForms app for testing |
| Tests | `tests/Rhombus.WinFormsMcp.Tests/` | NUnit test suite |

**Stack**: .NET 8.0-windows, FlaUI 4.0.0 (UIA2), NUnit 3.14.0

### MCP Tools

- **Process**: `launch_app`, `attach_to_process`, `close_app`
- **Discovery**: `find_element`, `element_exists`, `wait_for_element`
- **Interaction**: `click_element`, `type_text`, `set_value`, `drag_drop`, `send_keys`
- **Other**: `get_property`, `take_screenshot`

Session state: cached elements (elem_1, elem_2...), active AutomationHelper, process PIDs.

## CI/CD

- Version in `VERSION` file, auto-bumped on master commits
- Publishes to NuGet (`Rhombus.WinFormsMcp`) and NPM (`@rhom6us/winforms-mcp`)