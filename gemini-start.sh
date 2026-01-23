#!/bin/bash
# Starts Gemini with the project root and the sandbox shared folder (for logs)
gemini . \
  --include-directories /mnt/c/WinFormsMcpSandboxWorkspace/Shared \
  --prompt-interactive "You are initialized in the Rhombus.WinFormsMcp environment. This project bridges a Linux/WSL MCP client to a Windows Forms application running in an isolated Windows Sandbox.

Key Context:
- **Architecture**: A PowerShell bridge (mcp-sandbox-bridge.ps1) launches a Windows Sandbox (sandbox/sandbox-dev.wsb). Inside, sandbox/bootstrap.ps1 starts the C# MCP Server (src/Rhombus.WinFormsMcp.Server).
- **Communication**: STDIO over the bridge -> TCP socket to the Sandbox.
- **Logs**: Runtime logs are available in the included external directory /mnt/c/WinFormsMcpSandboxWorkspace/Shared.

Action Required:
Please run the codebase_investigator agent to index the current state of the project, focusing on the startup sequence and communication protocol. Once you have established context, await my specific instructions."