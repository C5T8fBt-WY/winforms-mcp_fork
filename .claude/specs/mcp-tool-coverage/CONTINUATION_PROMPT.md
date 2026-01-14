# Continuation Prompt: MCP Tool Coverage Implementation

Use this prompt after context compaction to resume work.

---

## Project Context

We're extending the **WinForms MCP server** (`/home/jhedin/workspace/magpie-craft/winforms-mcp`) to support autonomous UI exploration agents. The spec uses the **kiro-spec workflow** (requirements → design → tasks).

## Spec Location

All spec files are in: `/home/jhedin/workspace/magpie-craft/winforms-mcp/.claude/specs/mcp-tool-coverage/`

### Completed Spec Documents:
- `requirements.md` - v2, EARS-compliant, council-reviewed and updated
- `design.md` - Updated with architectural decision (MCP inside sandbox)
- `tasks.md` - 26 task groups across 11 phases, TDD-focused

### Research Documents:
- `research-sandbox-architecture.md` - Windows Sandbox kernel isolation, why Option B (MCP inside sandbox)
- `research-background-automation.md` - How to run automation while user uses laptop normally (VDD + Registry + InjectTouchInput)

### Backups:
- `requirements-v1-original.md` - Original requirements before v2 rewrite
- `tasks-v1-original.md` - Original tasks before architecture decision

## Key Architectural Decisions

1. **MCP runs INSIDE Windows Sandbox** (Option B) - UIA cannot cross VM boundaries
2. **Three-layer background automation**:
   - Layer 1: Indirect Display Driver (IDD/VDD) - keeps DWM rendering when minimized
   - Layer 2: Host registry key `RemoteDesktop_SuppressWhenMinimized = 2`
   - Layer 3: `InjectTouchInput` API - more reliable than mouse in headless state
3. **Transport**: Named pipe (preferred) or shared folder polling (fallback) - needs prototype
4. **Touch/Pen tools**: Leverage sandbox's default Admin privileges for `InjectTouchInput`

## Current State

✅ **Completed**:
- Spec documents created and reviewed by kiro-spec-council
- P0/P1 issues from council review addressed
- Research spikes completed (sandbox architecture, background automation)
- Architecture decision made (Option B)
- Tasks updated to reflect Option B architecture
- **VDD requirements integrated into spec** (requirements.md 1.1, 2.7.6; design.md 8.2; tasks.md Phase 0.5)

⏸️ **Ready to Start**:
- Begin Phase 0: Transport Layer Prototype (named pipe vs shared folder)
- Then Phase 0.5: VDD Integration & Background Automation

## Next Steps

### ~~Option A: Update Spec with VDD Findings~~ ✅ DONE
~~The background automation research revealed we need:~~
- ~~Add VDD installation to sandbox bootstrap in design.md~~ ✅
- ~~Add host prerequisite (registry key) to requirements.md~~ ✅
- ~~Add VDD tasks to tasks.md (Phase 0.5 or integrate into Phase 1)~~ ✅

### Option B: Start Implementation (RECOMMENDED NEXT)
Begin with Phase 0 (Transport Layer Prototype):
1. Test named pipe transport across sandbox VM boundary
2. Test shared folder polling as fallback
3. Document latency and choose approach

Then Phase 0.5 (VDD Integration):
1. Research and select VDD driver
2. Bundle driver with MCP server
3. Create bootstrap script
4. Test background automation

### Option C: Fetch Reference Documentation
Before implementing, fetch API docs for:
1. FlaUI 4.0.0 - UI automation library
2. InjectTouchInput API - Touch/pen injection
3. Windows Sandbox .wsb schema
4. IddCx (Indirect Display Driver) - For VDD integration

## Quick Commands

```bash
# Read the spec files
cat /home/jhedin/workspace/magpie-craft/winforms-mcp/.claude/specs/mcp-tool-coverage/requirements.md
cat /home/jhedin/workspace/magpie-craft/winforms-mcp/.claude/specs/mcp-tool-coverage/design.md
cat /home/jhedin/workspace/magpie-craft/winforms-mcp/.claude/specs/mcp-tool-coverage/tasks.md

# Read research files
cat /home/jhedin/workspace/magpie-craft/winforms-mcp/.claude/specs/mcp-tool-coverage/research-sandbox-architecture.md
cat /home/jhedin/workspace/magpie-craft/winforms-mcp/.claude/specs/mcp-tool-coverage/research-background-automation.md
```

## Resume Prompt

Copy and paste this to resume:

---

**Resume: MCP Tool Coverage Spec - Ready for Implementation**

I'm working on extending the WinForms MCP server to support autonomous UI exploration agents. The spec is complete and lives in `/home/jhedin/workspace/magpie-craft/winforms-mcp/.claude/specs/mcp-tool-coverage/`.

Key files:
- `requirements.md` - Functional requirements (EARS format)
- `design.md` - Architecture (MCP inside Windows Sandbox)
- `tasks.md` - 26 task groups, 11 phases
- `research-sandbox-architecture.md` - Why MCP must run inside sandbox
- `research-background-automation.md` - VDD + Registry for background operation

**Architecture**: MCP server runs INSIDE Windows Sandbox with:
1. Virtual Display Driver (IDD) for headless rendering
2. Host registry key to prevent RDP suspension
3. InjectTouchInput for reliable background automation

**Next step**: [Choose one]
- A) Update spec with VDD requirements from background research
- B) Start Phase 0: Transport Layer Prototype (named pipe vs shared folder)
- C) Fetch reference docs (FlaUI, InjectTouchInput API, .wsb schema)

Please read the spec files and continue from where we left off.

---
