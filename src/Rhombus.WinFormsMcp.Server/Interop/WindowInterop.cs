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
}
