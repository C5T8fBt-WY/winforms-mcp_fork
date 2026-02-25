# winforms-mcp Improvement Points (from EvaluationSys Testing)

Collected during a full end-to-end test session covering: complete measurement cycle, settings management, and general interaction patterns.

---

## Quick-Win Matrix

| # | Point | Impl Cost | Impact | Priority |
|---|-------|-----------|--------|----------|
| 3 | HWND target for `type` tool | ✅ Done (S) | H — unblocks all modal dialogs | **Done** |
| 5 | `click` tool: send WM_CLOSE for dialogs without buttons | ✅ Done (S) | H — no more PowerShell workaround needed | **Done** |
| 4 | `find(point)`: Win32 `WindowFromPoint` fallback for modals | ✅ Done (S) | H — coordinate-based find works for modals | **Done** |
| 1 | Modal dialog UIA fallback via EnumChildWindows | ✅ Done (M) | H — `snapshot`/`find` work for modal dialogs | **Done** |
| 6 | `snapshot` empty during ShowDialog | ✅ Done (M) | H — fixed by #1 | **Done** |
| 10 | UIA Name = adjacent label text (doc/guidance) | XS (30m) | M — guidance for app developers | Medium |
| 7 | Inconsistency: `windows[]` shows dialog but `find` misses it | M (1d) | M — confusing for LLM agents | Medium |
| 8 | `elem_N` IDs reset on MCP restart | M (1d) | M — inconvenient for multi-restart workflows | Medium |
| 9 | New process on "Back to Menu" (EvaluationSys behavior) | XS (1h) | M — `app auto-reattach` feature | Medium |
| 2 | WinForms PropertyGrid no UIA representation | L (3-5d) | M — complex custom UIA provider | Low |

**Cost legend**: XS < 1h, S < day, M < week, L > week  
**Impact**: H = currently blocks/workarounds needed, M = quality-of-life, L = edge case

---

## 1. Modal Dialogs (ShowDialog) Return Empty UIA Tree

**Symptom**: `snapshot()` and `find(recursive:true)` return empty results for forms shown via `Form.ShowDialog(parent)`.

**Root Cause**: `GetElementFromHandle()` times out for the dialog window and its children when the main form has a nested modal message loop running. Although the WinForms UI thread IS pumping messages during `ShowDialog`, UIA COM cross-process calls block beyond the 2-second timeout for these windows.

**Workaround Implemented**: Use Win32 `EnumChildWindows` to discover child HWNDs, then interact via `type(target:"0xHWND", ...)` with the new HWND target support.

**Improvement Ideas**:
- The `find` tool could fall back to Win32 `EnumChildWindows` if FlaUI/UIA returns empty for a known HWND.
- Expose a dedicated `enumerate_children(window_handle:...)` tool that returns child HWNDs via Win32 (bypassing UIA).

---

## 2. WinForms PropertyGrid Has No UIA Representation

**Symptom**: `snapshot()` and `find(controlType:...)` find nothing inside a `PropertyGrid` control. Even `find(point:{x,y})` inside the PropertyGrid returns "No element at point".

**Root Cause**: WinForms `PropertyGrid` renders its rows internally (owner-drawn) — the individual property name/value cells are NOT exposed as child UIA elements. FlaUI's `FindAllChildren()` returns empty for the PropertyGrid's internal grid view.

**Workaround Implemented**: 
1. Use `EnumChildWindows` to find the `WindowsForms10.Edit` HWND inside the PropertyGrid (the inline editor).
2. Click at calculated screen coordinates to select a property row (the edit control appears at that row's y-coordinate).
3. Use `type(target:"0xHWND", text:..., clear:true)` to set the value via `WM_SETTEXT` + `VK_RETURN`.

**Improvement Ideas**:
- The screenshot tool with `window_handle` correctly identifies the dialog is there (via Win32 window list), but the snapshot/find tools don't — the UIA and window-list paths are inconsistent from the LLM's perspective.
- A coordinate-to-HWND lookup using `WindowFromPoint` + `ChildWindowFromPointEx` could help identify which physical child HWND is at a given screen coordinate, even when UIA is empty.

---

## 3. type Tool: No Way to Target Controls by HWND

**Symptom** (pre-fix): When UIA can't access a control, there was no way to type into it via the MCP. The `target` parameter only accepted `elem_N` IDs, which require a cached UIA element.

**Fix Applied**: Added `"0xHHHH"` HWND format detection in `TypeHandler.cs`. When `target` starts with `"0x"` and parses as a valid 64-bit hex integer, `TypeIntoHwnd` is called:
- `WM_SETTEXT` (via `SendMessage`, synchronous) for `clear:true` — directly sets the Win32 edit control's text.
- `WM_GETTEXT` + `WM_SETTEXT` for append (`clear:false`).
- `WM_KEYDOWN(VK_RETURN)` via `PostMessage` to commit the value.

**Remaining Gap**: The HWND must be discovered externally (e.g., via `EnumChildWindows` in a PowerShell script). The MCP has no tool to enumerate child HWNDs of a given window handle.

---

## 4. find(point:{x,y}) Only Searches desktop.FindAllChildren()

**Symptom**: `find(point:{x:960, y:400})` returns "No element at point" even when those coordinates are inside a visible WinForms dialog.

**Root Cause**: The `FindAtPoint` implementation iterates `desktop.FindAllChildren()` (UIA tree top-level elements). Owned/modal dialogs (shown with an explicit owner via `ShowDialog(parent)`) appear as children of their **owner** in the UIA tree, NOT as direct children of the desktop. So the modal dialog is invisible to the desktop-level search.

**Fix Idea**: `FindAtPoint` should also check windows from `EnumWindows` (Win32) if the UIA desktop walk misses the coordinate. Or use FlaUI's `automation.FromPoint()` directly.

---

## 5. click Tool: modal dialog close fails when dialog has no OK/Cancel button

**Symptom**: `click(name:"Close", window_handle:"0xHHHH")` attempts to find and click a button named "Close" or IDOK/IDCANCEL. For a plain PropertyGrid dialog (no buttons), this either does nothing or incorrectly sends `WM_COMMAND(IDOK)`.

**Current Behavior**: The `postmessage:dialog` path fires `WM_COMMAND(1)` (IDOK) — but for a Form without Buttons registered, this may or may not close the dialog.

**Better Approach**: Support `click(action:"close", window_handle:"0xHHHH")` that sends `WM_CLOSE` to the dialog. The current only reliable option outside the MCP was using `PostMessage(hwnd, WM_CLOSE, ...)` from PowerShell.

---

## 6. snapshot Empty for All tracked Windows During ShowDialog

**Symptom**: While a modal dialog is open (both the dialog 0xE51B9C and the parent 0x4B16AA are tracked), `snapshot()` returns an empty string `""`.

**Root Cause**: Both tracked windows have timeouts in `BuildSnapshotNode.FindAllChildren()` (500ms) and `GetWindowsForPids.GetElementFromHandle()` (2000ms). During `ShowDialog`, both timeout.

**Impact**: There is no way to get a UIA snapshot of the application state while a modal dialog is open.

**Improvement**: Consider implementing a Win32-based fallback that at minimum reports the dialog's title, HWND, bounds, and child window class list when UIA is unavailable.

---

## 7. Owned Dialogs Not in windows List from snapshot

**Symptom**: After `ShowDialog(mainWindow)`, the modal dialog is visible in `windows[]` in tool responses (via Win32 `EnumWindows`), but `find(at:"root", recursive:true)` shows `{windows:[]}`.

**Root Cause**: The `windows[]` sidebar uses Win32 `EnumWindows` (includes all top-level HWNDs), but `find(recursive:true, at:"root")` uses UIA's `GetWindowsForPids` which calls `GetElementFromHandle()` per HWND — and that times out for owned-dialog windows during `ShowDialog`.

**Impact**: Inconsistency: the `windows` array says a window exists, but `find` can't find it or its children. Confusing for LLM agents.

---

## 8. elem_N IDs Reset on MCP Server Restart

**Symptom**: After restarting the MCP server connection (needed for deployment), all `elem_N` IDs are invalidated. Any element references from before restart must be re-obtained via new `find` or `snapshot` calls.

**Impact**: Multi-step workflows that span a restart require re-discovery of all element references.

**Improvement Idea**: Consider a short-lived HWND-based cache key that survives restarts (e.g., `hwnd:0xHHHH` as a stable reference) for windows/dialogs that are still open.

---

## 9. "Back to Menu" Spawns New EvaluationSys Process

**Symptom**: Clicking "Back to Menu" from the main screen closes the current EvaluationSys window and launches a new separate process. This breaks the MCP's tracking of the current PID.

**Impact**: Agent must explicitly re-attach (`app attach title:"StA2BLE: Mode Selection"`) after navigation. The previous `elem_N` IDs are also invalidated.

**Note**: This is an EvaluationSys behavior, not an MCP bug — but it's a pattern the MCP tooling should handle gracefully. An "app auto-reattach on process change" feature in the `app` tool could help.

---

## 10. UIA Name for WinForms TextBox is Adjacent Label Text

**Symptom**: When searching for a TextBox by `name`, the UIA name is the text of the label that visually precedes it (e.g., searching `name:"Subject ID"` finds the TextBox, not the Label).

**Root Cause**: WinForms does not automatically set `AccessibleName` on TextBox controls. Windows accessibility infrastructure associates the preceding label's text as the TextBox's name in the UIA tree.

**Impact**: `find(name:"Subject ID")` correctly finds the TextBox, but `find(name:"txtSubjectId", automationId:"txtSubjectId")` would be more precise and stable if the control has `AccessibleName` set.

**Guidance for EvaluationSys**: Set `Control.AccessibleName` on all form controls to avoid ambiguity.

---

## Summary of Changes Made During This Session

| Change | File | Status |
|--------|------|--------|
| HWND target in `type` tool (`"0xHHHH"` format) | `TypeHandler.cs` | ✅ Deployed |
| `SendMessage` overloads for `WM_SETTEXT`/`WM_GETTEXT` | `WindowInterop.cs` | ✅ Deployed |
| `WM_SETTEXT = 0x000C`, `WM_GETTEXT = 0x000D` constants | `WindowInterop.cs` | ✅ Deployed |
| Physical keyboard ban (removed all `SendKeys.SendWait`) | `TypeHandler.cs` | ✅ Deployed (previous session) |
| Analysis hang fix (`RedirectStandardInput=true`) | `AnalysisWithPython.cs` (EvaluationSys) | ✅ Applied |
