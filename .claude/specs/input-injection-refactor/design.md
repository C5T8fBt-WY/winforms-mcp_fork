# InputInjection Refactoring - Design Document

## Overview

This design refactors the 1,511-line `InputInjection` static class into a modular architecture using the Facade pattern. The facade preserves backwards compatibility while delegating to focused implementation classes.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Handlers                                 │
│  (TouchPenHandlers, InputHandlers, WindowHandlers, etc.)        │
└─────────────────────────────┬───────────────────────────────────┘
                              │ uses
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    InputInjection (Facade)                       │
│  Static class preserving existing API for backwards compat       │
│  - TouchTap, TouchDrag, PinchZoom, Rotate, MultiTouchGesture    │
│  - PenStroke, PenTap                                             │
│  - MouseClick, MouseDrag, MouseDragPath                          │
│  - GetWindowBounds, FocusWindow, WindowToScreen                  │
└──────────┬──────────────────┬──────────────────┬────────────────┘
           │                  │                  │
           ▼                  ▼                  ▼
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐
│   TouchInput     │ │    PenInput      │ │   MouseInput     │
│                  │ │                  │ │                  │
│ - TouchTap       │ │ - PenStroke      │ │ - MouseClick     │
│ - TouchDrag      │ │ - PenTap         │ │ - MouseDrag      │
│ - PinchZoom      │ │ - InjectPen      │ │ - MouseDragPath  │
│ - Rotate         │ │                  │ │                  │
│ - MultiTouch     │ │                  │ │                  │
│ - InjectTouch    │ │                  │ │                  │
│ - LegacyTouch    │ │                  │ │                  │
└────────┬─────────┘ └────────┬─────────┘ └────────┬─────────┘
         │                    │                    │
         └────────────────────┼────────────────────┘
                              │ uses
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Shared Infrastructure                         │
├─────────────────────────────────────────────────────────────────┤
│  Win32Interop          │ CoordinateUtils    │ WindowTargeting   │
│  - P/Invoke decls      │ - PixelToHimetric  │ - FindWindow      │
│  - Struct definitions  │ - DPI queries      │ - GetWindowBounds │
│  - Flag constants      │ - DPI caching      │ - FocusWindow     │
│  - Device handles      │ - VirtualScreen    │ - WindowToScreen  │
│                        │                    │                   │
│  DebugLogger           │ PointerIdManager   │                   │
│  - GetDebugLogPath     │ - Thread-safe IDs  │                   │
│  - LogStructLayout     │ - Frame ID mgmt    │                   │
└─────────────────────────────────────────────────────────────────┘
```

## File Structure

```
src/Rhombus.WinFormsMcp.Server/
├── Automation/
│   ├── InputInjection.cs           # Facade (static, backwards-compat)
│   ├── Input/
│   │   ├── TouchInput.cs           # Touch injection (~350 lines)
│   │   ├── PenInput.cs             # Pen injection (~200 lines)
│   │   └── MouseInput.cs           # Mouse input (~150 lines)
│   └── Interop/
│       ├── Win32Interop.cs         # P/Invoke, structs, constants (~250 lines)
│       ├── CoordinateUtils.cs      # Pixel/HIMETRIC, DPI (~100 lines)
│       ├── WindowTargeting.cs      # Window find/focus (~100 lines)
│       ├── PointerIdManager.cs     # Thread-safe ID allocation (~50 lines)
│       └── DebugLogger.cs          # Debug logging utilities (~80 lines)
```

## Interface Definitions

### ITouchInput

```csharp
namespace Rhombus.WinFormsMcp.Server.Automation.Input;

/// <summary>
/// Touch input injection interface for unit testing and future extensibility.
/// </summary>
public interface ITouchInput
{
    /// <summary>
    /// Simulate a touch tap at a location.
    /// </summary>
    /// <param name="x">Screen X coordinate</param>
    /// <param name="y">Screen Y coordinate</param>
    /// <param name="holdMs">Milliseconds to hold before release (default 0)</param>
    bool TouchTap(int x, int y, int holdMs = 0);

    /// <summary>
    /// Simulate a touch tap using legacy InjectTouchInput API (Windows 8+).
    /// May route through system touch device with proper coordinate mapping.
    /// </summary>
    bool LegacyTouchTap(int x, int y, int holdMs = 0);

    /// <summary>
    /// Simulate a touch drag from one point to another.
    /// </summary>
    bool TouchDrag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 5);

    /// <summary>
    /// Simulate pinch-to-zoom gesture.
    /// </summary>
    bool PinchZoom(int centerX, int centerY, int startDistance, int endDistance,
                   int steps = 20, int delayMs = 0);

    /// <summary>
    /// Simulate two-finger rotate gesture.
    /// </summary>
    bool Rotate(int centerX, int centerY, int radius, double startAngle, double endAngle,
                int steps = 20, int delayMs = 0);

    /// <summary>
    /// Execute a multi-finger gesture with time-synchronized waypoints.
    /// </summary>
    (bool success, int fingersProcessed, int totalSteps) MultiTouchGesture(
        (int x, int y, int timeMs)[][] fingers, int interpolationSteps = 5);

    /// <summary>
    /// Inject multiple touch contacts simultaneously.
    /// </summary>
    bool InjectMultiTouch(params (int x, int y, uint pointerId, uint flags)[] contacts);
}
```

### IPenInput

```csharp
namespace Rhombus.WinFormsMcp.Server.Automation.Input;

/// <summary>
/// Pen/stylus input injection interface.
/// </summary>
public interface IPenInput
{
    /// <summary>
    /// Simulate a pen stroke from one point to another.
    /// </summary>
    /// <param name="x1">Start X screen coordinate</param>
    /// <param name="y1">Start Y screen coordinate</param>
    /// <param name="x2">End X screen coordinate</param>
    /// <param name="y2">End Y screen coordinate</param>
    /// <param name="steps">Number of interpolation steps</param>
    /// <param name="pressure">Pen pressure 0-1024</param>
    /// <param name="eraser">Use eraser end of pen</param>
    /// <param name="delayMs">Delay between steps in ms</param>
    /// <param name="hwndTarget">Target window handle (optional)</param>
    bool PenStroke(int x1, int y1, int x2, int y2, int steps = 20, uint pressure = 512,
                   bool eraser = false, int delayMs = 2, IntPtr hwndTarget = default);

    /// <summary>
    /// Simulate pen tap (like clicking with pen tip).
    /// </summary>
    /// <param name="x">Screen X coordinate</param>
    /// <param name="y">Screen Y coordinate</param>
    /// <param name="pressure">Pen pressure 0-1024 (default 512)</param>
    /// <param name="holdMs">Milliseconds to hold before release (default 0)</param>
    /// <param name="hwndTarget">Target window handle (optional)</param>
    bool PenTap(int x, int y, uint pressure = 512, int holdMs = 0, IntPtr hwndTarget = default);
}
```

### IMouseInput

```csharp
namespace Rhombus.WinFormsMcp.Server.Automation.Input;

/// <summary>
/// Mouse input injection interface using FlaUI.
/// </summary>
public interface IMouseInput
{
    /// <summary>
    /// Simulate mouse click at coordinates.
    /// </summary>
    bool MouseClick(int x, int y, bool doubleClick = false, int delayMs = 0);

    /// <summary>
    /// Simulate mouse drag from one point to another.
    /// </summary>
    bool MouseDrag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 0,
                   string? targetWindow = null);

    /// <summary>
    /// Simulate mouse drag through multiple waypoints.
    /// </summary>
    (bool success, int pointsProcessed, int totalSteps) MouseDragPath(
        (int x, int y)[] waypoints, int stepsPerSegment = 1, int delayMs = 0);
}
```

### IWindowTargeting

```csharp
namespace Rhombus.WinFormsMcp.Server.Automation.Interop;

/// <summary>
/// Window targeting utilities for coordinate translation and focus management.
/// </summary>
public interface IWindowTargeting
{
    /// <summary>
    /// Get window bounds by window title (supports partial match).
    /// </summary>
    (int x, int y, int width, int height)? GetWindowBounds(string windowTitle);

    /// <summary>
    /// Focus a window by title (supports partial match).
    /// </summary>
    bool FocusWindow(string windowTitle);

    /// <summary>
    /// Convert window-relative coordinates to screen coordinates.
    /// </summary>
    (int screenX, int screenY)? WindowToScreen(string windowTitle, int windowX, int windowY);
}
```

## Class Implementations

### TouchInput Class

```csharp
namespace Rhombus.WinFormsMcp.Server.Automation.Input;

/// <summary>
/// Touch input injection using Windows Synthetic Pointer API (Windows 10 1809+)
/// and legacy InjectTouchInput API (Windows 8+) as fallback.
/// </summary>
internal sealed class TouchInput : ITouchInput, IDisposable
{
    private readonly Win32Interop _interop;
    private readonly CoordinateUtils _coords;
    private readonly PointerIdManager _pointerIds;
    private readonly DebugLogger _logger;

    private IntPtr _touchDevice = IntPtr.Zero;
    private bool _legacyInitialized = false;

    public TouchInput(Win32Interop interop, CoordinateUtils coords,
                      PointerIdManager pointerIds, DebugLogger logger)
    {
        _interop = interop;
        _coords = coords;
        _pointerIds = pointerIds;
        _logger = logger;
    }

    public bool EnsureInitialized(uint maxContacts = 10)
    {
        if (_touchDevice != IntPtr.Zero) return true;
        _touchDevice = _interop.CreateSyntheticPointerDevice(
            Win32Interop.PT_TOUCH, maxContacts, Win32Interop.POINTER_FEEDBACK_DEFAULT);
        return _touchDevice != IntPtr.Zero;
    }

    // ... implementation methods moved from InputInjection.cs

    public void Dispose()
    {
        if (_touchDevice != IntPtr.Zero)
        {
            _interop.DestroySyntheticPointerDevice(_touchDevice);
            _touchDevice = IntPtr.Zero;
        }
    }
}
```

### PenInput Class

```csharp
namespace Rhombus.WinFormsMcp.Server.Automation.Input;

/// <summary>
/// Pen/stylus input injection using Windows Synthetic Pointer API (Windows 10 1809+).
/// </summary>
internal sealed class PenInput : IPenInput, IDisposable
{
    public const string VERSION = "v10.0-hwndTarget-noPremult";

    private readonly Win32Interop _interop;
    private readonly CoordinateUtils _coords;
    private readonly PointerIdManager _pointerIds;
    private readonly DebugLogger _logger;

    private IntPtr _penDevice = IntPtr.Zero;

    public PenInput(Win32Interop interop, CoordinateUtils coords,
                    PointerIdManager pointerIds, DebugLogger logger)
    {
        _interop = interop;
        _coords = coords;
        _pointerIds = pointerIds;
        _logger = logger;
    }

    public bool EnsureInitialized()
    {
        if (_penDevice != IntPtr.Zero) return true;
        _penDevice = _interop.CreateSyntheticPointerDevice(
            Win32Interop.PT_PEN, 1, Win32Interop.POINTER_FEEDBACK_DEFAULT);

        if (_penDevice != IntPtr.Zero)
        {
            _logger.LogPenDeviceCreated(_penDevice);
            _interop.QueryAndLogDeviceRects(_penDevice, "Synthetic PEN", _logger);
        }

        return _penDevice != IntPtr.Zero;
    }

    // ... implementation methods moved from InputInjection.cs

    public void Dispose()
    {
        if (_penDevice != IntPtr.Zero)
        {
            _interop.DestroySyntheticPointerDevice(_penDevice);
            _penDevice = IntPtr.Zero;
        }
    }
}
```

### MouseInput Class

```csharp
namespace Rhombus.WinFormsMcp.Server.Automation.Input;

using FlaUI.Core.Input;

/// <summary>
/// Mouse input using FlaUI with optimized speed settings.
/// </summary>
internal sealed class MouseInput : IMouseInput
{
    private bool _speedInitialized;

    /// <summary>
    /// Ensure FlaUI mouse speed is set to fast (near-instant) movement.
    /// </summary>
    private void EnsureFastMouseSpeed()
    {
        if (_speedInitialized) return;
        Mouse.MovePixelsPerMillisecond = 10000;
        Mouse.MovePixelsPerStep = 10000;
        _speedInitialized = true;
    }

    public bool MouseClick(int x, int y, bool doubleClick = false, int delayMs = 0)
    {
        try
        {
            var point = new System.Drawing.Point(x, y);
            Mouse.MoveTo(point);

            if (delayMs > 0)
                Thread.Sleep(delayMs);

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

    // ... MouseDrag and MouseDragPath implementations
}
```

### Win32Interop Class

```csharp
namespace Rhombus.WinFormsMcp.Server.Automation.Interop;

using System.Runtime.InteropServices;

/// <summary>
/// Windows P/Invoke declarations, struct definitions, and constants
/// for pointer input injection.
/// </summary>
internal static class Win32Interop
{
    #region Constants

    // Pointer types
    public const uint PT_TOUCH = 0x00000002;
    public const uint PT_PEN = 0x00000003;

    // Pointer flags
    public const uint POINTER_FLAG_NONE = 0x00000000;
    public const uint POINTER_FLAG_NEW = 0x00000001;
    public const uint POINTER_FLAG_INRANGE = 0x00000002;
    public const uint POINTER_FLAG_INCONTACT = 0x00000004;
    // ... all other POINTER_FLAG_* constants

    // Pen flags
    public const uint PEN_FLAG_NONE = 0x00000000;
    public const uint PEN_FLAG_BARREL = 0x00000001;
    public const uint PEN_FLAG_INVERTED = 0x00000002;
    public const uint PEN_FLAG_ERASER = 0x00000004;

    // Pen mask
    public const uint PEN_MASK_NONE = 0x00000000;
    public const uint PEN_MASK_PRESSURE = 0x00000001;
    public const uint PEN_MASK_ROTATION = 0x00000002;
    public const uint PEN_MASK_TILT_X = 0x00000004;
    public const uint PEN_MASK_TILT_Y = 0x00000008;

    // Touch feedback
    public const uint TOUCH_FEEDBACK_DEFAULT = 0x1;
    public const uint TOUCH_FEEDBACK_INDIRECT = 0x2;
    public const uint TOUCH_FEEDBACK_NONE = 0x3;

    // Touch mask
    public const uint TOUCH_MASK_NONE = 0x00000000;
    public const uint TOUCH_MASK_CONTACTAREA = 0x00000001;
    public const uint TOUCH_MASK_ORIENTATION = 0x00000002;
    public const uint TOUCH_MASK_PRESSURE = 0x00000004;

    #endregion

    #region Structs

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

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_INFO
    {
        public int pointerType;
        public uint pointerId;
        public uint frameId;
        public int pointerFlags;
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
    public struct POINTER_PEN_INFO
    {
        public POINTER_INFO pointerInfo;
        public int penFlags;
        public int penMask;
        public uint pressure;
        public uint rotation;
        public int tiltX;
        public int tiltY;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct POINTER_TYPE_INFO_PEN
    {
        [FieldOffset(0)]
        public uint type;
        [FieldOffset(8)]  // 8-byte alignment for nested struct containing IntPtr
        public POINTER_PEN_INFO penInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct POINTER_TYPE_INFO_TOUCH
    {
        [FieldOffset(0)]
        public uint type;
        [FieldOffset(8)]
        public POINTER_TOUCH_INFO touchInfo;
    }

    #endregion

    #region P/Invoke Declarations

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateSyntheticPointerDevice(uint pointerType, uint maxCount, uint mode);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InjectSyntheticPointerInput(IntPtr device,
        [MarshalAs(UnmanagedType.LPArray)] POINTER_TYPE_INFO_PEN[] pointerInfo, uint count);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InjectSyntheticPointerInput(IntPtr device,
        [MarshalAs(UnmanagedType.LPArray)] POINTER_TYPE_INFO_TOUCH[] pointerInfo, uint count);

    [DllImport("user32.dll", SetLastError = false)]
    public static extern void DestroySyntheticPointerDevice(IntPtr device);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InitializeTouchInjection(uint maxCount, uint dwMode);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InjectTouchInput(uint count,
        [MarshalAs(UnmanagedType.LPArray)] POINTER_TOUCH_INFO[] contacts);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetPointerDeviceRects(IntPtr device, out RECT pointerDeviceRect, out RECT displayRect);

    // DPI and screen metrics
    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("kernel32.dll")]
    public static extern uint GetTickCount();

    // Window management
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    #endregion

    #region Helper Methods

    public static void QueryAndLogDeviceRects(IntPtr device, string deviceName, DebugLogger logger)
    {
        if (GetPointerDeviceRects(device, out RECT deviceRect, out RECT displayRect))
        {
            logger.LogDeviceRects(deviceName, device, deviceRect, displayRect);
        }
        else
        {
            logger.LogDeviceRectsError(deviceName, Marshal.GetLastWin32Error());
        }
    }

    #endregion
}
```

### CoordinateUtils Class

```csharp
namespace Rhombus.WinFormsMcp.Server.Automation.Interop;

/// <summary>
/// Coordinate translation utilities for pixel-to-HIMETRIC conversion and DPI handling.
/// </summary>
internal sealed class CoordinateUtils
{
    private const double HIMETRIC_PER_INCH = 2540.0;
    private const int LOGPIXELSX = 88;
    private const int LOGPIXELSY = 90;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;

    private int _cachedDpiX = 0;
    private int _cachedDpiY = 0;
    private readonly object _dpiLock = new();

    /// <summary>
    /// Query the actual system DPI at runtime.
    /// </summary>
    public (int dpiX, int dpiY) GetSystemDpi()
    {
        lock (_dpiLock)
        {
            if (_cachedDpiX > 0 && _cachedDpiY > 0)
                return (_cachedDpiX, _cachedDpiY);

            IntPtr hdc = Win32Interop.GetDC(IntPtr.Zero);
            try
            {
                _cachedDpiX = Win32Interop.GetDeviceCaps(hdc, LOGPIXELSX);
                _cachedDpiY = Win32Interop.GetDeviceCaps(hdc, LOGPIXELSY);

                if (_cachedDpiX <= 0) _cachedDpiX = 96;
                if (_cachedDpiY <= 0) _cachedDpiY = 96;

                return (_cachedDpiX, _cachedDpiY);
            }
            finally
            {
                Win32Interop.ReleaseDC(IntPtr.Zero, hdc);
            }
        }
    }

    /// <summary>
    /// Convert pixel coordinates to HIMETRIC units using dynamic DPI.
    /// Critical for WPF EnablePointerSupport to receive correct coordinates.
    /// </summary>
    public Win32Interop.POINT PixelToHimetric(int x, int y)
    {
        var (dpiX, dpiY) = GetSystemDpi();
        return new Win32Interop.POINT
        {
            x = (int)((x * HIMETRIC_PER_INCH) / dpiX),
            y = (int)((y * HIMETRIC_PER_INCH) / dpiY)
        };
    }

    /// <summary>
    /// Get virtual screen origin offset for multi-monitor setups.
    /// </summary>
    public (int offsetX, int offsetY) GetVirtualScreenOrigin()
    {
        int offsetX = Win32Interop.GetSystemMetrics(SM_XVIRTUALSCREEN);
        int offsetY = Win32Interop.GetSystemMetrics(SM_YVIRTUALSCREEN);
        return (offsetX, offsetY);
    }

    /// <summary>
    /// Force re-query of DPI on next conversion.
    /// </summary>
    public void InvalidateDpiCache()
    {
        lock (_dpiLock)
        {
            _cachedDpiX = 0;
            _cachedDpiY = 0;
        }
    }
}
```

### PointerIdManager Class

```csharp
namespace Rhombus.WinFormsMcp.Server.Automation.Interop;

/// <summary>
/// Thread-safe pointer ID allocation for touch and pen injection.
/// </summary>
internal sealed class PointerIdManager
{
    private uint _nextPointerId = 1;
    private uint _frameId = 0;
    private readonly object _lock = new();

    /// <summary>
    /// Get the next unique pointer ID.
    /// </summary>
    public uint GetNextPointerId()
    {
        lock (_lock)
        {
            return _nextPointerId++;
        }
    }

    /// <summary>
    /// Get the next frame ID (increment and return).
    /// </summary>
    public uint GetNextFrameId()
    {
        lock (_lock)
        {
            return ++_frameId;
        }
    }

    /// <summary>
    /// Get multiple consecutive pointer IDs atomically.
    /// </summary>
    public uint[] GetPointerIds(int count)
    {
        lock (_lock)
        {
            var ids = new uint[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = _nextPointerId++;
            }
            return ids;
        }
    }
}
```

### DebugLogger Class

```csharp
namespace Rhombus.WinFormsMcp.Server.Automation.Interop;

using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Debug logging utilities for pen/touch injection troubleshooting.
/// </summary>
internal sealed class DebugLogger
{
    private readonly string _logPath;

    public DebugLogger()
    {
        _logPath = GetDebugLogPath();
    }

    /// <summary>
    /// Get debug log path that works in both sandbox and host environments.
    /// </summary>
    private static string GetDebugLogPath()
    {
        const string sandboxPath = @"C:\Shared";
        if (Directory.Exists(sandboxPath))
            return Path.Combine(sandboxPath, "pen-debug.log");
        return Path.Combine(Path.GetTempPath(), "pen-debug.log");
    }

    public void Log(string message)
    {
        try
        {
            File.AppendAllText(_logPath, message + "\n");
        }
        catch
        {
            // Silently ignore logging failures
        }
    }

    public void LogPenDeviceCreated(IntPtr device)
    {
        var bitness = Environment.Is64BitProcess ? "x64" : "x86";
        var ptrSize = IntPtr.Size;
        var infoSize = Marshal.SizeOf<Win32Interop.POINTER_INFO>();
        Log($"[PEN {PenInput.VERSION}] Device created: 0x{device:X} ({bitness}, IntPtr={ptrSize}, POINTER_INFO={infoSize})");
    }

    public void LogDeviceRects(string deviceName, IntPtr device,
        Win32Interop.RECT deviceRect, Win32Interop.RECT displayRect)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[DEVICE RECTS] {deviceName} (handle=0x{device:X}):");
        sb.AppendLine($"  pointerDeviceRect: ({deviceRect.left},{deviceRect.top}) - ({deviceRect.right},{deviceRect.bottom})");
        sb.AppendLine($"  displayRect: ({displayRect.left},{displayRect.top}) - ({displayRect.right},{displayRect.bottom})");
        Log(sb.ToString());
    }

    public void LogDeviceRectsError(string deviceName, int errorCode)
    {
        Log($"[DEVICE RECTS] GetPointerDeviceRects failed for {deviceName}: error={errorCode}");
    }

    public void LogStructLayout<T>(T obj, string label) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(obj, ptr, false);
            byte[] bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, size);

            var sb = new StringBuilder();
            sb.AppendLine($"[STRUCT] {label} - Total size: {size} bytes");

            for (int i = 0; i < bytes.Length; i += 16)
            {
                sb.Append($"  [{i,3}] ");
                for (int j = 0; j < 16 && i + j < bytes.Length; j++)
                {
                    sb.Append($"{bytes[i + j]:X2} ");
                }
                sb.AppendLine();
            }

            Log(sb.ToString());
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
```

### WindowTargeting Class

```csharp
namespace Rhombus.WinFormsMcp.Server.Automation.Interop;

using System.Text;

/// <summary>
/// Window targeting utilities for coordinate translation and focus management.
/// </summary>
internal sealed class WindowTargeting : IWindowTargeting
{
    public (int x, int y, int width, int height)? GetWindowBounds(string windowTitle)
    {
        var hwnd = FindWindow(null, windowTitle);
        if (hwnd == IntPtr.Zero)
            hwnd = FindWindowByPartialTitle(windowTitle);

        if (hwnd == IntPtr.Zero) return null;

        if (Win32Interop.GetWindowRect(hwnd, out var rect))
        {
            return (rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
        }
        return null;
    }

    public bool FocusWindow(string windowTitle)
    {
        var hwnd = Win32Interop.FindWindow(null, windowTitle);
        if (hwnd == IntPtr.Zero)
            hwnd = FindWindowByPartialTitle(windowTitle);

        if (hwnd == IntPtr.Zero) return false;
        return Win32Interop.SetForegroundWindow(hwnd);
    }

    public (int screenX, int screenY)? WindowToScreen(string windowTitle, int windowX, int windowY)
    {
        var bounds = GetWindowBounds(windowTitle);
        if (bounds == null) return null;
        return (bounds.Value.x + windowX, bounds.Value.y + windowY);
    }

    private static IntPtr FindWindowByPartialTitle(string partialTitle)
    {
        IntPtr foundHwnd = IntPtr.Zero;

        Win32Interop.EnumWindows((hwnd, lParam) =>
        {
            var sb = new StringBuilder(256);
            Win32Interop.GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (!string.IsNullOrEmpty(title) &&
                title.Contains(partialTitle, StringComparison.OrdinalIgnoreCase))
            {
                foundHwnd = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return foundHwnd;
    }
}
```

### InputInjection Facade

```csharp
namespace Rhombus.WinFormsMcp.Server.Automation;

using Rhombus.WinFormsMcp.Server.Automation.Input;
using Rhombus.WinFormsMcp.Server.Automation.Interop;

/// <summary>
/// Facade for input injection, preserving backwards compatibility.
/// Delegates to focused implementation classes.
/// </summary>
public static class InputInjection
{
    // Shared infrastructure (lazy initialization)
    private static readonly Lazy<DebugLogger> _logger = new(() => new DebugLogger());
    private static readonly Lazy<CoordinateUtils> _coords = new(() => new CoordinateUtils());
    private static readonly Lazy<PointerIdManager> _pointerIds = new(() => new PointerIdManager());
    private static readonly Lazy<WindowTargeting> _windowTargeting = new(() => new WindowTargeting());

    // Input implementations (lazy initialization)
    private static readonly Lazy<TouchInput> _touchInput = new(() =>
        new TouchInput(_coords.Value, _pointerIds.Value, _logger.Value));
    private static readonly Lazy<PenInput> _penInput = new(() =>
        new PenInput(_coords.Value, _pointerIds.Value, _logger.Value));
    private static readonly Lazy<MouseInput> _mouseInput = new(() => new MouseInput());

    // Version for deployment verification
    public const string PEN_INJECTION_VERSION = PenInput.VERSION;

    #region Touch Input (delegates to TouchInput)

    public static bool TouchTap(int x, int y, int holdMs = 0)
        => _touchInput.Value.TouchTap(x, y, holdMs);

    public static bool LegacyTouchTap(int x, int y, int holdMs = 0)
        => _touchInput.Value.LegacyTouchTap(x, y, holdMs);

    public static bool TouchDrag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 5)
        => _touchInput.Value.TouchDrag(x1, y1, x2, y2, steps, delayMs);

    public static bool PinchZoom(int centerX, int centerY, int startDistance, int endDistance,
                                  int steps = 20, int delayMs = 0)
        => _touchInput.Value.PinchZoom(centerX, centerY, startDistance, endDistance, steps, delayMs);

    public static bool Rotate(int centerX, int centerY, int radius, double startAngle, double endAngle,
                              int steps = 20, int delayMs = 0)
        => _touchInput.Value.Rotate(centerX, centerY, radius, startAngle, endAngle, steps, delayMs);

    public static (bool success, int fingersProcessed, int totalSteps) MultiTouchGesture(
        (int x, int y, int timeMs)[][] fingers, int interpolationSteps = 5)
        => _touchInput.Value.MultiTouchGesture(fingers, interpolationSteps);

    public static bool InjectMultiTouch(params (int x, int y, uint pointerId, uint flags)[] contacts)
        => _touchInput.Value.InjectMultiTouch(contacts);

    // Legacy exposure for advanced scenarios
    public static bool InjectTouch(int x, int y, uint pointerId, uint flags)
        => _touchInput.Value.InjectTouch(x, y, pointerId, flags);

    public static bool InjectLegacyTouch(int x, int y, uint pointerId, uint flags)
        => _touchInput.Value.InjectLegacyTouch(x, y, pointerId, flags);

    public static bool EnsureTouchInitialized(uint maxContacts = 10)
        => _touchInput.Value.EnsureInitialized(maxContacts);

    public static bool InitializeLegacyTouch(uint maxContacts = 10)
        => _touchInput.Value.InitializeLegacyTouch(maxContacts);

    public static void CleanupTouchDevice()
        => _touchInput.Value.Dispose();

    #endregion

    #region Pen Input (delegates to PenInput)

    public static bool PenStroke(int x1, int y1, int x2, int y2, int steps = 20, uint pressure = 512,
                                  bool eraser = false, int delayMs = 2, IntPtr hwndTarget = default)
        => _penInput.Value.PenStroke(x1, y1, x2, y2, steps, pressure, eraser, delayMs, hwndTarget);

    public static bool PenTap(int x, int y, uint pressure = 512, int holdMs = 0, IntPtr hwndTarget = default)
        => _penInput.Value.PenTap(x, y, pressure, holdMs, hwndTarget);

    public static bool InjectPen(int x, int y, uint pointerId, uint flags, uint penFlags = 0,
                                  uint pressure = 512, int tiltX = 0, int tiltY = 0, IntPtr hwndTarget = default)
        => _penInput.Value.InjectPen(x, y, pointerId, flags, penFlags, pressure, tiltX, tiltY, hwndTarget);

    public static bool EnsurePenInitialized()
        => _penInput.Value.EnsureInitialized();

    public static void CleanupPenDevice()
        => _penInput.Value.Dispose();

    #endregion

    #region Mouse Input (delegates to MouseInput)

    public static bool MouseClick(int x, int y, bool doubleClick = false, int delayMs = 0)
        => _mouseInput.Value.MouseClick(x, y, doubleClick, delayMs);

    public static bool MouseDrag(int x1, int y1, int x2, int y2, int steps = 10, int delayMs = 0,
                                  string? targetWindow = null)
        => _mouseInput.Value.MouseDrag(x1, y1, x2, y2, steps, delayMs, targetWindow);

    public static (bool success, int pointsProcessed, int totalSteps) MouseDragPath(
        (int x, int y)[] waypoints, int stepsPerSegment = 1, int delayMs = 0)
        => _mouseInput.Value.MouseDragPath(waypoints, stepsPerSegment, delayMs);

    #endregion

    #region Window Targeting (delegates to WindowTargeting)

    public static (int x, int y, int width, int height)? GetWindowBounds(string windowTitle)
        => _windowTargeting.Value.GetWindowBounds(windowTitle);

    public static bool FocusWindow(string windowTitle)
        => _windowTargeting.Value.FocusWindow(windowTitle);

    public static (int screenX, int screenY)? WindowToScreen(string windowTitle, int windowX, int windowY)
        => _windowTargeting.Value.WindowToScreen(windowTitle, windowX, windowY);

    #endregion

    #region DPI Utilities (delegates to CoordinateUtils)

    public static void InvalidateDpiCache()
        => _coords.Value.InvalidateDpiCache();

    #endregion

    #region Constants (exposed for handler compatibility)

    // Pointer flags - commonly used by handlers
    public const uint POINTER_FLAG_NONE = Win32Interop.POINTER_FLAG_NONE;
    public const uint POINTER_FLAG_NEW = Win32Interop.POINTER_FLAG_NEW;
    public const uint POINTER_FLAG_INRANGE = Win32Interop.POINTER_FLAG_INRANGE;
    public const uint POINTER_FLAG_INCONTACT = Win32Interop.POINTER_FLAG_INCONTACT;
    public const uint POINTER_FLAG_DOWN = Win32Interop.POINTER_FLAG_DOWN;
    public const uint POINTER_FLAG_UPDATE = Win32Interop.POINTER_FLAG_UPDATE;
    public const uint POINTER_FLAG_UP = Win32Interop.POINTER_FLAG_UP;
    public const uint POINTER_FLAG_PRIMARY = Win32Interop.POINTER_FLAG_PRIMARY;
    public const uint POINTER_FLAG_CONFIDENCE = Win32Interop.POINTER_FLAG_CONFIDENCE;

    // Pen flags
    public const uint PEN_FLAG_NONE = Win32Interop.PEN_FLAG_NONE;
    public const uint PEN_FLAG_BARREL = Win32Interop.PEN_FLAG_BARREL;
    public const uint PEN_FLAG_INVERTED = Win32Interop.PEN_FLAG_INVERTED;
    public const uint PEN_FLAG_ERASER = Win32Interop.PEN_FLAG_ERASER;

    // Pointer types
    public const uint PT_TOUCH = Win32Interop.PT_TOUCH;
    public const uint PT_PEN = Win32Interop.PT_PEN;

    #endregion
}
```

## Migration Path

### Phase 1: Create Infrastructure (No Breaking Changes)

1. Create `Automation/Interop/` directory with:
   - `Win32Interop.cs` - Extract P/Invoke declarations and structs
   - `CoordinateUtils.cs` - Extract DPI and coordinate methods
   - `DebugLogger.cs` - Extract logging utilities
   - `PointerIdManager.cs` - Extract thread-safe ID allocation
   - `WindowTargeting.cs` - Extract window find/focus methods

2. Update `InputInjection.cs` to use new infrastructure classes internally
3. Run existing tests to verify no regressions

### Phase 2: Create Input Classes

1. Create `Automation/Input/` directory with:
   - `ITouchInput.cs` - Interface definition
   - `TouchInput.cs` - Implementation (extract from InputInjection)
   - `IPenInput.cs` - Interface definition
   - `PenInput.cs` - Implementation (extract from InputInjection)
   - `IMouseInput.cs` - Interface definition
   - `MouseInput.cs` - Implementation (extract from InputInjection)

2. Update `InputInjection.cs` to delegate to new classes
3. Run existing tests to verify no regressions

### Phase 3: Handler Integration

1. No changes required to handlers - they continue using `InputInjection.*` static methods
2. For new code, handlers can optionally inject interfaces for testability:

```csharp
// Optional: Constructor injection for testing
internal class TouchPenHandlers : HandlerBase
{
    private readonly ITouchInput? _touchInput;
    private readonly IPenInput? _penInput;

    // Production constructor (uses facade)
    public TouchPenHandlers(SessionManager session, WindowManager windows)
        : base(session, windows)
    {
    }

    // Test constructor (uses injected mocks)
    public TouchPenHandlers(SessionManager session, WindowManager windows,
                            ITouchInput touchInput, IPenInput penInput)
        : base(session, windows)
    {
        _touchInput = touchInput;
        _penInput = penInput;
    }

    private ITouchInput Touch => _touchInput ?? DefaultTouchInput.Instance;
}
```

### Phase 4: Cleanup (Optional Future Work)

1. Remove redundant code from `InputInjection.cs` (now just thin facade)
2. Add unit tests using mocked interfaces
3. Consider deprecating direct `InputInjection.*` calls in favor of DI

## Testing Strategy

### Unit Tests

```csharp
[TestFixture]
public class TouchInputTests
{
    private Mock<ICoordinateUtils> _mockCoords;
    private Mock<IPointerIdManager> _mockPointerIds;
    private Mock<IDebugLogger> _mockLogger;

    [SetUp]
    public void Setup()
    {
        _mockCoords = new Mock<ICoordinateUtils>();
        _mockPointerIds = new Mock<IPointerIdManager>();
        _mockLogger = new Mock<IDebugLogger>();

        _mockCoords.Setup(c => c.GetSystemDpi()).Returns((96, 96));
        _mockPointerIds.Setup(p => p.GetNextPointerId()).Returns(1);
    }

    [Test]
    public void TouchTap_CallsCorrectFlagsSequence()
    {
        // Verify DOWN, UPDATE, UP sequence with correct flags
    }

    [Test]
    public void PinchZoom_CalculatesCorrectPositions()
    {
        // Verify finger positions during pinch
    }
}
```

### Integration Tests

```csharp
[TestFixture]
public class InputInjectionFacadeTests
{
    [Test]
    public void Facade_DelegatesToTouchInput()
    {
        // Verify InputInjection.TouchTap calls TouchInput
        var result = InputInjection.TouchTap(100, 100);
        Assert.IsTrue(result); // Assumes device init works
    }

    [Test]
    public void Facade_PreservesBackwardsCompatibility()
    {
        // Test all existing public API signatures work
    }
}
```

### E2E Tests

Existing E2E tests in `tests/Rhombus.WinFormsMcp.Tests/E2ETests.cs` should pass unchanged.

## Code Metrics (Expected)

| Class | Lines | Cyclomatic Complexity | CRAP Score |
|-------|-------|----------------------|------------|
| InputInjection (facade) | ~150 | 1 (delegation only) | <10 |
| TouchInput | ~350 | <10 per method | <50 |
| PenInput | ~200 | <10 per method | <40 |
| MouseInput | ~150 | <5 per method | <20 |
| Win32Interop | ~250 | 1 (declarations) | <10 |
| CoordinateUtils | ~100 | <5 per method | <20 |
| WindowTargeting | ~100 | <5 per method | <20 |
| PointerIdManager | ~50 | <3 per method | <10 |
| DebugLogger | ~80 | <5 per method | <15 |

**Total: ~1,430 lines** (vs. original 1,511 lines - slight reduction with better organization)

## Decision Log

| Decision | Rationale | Alternatives Considered |
|----------|-----------|------------------------|
| Static facade pattern | Preserves backwards compatibility, no handler changes needed | DI container (too invasive) |
| Lazy initialization | Thread-safe, preserves current behavior, avoids startup cost | Static constructors (less control) |
| Separate Win32Interop class | Centralizes P/Invoke, enables unit testing of logic | Keep in individual classes (duplication) |
| Interface per input type | Enables mocking for unit tests | Abstract base class (less flexible) |
| No handler changes in Phase 1-2 | Reduces risk, allows incremental migration | Full DI refactor (higher risk) |

## Open Questions

1. **Should Win32Interop constants be duplicated in facade?**
   - Current design: Yes, for backwards compat
   - Alternative: Require callers to reference Win32Interop directly

2. **Should device cleanup be automatic via finalizer?**
   - Current design: Explicit cleanup methods preserved
   - Alternative: Add IDisposable pattern with finalizer

3. **Should MouseInput use interfaces for FlaUI dependency?**
   - Current design: Direct FlaUI usage (simple)
   - Alternative: Abstraction layer (more testable but adds complexity)
