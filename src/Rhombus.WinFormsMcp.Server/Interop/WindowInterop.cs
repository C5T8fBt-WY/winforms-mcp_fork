using System;
using System.Runtime.InteropServices;
using System.Text;
using static Rhombus.WinFormsMcp.Server.Interop.Win32Types;

namespace Rhombus.WinFormsMcp.Server.Interop;

/// <summary>
/// P/Invoke declarations for window management APIs.
/// </summary>
public static class WindowInterop
{
    /// <summary>
    /// Delegate for EnumWindows callback.
    /// </summary>
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    /// <summary>
    /// Enumerate all top-level windows.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    /// Get the text/title of a window.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    /// <summary>
    /// Get the length of window text.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    /// <summary>
    /// Get the window class name.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    /// <summary>
    /// Get the rectangle of a window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// Check if a window is visible.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>
    /// Get the foreground (active) window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// Set the foreground (active) window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Find a window by class name and/or window title.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    /// <summary>
    /// Get the process ID for a window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>
    /// Show or hide a window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// Get the desktop window handle.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    /// <summary>
    /// Get the parent window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    /// <summary>
    /// Get an ancestor window. gaFlags: GA_PARENT=1, GA_ROOT=2, GA_ROOTOWNER=3.
    /// Use GA_ROOT (2) to walk up to the topmost non-child ancestor (the top-level window).
    /// Required by SetForegroundWindow which silently ignores child windows.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    /// <summary>GA_ROOT — walk up to the topmost non-child ancestor.</summary>
    public const uint GA_ROOT = 2;

    /// <summary>
    /// Get the window at a specific point.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    /// <summary>
    /// Convert client coordinates to screen coordinates.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    /// <summary>
    /// Convert screen coordinates to client coordinates.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    /// <summary>
    /// Get the client area rectangle of a window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// Post a message to a window's message queue (non-blocking, safe for background use).
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Send a message to a window synchronously (waits for processing).
    /// String overload: lParam is a Unicode string, used for WM_SETTEXT.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

    /// <summary>
    /// Send a message to a window synchronously.
    /// StringBuilder overload: used for WM_GETTEXT to receive a string result.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, System.Text.StringBuilder lParam);

    /// <summary>
    /// Find the deepest child window at client-relative coordinates.
    /// CWP_SKIPINVISIBLE (0x01) | CWP_SKIPDISABLED (0x02) — only enabled, visible children.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr ChildWindowFromPointEx(IntPtr hWndParent, POINT Point, uint uFlags);

    public const uint CWP_SKIPINVISIBLE = 0x01;
    public const uint CWP_SKIPDISABLED = 0x02;

    /// <summary>
    /// Recursively find the deepest visible child HWND at a given client-relative point.
    /// Falls back to parent if no child is found at the point.
    /// </summary>
    public static IntPtr DeepChildFromClientPoint(IntPtr parent, POINT clientPt)
    {
        var child = ChildWindowFromPointEx(parent, clientPt, CWP_SKIPINVISIBLE | CWP_SKIPDISABLED);
        if (child == IntPtr.Zero || child == parent) return parent;
        // Convert to child's client coords for deeper search
        var screenPt = clientPt;
        ClientToScreen(parent, ref screenPt);
        var childClientPt = screenPt;
        ScreenToClient(child, ref childClientPt);
        var deeper = ChildWindowFromPointEx(child, childClientPt, CWP_SKIPINVISIBLE | CWP_SKIPDISABLED);
        if (deeper != IntPtr.Zero && deeper != child) return deeper;
        return child;
    }

    /// <summary>
    /// Pack X and Y coordinates into an lParam for mouse messages.
    /// </summary>
    public static IntPtr MakeLParam(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));

    // Text control messages (for WinForms controls that don't support UIA ValuePattern)
    public const uint WM_SETTEXT = 0x000C;  // Replace control text directly (synchronous)
    public const uint WM_GETTEXT = 0x000D;  // Read control text (synchronous)
    public const uint WM_CLOSE = 0x0010;  // Request window close (safe for modal dialogs)

    // Standard mouse messages
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint MK_LBUTTON = 0x0001;
    public const uint MK_RBUTTON = 0x0002;

    // Keyboard messages (for dialog dismissal and programmatic text input via PostMessage)
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CHAR = 0x0102;
    public const uint VK_BACK = 0x08;
    public const uint VK_RETURN = 0x0D;
    public const uint VK_SHIFT = 0x10;
    public const uint VK_CONTROL = 0x11;
    public const uint VK_MENU = 0x12;  // Alt
    public const uint VK_ESCAPE = 0x1B;
    public const uint VK_DELETE = 0x2E;
    public const uint VK_A = 0x41;

    // Button message (simulates a button click — triggers WM_COMMAND internally)
    public const uint BM_CLICK = 0x00F5;

    // WM_COMMAND: used to accept/cancel dialogs programmatically via PostMessage.
    // Works even when UIA is blocked by a Win32 MessageBox's message loop.
    public const uint WM_COMMAND = 0x0111;
    public const int IDOK = 1;
    public const int IDCANCEL = 2;

    /// <summary>
    /// Find a control in a dialog by its dialog control ID (e.g. IDOK=1, IDCANCEL=2).
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

    /// <summary>
    /// Enumerate child windows of a parent window.
    /// Used to find dialog buttons by class/text when control IDs are unknown.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    /// Capture a window's content into a device context, even if the window is occluded or off-screen.
    /// nFlags: 0 = WM_PRINT, 2 = PW_RENDERFULLCONTENT (composited/DWM-aware).
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    /// <summary>PW_RENDERFULLCONTENT — render the full window including DirectComposition content.</summary>
    public const uint PW_RENDERFULLCONTENT = 2;

    /// <summary>
    /// Attaches or detaches the input processing mechanism of one thread to another.
    /// Allows SetForegroundWindow to work from a background process by borrowing the
    /// foreground thread's input permission.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    /// <summary>Get the thread ID of the calling thread (kernel32).</summary>
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    // ── UIPI / integrity-level helpers ─────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, uint TokenInformationClass,
        IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TokenIntegrityLevel = 25; // TOKEN_INFORMATION_CLASS

    /// <summary>
    /// Returns true when the window's host process is running at High or System integrity
    /// (i.e., elevated via UAC). In that case UIPI will silently drop PostMessage calls
    /// from a normal medium-integrity MCP server process, and we must use physical input.
    /// </summary>
    public static bool IsWindowProcessElevated(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;

        var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero)
        {
            // Cannot open even with LIMITED_INFORMATION → likely System or higher
            return true;
        }

        try
        {
            if (!OpenProcessToken(hProcess, TOKEN_QUERY, out var hToken))
                return false;
            try
            {
                // First call: get required buffer size
                GetTokenInformation(hToken, TokenIntegrityLevel, IntPtr.Zero, 0, out uint size);
                if (size == 0) return false;

                var buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (!GetTokenInformation(hToken, TokenIntegrityLevel, buf, size, out _))
                        return false;

                    // TOKEN_MANDATORY_LABEL: { SID_AND_ATTRIBUTES Sid, DWORD Attributes }
                    // The SID pointer is the first IntPtr in the structure.
                    var sidPtr = Marshal.ReadIntPtr(buf);
                    // SID layout: Revision(1), SubAuthorityCount(1), IdentifierAuthority(6), SubAuthority[n](4 each)
                    byte subAuthorityCount = Marshal.ReadByte(sidPtr, 1);
                    // Last sub-authority holds the integrity RID
                    int rid = Marshal.ReadInt32(sidPtr, 8 + (subAuthorityCount - 1) * 4);
                    // High integrity = 0x3000, System = 0x4000
                    return rid >= 0x3000;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { CloseHandle(hToken); }
        }
        finally { CloseHandle(hProcess); }
    }
}
