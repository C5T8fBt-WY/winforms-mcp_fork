# Sandbox Hot-Reload Workflow - Implementation Tasks

## Phase 1: Setup Infrastructure

- [x] 1. Create `sandbox/` directory and move existing scripts
  - Move `prototypes/transport-test/watch-dev.ps1` → `sandbox/watch-dev.ps1`
  - Move `src/Rhombus.WinFormsMcp.Server/bootstrap.ps1` → `sandbox/bootstrap.ps1`
  - Update any path references
  - Implements requirement 2.2

- [x] 2. Create `sandbox/setup-dotnet.ps1` - one-time .NET download
  - Download .NET 8 runtime to `C:\WinFormsMcpSandboxWorkspace\DotNet`
  - Use official dotnet-install.ps1 script
  - Verify dotnet.exe exists after install
  - Implements design decision 1 (pre-downloaded .NET)

- [x] 3. Create `sandbox/sandbox-dev.wsb` - updated sandbox config
  - Add 4 mapped folders: Server, App, DotNet, Shared
  - Update LogonCommand to set DOTNET_ROOT, PATH, and run bootstrap
  - Implements design section 3.3

## Phase 2: Update Watch Scripts

- [x] 4. Refactor `watch-dev.ps1` with debounce + concurrency lock
  - [x] 4.1. Implement 100ms debounce timer that resets on each change
  - [x] 4.2. Implement concurrency lock (if build running, queue one "next build")
  - [x] 4.3. Remove fixed throttle - build time is natural throttle
  - [x] 4.4. Switch to framework-dependent build (remove `--self-contained`)
  - Implements design decision 2

- [x] 5. Add atomic trigger file writes
  - Write trigger content to `$TriggerName.tmp`
  - Rename to final `$TriggerName` (atomic operation)
  - Prevents partial-read race condition
  - Implements design decision 5

- [x] 6. Rename paths to match spec
  - Change `$DeployPath` default from `Phase1` to `Server`
  - Change trigger file from `update.trigger` to `server.trigger`
  - Update all `$LocalMcpDir` references to `$LocalServerDir`
  - Implements design decision 3

- [x] 7. Create `sandbox/watch-app.ps1` for test application
  - Copy structure from watch-dev.ps1
  - Change project path to TestApp.csproj
  - Change deploy path to `C:\WinFormsMcpSandboxWorkspace\App`
  - Change trigger to `app.trigger`
  - Implements requirement 2.1 (hot-reload for app)

- [x] 8. Add infrastructure change detection
  - Detect changes to `bootstrap.ps1`, `*.wsb` files
  - Print warning that sandbox restart is required
  - Implements requirement 2.4

## Phase 3: Update Bootstrap Script

- [x] 9. Implement Windows Job Object for subprocess management
  - [x] 9.1. Add P/Invoke types for CreateJobObject, AssignProcessToJobObject, SetInformationJobObject
  - [x] 9.2. Create Job Object with KillOnJobClose at bootstrap start
  - [x] 9.3. Assign server and app processes to Job Object when starting
  - [x] 9.4. Job Object auto-kills children when bootstrap exits (no orphans)
  - Implements design decision 3

- [x] 10. Implement FileSystemWatcher + fallback poll for triggers
  - [x] 10.1. Create FSW watching `C:\Shared\*.trigger`
  - [x] 10.2. Register event handler for Created events
  - [x] 10.3. Add 20s fallback poll loop as safety net
  - Implements design decision 6

- [x] 11. Implement Handle-Trigger function
  - Check trigger type (server.trigger or app.trigger)
  - Delete trigger file
  - Stop specific process by tracked PID
  - Copy files from mapped folder to local execution folder
  - Start new process, assign to Job Object, track new PID
  - Log restart with timestamp
  - Implements design section 3.2

- [x] 12. Add PID tracking for precise hot-reload
  - Track `$ServerPid` and `$AppPid` when starting processes
  - Use tracked PID to stop specific process on hot-reload
  - Implements design decision 7

- [x] 13. Update path variables to match spec
  - `$ServerSource` = `C:\Server`
  - `$AppSource` = `C:\App`
  - `$LocalServerDir` = `C:\LocalServer`
  - `$LocalAppDir` = `C:\LocalApp`
  - Implements design decision 3

- [x] 14. Add crash logging
  - Log warning when server or app process exits unexpectedly
  - Include exit code in log message
  - Continue monitoring for next trigger
  - Implements requirement 2.3.1

## Phase 4: Setup and Test Scripts

- [x] 15. Create `sandbox/setup.ps1` - full initial setup
  - Create `C:\WinFormsMcpSandboxWorkspace\` directory structure (Server, App, DotNet, Shared)
  - Call `setup-dotnet.ps1` if DotNet folder doesn't exist
  - Initial build and deploy of server and app
  - Copy bootstrap.ps1 to Server folder
  - Copy sandbox-dev.wsb to WinFormsMcpSandboxWorkspace folder
  - Implements requirement 2.2

- [x] 16. Create `sandbox/test.ps1` - integration test
  - Launch sandbox
  - Wait for mcp-ready.signal
  - Send ping request, verify response
  - Optionally test hot-reload cycle
  - Graceful shutdown
  - Implements design section 6.2

- [x] 17. Create `sandbox/watch-all.ps1` - combined watcher (optional)
  - Run watch-dev.ps1 and watch-app.ps1 as background jobs
  - Forward console output from both
  - Handle Ctrl+C to stop both
  - Implements design section 3.1.4

## Phase 5: Documentation

- [x] 18. Update `docs/SANDBOX_DEVELOPMENT.md`
  - Document new setup process (setup.ps1, setup-dotnet.ps1)
  - Document watch-dev.ps1, watch-app.ps1, watch-all.ps1
  - Document when sandbox restart is required
  - Include troubleshooting section
  - Implements requirement 2.4

## Phase 6: Cleanup

- [x] 19. Remove old files from `prototypes/transport-test/`
  - Delete migrated scripts (watch-dev.ps1)
  - Keep or archive transport test prototypes
  - Update any references in CLAUDE.md
