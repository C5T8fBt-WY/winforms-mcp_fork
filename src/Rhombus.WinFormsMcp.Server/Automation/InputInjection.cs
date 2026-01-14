using System;
using System.Runtime.InteropServices;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Windows Touch, Pen, and Mouse input injection APIs for simulating user input.
///
/// TIMING DESIGN:
/// All input methods have optional delay parameters (delayMs, holdMs) that default to 0.
/// This means inputs are instant by default for maximum speed. Callers can add delays
/// when needed for specific scenarios (e.g., long-press gestures, slow animations).
///
/// MOUSE INPUT:
/// Uses direct SendInput P/Invoke for instant mouse positioning instead of FlaUI's
/// Mouse.MoveTo() which has built-in animation overhead. See the Mouse Input region
/// for detailed documentation on why this was necessary.
/// </summary>
public static class InputInjection
{
    #region Touch Injection

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InitializeTouchInjection(uint maxCount, uint dwMode);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InjectTouchInput(uint count, [MarshalAs(UnmanagedType.LPArray)] POINTER_TOUCH_INFO[] contacts);

    public const uint TOUCH_FEEDBACK_DEFAULT = 0x1;
    public const uint TOUCH_FEEDBACK_INDIRECT = 0x2;
    public const uint TOUCH_FEEDBACK_NONE = 0x3;

    public const uint POINTER_FLAG_NONE = 0x00000000;
    public const uint POINTER_FLAG_NEW = 0x00000001;
    public const uint POINTER_FLAG_INRANGE = 0x00000002;
    public const uint POINTER_FLAG_INCONTACT = 0x00000004;
    public const uint POINTER_FLAG_FIRSTBUTTON = 0x00000010;
    public const uint POINTER_FLAG_SECONDBUTTON = 0x00000020;
    public const uint POINTER_FLAG_PRIMARY = 0x00002000;
    public const uint POINTER_FLAG_CONFIDENCE = 0x00004000;
    public const uint POINTER_FLAG_CANCELED = 0x00008000;
    public const uint POINTER_FLAG_DOWN = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_PRIMARY;
    public const uint POINTER_FLAG_UPDATE = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT;
    public const uint POINTER_FLAG_UP = POINTER_FLAG_NONE;

    public const uint PT_TOUCH = 0x00000002;
    public const uint PT_PEN = 0x00000003;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_INFO
    {
        public uint pointerType;
        public uint pointerId;
        public uint frameId;
        public uint pointerFlags;
        public IntPtr sourceDevice;
        public IntPtr hwndTarget;
        public POINT ptPixelLocation;
        public POINT ptHimetricLocation;
        public POINT ptPixelLocationRaw;
        public POINT ptHimetricLocationRaw;
        public uint dwTime;
        public uint historyCount;
        public int inputData;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public uint ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint touchFlags;
        public uint touchMask;
        public RECT rcContact;
        public RECT rcContactRaw;
        public uint orientation;
        public uint pressure;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    #endregion

    #region Pen Injection (Windows 10 1809+ Synthetic Pointer API)

    // CreateSyntheticPointerDevice - creates pen/touch injection device
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateSyntheticPointerDevice(uint pointerType, uint maxCount, uint mode);

    // InjectSyntheticPointerInput - injects pen/touch input
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InjectSyntheticPointerInput(IntPtr device, [MarshalAs(UnmanagedType.LPArray)] POINTER_TYPE_INFO[] pointerInfo, uint count);

    // DestroySyntheticPointerDevice - cleanup
    [DllImport("user32.dll", SetLastError = false)]
    public static extern void DestroySyntheticPointerDevice(IntPtr device);

    // POINTER_FEEDBACK_MODE
    public const uint POINTER_FEEDBACK_DEFAULT = 0x1;
    public const uint POINTER_FEEDBACK_INDIRECT = 0x2;
    public const uint POINTER_FEEDBACK_NONE = 0x3;

    // Pen flags
    public const uint PEN_FLAG_NONE = 0x00000000;
    public const uint PEN_FLAG_BARREL = 0x00000001;
    public const uint PEN_FLAG_INVERTED = 0x00000002;
    public const uint PEN_FLAG_ERASER = 0x00000004;

    // Pen mask - which fields are valid
    public const uint PEN_MASK_NONE = 0x00000000;
    public const uint PEN_MASK_PRESSURE = 0x00000001;
    public const uint PEN_MASK_ROTATION = 0x00000002;
    public const uint PEN_MASK_TILT_X = 0x00000004;
    public const uint PEN_MASK_TILT_Y = 0x00000008;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_PEN_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint penFlags;
        public uint penMask;
        public uint pressure;      // 0-1024
        public uint rotation;      // 0-359
        public int tiltX;          // -90 to +90
        public int tiltY;          // -90 to +90
    }

    // POINTER_TYPE_INFO - union structure for pen/touch injection
    [StructLayout(LayoutKind.Explicit)]
    public struct POINTER_TYPE_INFO
    {
        [FieldOffset(0)]
        public uint type;  // PT_TOUCH or PT_PEN

        // Union - penInfo starts at offset 4 (after type field)
        [FieldOffset(4)]
        public POINTER_PEN_INFO penInfo;

        [FieldOffset(4)]
        public POINTER_TOUCH_INFO touchInfo;
    }

    #endregion

    #region Helper Methods

    private static bool _touchInitialized;
    private static IntPtr _penDevice = IntPtr.Zero;
    private static uint _nextPointerId = 1;

    /// <summary>
    /// Initialize touch injection (call once before injecting touch)
    /// </summary>
    public static bool EnsureTouchInitialized(uint maxContacts = 10)
    {
        if (_touchInitialized) return true;
        _touchInitialized = InitializeTouchInjection(maxContacts, TOUCH_FEEDBACK_DEFAULT);
        return _touchInitialized;
    }

    /// <summary>
    /// Initialize pen injection using Synthetic Pointer API (Windows 10 1809+)
    /// </summary>
    public static bool EnsurePenInitialized()
    {
        if (_penDevice != IntPtr.Zero) return true;
        _penDevice = CreateSyntheticPointerDevice(PT_PEN, 1, POINTER_FEEDBACK_DEFAULT);
        return _penDevice != IntPtr.Zero;
    }

    /// <summary>
    /// Cleanup pen device
    /// </summary>
    public static void CleanupPenDevice()
    {
        if (_penDevice != IntPtr.Zero)
        {
            DestroySyntheticPointerDevice(_penDevice);
            _penDevice = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Inject a single touch point (down, move, or up)
    /// </summary>
    public static bool InjectTouch(int x, int y, uint pointerId, uint flags)
    {
        if (!EnsureTouchInitialized()) return false;

        var contact = new POINTER_TOUCH_INFO
        {
            pointerInfo = new POINTER_INFO
            {
                pointerType = PT_TOUCH,
                pointerId = pointerId,
                pointerFlags = flags | POINTER_FLAG_CONFIDENCE,
                ptPixelLocation = new POINT { x = x, y = y }
            },
            touchFlags = 0,
            touchMask = 0,
            rcContact = new RECT { left = x - 2, top = y - 2, right = x + 2, bottom = y + 2 },
            orientation = 0,
            pressure = 512
        };

        return InjectTouchInput(1, new[] { contact });
    }

    /// <summary>
    /// Simulate a touch tap at a location
    /// </summary>
    /// <param name="x">Screen X coordinate</param>
    /// <param name="y">Screen Y coordinate</param>
    /// <param name="holdMs">Milliseconds to hold before release (default 0)</param>
    public static bool TouchTap(int x, int y, int holdMs = 0)
    {
        var id = _nextPointerId++;

        // Down
        if (!InjectTouch(x, y, id, POINTER_FLAG_DOWN | POINTER_FLAG_NEW))
            return false;

        if (holdMs > 0)
            System.Threading.Thread.Sleep(holdMs);

        // Up
        return InjectTouch(x, y, id, POINTER_FLAG_UP);
    }

    /// <summary>
    /// Simulate a touch drag from one point to another
    /// </summary>
    public static bool TouchDrag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 5)
    {
        var id = _nextPointerId++;

        // Down
        if (!InjectTouch(x1, y1, id, POINTER_FLAG_DOWN | POINTER_FLAG_NEW))
            return false;

        // Move
        for (int i = 1; i <= steps; i++)
        {
            int x = x1 + (x2 - x1) * i / steps;
            int y = y1 + (y2 - y1) * i / steps;
            if (!InjectTouch(x, y, id, POINTER_FLAG_UPDATE))
                return false;
            if (delayMs > 0)
                System.Threading.Thread.Sleep(delayMs);
        }

        // Up
        return InjectTouch(x2, y2, id, POINTER_FLAG_UP);
    }

    /// <summary>
    /// Inject a pen stroke with pressure using Synthetic Pointer API
    /// </summary>
    public static bool InjectPen(int x, int y, uint flags, uint penFlags = PEN_FLAG_NONE, uint pressure = 512, int tiltX = 0, int tiltY = 0)
    {
        if (!EnsurePenInitialized()) return false;

        var typeInfo = new POINTER_TYPE_INFO
        {
            type = PT_PEN,
            penInfo = new POINTER_PEN_INFO
            {
                pointerInfo = new POINTER_INFO
                {
                    pointerType = PT_PEN,
                    pointerId = 1,
                    pointerFlags = flags | POINTER_FLAG_CONFIDENCE,
                    ptPixelLocation = new POINT { x = x, y = y }
                },
                penFlags = penFlags,
                penMask = PEN_MASK_PRESSURE | PEN_MASK_TILT_X | PEN_MASK_TILT_Y,
                pressure = pressure,
                rotation = 0,
                tiltX = tiltX,
                tiltY = tiltY
            }
        };

        return InjectSyntheticPointerInput(_penDevice, new[] { typeInfo }, 1);
    }

    /// <summary>
    /// Simulate a pen stroke from one point to another
    /// </summary>
    public static bool PenStroke(int x1, int y1, int x2, int y2, int steps = 20, uint pressure = 512, bool eraser = false, int delayMs = 2)
    {
        uint penFlags = eraser ? PEN_FLAG_INVERTED : PEN_FLAG_NONE;

        // Down
        if (!InjectPen(x1, y1, POINTER_FLAG_DOWN | POINTER_FLAG_NEW, penFlags, pressure))
            return false;

        // Move
        for (int i = 1; i <= steps; i++)
        {
            int x = x1 + (x2 - x1) * i / steps;
            int y = y1 + (y2 - y1) * i / steps;
            if (!InjectPen(x, y, POINTER_FLAG_UPDATE, penFlags, pressure))
                return false;
            if (delayMs > 0)
                System.Threading.Thread.Sleep(delayMs);
        }

        // Up
        return InjectPen(x2, y2, POINTER_FLAG_UP, penFlags, 0);
    }

    /// <summary>
    /// Simulate pen tap (like clicking with pen tip)
    /// </summary>
    /// <param name="x">Screen X coordinate</param>
    /// <param name="y">Screen Y coordinate</param>
    /// <param name="pressure">Pen pressure 0-1024 (default 512)</param>
    /// <param name="holdMs">Milliseconds to hold before release (default 0)</param>
    public static bool PenTap(int x, int y, uint pressure = 512, int holdMs = 0)
    {
        // Down
        if (!InjectPen(x, y, POINTER_FLAG_DOWN | POINTER_FLAG_NEW, PEN_FLAG_NONE, pressure))
            return false;

        if (holdMs > 0)
            System.Threading.Thread.Sleep(holdMs);

        // Up
        return InjectPen(x, y, POINTER_FLAG_UP, PEN_FLAG_NONE, 0);
    }

    /// <summary>
    /// Inject multiple touch contacts simultaneously
    /// </summary>
    public static bool InjectMultiTouch(params (int x, int y, uint pointerId, uint flags)[] contacts)
    {
        if (!EnsureTouchInitialized()) return false;

        var touchInfos = new POINTER_TOUCH_INFO[contacts.Length];
        for (int i = 0; i < contacts.Length; i++)
        {
            var (x, y, pointerId, flags) = contacts[i];
            // Only first contact is PRIMARY
            var ptrFlags = flags | POINTER_FLAG_CONFIDENCE;
            if (i == 0) ptrFlags |= POINTER_FLAG_PRIMARY;

            touchInfos[i] = new POINTER_TOUCH_INFO
            {
                pointerInfo = new POINTER_INFO
                {
                    pointerType = PT_TOUCH,
                    pointerId = pointerId,
                    pointerFlags = ptrFlags,
                    ptPixelLocation = new POINT { x = x, y = y }
                },
                touchFlags = 0,
                touchMask = 0,
                rcContact = new RECT { left = x - 2, top = y - 2, right = x + 2, bottom = y + 2 },
                orientation = 0,
                pressure = 512
            };
        }

        return InjectTouchInput((uint)contacts.Length, touchInfos);
    }

    /// <summary>
    /// Simulate pinch-to-zoom gesture
    /// </summary>
    /// <param name="centerX">Center X of the pinch gesture</param>
    /// <param name="centerY">Center Y of the pinch gesture</param>
    /// <param name="startDistance">Initial distance between fingers (pixels)</param>
    /// <param name="endDistance">Final distance between fingers (pixels)</param>
    /// <param name="steps">Number of animation steps (default 20)</param>
    /// <param name="delayMs">Delay between steps in milliseconds (default 0)</param>
    /// <returns>True if successful</returns>
    public static bool PinchZoom(int centerX, int centerY, int startDistance, int endDistance, int steps = 20, int delayMs = 0)
    {
        var id1 = _nextPointerId++;
        var id2 = _nextPointerId++;

        // Calculate start positions (horizontal pinch)
        int halfStart = startDistance / 2;
        int x1Start = centerX - halfStart;
        int x2Start = centerX + halfStart;

        // Down - both fingers
        if (!InjectMultiTouch(
            (x1Start, centerY, id1, POINTER_FLAG_DOWN | POINTER_FLAG_NEW),
            (x2Start, centerY, id2, POINTER_FLAG_DOWN | POINTER_FLAG_NEW)))
            return false;

        // Move - animate the pinch
        for (int i = 1; i <= steps; i++)
        {
            int halfDist = halfStart + (endDistance / 2 - halfStart) * i / steps;
            int x1 = centerX - halfDist;
            int x2 = centerX + halfDist;

            if (!InjectMultiTouch(
                (x1, centerY, id1, POINTER_FLAG_UPDATE),
                (x2, centerY, id2, POINTER_FLAG_UPDATE)))
                return false;

            if (delayMs > 0)
                System.Threading.Thread.Sleep(delayMs);
        }

        // Up - both fingers
        int halfEnd = endDistance / 2;
        return InjectMultiTouch(
            (centerX - halfEnd, centerY, id1, POINTER_FLAG_UP),
            (centerX + halfEnd, centerY, id2, POINTER_FLAG_UP));
    }

    #endregion

    #region Window Targeting

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    /// <summary>
    /// Find window by partial title match
    /// </summary>
    private static IntPtr FindWindowByPartialTitle(string partialTitle)
    {
        IntPtr foundHwnd = IntPtr.Zero;

        EnumWindows((hwnd, lParam) =>
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (!string.IsNullOrEmpty(title) && title.Contains(partialTitle, StringComparison.OrdinalIgnoreCase))
            {
                foundHwnd = hwnd;
                return false; // Stop enumeration
            }
            return true; // Continue enumeration
        }, IntPtr.Zero);

        return foundHwnd;
    }

    /// <summary>
    /// Get window bounds by window title (supports partial match)
    /// </summary>
    public static (int x, int y, int width, int height)? GetWindowBounds(string windowTitle)
    {
        // Try exact match first
        var hwnd = FindWindow(null, windowTitle);

        // If not found, try partial match
        if (hwnd == IntPtr.Zero)
            hwnd = FindWindowByPartialTitle(windowTitle);

        if (hwnd == IntPtr.Zero) return null;

        if (GetWindowRect(hwnd, out RECT rect))
        {
            return (rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
        }
        return null;
    }

    /// <summary>
    /// Focus a window by title (supports partial match)
    /// </summary>
    public static bool FocusWindow(string windowTitle)
    {
        // Try exact match first
        var hwnd = FindWindow(null, windowTitle);

        // If not found, try partial match
        if (hwnd == IntPtr.Zero)
            hwnd = FindWindowByPartialTitle(windowTitle);

        if (hwnd == IntPtr.Zero) return false;
        return SetForegroundWindow(hwnd);
    }

    /// <summary>
    /// Convert window-relative coordinates to screen coordinates
    /// </summary>
    public static (int screenX, int screenY)? WindowToScreen(string windowTitle, int windowX, int windowY)
    {
        var bounds = GetWindowBounds(windowTitle);
        if (bounds == null) return null;
        return (bounds.Value.x + windowX, bounds.Value.y + windowY);
    }

    #endregion

    #region Mouse Input (Direct SendInput - bypasses FlaUI's slow interpolation)

    // ===================================================================================
    // WHY WE USE DIRECT SendInput INSTEAD OF FlaUI's Mouse CLASS
    // ===================================================================================
    //
    // FlaUI's Mouse.MoveTo() has built-in animation that interpolates the mouse path.
    // With 1000 waypoints, FlaUI took 1m48s vs our direct SendInput taking <1 second.
    //
    // THE ROOT CAUSE: DOUBLE INTERPOLATION
    // Our original code was calling Mouse.MoveTo in a loop for each intermediate point:
    //   for (int i = 0; i < steps; i++) { Mouse.MoveTo(intermediatePoint); }
    //
    // But FlaUI ALREADY interpolates each MoveTo call! So we were:
    //   1. Calculating our own intermediate points (the `steps` loop)
    //   2. FlaUI then animated EACH of those with its own interpolation
    //
    // FOR SIMPLE DRAGS (A to B): Could call Mouse.MoveTo(endPoint) ONCE and let FlaUI
    // handle all interpolation at its configured speed. No loop needed.
    //
    // FOR WAYPOINT PATHS: FlaUI can only go straight to each point, so multiple calls
    // are unavoidable. But we could skip our own interpolation and just call MoveTo
    // once per waypoint, letting FlaUI animate between them.
    //
    // FlaUI HAS configurable speed settings (in FlaUI.Core.Input.Mouse):
    //   - Mouse.MovePixelsPerMillisecond (default 0.5) - pixels moved per ms
    //   - Mouse.MovePixelsPerStep (default 10) - pixels per interpolation step
    //
    // FUTURE FIX: Instead of direct SendInput, try:
    //   1. For MouseDrag: Single Mouse.MoveTo(endPoint) call, let FlaUI animate
    //   2. For MouseDragPath: One Mouse.MoveTo per waypoint, no extra interpolation
    //   3. Adjust Mouse.MovePixelsPerMillisecond for desired animation speed
    //
    // FlaUI source: https://github.com/FlaUI/FlaUI/blob/master/src/FlaUI.Core/Input/Mouse.cs
    // The MoveTo method (lines 117-138) calculates duration and steps, then interpolates
    //
    // Our current solution: Direct P/Invoke to user32.dll SendInput for instant positioning
    // This bypasses FlaUI entirely, giving us full control over timing via our delayMs param
    // ===================================================================================

    // P/Invoke for direct mouse input
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const int INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // Cache screen dimensions for coordinate conversion
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    /// <summary>
    /// Move mouse to absolute screen coordinates using direct SendInput (instant, no FlaUI overhead)
    /// </summary>
    private static void DirectMouseMoveTo(int x, int y)
    {
        // Convert screen coordinates to normalized absolute coordinates (0-65535)
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYSCREEN);
        int absoluteX = (x * 65536) / screenWidth;
        int absoluteY = (y * 65536) / screenHeight;

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = absoluteX,
                dy = absoluteY,
                mouseData = 0,
                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Send mouse button down using direct SendInput
    /// </summary>
    private static void DirectMouseDown()
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = 0,
                dwFlags = MOUSEEVENTF_LEFTDOWN,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Send mouse button up using direct SendInput
    /// </summary>
    private static void DirectMouseUp()
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = 0,
                dwFlags = MOUSEEVENTF_LEFTUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Simulate mouse drag from one point to another (works for InkCanvas drawing)
    /// Uses direct SendInput for instant mouse movement without FlaUI overhead.
    /// </summary>
    /// <param name="x1">Start X coordinate</param>
    /// <param name="y1">Start Y coordinate</param>
    /// <param name="x2">End X coordinate</param>
    /// <param name="y2">End Y coordinate</param>
    /// <param name="steps">Number of intermediate points (default 10)</param>
    /// <param name="delayMs">Delay in milliseconds between each step (default 0)</param>
    /// <param name="targetWindow">Optional window to target (not used currently)</param>
    public static bool MouseDrag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 0, string? targetWindow = null)
    {
        try
        {
            // Move to start using direct SendInput (instant, no FlaUI overhead)
            DirectMouseMoveTo(x1, y1);

            // Press left button
            DirectMouseDown();

            // Move in steps
            for (int i = 1; i <= steps; i++)
            {
                int x = x1 + (x2 - x1) * i / steps;
                int y = y1 + (y2 - y1) * i / steps;
                DirectMouseMoveTo(x, y);
                if (delayMs > 0)
                    System.Threading.Thread.Sleep(delayMs);
            }

            // Release
            DirectMouseUp();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Simulate mouse drag through multiple waypoints (for drawing shapes, curves, complex gestures)
    /// Uses direct SendInput for instant mouse movement without FlaUI overhead.
    /// </summary>
    /// <param name="waypoints">Array of (x, y) coordinates to drag through (minimum 2 points)</param>
    /// <param name="stepsPerSegment">Number of interpolation steps between each waypoint (default 1 - no extra interpolation since waypoints define the path)</param>
    /// <param name="delayMs">Delay in milliseconds between steps (default 0)</param>
    /// <returns>Tuple of (success, pointsProcessed, totalSteps) or (false, 0, 0) on error</returns>
    public static (bool success, int pointsProcessed, int totalSteps) MouseDragPath(
        (int x, int y)[] waypoints,
        int stepsPerSegment = 1,
        int delayMs = 0)
    {
        // Input validation
        if (waypoints == null || waypoints.Length < 2)
            return (false, 0, 0);

        if (waypoints.Length > 1000)
            return (false, 0, 0);

        // Validate all coordinates are non-negative
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i].x < 0 || waypoints[i].y < 0)
                return (false, 0, 0);
        }

        if (stepsPerSegment < 1)
            stepsPerSegment = 1;

        if (delayMs < 0)
            delayMs = 0;

        int totalSteps = 0;

        try
        {
            var startPoint = waypoints[0];

            // Move to first point using direct SendInput (instant, no FlaUI overhead)
            DirectMouseMoveTo(startPoint.x, startPoint.y);

            // Press left button
            DirectMouseDown();

            // Iterate through segments
            for (int i = 0; i < waypoints.Length - 1; i++)
            {
                var start = waypoints[i];
                var end = waypoints[i + 1];

                // Interpolate this segment
                for (int step = 1; step <= stepsPerSegment; step++)
                {
                    float t = (float)step / stepsPerSegment;
                    int x = (int)(start.x + (end.x - start.x) * t);
                    int y = (int)(start.y + (end.y - start.y) * t);

                    DirectMouseMoveTo(x, y);
                    totalSteps++;

                    if (delayMs > 0)
                        System.Threading.Thread.Sleep(delayMs);
                }
            }

            // Release mouse button
            DirectMouseUp();

            return (true, waypoints.Length, totalSteps);
        }
        catch
        {
            // Ensure mouse is released on error
            try { DirectMouseUp(); } catch { }
            return (false, 0, 0);
        }
    }

    /// <summary>
    /// Simulate mouse click at coordinates
    /// </summary>
    /// <param name="x">Screen X coordinate</param>
    /// <param name="y">Screen Y coordinate</param>
    /// <param name="doubleClick">Double-click if true (default false)</param>
    /// <param name="delayMs">Delay in milliseconds before click (default 0)</param>
    public static bool MouseClick(int x, int y, bool doubleClick = false, int delayMs = 0)
    {
        try
        {
            var point = new System.Drawing.Point(x, y);
            Mouse.MoveTo(point);

            if (delayMs > 0)
                System.Threading.Thread.Sleep(delayMs);

            if (doubleClick)
                Mouse.DoubleClick(MouseButton.Left);
            else
                Mouse.Click(MouseButton.Left);

            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
