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

    // POINTER_FLAG constants - from https://learn.microsoft.com/en-us/windows/win32/inputmsg/pointer-flags-contants
    public const uint POINTER_FLAG_NONE = 0x00000000;
    public const uint POINTER_FLAG_NEW = 0x00000001;
    public const uint POINTER_FLAG_INRANGE = 0x00000002;
    public const uint POINTER_FLAG_INCONTACT = 0x00000004;
    public const uint POINTER_FLAG_FIRSTBUTTON = 0x00000010;
    public const uint POINTER_FLAG_SECONDBUTTON = 0x00000020;
    public const uint POINTER_FLAG_THIRDBUTTON = 0x00000040;
    public const uint POINTER_FLAG_FOURTHBUTTON = 0x00000080;
    public const uint POINTER_FLAG_FIFTHBUTTON = 0x00000100;
    public const uint POINTER_FLAG_PRIMARY = 0x00002000;
    public const uint POINTER_FLAG_CONFIDENCE = 0x00004000;
    public const uint POINTER_FLAG_CANCELED = 0x00008000;
    public const uint POINTER_FLAG_DOWN = 0x00010000;
    public const uint POINTER_FLAG_UPDATE = 0x00020000;
    public const uint POINTER_FLAG_UP = 0x00040000;
    public const uint POINTER_FLAG_WHEEL = 0x00080000;
    public const uint POINTER_FLAG_HWHEEL = 0x00100000;
    public const uint POINTER_FLAG_CAPTURECHANGED = 0x00200000;
    public const uint POINTER_FLAG_HASTRANSFORM = 0x00400000;

    public const uint PT_TOUCH = 0x00000002;
    public const uint PT_PEN = 0x00000003;

    // POINTER_INFO - Sequential layout lets marshaller handle IntPtr sizing automatically
    // This works correctly for both 32-bit (IntPtr=4 bytes) and 64-bit (IntPtr=8 bytes)
    // Windows SDK: https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-pointer_info
    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_INFO
    {
        public int pointerType;
        public uint pointerId;
        public uint frameId;
        public int pointerFlags;
        // Marshaller automatically sizes these as 4 or 8 bytes based on process bitness
        public IntPtr sourceDevice;
        public IntPtr hwndTarget;
        // Marshaller packs these at correct offset (24 for x86, 32 for x64)
        public POINT ptPixelLocation;
        public POINT ptHimetricLocation;
        public POINT ptPixelLocationRaw;
        public POINT ptHimetricLocationRaw;
        public uint dwTime;
        public uint historyCount;
        public int inputData;
        public uint dwKeyStates;
        public ulong performanceCount;
        public int buttonChangeType;
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

    // Version for deployment verification - increment when making changes
    public const string PEN_INJECTION_VERSION = "v10.0-hwndTarget-noPremult";

    #region Legacy Touch Injection (uses system touch device, not synthetic)

    private static bool _legacyTouchInitialized = false;

    /// <summary>
    /// Initialize legacy touch injection (Windows 8+ API).
    /// This API uses the system's touch device (VIRTUAL_DIGITIZER) instead of
    /// creating a synthetic device, which may have proper device rects.
    /// </summary>
    public static bool InitializeLegacyTouch(uint maxContacts = 10)
    {
        if (_legacyTouchInitialized) return true;

        // TOUCH_FEEDBACK_INDIRECT = 2 - shows feedback at injected location
        _legacyTouchInitialized = InitializeTouchInjection(maxContacts, TOUCH_FEEDBACK_INDIRECT);

        var logPath = GetDebugLogPath();
        System.IO.File.AppendAllText(logPath,
            _legacyTouchInitialized
                ? $"[LEGACY TOUCH] Initialized with {maxContacts} contacts\n"
                : $"[LEGACY TOUCH] InitializeTouchInjection FAILED: error={Marshal.GetLastWin32Error()}\n");

        return _legacyTouchInitialized;
    }

    // Touch mask constants for legacy API
    public const uint TOUCH_MASK_NONE = 0x00000000;
    public const uint TOUCH_MASK_CONTACTAREA = 0x00000001;
    public const uint TOUCH_MASK_ORIENTATION = 0x00000002;
    public const uint TOUCH_MASK_PRESSURE = 0x00000004;

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    /// <summary>
    /// Inject touch using legacy API (Windows 8+).
    /// Uses InjectTouchInput which routes through system touch device.
    /// </summary>
    public static bool InjectLegacyTouch(int x, int y, uint pointerId, uint flags)
    {
        if (!InitializeLegacyTouch()) return false;

        var pixelPoint = new POINT { x = x, y = y };
        var himetricPoint = PixelToHimetric(x, y);
        var tickCount = GetTickCount();

        // Legacy API requires touchMask to indicate which fields are valid
        // Per Microsoft sample: TOUCH_MASK_CONTACTAREA | TOUCH_MASK_ORIENTATION | TOUCH_MASK_PRESSURE
        var contact = new POINTER_TOUCH_INFO
        {
            pointerInfo = new POINTER_INFO
            {
                pointerType = (int)PT_TOUCH,
                pointerId = pointerId,
                pointerFlags = (int)(flags | POINTER_FLAG_CONFIDENCE),
                ptPixelLocation = pixelPoint,
                ptHimetricLocation = himetricPoint,
                ptPixelLocationRaw = pixelPoint,
                ptHimetricLocationRaw = himetricPoint,
                dwTime = tickCount  // MS sample sets this
            },
            touchFlags = 0,
            touchMask = TOUCH_MASK_CONTACTAREA | TOUCH_MASK_ORIENTATION | TOUCH_MASK_PRESSURE,
            rcContact = new RECT { left = x - 2, top = y - 2, right = x + 2, bottom = y + 2 },
            rcContactRaw = new RECT { left = x - 2, top = y - 2, right = x + 2, bottom = y + 2 },
            orientation = 90,  // Perpendicular to screen
            pressure = 32000   // Standard pressure value per MS sample
        };

        // Log struct sizes for debugging
        var logPath = GetDebugLogPath();
        System.IO.File.AppendAllText(logPath,
            $"[LEGACY] Struct sizes: POINTER_INFO={Marshal.SizeOf<POINTER_INFO>()}, POINTER_TOUCH_INFO={Marshal.SizeOf<POINTER_TOUCH_INFO>()}\n" +
            $"[LEGACY] Input: pixel=({x},{y}) himetric=({himetricPoint.x},{himetricPoint.y}) flags=0x{flags:X} id={pointerId} tick={tickCount}\n");

        return InjectTouchInput(1, new[] { contact });
    }

    /// <summary>
    /// Test legacy touch tap - uses system touch device instead of synthetic
    /// </summary>
    public static bool LegacyTouchTap(int x, int y, int holdMs = 0)
    {
        var id = _nextPointerId++;

        var logPath = GetDebugLogPath();
        System.IO.File.AppendAllText(logPath, $"[LEGACY TOUCH] Tap at ({x},{y}) with id={id}\n");

        uint downFlags = POINTER_FLAG_NEW | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_DOWN | POINTER_FLAG_PRIMARY;
        uint updateFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_UPDATE | POINTER_FLAG_PRIMARY;
        uint upFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_UP | POINTER_FLAG_PRIMARY;

        // Down with 1px offset
        if (!InjectLegacyTouch(x - 1, y, id, downFlags))
        {
            System.IO.File.AppendAllText(logPath, $"[LEGACY TOUCH] DOWN failed: error={Marshal.GetLastWin32Error()}\n");
            return false;
        }

        System.Threading.Thread.Sleep(1);

        // Update to actual position
        if (!InjectLegacyTouch(x, y, id, updateFlags))
        {
            System.IO.File.AppendAllText(logPath, $"[LEGACY TOUCH] UPDATE failed: error={Marshal.GetLastWin32Error()}\n");
            return false;
        }

        if (holdMs > 0)
            System.Threading.Thread.Sleep(holdMs);

        System.Threading.Thread.Sleep(1);

        // Up
        if (!InjectLegacyTouch(x, y, id, upFlags))
        {
            System.IO.File.AppendAllText(logPath, $"[LEGACY TOUCH] UP failed: error={Marshal.GetLastWin32Error()}\n");
            return false;
        }

        System.IO.File.AppendAllText(logPath, $"[LEGACY TOUCH] Tap completed successfully\n");
        return true;
    }

    #endregion

    /// <summary>
    /// Get debug log path that works in both sandbox and host environments.
    /// Sandbox: C:\Shared\pen-debug.log (if exists)
    /// Host: temp folder
    /// </summary>
    private static string GetDebugLogPath()
    {
        // Try sandbox shared folder first
        const string sandboxPath = @"C:\Shared";
        if (System.IO.Directory.Exists(sandboxPath))
            return System.IO.Path.Combine(sandboxPath, "pen-debug.log");

        // Fall back to temp folder
        return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pen-debug.log");
    }

    /// <summary>
    /// Log key fields with byte offsets for debugging struct layout
    /// </summary>
    private static void LogStructLayout<T>(T obj, string label) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(obj, ptr, false);
            byte[] bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, size);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[STRUCT] {label} - Total size: {size} bytes");

            // Show bytes in 16-byte rows with offsets
            for (int i = 0; i < bytes.Length; i += 16)
            {
                sb.Append($"  [{i,3}] ");
                for (int j = 0; j < 16 && i + j < bytes.Length; j++)
                {
                    sb.Append($"{bytes[i + j]:X2} ");
                }
                sb.AppendLine();
            }

            System.IO.File.AppendAllText(GetDebugLogPath(), sb.ToString());
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    // DPI for HIMETRIC conversion (1 inch = 2540 HIMETRIC units)
    // Formula: Himetric = (Pixel * 2540) / DPI
    private const double HIMETRIC_PER_INCH = 2540.0;

    // DPI querying APIs - critical for correct HIMETRIC calculation
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    // Virtual screen metrics for coordinate origin
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int LOGPIXELSX = 88;  // DPI in X direction
    private const int LOGPIXELSY = 90;  // DPI in Y direction
    private const int SM_XVIRTUALSCREEN = 76;  // Virtual screen left edge
    private const int SM_YVIRTUALSCREEN = 77;  // Virtual screen top edge

    // Cached DPI values (queried once at startup, or per-injection)
    private static int _cachedDpiX = 0;
    private static int _cachedDpiY = 0;

    /// <summary>
    /// Query the actual system DPI at runtime.
    /// Critical for Windows Sandbox and high-DPI displays.
    /// </summary>
    private static (int dpiX, int dpiY) GetSystemDpi()
    {
        if (_cachedDpiX > 0 && _cachedDpiY > 0)
            return (_cachedDpiX, _cachedDpiY);

        IntPtr hdc = GetDC(IntPtr.Zero);
        try
        {
            _cachedDpiX = GetDeviceCaps(hdc, LOGPIXELSX);
            _cachedDpiY = GetDeviceCaps(hdc, LOGPIXELSY);

            // Fallback to 96 if query fails
            if (_cachedDpiX <= 0) _cachedDpiX = 96;
            if (_cachedDpiY <= 0) _cachedDpiY = 96;

            // Log the detected DPI
            System.IO.File.AppendAllText(GetDebugLogPath(),
                $"[DPI] Detected system DPI: {_cachedDpiX}x{_cachedDpiY}\n");

            return (_cachedDpiX, _cachedDpiY);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    /// <summary>
    /// Get virtual screen origin offset.
    /// In Windows Sandbox or multi-monitor setups, this may be non-zero.
    /// </summary>
    private static (int offsetX, int offsetY) GetVirtualScreenOrigin()
    {
        int offsetX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int offsetY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        return (offsetX, offsetY);
    }

    /// <summary>
    /// Convert pixel coordinates to HIMETRIC units using DYNAMIC DPI.
    /// This is critical for WPF EnablePointerSupport to receive correct coordinates.
    /// HIMETRIC = (pixel * 2540) / DPI
    /// </summary>
    private static POINT PixelToHimetric(int x, int y)
    {
        var (dpiX, dpiY) = GetSystemDpi();
        return new POINT
        {
            x = (int)((x * HIMETRIC_PER_INCH) / dpiX),
            y = (int)((y * HIMETRIC_PER_INCH) / dpiY)
        };
    }

    /// <summary>
    /// Force re-query of DPI on next conversion (useful if display settings change)
    /// </summary>
    public static void InvalidateDpiCache()
    {
        _cachedDpiX = 0;
        _cachedDpiY = 0;
    }

    #region Pen Injection (Windows 10 1809+ Synthetic Pointer API)

    // CreateSyntheticPointerDevice - creates pen/touch injection device
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateSyntheticPointerDevice(uint pointerType, uint maxCount, uint mode);

    // GetPointerDeviceRects - query device coordinate space
    // This tells us what coordinate range the device reports to WPF
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetPointerDeviceRects(IntPtr device, out RECT pointerDeviceRect, out RECT displayRect);

    // GetPointerDevices - enumerate all pointer devices
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetPointerDevices(ref uint deviceCount, [Out] POINTER_DEVICE_INFO[]? pointerDevices);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct POINTER_DEVICE_INFO
    {
        public uint displayOrientation;
        public IntPtr device;
        public uint pointerDeviceType;
        public IntPtr monitor;
        public uint startingCursorId;
        public ushort maxActiveContacts;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 520)]
        public string productString;
    }

    // InjectSyntheticPointerInput - injects pen/touch input
    // Separate overloads for pen and touch since they use different struct types
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InjectSyntheticPointerInput(IntPtr device, [MarshalAs(UnmanagedType.LPArray)] POINTER_TYPE_INFO_PEN[] pointerInfo, uint count);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InjectSyntheticPointerInput(IntPtr device, [MarshalAs(UnmanagedType.LPArray)] POINTER_TYPE_INFO_TOUCH[] pointerInfo, uint count);

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
        public int penFlags;
        public int penMask;
        public uint pressure;      // 0-1024
        public uint rotation;      // 0-359
        public int tiltX;          // -90 to +90
        public int tiltY;          // -90 to +90
    }

    // POINTER_TYPE_INFO - Explicit layout with 8-byte alignment for nested struct
    // v4.4: Gemini recommendation - on x64, nested struct with IntPtr needs 8-byte alignment
    // type (4 bytes) at offset 0, penInfo at offset 8 (not 4!) for proper alignment
    // Windows expects this alignment for InjectSyntheticPointerInput to work correctly
    [StructLayout(LayoutKind.Explicit)]
    public struct POINTER_TYPE_INFO_PEN
    {
        [FieldOffset(0)]
        public uint type;  // PT_PEN = 3
        [FieldOffset(8)]  // 8-byte alignment for nested struct containing IntPtr
        public POINTER_PEN_INFO penInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct POINTER_TYPE_INFO_TOUCH
    {
        [FieldOffset(0)]
        public uint type;  // PT_TOUCH = 2
        [FieldOffset(8)]  // 8-byte alignment for nested struct containing IntPtr
        public POINTER_TOUCH_INFO touchInfo;
    }

    #endregion

    #region Helper Methods

    private static IntPtr _touchDevice = IntPtr.Zero;
    private static IntPtr _penDevice = IntPtr.Zero;
    private static uint _nextPointerId = 1;
    private static uint _frameId = 0;  // Increment with each injection

    /// <summary>
    /// Initialize touch injection using Synthetic Pointer API (Windows 10 1809+).
    /// This works without touch hardware - creates a virtual touch device.
    /// </summary>
    public static bool EnsureTouchInitialized(uint maxContacts = 10)
    {
        if (_touchDevice != IntPtr.Zero) return true;

        // Log devices BEFORE creating synthetic device
        LogAllPointerDevices("BEFORE touch device creation");

        _touchDevice = CreateSyntheticPointerDevice(PT_TOUCH, maxContacts, POINTER_FEEDBACK_DEFAULT);
        var error = Marshal.GetLastWin32Error();

        var logMsg = _touchDevice != IntPtr.Zero
            ? $"[TOUCH {PEN_INJECTION_VERSION}] Synthetic touch device created: 0x{_touchDevice:X} (maxContacts={maxContacts})"
            : $"[TOUCH {PEN_INJECTION_VERSION}] CreateSyntheticPointerDevice(PT_TOUCH) failed, error={error}";
        System.IO.File.AppendAllText(GetDebugLogPath(), logMsg + "\n");

        // Log devices AFTER creating synthetic device - see if it appears and what type
        if (_touchDevice != IntPtr.Zero)
        {
            QueryAndLogDeviceRects(_touchDevice, "Synthetic TOUCH");
            LogAllPointerDevices("AFTER touch device creation");
        }

        return _touchDevice != IntPtr.Zero;
    }

    /// <summary>
    /// Initialize pen injection using Synthetic Pointer API (Windows 10 1809+)
    /// </summary>
    public static bool EnsurePenInitialized()
    {
        if (_penDevice != IntPtr.Zero) return true;

        // Log devices BEFORE creating synthetic device
        LogAllPointerDevices("BEFORE pen device creation");

        _penDevice = CreateSyntheticPointerDevice(PT_PEN, 1, POINTER_FEEDBACK_DEFAULT);
        var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        var bitness = Environment.Is64BitProcess ? "x64" : "x86";
        var ptrSize = IntPtr.Size;
        var infoSize = Marshal.SizeOf<POINTER_INFO>();
        var logMsg = _penDevice != IntPtr.Zero
            ? $"[PEN {PEN_INJECTION_VERSION}] Device created: 0x{_penDevice:X} ({bitness}, IntPtr={ptrSize}, POINTER_INFO={infoSize})"
            : $"[PEN {PEN_INJECTION_VERSION}] CreateSyntheticPointerDevice failed, error={error} ({bitness})";
        System.IO.File.AppendAllText(GetDebugLogPath(), logMsg + "\n");

        // Query the synthetic device's coordinate space - CRITICAL for understanding WPF transformation
        if (_penDevice != IntPtr.Zero)
        {
            QueryAndLogDeviceRects(_penDevice, "Synthetic PEN");
            LogAllPointerDevices("AFTER pen device creation");
        }

        return _penDevice != IntPtr.Zero;
    }

    /// <summary>
    /// Query and log the coordinate space reported by a pointer device.
    /// This is critical for understanding how WPF transforms coordinates.
    /// </summary>
    private static void QueryAndLogDeviceRects(IntPtr device, string deviceName)
    {
        try
        {
            if (GetPointerDeviceRects(device, out RECT deviceRect, out RECT displayRect))
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[DEVICE RECTS] {deviceName} (handle=0x{device:X}):");
                sb.AppendLine($"  pointerDeviceRect: ({deviceRect.left},{deviceRect.top}) - ({deviceRect.right},{deviceRect.bottom})");
                sb.AppendLine($"    Width: {deviceRect.right - deviceRect.left}, Height: {deviceRect.bottom - deviceRect.top}");
                sb.AppendLine($"  displayRect: ({displayRect.left},{displayRect.top}) - ({displayRect.right},{displayRect.bottom})");
                sb.AppendLine($"    Width: {displayRect.right - displayRect.left}, Height: {displayRect.bottom - displayRect.top}");

                // Calculate scaling factor that WPF might be using
                double scaleX = (double)(deviceRect.right - deviceRect.left) / (displayRect.right - displayRect.left);
                double scaleY = (double)(deviceRect.bottom - deviceRect.top) / (displayRect.bottom - displayRect.top);
                sb.AppendLine($"  Device/Display ratio: {scaleX:F2}x, {scaleY:F2}x");

                System.IO.File.AppendAllText(GetDebugLogPath(), sb.ToString());
            }
            else
            {
                var err = Marshal.GetLastWin32Error();
                System.IO.File.AppendAllText(GetDebugLogPath(),
                    $"[DEVICE RECTS] GetPointerDeviceRects failed for {deviceName}: error={err}\n");
            }
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(GetDebugLogPath(),
                $"[DEVICE RECTS] Exception querying {deviceName}: {ex.Message}\n");
        }
    }

    // POINTER_DEVICE_TYPE enum values (from winuser.h)
    public const uint POINTER_DEVICE_TYPE_INTEGRATED_PEN = 0x00000001;
    public const uint POINTER_DEVICE_TYPE_EXTERNAL_PEN = 0x00000002;
    public const uint POINTER_DEVICE_TYPE_TOUCH = 0x00000003;
    public const uint POINTER_DEVICE_TYPE_TOUCH_PAD = 0x00000004;
    public const uint POINTER_DEVICE_TYPE_MAX = 0xFFFFFFFF;

    /// <summary>
    /// Enumerate all pointer devices and log their types.
    /// This helps diagnose how Windows reports synthetic devices to WPF.
    /// </summary>
    public static void LogAllPointerDevices(string context)
    {
        try
        {
            uint deviceCount = 0;
            // First call with null array to get count
            if (!GetPointerDevices(ref deviceCount, null))
            {
                var err = Marshal.GetLastWin32Error();
                System.IO.File.AppendAllText(GetDebugLogPath(),
                    $"[POINTER DEVICES] {context}: GetPointerDevices count query failed, error={err}\n");
                return;
            }

            if (deviceCount == 0)
            {
                System.IO.File.AppendAllText(GetDebugLogPath(),
                    $"[POINTER DEVICES] {context}: No pointer devices found\n");
                return;
            }

            // Second call to get device info
            var devices = new POINTER_DEVICE_INFO[deviceCount];
            if (!GetPointerDevices(ref deviceCount, devices))
            {
                var err = Marshal.GetLastWin32Error();
                System.IO.File.AppendAllText(GetDebugLogPath(),
                    $"[POINTER DEVICES] {context}: GetPointerDevices info query failed, error={err}\n");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[POINTER DEVICES] {context}: Found {deviceCount} pointer device(s):");

            for (int i = 0; i < deviceCount; i++)
            {
                var dev = devices[i];
                string typeName = dev.pointerDeviceType switch
                {
                    POINTER_DEVICE_TYPE_INTEGRATED_PEN => "INTEGRATED_PEN",
                    POINTER_DEVICE_TYPE_EXTERNAL_PEN => "EXTERNAL_PEN",
                    POINTER_DEVICE_TYPE_TOUCH => "TOUCH",
                    POINTER_DEVICE_TYPE_TOUCH_PAD => "TOUCH_PAD",
                    _ => $"UNKNOWN({dev.pointerDeviceType})"
                };

                sb.AppendLine($"  [{i}] Type={typeName} Handle=0x{dev.device:X} MaxContacts={dev.maxActiveContacts}");
                sb.AppendLine($"      Product=\"{dev.productString}\"");
                sb.AppendLine($"      Monitor=0x{dev.monitor:X} StartCursorId={dev.startingCursorId} Orientation={dev.displayOrientation}");

                // Also query device rects for each
                if (dev.device != IntPtr.Zero && GetPointerDeviceRects(dev.device, out RECT deviceRect, out RECT displayRect))
                {
                    sb.AppendLine($"      DeviceRect=({deviceRect.left},{deviceRect.top})-({deviceRect.right},{deviceRect.bottom})");
                    sb.AppendLine($"      DisplayRect=({displayRect.left},{displayRect.top})-({displayRect.right},{displayRect.bottom})");
                }
            }

            System.IO.File.AppendAllText(GetDebugLogPath(), sb.ToString());
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(GetDebugLogPath(),
                $"[POINTER DEVICES] {context}: Exception: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Cleanup touch device
    /// </summary>
    public static void CleanupTouchDevice()
    {
        if (_touchDevice != IntPtr.Zero)
        {
            DestroySyntheticPointerDevice(_touchDevice);
            _touchDevice = IntPtr.Zero;
        }
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
    /// Inject a single touch point using Synthetic Pointer API (down, move, or up)
    /// </summary>
    public static bool InjectTouch(int x, int y, uint pointerId, uint flags)
    {
        if (!EnsureTouchInitialized()) return false;

        // Both ptPixelLocation and ptPixelLocationRaw should be set to the screen coordinates
        var pixelPoint = new POINT { x = x, y = y };

        // CRITICAL: WPF with EnablePointerSupport reads ptHimetricLocation for high-precision coordinates
        var himetricPoint = PixelToHimetric(x, y);

        var typeInfo = new POINTER_TYPE_INFO_TOUCH
        {
            type = PT_TOUCH,
            touchInfo = new POINTER_TOUCH_INFO
            {
                pointerInfo = new POINTER_INFO
                {
                    pointerType = (int)PT_TOUCH,
                    pointerId = pointerId,
                    pointerFlags = (int)(flags | POINTER_FLAG_CONFIDENCE),
                    // v4.3: Remove explicit sourceDevice/hwndTarget - match original working version
                    ptPixelLocation = pixelPoint,
                    ptHimetricLocation = himetricPoint,
                    ptPixelLocationRaw = pixelPoint,
                    ptHimetricLocationRaw = himetricPoint
                },
                touchFlags = 0,
                touchMask = 0,
                rcContact = new RECT { left = x - 2, top = y - 2, right = x + 2, bottom = y + 2 },
                rcContactRaw = new RECT { left = x - 2, top = y - 2, right = x + 2, bottom = y + 2 },
                orientation = 0,
                pressure = 512
            }
        };

        return InjectSyntheticPointerInput(_touchDevice, new[] { typeInfo }, 1);
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

        // IMPORTANT: Windows has a "press and hold to right-click" gesture.
        // To prevent this, we must complete the full gesture quickly:
        // DOWN -> immediate UPDATE (wiggle) -> immediate UP
        //
        // Flag requirements for WPF Manipulation events (per Gemini analysis):
        // - NEW on DOWN: Required for Direct Manipulation engine to initialize
        // - CONFIDENCE on all: Required for WPF to treat as intentional touch
        // - PRIMARY on all: Marks this as the main contact point
        // Note: CONFIDENCE is added by InjectTouch automatically

        uint downFlags = POINTER_FLAG_NEW | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_DOWN | POINTER_FLAG_PRIMARY;
        uint updateFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_UPDATE | POINTER_FLAG_PRIMARY;
        uint upFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_UP | POINTER_FLAG_PRIMARY;

        // Down - finger makes contact with 1px offset
        if (!InjectTouch(x - 1, y, id, downFlags))
            return false;

        // Brief pause for event processing
        System.Threading.Thread.Sleep(1);

        // Update to actual position (the movement cancels hold gesture)
        if (!InjectTouch(x, y, id, updateFlags))
            return false;

        if (holdMs > 0)
        {
            // If holding, continue to wiggle to prevent right-click
            for (int i = 0; i < holdMs / 50; i++)
            {
                System.Threading.Thread.Sleep(50);
                int wiggleX = x + (i % 2);
                if (!InjectTouch(wiggleX, y, id, updateFlags))
                    return false;
            }
        }

        // Brief pause before UP
        System.Threading.Thread.Sleep(1);

        // Up - finger lifts (completes the gesture before hold timer)
        return InjectTouch(x, y, id, upFlags);
    }

    /// <summary>
    /// Simulate a touch drag from one point to another
    /// </summary>
    public static bool TouchDrag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 5)
    {
        var id = _nextPointerId++;

        // Flag requirements for WPF Manipulation events (per Gemini analysis):
        // - NEW on DOWN: Required for Direct Manipulation engine to initialize
        // - CONFIDENCE on all: Required for WPF to treat as intentional touch (added by InjectTouch)
        // - PRIMARY on all: Marks this as the main contact point
        //
        // Also starts with 1px offset and immediate move to cancel "press and hold to right-click" gesture.

        // Down - finger makes contact with 1px offset
        uint downFlags = POINTER_FLAG_NEW | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_DOWN | POINTER_FLAG_PRIMARY;
        if (!InjectTouch(x1 - 1, y1, id, downFlags))
            return false;

        // Immediately update to actual position (cancels hold gesture)
        uint updateFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_UPDATE | POINTER_FLAG_PRIMARY;
        if (!InjectTouch(x1, y1, id, updateFlags))
            return false;

        // Move - finger in contact, updating position
        for (int i = 1; i <= steps; i++)
        {
            int x = x1 + (x2 - x1) * i / steps;
            int y = y1 + (y2 - y1) * i / steps;
            if (!InjectTouch(x, y, id, updateFlags))
                return false;
            if (delayMs > 0)
                System.Threading.Thread.Sleep(delayMs);
        }

        // Up - finger lifts
        uint upFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_UP | POINTER_FLAG_PRIMARY;
        return InjectTouch(x2, y2, id, upFlags);
    }

    /// <summary>
    /// Inject a pen stroke with pressure using Synthetic Pointer API
    /// </summary>
    /// <param name="x">Screen X coordinate</param>
    /// <param name="y">Screen Y coordinate</param>
    /// <param name="pointerId">Pointer ID (assigned by caller)</param>
    /// <param name="flags">Pointer flags (DOWN, UPDATE, UP)</param>
    /// <param name="penFlags">Pen-specific flags (ERASER, etc.)</param>
    /// <param name="pressure">Pen pressure 0-1024</param>
    /// <param name="tiltX">Pen tilt X (-90 to +90)</param>
    /// <param name="tiltY">Pen tilt Y (-90 to +90)</param>
    /// <param name="hwndTarget">Target window handle (if IntPtr.Zero, uses window at coordinates)</param>
    // Standard tablet digitizer coordinate range (common for Wacom, etc.)
    private const int TABLET_COORD_MAX = 32767;

    public static bool InjectPen(int x, int y, uint pointerId, uint flags, uint penFlags = PEN_FLAG_NONE, uint pressure = 512, int tiltX = 0, int tiltY = 0, IntPtr hwndTarget = default)
    {
        if (!EnsurePenInitialized()) return false;

        // Increment frame ID for each injection (required by some implementations)
        _frameId++;

        // v10.0: Test hwndTarget WITHOUT pre-multiplication.
        // v9.0 showed that pre-multiplied coords cause events to not reach the window,
        // even though InjectSyntheticPointerInput returns success.
        //
        // This test: use hwndTarget but keep normal pixel coordinates.
        // Goal: Determine if hwndTarget itself causes the problem or if it's the coords.
        var screenWidth = GetSystemMetrics(0);   // SM_CXSCREEN
        var screenHeight = GetSystemMetrics(1);  // SM_CYSCREEN

        // Use original pixel coordinates (NO pre-multiplication)
        var pixelPoint = new POINT { x = x, y = y };
        var himetricPoint = PixelToHimetric(x, y);

        // v10.0: Standard flags
        uint finalFlags = flags | POINTER_FLAG_CONFIDENCE;

        var typeInfo = new POINTER_TYPE_INFO_PEN
        {
            type = PT_PEN,
            penInfo = new POINTER_PEN_INFO
            {
                pointerInfo = new POINTER_INFO
                {
                    pointerType = (int)PT_PEN,
                    pointerId = pointerId,
                    pointerFlags = (int)finalFlags,
                    // v10.0: Set hwndTarget but use normal pixel coords
                    hwndTarget = hwndTarget,
                    // v10.0: Use NORMAL pixel coords (no pre-multiplication)
                    ptPixelLocation = pixelPoint,
                    ptHimetricLocation = himetricPoint,
                    ptPixelLocationRaw = pixelPoint,
                    ptHimetricLocationRaw = himetricPoint
                },
                penFlags = (int)penFlags,
                penMask = (int)(PEN_MASK_PRESSURE | PEN_MASK_TILT_X | PEN_MASK_TILT_Y),
                pressure = pressure,
                rotation = 0,
                tiltX = tiltX,
                tiltY = tiltY
            }
        };

        // Log struct layout on first DOWN event to debug coordinate issues
        if ((flags & POINTER_FLAG_DOWN) != 0)
        {
            LogStructLayout(typeInfo, $"POINTER_TYPE_INFO_PEN at ({x},{y})");
            var (dpiX, dpiY) = GetSystemDpi();
            var (vsOffsetX, vsOffsetY) = GetVirtualScreenOrigin();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[FIELDS] Values (v10.0 - hwndTarget, NO premultiply):");
            sb.AppendLine($"  Screen: {screenWidth}x{screenHeight}");
            sb.AppendLine($"  SYSTEM DPI: {dpiX}x{dpiY}");
            sb.AppendLine($"  Virtual Screen Origin: ({vsOffsetX}, {vsOffsetY})");
            sb.AppendLine($"  hwndTarget: 0x{hwndTarget:X}");
            sb.AppendLine($"  Input pixel: ({x}, {y})");
            sb.AppendLine($"  ptPixelLocation: ({x}, {y})  [NORMAL COORDS]");
            sb.AppendLine($"  ptHimetricLocation: ({himetricPoint.x}, {himetricPoint.y})");
            sb.AppendLine($"  Flags: 0x{finalFlags:X}");
            System.IO.File.AppendAllText(GetDebugLogPath(), sb.ToString());
        }

        var result = InjectSyntheticPointerInput(_penDevice, new[] { typeInfo }, 1);
        var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        try
        {
            var logMsg = result
                ? $"[PEN] OK at ({x},{y}) ID={pointerId} flags=0x{flags:X} hwnd=0x{hwndTarget:X}"
                : $"[PEN] FAIL at ({x},{y}) ID={pointerId} flags=0x{flags:X} hwnd=0x{hwndTarget:X} error={error}";
            System.IO.File.AppendAllText(GetDebugLogPath(), logMsg + "\n");
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(GetDebugLogPath(), $"Log error: {ex.Message}\n");
        }
        return result;
    }

    /// <summary>
    /// Simulate a pen stroke from one point to another
    /// </summary>
    /// <param name="x1">Start X screen coordinate</param>
    /// <param name="y1">Start Y screen coordinate</param>
    /// <param name="x2">End X screen coordinate</param>
    /// <param name="y2">End Y screen coordinate</param>
    /// <param name="steps">Number of interpolation steps</param>
    /// <param name="pressure">Pen pressure 0-1024</param>
    /// <param name="eraser">Use eraser end of pen</param>
    /// <param name="delayMs">Delay between steps in ms</param>
    /// <param name="hwndTarget">Target window handle (if IntPtr.Zero, uses window at coordinates)</param>
    public static bool PenStroke(int x1, int y1, int x2, int y2, int steps = 20, uint pressure = 512, bool eraser = false, int delayMs = 2, IntPtr hwndTarget = default)
    {
        uint penFlags = eraser ? PEN_FLAG_INVERTED : PEN_FLAG_NONE;
        uint pointerId = _nextPointerId++;

        // Flag combinations per Gemini suggestions for WPF InkCanvas:
        // - INCONTACT on DOWN is critical for WPF to start a stroke
        // - PRIMARY flag marks this as the main pointer
        // DOWN: INRANGE | INCONTACT | DOWN | PRIMARY
        // MOVE: INRANGE | INCONTACT | UPDATE | PRIMARY
        // UP: INRANGE | UP

        // Down - pen makes initial contact (INCONTACT is required for WPF!)
        uint downFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_DOWN | POINTER_FLAG_PRIMARY;
        if (!InjectPen(x1, y1, pointerId, downFlags, penFlags, pressure, 0, 0, hwndTarget))
            return false;

        // Contact - pen in contact, updating position
        uint contactFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_UPDATE | POINTER_FLAG_PRIMARY;
        for (int i = 1; i <= steps; i++)
        {
            int x = x1 + (x2 - x1) * i / steps;
            int y = y1 + (y2 - y1) * i / steps;
            if (!InjectPen(x, y, pointerId, contactFlags, penFlags, pressure, 0, 0, hwndTarget))
                return false;
            if (delayMs > 0)
                System.Threading.Thread.Sleep(delayMs);
        }

        // Up - pen lifts
        uint upFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_UP;
        return InjectPen(x2, y2, pointerId, upFlags, penFlags, 0, 0, 0, hwndTarget);
    }

    /// <summary>
    /// Simulate pen tap (like clicking with pen tip)
    /// </summary>
    /// <param name="x">Screen X coordinate</param>
    /// <param name="y">Screen Y coordinate</param>
    /// <param name="pressure">Pen pressure 0-1024 (default 512)</param>
    /// <param name="holdMs">Milliseconds to hold before release (default 0)</param>
    /// <param name="hwndTarget">Target window handle (if IntPtr.Zero, uses window at coordinates)</param>
    public static bool PenTap(int x, int y, uint pressure = 512, int holdMs = 0, IntPtr hwndTarget = default)
    {
        // IMPORTANT: Windows has a "press and hold to right-click" gesture.
        // To prevent this, we must complete the full gesture quickly:
        // DOWN -> immediate UPDATE (wiggle) -> immediate UP
        // The rapid sequence prevents the hold timer from firing.

        uint pointerId = _nextPointerId++;
        uint downFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_DOWN | POINTER_FLAG_PRIMARY | POINTER_FLAG_FIRSTBUTTON;
        uint updateFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_UPDATE | POINTER_FLAG_FIRSTBUTTON;
        uint upFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_UP;

        // Down - pen makes contact with 1px offset
        if (!InjectPen(x - 1, y, pointerId, downFlags, PEN_FLAG_NONE, pressure, 0, 0, hwndTarget))
            return false;

        // Brief pause for event processing
        System.Threading.Thread.Sleep(1);

        // Update to actual position (the movement cancels hold gesture)
        if (!InjectPen(x, y, pointerId, updateFlags, PEN_FLAG_NONE, pressure, 0, 0, hwndTarget))
            return false;

        if (holdMs > 0)
        {
            // If holding, continue to wiggle to prevent right-click
            for (int i = 0; i < holdMs / 50; i++)
            {
                System.Threading.Thread.Sleep(50);
                int wiggleX = x + (i % 2);
                if (!InjectPen(wiggleX, y, pointerId, updateFlags, PEN_FLAG_NONE, pressure, 0, 0, hwndTarget))
                    return false;
            }
        }

        // Brief pause before UP
        System.Threading.Thread.Sleep(1);

        // Up - pen lifts (completes the gesture before hold timer)
        return InjectPen(x, y, pointerId, upFlags, PEN_FLAG_NONE, 0, 0, 0, hwndTarget);
    }

    /// <summary>
    /// Inject multiple touch contacts simultaneously using Synthetic Pointer API
    /// </summary>
    public static bool InjectMultiTouch(params (int x, int y, uint pointerId, uint flags)[] contacts)
    {
        if (!EnsureTouchInitialized()) return false;

        var logPath = GetDebugLogPath();
        var logDetails = new System.Text.StringBuilder();
        logDetails.Append($"[MULTI-TOUCH] Injecting {contacts.Length} contacts: ");

        var typeInfos = new POINTER_TYPE_INFO_TOUCH[contacts.Length];
        for (int i = 0; i < contacts.Length; i++)
        {
            var (x, y, pointerId, flags) = contacts[i];
            // Only first contact is PRIMARY
            var ptrFlags = flags | POINTER_FLAG_CONFIDENCE;
            if (i == 0) ptrFlags |= POINTER_FLAG_PRIMARY;

            logDetails.Append($"[ID={pointerId} ({x},{y}) flags=0x{ptrFlags:X}] ");

            var pixelPoint = new POINT { x = x, y = y };
            // CRITICAL: WPF with EnablePointerSupport reads ptHimetricLocation for high-precision coordinates
            var himetricPoint = PixelToHimetric(x, y);

            typeInfos[i] = new POINTER_TYPE_INFO_TOUCH
            {
                type = PT_TOUCH,
                touchInfo = new POINTER_TOUCH_INFO
                {
                    pointerInfo = new POINTER_INFO
                    {
                        pointerType = (int)PT_TOUCH,
                        pointerId = pointerId,
                        pointerFlags = (int)ptrFlags,
                        ptPixelLocation = pixelPoint,
                        ptHimetricLocation = himetricPoint,  // Required for WPF EnablePointerSupport!
                        ptPixelLocationRaw = pixelPoint,
                        ptHimetricLocationRaw = himetricPoint
                    },
                    touchFlags = 0,
                    touchMask = 0,
                    rcContact = new RECT { left = x - 2, top = y - 2, right = x + 2, bottom = y + 2 },
                    rcContactRaw = new RECT { left = x - 2, top = y - 2, right = x + 2, bottom = y + 2 },
                    orientation = 0,
                    pressure = 512
                }
            };
        }

        var result = InjectSyntheticPointerInput(_touchDevice, typeInfos, (uint)contacts.Length);
        var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        logDetails.Append(result ? "OK" : $"FAILED error={error}");
        System.IO.File.AppendAllText(logPath, logDetails.ToString() + "\n");

        return result;
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

        // Flag combinations for multi-touch (must include INRANGE and INCONTACT for WPF):
        // - NEW on DOWN: Required for Direct Manipulation engine to initialize
        // - INRANGE: Finger is within touch detection range
        // - INCONTACT: Finger is actually touching the surface
        // Note: PRIMARY is added by InjectMultiTouch only for first contact
        uint downFlags = POINTER_FLAG_NEW | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_DOWN;
        uint updateFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_UPDATE;
        uint upFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_UP;

        // Down - both fingers
        if (!InjectMultiTouch(
            (x1Start, centerY, id1, downFlags),
            (x2Start, centerY, id2, downFlags)))
            return false;

        // Move - animate the pinch
        for (int i = 1; i <= steps; i++)
        {
            int halfDist = halfStart + (endDistance / 2 - halfStart) * i / steps;
            int x1 = centerX - halfDist;
            int x2 = centerX + halfDist;

            if (!InjectMultiTouch(
                (x1, centerY, id1, updateFlags),
                (x2, centerY, id2, updateFlags)))
                return false;

            if (delayMs > 0)
                System.Threading.Thread.Sleep(delayMs);
        }

        // Up - both fingers
        int halfEnd = endDistance / 2;
        return InjectMultiTouch(
            (centerX - halfEnd, centerY, id1, upFlags),
            (centerX + halfEnd, centerY, id2, upFlags));
    }

    /// <summary>
    /// Simulate two-finger rotate gesture around a center point
    /// </summary>
    /// <param name="centerX">Center X of the rotation</param>
    /// <param name="centerY">Center Y of the rotation</param>
    /// <param name="radius">Distance from center to each finger (pixels)</param>
    /// <param name="startAngle">Starting angle in degrees (0 = right, 90 = down)</param>
    /// <param name="endAngle">Ending angle in degrees</param>
    /// <param name="steps">Number of animation steps (default 20)</param>
    /// <param name="delayMs">Delay between steps in milliseconds (default 0)</param>
    /// <returns>True if successful</returns>
    public static bool Rotate(int centerX, int centerY, int radius, double startAngle, double endAngle, int steps = 20, int delayMs = 0)
    {
        var id1 = _nextPointerId++;
        var id2 = _nextPointerId++;

        // Convert to radians
        double startRad = startAngle * Math.PI / 180.0;
        double endRad = endAngle * Math.PI / 180.0;

        // Calculate start positions (two fingers opposite each other)
        int x1Start = centerX + (int)(radius * Math.Cos(startRad));
        int y1Start = centerY + (int)(radius * Math.Sin(startRad));
        int x2Start = centerX + (int)(radius * Math.Cos(startRad + Math.PI));
        int y2Start = centerY + (int)(radius * Math.Sin(startRad + Math.PI));

        // Flag combinations for multi-touch (must include INRANGE and INCONTACT for WPF):
        uint downFlags = POINTER_FLAG_NEW | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_DOWN;
        uint updateFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_UPDATE;
        uint upFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_UP;

        // Down - both fingers
        if (!InjectMultiTouch(
            (x1Start, y1Start, id1, downFlags),
            (x2Start, y2Start, id2, downFlags)))
            return false;

        // Move - animate the rotation
        for (int i = 1; i <= steps; i++)
        {
            double angle = startRad + (endRad - startRad) * i / steps;
            int x1 = centerX + (int)(radius * Math.Cos(angle));
            int y1 = centerY + (int)(radius * Math.Sin(angle));
            int x2 = centerX + (int)(radius * Math.Cos(angle + Math.PI));
            int y2 = centerY + (int)(radius * Math.Sin(angle + Math.PI));

            if (!InjectMultiTouch(
                (x1, y1, id1, updateFlags),
                (x2, y2, id2, updateFlags)))
                return false;

            if (delayMs > 0)
                System.Threading.Thread.Sleep(delayMs);
        }

        // Up - both fingers at final positions
        int x1End = centerX + (int)(radius * Math.Cos(endRad));
        int y1End = centerY + (int)(radius * Math.Sin(endRad));
        int x2End = centerX + (int)(radius * Math.Cos(endRad + Math.PI));
        int y2End = centerY + (int)(radius * Math.Sin(endRad + Math.PI));

        return InjectMultiTouch(
            (x1End, y1End, id1, upFlags),
            (x2End, y2End, id2, upFlags));
    }

    /// <summary>
    /// Execute a multi-finger gesture with time-synchronized waypoints.
    /// Each finger follows its own path, interpolated by timestamp.
    /// </summary>
    /// <param name="fingers">Array of finger paths. Each finger has an array of waypoints [x, y, timeMs]</param>
    /// <param name="interpolationSteps">Steps between waypoints for smooth movement (default 5)</param>
    /// <returns>Tuple of (success, fingersProcessed, totalSteps)</returns>
    public static (bool success, int fingersProcessed, int totalSteps) MultiTouchGesture(
        (int x, int y, int timeMs)[][] fingers,
        int interpolationSteps = 5)
    {
        if (fingers == null || fingers.Length == 0 || fingers.Length > 10)
            return (false, 0, 0);

        // Validate all fingers have at least 2 waypoints
        foreach (var finger in fingers)
        {
            if (finger == null || finger.Length < 2)
                return (false, 0, 0);
        }

        // Assign pointer IDs
        var fingerIds = new uint[fingers.Length];
        for (int i = 0; i < fingers.Length; i++)
            fingerIds[i] = _nextPointerId++;

        // Find total duration (max end time across all fingers)
        int totalDuration = 0;
        foreach (var finger in fingers)
        {
            int endTime = finger[finger.Length - 1].timeMs;
            if (endTime > totalDuration) totalDuration = endTime;
        }

        if (totalDuration <= 0)
            return (false, 0, 0);

        int stepCount = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Flag combinations for multi-touch (must include INRANGE and INCONTACT for WPF):
            uint downFlags = POINTER_FLAG_NEW | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_DOWN;
            uint updateFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_UPDATE;
            uint upFlags = POINTER_FLAG_INRANGE | POINTER_FLAG_UP;

            // Down - all fingers at their first position
            var downContacts = new (int x, int y, uint pointerId, uint flags)[fingers.Length];
            for (int f = 0; f < fingers.Length; f++)
            {
                var wp = fingers[f][0];
                downContacts[f] = (wp.x, wp.y, fingerIds[f], downFlags);
            }
            if (!InjectMultiTouch(downContacts))
                return (false, 0, 0);

            // Move - interpolate all fingers using real elapsed time
            // Inject at ~100Hz (10ms intervals) but use actual elapsed time for interpolation
            int lastInjectedTime = 0;
            while (lastInjectedTime < totalDuration)
            {
                int currentTime = (int)stopwatch.ElapsedMilliseconds;
                if (currentTime > totalDuration) currentTime = totalDuration;

                // Only inject if we've moved forward in time (avoid duplicate positions)
                if (currentTime > lastInjectedTime)
                {
                    var updateContacts = new (int x, int y, uint pointerId, uint flags)[fingers.Length];
                    for (int f = 0; f < fingers.Length; f++)
                    {
                        var (x, y) = InterpolatePosition(fingers[f], currentTime);
                        updateContacts[f] = (x, y, fingerIds[f], updateFlags);
                    }

                    if (!InjectMultiTouch(updateContacts))
                        return (false, fingers.Length, stepCount);

                    stepCount++;
                    lastInjectedTime = currentTime;
                }

                // Small sleep to avoid spinning CPU, but keep injection rate high
                if (lastInjectedTime < totalDuration)
                    System.Threading.Thread.Sleep(5);
            }

            // Up - all fingers at their final positions
            var upContacts = new (int x, int y, uint pointerId, uint flags)[fingers.Length];
            for (int f = 0; f < fingers.Length; f++)
            {
                var lastWp = fingers[f][fingers[f].Length - 1];
                upContacts[f] = (lastWp.x, lastWp.y, fingerIds[f], upFlags);
            }
            if (!InjectMultiTouch(upContacts))
                return (false, fingers.Length, stepCount);

            return (true, fingers.Length, stepCount);
        }
        catch
        {
            return (false, 0, stepCount);
        }
    }

    /// <summary>
    /// Interpolate position along waypoints at a given time
    /// </summary>
    private static (int x, int y) InterpolatePosition((int x, int y, int timeMs)[] waypoints, int currentTime)
    {
        // Find the two waypoints we're between
        for (int i = 0; i < waypoints.Length - 1; i++)
        {
            var wp1 = waypoints[i];
            var wp2 = waypoints[i + 1];

            if (currentTime >= wp1.timeMs && currentTime <= wp2.timeMs)
            {
                // Interpolate between wp1 and wp2
                int duration = wp2.timeMs - wp1.timeMs;
                if (duration == 0)
                    return (wp2.x, wp2.y);

                double t = (double)(currentTime - wp1.timeMs) / duration;
                int x = wp1.x + (int)((wp2.x - wp1.x) * t);
                int y = wp1.y + (int)((wp2.y - wp1.y) * t);
                return (x, y);
            }
        }

        // If past all waypoints, return last position
        var last = waypoints[waypoints.Length - 1];
        return (last.x, last.y);
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

    #region Mouse Input (via FlaUI with fast settings)

    // ===================================================================================
    // MOUSE INPUT USING FlaUI WITH OPTIMIZED SETTINGS
    // ===================================================================================
    //
    // FlaUI's Mouse.MoveTo() has built-in animation. Key settings:
    //   - Mouse.MovePixelsPerMillisecond (default 0.5) - pixels moved per ms
    //   - Mouse.MovePixelsPerStep (default 10) - pixels per interpolation step
    //
    // We set these to high values for near-instant movement while still using FlaUI's
    // battle-tested input injection. This avoids maintaining custom P/Invoke code.
    //
    // IMPORTANT: Use single MoveTo calls, not loops! FlaUI handles interpolation.
    //   - MouseDrag: MoveTo(start), Down, MoveTo(end), Up - FlaUI animates the path
    //   - MouseDragPath: MoveTo each waypoint - FlaUI animates between them
    //
    // FlaUI source: https://github.com/FlaUI/FlaUI/blob/master/src/FlaUI.Core/Input/Mouse.cs
    // ===================================================================================

    private static bool _mouseSpeedInitialized;

    /// <summary>
    /// Ensure FlaUI mouse speed is set to fast (near-instant) movement
    /// </summary>
    private static void EnsureFastMouseSpeed()
    {
        if (_mouseSpeedInitialized) return;

        // Set very high values for near-instant mouse movement
        Mouse.MovePixelsPerMillisecond = 10000;  // Default is 0.5
        Mouse.MovePixelsPerStep = 10000;          // Default is 10
        _mouseSpeedInitialized = true;
    }

    /// <summary>
    /// Simulate mouse drag from one point to another (works for InkCanvas drawing)
    /// Uses FlaUI with fast settings - single MoveTo call, FlaUI handles animation.
    /// </summary>
    /// <param name="x1">Start X coordinate</param>
    /// <param name="y1">Start Y coordinate</param>
    /// <param name="x2">End X coordinate</param>
    /// <param name="y2">End Y coordinate</param>
    /// <param name="steps">Ignored - kept for API compatibility. FlaUI handles interpolation.</param>
    /// <param name="delayMs">Delay in milliseconds after drag completes (default 0)</param>
    /// <param name="targetWindow">Optional window to target (not used currently)</param>
    public static bool MouseDrag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 0, string? targetWindow = null)
    {
        try
        {
            EnsureFastMouseSpeed();

            // Move to start
            Mouse.MoveTo(new System.Drawing.Point(x1, y1));

            // Press left button
            Mouse.Down(MouseButton.Left);

            // Single MoveTo to end - FlaUI handles all interpolation
            Mouse.MoveTo(new System.Drawing.Point(x2, y2));

            // Release
            Mouse.Up(MouseButton.Left);

            if (delayMs > 0)
                System.Threading.Thread.Sleep(delayMs);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Simulate mouse drag through multiple waypoints (for drawing shapes, curves, complex gestures).
    /// Uses FlaUI with fast settings - one MoveTo per waypoint, FlaUI handles animation between.
    ///
    /// PERFORMANCE NOTE: Keep waypoint count low (2-50 points typically). Each waypoint has
    /// framework overhead (~100ms per point). For straight line drags, use just start and end
    /// points (2 points) - FlaUI will interpolate the path smoothly. Only add intermediate
    /// waypoints when you need actual curve control points (splines, complex shapes).
    ///
    /// RECOMMENDED USAGE:
    /// - Simple drag: 2 points (start, end) - FlaUI interpolates
    /// - Rectangle: 5 points (4 corners + return to start)
    /// - Curved path: 10-20 waypoints along the spline
    /// - Max supported: 1000 points (but expect ~2 minutes execution time)
    /// </summary>
    /// <param name="waypoints">Array of (x, y) coordinates to drag through (minimum 2, max 1000)</param>
    /// <param name="stepsPerSegment">Ignored - kept for API compatibility. FlaUI handles interpolation.</param>
    /// <param name="delayMs">Delay in milliseconds between waypoints (default 0)</param>
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

        if (delayMs < 0)
            delayMs = 0;

        try
        {
            EnsureFastMouseSpeed();

            var startPoint = waypoints[0];

            // Move to first point
            Mouse.MoveTo(new System.Drawing.Point(startPoint.x, startPoint.y));

            // Press left button
            Mouse.Down(MouseButton.Left);

            // One MoveTo per waypoint - FlaUI handles interpolation between them
            for (int i = 1; i < waypoints.Length; i++)
            {
                var point = waypoints[i];
                Mouse.MoveTo(new System.Drawing.Point(point.x, point.y));

                if (delayMs > 0)
                    System.Threading.Thread.Sleep(delayMs);
            }

            // Release mouse button
            Mouse.Up(MouseButton.Left);

            return (true, waypoints.Length, waypoints.Length - 1);
        }
        catch
        {
            // Ensure mouse is released on error
            try { Mouse.Up(MouseButton.Left); } catch { }
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
