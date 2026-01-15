## Critical Bug: Handle String Conversion

**Location**: winforms-mcp WindowManager and Program.cs

**Issue**: Window handles are converted from IntPtr to string ("0x{hwnd:X}") when stored in WindowInfo, then converted back to IntPtr when needed. This is wasteful and error-prone.

**Recommendation**: Keep handles as IntPtr internally. Only convert to string for JSON/MCP output.

**Files affected**:
- Automation/WindowManager.cs:121, 199 - converts to string
- Program.cs:3329, 4110 - converts back to IntPtr


## Issue: Synthetic Pointer Input Not Reaching WPF Applications

**Status**: RESOLVED (2026-01-15)

**Root Cause**:
WPF by default uses the legacy RealTimeStylus (Wintab) stack, which ignores `WM_POINTER` messages from `InjectSyntheticPointerInput`. Two fixes were required:

**Fix 1 - WPF Application Side**:
Enable WPF Pointer Support in the target WPF app's `App.xaml.cs`:
```csharp
public App()
{
    AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", true);
}
```

**Fix 2 - Injection Side** (InputInjection.cs):
- Add `POINTER_FLAG_INCONTACT` to DOWN flags (required for WPF to start stroke)
- Add `POINTER_FLAG_PRIMARY` to all contact flags
- Correct flag sequence:
  - DOWN: `INRANGE | INCONTACT | DOWN | PRIMARY` (0x12006)
  - MOVE: `INRANGE | INCONTACT | UPDATE | PRIMARY` (0x22006)
  - UP: `INRANGE | UP` (0x40002)

**References**:
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/disable-real-time-stylus
- Gemini AI analysis of WPF Stylus input stack
