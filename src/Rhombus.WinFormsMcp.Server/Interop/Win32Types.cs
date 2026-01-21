using System;
using System.Runtime.InteropServices;

namespace Rhombus.WinFormsMcp.Server.Interop;

/// <summary>
/// Win32 API structure definitions for input injection and window management.
/// These are used by multiple components (TouchInput, PenInput, WindowInterop).
/// </summary>
public static class Win32Types
{
    /// <summary>
    /// Represents a point in screen coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;

        public POINT(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    /// <summary>
    /// Represents a rectangle in screen coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;

        public int Width => right - left;
        public int Height => bottom - top;

        public RECT(int left, int top, int right, int bottom)
        {
            this.left = left;
            this.top = top;
            this.right = right;
            this.bottom = bottom;
        }
    }

    /// <summary>
    /// Core pointer information structure used by both touch and pen injection.
    /// Sequential layout allows the marshaller to handle IntPtr sizing correctly
    /// for both 32-bit (IntPtr=4 bytes) and 64-bit (IntPtr=8 bytes) processes.
    /// </summary>
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

    /// <summary>
    /// Touch contact information for touch injection.
    /// </summary>
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

    /// <summary>
    /// Pen information for pen injection.
    /// </summary>
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

    /// <summary>
    /// Wrapper for pen POINTER_TYPE_INFO with correct alignment.
    /// Uses explicit layout with 8-byte alignment for nested struct containing IntPtr.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct POINTER_TYPE_INFO_PEN
    {
        [FieldOffset(0)]
        public uint type;  // PT_PEN = 3
        [FieldOffset(8)]  // 8-byte alignment for nested struct containing IntPtr
        public POINTER_PEN_INFO penInfo;
    }

    /// <summary>
    /// Wrapper for touch POINTER_TYPE_INFO with correct alignment.
    /// Uses explicit layout with 8-byte alignment for nested struct containing IntPtr.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct POINTER_TYPE_INFO_TOUCH
    {
        [FieldOffset(0)]
        public uint type;  // PT_TOUCH = 2
        [FieldOffset(8)]  // 8-byte alignment for nested struct containing IntPtr
        public POINTER_TOUCH_INFO touchInfo;
    }

    /// <summary>
    /// Pointer device information for device enumeration.
    /// </summary>
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
}
