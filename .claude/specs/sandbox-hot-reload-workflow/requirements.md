# Sandbox Hot-Reload Development Workflow

## 1. Introduction

Enable rapid development iteration for the WinForms MCP server by minimizing Windows Sandbox restarts. The sandbox VM takes 15-30 seconds to boot and is unstable during restart cycles, but runs stably once started. This feature implements a hot-reload workflow where code changes are deployed and reloaded without restarting the sandbox VM.

**Key Insight**: Only restart the sandbox when the bootstrap/watch infrastructure itself changes. All other changes (server code, tools, handlers) can be hot-reloaded.

## 2. User Stories

### 2.1 Developer Hot-Reload Workflow
**As a** developer working on the MCP server
**I want** code changes to be automatically deployed and reloaded in the sandbox
**So that** I can iterate quickly without waiting for sandbox restarts

**Acceptance Criteria:**
- 2.1.1 When I save a .cs file in the server project, the code is built and deployed within 5 seconds
- 2.1.2 The running server inside the sandbox restarts with the new code within 3 seconds of deployment
- 2.1.3 I see clear console output indicating: change detected → building → deploying → server restarted
- 2.1.4 Build failures are reported clearly without crashing the watch process

### 2.2 Initial Sandbox Setup
**As a** developer starting a new session
**I want** a single command to launch the sandbox with hot-reload enabled
**So that** I can start working immediately

**Acceptance Criteria:**
- 2.2.1 Running `watch-dev.ps1` without an existing sandbox offers to launch one
- 2.2.2 The sandbox is ready for communication within 60 seconds of launch
- 2.2.3 The host-side watcher connects to the sandbox automatically when it becomes ready

### 2.3 Graceful Degradation
**As a** developer
**I want** the system to handle errors gracefully
**So that** I don't lose my work or need manual intervention

**Acceptance Criteria:**
- 2.3.1 If the server crashes inside the sandbox, the bootstrap script detects this and waits for the next update to restart
- 2.3.2 If the sandbox VM crashes, the watcher script notifies me and offers to restart
- 2.3.3 Locked files during copy are retried or skipped with warnings, not fatal errors

### 2.4 Bootstrap Infrastructure Changes
**As a** developer modifying the watch/bootstrap infrastructure
**I want** clear guidance on when a sandbox restart is required
**So that** I don't waste time debugging stale infrastructure

**Acceptance Criteria:**
- 2.4.1 Changes to `bootstrap.ps1` trigger a warning that sandbox restart is required
- 2.4.2 Changes to `.wsb` config files trigger a warning that sandbox restart is required
- 2.4.3 The watch script detects infrastructure file changes and prompts for action

### 2.5 Communication Verification
**As a** developer
**I want** to verify the hot-reloaded server is working
**So that** I can confirm my changes are live

**Acceptance Criteria:**
- 2.5.1 The watch script sends a health check ping after each successful reload
- 2.5.2 Health check response includes server version/build timestamp
- 2.5.3 Failed health checks after reload are reported as warnings

## 3. Non-Functional Requirements

### 3.1 Performance
- Build + deploy + reload cycle completes in under 10 seconds for typical changes
- File watcher debouncing prevents duplicate builds (2-second window)
- Self-contained publish is cached/incremental where possible

### 3.2 Reliability
- Watch script handles Ctrl+C gracefully, cleaning up file watchers
- Bootstrap script handles unexpected exits without leaving zombie processes
- File locking conflicts are handled with retry logic

### 3.3 Observability
- All operations are logged with timestamps
- Bootstrap log is accessible from host via shared folder
- Console output uses color coding: yellow=building, green=success, red=error, cyan=info

## 4. Edge Cases

### 4.1 Rapid File Changes
When multiple files change in rapid succession (e.g., git checkout), only one build is triggered after the debounce window.

### 4.2 Build Failures
Build failures leave the sandbox running with the previous working version. The watcher continues monitoring for the next valid change.

### 4.3 Sandbox Not Running
If the watcher starts without a running sandbox, it should either:
- Offer to launch one, OR
- Wait for a sandbox to appear and connect when ready

### 4.4 Partial Deploys
If file copy is interrupted (e.g., Ctrl+C during deploy), the next successful build should overwrite all files cleanly.

### 4.5 Infrastructure File Changes
Changes to these files require sandbox restart (cannot hot-reload):
- `bootstrap.ps1` - runs once at sandbox startup
- `*.wsb` files - define the sandbox configuration
- `watch-dev.ps1` - can be restarted on host without affecting sandbox

## 5. Out of Scope

- **Named pipe transport**: Doesn't work across sandbox VM boundary; shared folder polling is the required approach
- **Automatic sandbox restart**: When infrastructure changes, user manually restarts; no automatic VM restart
- **Multiple sandbox instances**: Only one sandbox supported at a time
- **Cross-platform support**: Windows Sandbox is Windows-only; this is intentional
- **Production deployment**: This is purely a development workflow
- **Test execution inside sandbox**: Testing is out of scope; this is for manual development iteration
