using System;
using System.Runtime.InteropServices;
using static C5T8fBtWY.WinFormsMcp.Server.Interop.Win32Types;

namespace C5T8fBtWY.WinFormsMcp.Server.Interop;

/// <summary>
/// P/Invoke declarations for input injection APIs.
/// Includes legacy touch injection (Windows 8+) and synthetic pointer (Windows 10 1809+).
/// </summary>
public static class InputInterop
{
    #region Legacy Touch Injection (Windows 8+)

    /// <summary>
    /// Initialize legacy touch injection.
    /// </summary>
    /// <param name="maxCount">Maximum number of simultaneous touch contacts.</param>
    /// <param name="dwMode">Touch feedback mode (TOUCH_FEEDBACK_*).</param>
    /// <returns>True if successful.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InitializeTouchInjection(uint maxCount, uint dwMode);

    /// <summary>
    /// Inject touch input using legacy API.
    /// </summary>
    /// <param name="count">Number of touch contacts.</param>
    /// <param name="contacts">Array of touch contacts.</param>
    /// <returns>True if successful.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InjectTouchInput(uint count, [MarshalAs(UnmanagedType.LPArray)] Win32Types.POINTER_TOUCH_INFO[] contacts);

    #endregion

    #region Synthetic Pointer Device (Windows 10 1809+)

    /// <summary>
    /// Create a synthetic pointer device for touch or pen injection.
    /// </summary>
    /// <param name="pointerType">Type of pointer (PT_TOUCH or PT_PEN).</param>
    /// <param name="maxCount">Maximum number of simultaneous contacts.</param>
    /// <param name="mode">Feedback mode (POINTER_FEEDBACK_*).</param>
    /// <returns>Device handle, or IntPtr.Zero on failure.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateSyntheticPointerDevice(uint pointerType, uint maxCount, uint mode);

    /// <summary>
    /// Inject pen input using synthetic pointer device.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InjectSyntheticPointerInput(IntPtr device, [MarshalAs(UnmanagedType.LPArray)] Win32Types.POINTER_TYPE_INFO_PEN[] pointerInfo, uint count);

    /// <summary>
    /// Inject touch input using synthetic pointer device.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InjectSyntheticPointerInput(IntPtr device, [MarshalAs(UnmanagedType.LPArray)] Win32Types.POINTER_TYPE_INFO_TOUCH[] pointerInfo, uint count);

    /// <summary>
    /// Destroy a synthetic pointer device.
    /// </summary>
    [DllImport("user32.dll", SetLastError = false)]
    public static extern void DestroySyntheticPointerDevice(IntPtr device);

    /// <summary>
    /// Get the coordinate rectangles for a pointer device.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetPointerDeviceRects(IntPtr device, out Win32Types.RECT pointerDeviceRect, out Win32Types.RECT displayRect);

    /// <summary>
    /// Enumerate all pointer devices.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetPointerDevices(ref uint deviceCount, [Out] Win32Types.POINTER_DEVICE_INFO[]? pointerDevices);

    #endregion

    #region Timer

    /// <summary>
    /// Get the system tick count.
    /// </summary>
    [DllImport("kernel32.dll")]
    public static extern uint GetTickCount();

    #endregion
}
