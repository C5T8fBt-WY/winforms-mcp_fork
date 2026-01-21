using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Rhombus.WinFormsMcp.Server.Models;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Centralizes window enumeration and coordinate translation.
/// Provides window context for every tool response.
/// </summary>
public class WindowManager
{
    #region Win32 Imports

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;

    // SW_RESTORE now in Constants.Win32.ShowWindow.Restore

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    #endregion

    /// <summary>
    /// Get all visible top-level windows.
    /// </summary>
    public List<WindowInfo> GetAllWindows()
    {
        var windows = new List<WindowInfo>();
        var foregroundHwnd = GetForegroundWindow();

        EnumWindows((hwnd, lParam) =>
        {
            // Skip invisible windows
            if (!IsWindowVisible(hwnd))
                return true;

            // Skip windows without titles
            var titleLength = GetWindowTextLength(hwnd);
            if (titleLength == 0)
                return true;

            // Get title
            var sb = new StringBuilder(titleLength + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            // Skip empty titles (after retrieving)
            if (string.IsNullOrWhiteSpace(title))
                return true;

            // Get bounds
            if (!GetWindowRect(hwnd, out RECT rect))
                return true;

            // Skip windows with zero size
            var width = rect.right - rect.left;
            var height = rect.bottom - rect.top;
            if (width <= 0 || height <= 0)
                return true;

            // Get process ID
            GetWindowThreadProcessId(hwnd, out uint processId);

            windows.Add(new WindowInfo
            {
                HandlePtr = hwnd,
                Title = title,
                AutomationId = "", // Would need UI Automation to get this
                Bounds = new WindowBounds
                {
                    X = rect.left,
                    Y = rect.top,
                    Width = width,
                    Height = height
                },
                IsActive = hwnd == foregroundHwnd,
                ProcessId = (int)processId
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    /// <summary>
    /// Find a window by handle (hex string) or title (substring match).
    /// </summary>
    /// <param name="windowHandle">Hex handle like "0x1A2B3C" (optional)</param>
    /// <param name="windowTitle">Title substring to match (optional)</param>
    /// <returns>WindowInfo if found, null otherwise</returns>
    public WindowInfo? FindWindow(string? windowHandle, string? windowTitle)
    {
        // Handle takes priority
        if (!string.IsNullOrEmpty(windowHandle))
        {
            return FindWindowByHandle(windowHandle);
        }

        if (!string.IsNullOrEmpty(windowTitle))
        {
            return FindWindowByTitle(windowTitle);
        }

        return null;
    }

    /// <summary>
    /// Find window by hex handle string.
    /// </summary>
    public WindowInfo? FindWindowByHandle(string handleHex)
    {
        try
        {
            var hwnd = ParseHandleString(handleHex);
            if (hwnd == IntPtr.Zero)
                return null;

            // Verify window exists and is visible
            if (!IsWindowVisible(hwnd))
                return null;

            // Get title
            var titleLength = GetWindowTextLength(hwnd);
            var title = "";
            if (titleLength > 0)
            {
                var sb = new StringBuilder(titleLength + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                title = sb.ToString();
            }

            // Get bounds
            if (!GetWindowRect(hwnd, out RECT rect))
                return null;

            var foregroundHwnd = GetForegroundWindow();

            // Get process ID
            GetWindowThreadProcessId(hwnd, out uint processId);

            return new WindowInfo
            {
                HandlePtr = hwnd,
                Title = title,
                AutomationId = "",
                Bounds = new WindowBounds
                {
                    X = rect.left,
                    Y = rect.top,
                    Width = rect.right - rect.left,
                    Height = rect.bottom - rect.top
                },
                IsActive = hwnd == foregroundHwnd,
                ProcessId = (int)processId
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Find window by title substring match.
    /// Returns null if no match or multiple matches.
    /// </summary>
    public WindowInfo? FindWindowByTitle(string titleSubstring)
    {
        var matches = new List<WindowInfo>();
        var foregroundHwnd = GetForegroundWindow();

        EnumWindows((hwnd, lParam) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            var titleLength = GetWindowTextLength(hwnd);
            if (titleLength == 0)
                return true;

            var sb = new StringBuilder(titleLength + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
            {
                if (!GetWindowRect(hwnd, out RECT rect))
                    return true;

                // Get process ID
                GetWindowThreadProcessId(hwnd, out uint processId);

                matches.Add(new WindowInfo
                {
                    HandlePtr = hwnd,
                    Title = title,
                    AutomationId = "",
                    Bounds = new WindowBounds
                    {
                        X = rect.left,
                        Y = rect.top,
                        Width = rect.right - rect.left,
                        Height = rect.bottom - rect.top
                    },
                    IsActive = hwnd == foregroundHwnd,
                    ProcessId = (int)processId
                });
            }

            return true;
        }, IntPtr.Zero);

        // Return single match only
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// Find windows matching title (for error reporting when multiple match).
    /// </summary>
    public List<WindowInfo> FindWindowsByTitle(string titleSubstring)
    {
        var matches = new List<WindowInfo>();
        var foregroundHwnd = GetForegroundWindow();

        EnumWindows((hwnd, lParam) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            var titleLength = GetWindowTextLength(hwnd);
            if (titleLength == 0)
                return true;

            var sb = new StringBuilder(titleLength + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
            {
                if (!GetWindowRect(hwnd, out RECT rect))
                    return true;

                // Get process ID
                GetWindowThreadProcessId(hwnd, out uint processId);

                matches.Add(new WindowInfo
                {
                    HandlePtr = hwnd,
                    Title = title,
                    AutomationId = "",
                    Bounds = new WindowBounds
                    {
                        X = rect.left,
                        Y = rect.top,
                        Width = rect.right - rect.left,
                        Height = rect.bottom - rect.top
                    },
                    IsActive = hwnd == foregroundHwnd,
                    ProcessId = (int)processId
                });
            }

            return true;
        }, IntPtr.Zero);

        return matches;
    }

    /// <summary>
    /// Translate window-relative coordinates to screen coordinates.
    /// </summary>
    /// <param name="window">Target window</param>
    /// <param name="windowX">X relative to window client area</param>
    /// <param name="windowY">Y relative to window client area</param>
    /// <returns>Screen coordinates</returns>
    public (int screenX, int screenY) TranslateCoordinates(WindowInfo window, int windowX, int windowY)
    {
        return (window.Bounds.X + windowX, window.Bounds.Y + windowY);
    }

    /// <summary>
    /// Translate window-relative coordinates by handle or title.
    /// </summary>
    public (int screenX, int screenY)? TranslateCoordinates(string? windowHandle, string? windowTitle, int windowX, int windowY)
    {
        var window = FindWindow(windowHandle, windowTitle);
        if (window == null)
            return null;

        return TranslateCoordinates(window, windowX, windowY);
    }

    /// <summary>
    /// Check if window is minimized.
    /// </summary>
    public bool IsWindowMinimized(string? windowHandle, string? windowTitle)
    {
        IntPtr hwnd = IntPtr.Zero;

        if (!string.IsNullOrEmpty(windowHandle))
        {
            hwnd = ParseHandleString(windowHandle);
        }
        else if (!string.IsNullOrEmpty(windowTitle))
        {
            var window = FindWindowByTitle(windowTitle);
            if (window != null)
                hwnd = window.HandlePtr;
        }

        if (hwnd == IntPtr.Zero)
            return false;

        return IsIconic(hwnd);
    }

    /// <summary>
    /// Parse a hex handle string (e.g., "0x1A2B3C") to IntPtr.
    /// </summary>
    public static IntPtr ParseHandleString(string handleHex)
    {
        try
        {
            var cleanHex = handleHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? handleHex.Substring(2)
                : handleHex;
            return new IntPtr(long.Parse(cleanHex, System.Globalization.NumberStyles.HexNumber));
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Get the current foreground window handle.
    /// </summary>
    public string? GetCurrentForegroundHandle()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;
        return $"0x{hwnd.ToInt64():X}";
    }

    /// <summary>
    /// Focus a window by its hex handle string. Returns true if successful.
    /// Uses HWND_TOPMOST/HWND_NOTOPMOST trick for reliable window activation.
    /// </summary>
    public bool FocusWindowByHandle(string handleHex)
    {
        var hwnd = ParseHandleString(handleHex);
        if (hwnd == IntPtr.Zero)
            return false;

        // Restore if minimized
        if (IsIconic(hwnd))
            ShowWindow(hwnd, Constants.Win32.ShowWindow.Restore);

        // Reliable window activation trick:
        // 1. Make window topmost (forces it to front)
        // 2. Immediately make it non-topmost (allows normal z-order behavior)
        // This bypasses SetForegroundWindow restrictions
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

        // Also call SetForegroundWindow for keyboard focus
        SetForegroundWindow(hwnd);

        return true;
    }

    /// <summary>
    /// Focus a window and return the previous foreground window handle for restoration.
    /// </summary>
    public (bool success, string? previousHandle) FocusWindowWithRestore(string? windowHandle, string? windowTitle)
    {
        var previousHandle = GetCurrentForegroundHandle();
        var window = FindWindow(windowHandle, windowTitle);
        if (window == null)
            return (false, previousHandle);

        var success = FocusWindowByHandle(window.Handle);
        return (success, previousHandle);
    }

    /// <summary>
    /// Get client area bounds in screen coordinates (excludes title bar and borders).
    /// </summary>
    /// <param name="handleHex">Window handle as hex string</param>
    /// <returns>Client area bounds (x, y, width, height) in screen coordinates, or null if failed</returns>
    public WindowBounds? GetClientAreaBounds(string handleHex)
    {
        var hwnd = ParseHandleString(handleHex);
        if (hwnd == IntPtr.Zero)
            return null;

        // Get client area size (relative to client origin)
        if (!GetClientRect(hwnd, out RECT clientRect))
            return null;

        // Convert client origin (0,0) to screen coordinates
        var clientOrigin = new POINT { x = 0, y = 0 };
        if (!ClientToScreen(hwnd, ref clientOrigin))
            return null;

        return new WindowBounds
        {
            X = clientOrigin.x,
            Y = clientOrigin.y,
            Width = clientRect.right - clientRect.left,
            Height = clientRect.bottom - clientRect.top
        };
    }

    /// <summary>
    /// Translate client-relative coordinates to screen coordinates.
    /// Uses actual client area origin (accounts for title bar and borders).
    /// </summary>
    public (int screenX, int screenY)? TranslateClientToScreen(string handleHex, int clientX, int clientY)
    {
        var clientBounds = GetClientAreaBounds(handleHex);
        if (clientBounds == null)
            return null;

        return (clientBounds.X + clientX, clientBounds.Y + clientY);
    }

    /// <summary>
    /// Get windows belonging to specific process IDs.
    /// </summary>
    /// <param name="pids">Set of process IDs to filter by.</param>
    /// <returns>List of windows belonging to the specified processes.</returns>
    public List<WindowInfo> GetWindowsByPids(IReadOnlySet<int> pids)
    {
        if (pids.Count == 0)
            return new List<WindowInfo>();

        return GetAllWindows().Where(w => pids.Contains(w.ProcessId)).ToList();
    }

    /// <summary>
    /// Get windows belonging to a specific process ID.
    /// </summary>
    /// <param name="pid">Process ID to filter by.</param>
    /// <returns>List of windows belonging to the specified process.</returns>
    public List<WindowInfo> GetWindowsByPid(int pid)
    {
        return GetAllWindows().Where(w => w.ProcessId == pid).ToList();
    }
}
