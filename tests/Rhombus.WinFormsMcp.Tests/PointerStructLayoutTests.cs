using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Tests;

/// <summary>
/// Tests to verify that pointer injection struct layouts match Windows SDK expectations.
///
/// These tests are critical because:
/// 1. InjectSyntheticPointerInput requires exact struct layouts matching the Windows SDK
/// 2. Incorrect layouts cause coordinate corruption (e.g., 200px becomes 6px)
/// 3. The union in POINTER_TYPE_INFO requires careful alignment (penInfo/touchInfo at offset 8)
///
/// Reference documentation:
/// - POINTER_INFO: https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-pointer_info
/// - POINTER_PEN_INFO: https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-pointer_pen_info
/// - POINTER_TOUCH_INFO: https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-pointer_touch_info
/// - POINTER_TYPE_INFO: https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-pointer_type_info
/// </summary>
[TestFixture]
public class PointerStructLayoutTests
{
    // Windows SDK type sizes on x64
    // POINTER_INPUT_TYPE = int (4 bytes)
    // UINT32 = uint (4 bytes)
    // POINTER_FLAGS = int (4 bytes)
    // HANDLE = IntPtr (8 bytes on x64)
    // HWND = IntPtr (8 bytes on x64)
    // POINT = 2x LONG = 8 bytes
    // DWORD = uint (4 bytes)
    // UINT64 = ulong (8 bytes)
    // POINTER_BUTTON_CHANGE_TYPE = int (4 bytes)
    // RECT = 4x LONG = 16 bytes

    #region POINTER_INFO Layout Tests

    [Test]
    public void POINTER_INFO_ShouldHaveCorrectSize()
    {
        // POINTER_INFO on x64 with our explicit layout:
        // [FieldOffset(0)]  pointerType: 4
        // [FieldOffset(4)]  pointerId: 4
        // [FieldOffset(8)]  frameId: 4
        // [FieldOffset(12)] pointerFlags: 4
        // [FieldOffset(16)] sourceDevice: 8 (IntPtr)
        // [FieldOffset(24)] hwndTarget: 8 (IntPtr)
        // [FieldOffset(32)] ptPixelLocation: 8 (POINT)
        // [FieldOffset(40)] ptHimetricLocation: 8
        // [FieldOffset(48)] ptPixelLocationRaw: 8
        // [FieldOffset(56)] ptHimetricLocationRaw: 8
        // [FieldOffset(64)] dwTime: 4
        // [FieldOffset(68)] historyCount: 4
        // [FieldOffset(72)] inputData: 4
        // [FieldOffset(76)] dwKeyStates: 4
        // [FieldOffset(80)] performanceCount: 8 (UINT64)
        // [FieldOffset(88)] buttonChangeType: 4
        // Total: 92 bytes + 4 padding = 96 bytes (aligned to 8)

        int actualSize = Marshal.SizeOf<InputInjection.POINTER_INFO>();

        // Expected: 96 bytes on x64 (92 bytes of data + 4 bytes padding for 8-byte alignment)
        Assert.That(actualSize, Is.EqualTo(96));
    }

    [Test]
    public void POINTER_INFO_FieldOffsets_ShouldMatchWindowsSDK()
    {
        // Verify critical field offsets match our explicit layout
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("pointerType").ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("pointerId").ToInt32(), Is.EqualTo(4));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("frameId").ToInt32(), Is.EqualTo(8));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("pointerFlags").ToInt32(), Is.EqualTo(12));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("sourceDevice").ToInt32(), Is.EqualTo(16));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("hwndTarget").ToInt32(), Is.EqualTo(24));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("ptPixelLocation").ToInt32(), Is.EqualTo(32));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("ptHimetricLocation").ToInt32(), Is.EqualTo(40));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("ptPixelLocationRaw").ToInt32(), Is.EqualTo(48));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("ptHimetricLocationRaw").ToInt32(), Is.EqualTo(56));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("dwTime").ToInt32(), Is.EqualTo(64));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("historyCount").ToInt32(), Is.EqualTo(68));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("inputData").ToInt32(), Is.EqualTo(72));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("dwKeyStates").ToInt32(), Is.EqualTo(76));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("performanceCount").ToInt32(), Is.EqualTo(80));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_INFO>("buttonChangeType").ToInt32(), Is.EqualTo(88));
    }

    #endregion

    #region POINTER_PEN_INFO Layout Tests

    [Test]
    public void POINTER_PEN_INFO_ShouldHaveCorrectSize()
    {
        // POINTER_PEN_INFO = POINTER_INFO (96) + penFlags (4) + penMask (4) + pressure (4) + rotation (4) + tiltX (4) + tiltY (4)
        // = 96 + 24 = 120 bytes
        int actualSize = Marshal.SizeOf<InputInjection.POINTER_PEN_INFO>();
        Assert.That(actualSize, Is.EqualTo(120));
    }

    [Test]
    public void POINTER_PEN_INFO_PointerInfoOffset_ShouldBeZero()
    {
        // pointerInfo should be at the start of the struct
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_PEN_INFO>("pointerInfo").ToInt32(), Is.EqualTo(0));
    }

    [Test]
    public void POINTER_PEN_INFO_PenFieldOffsets_ShouldFollowPointerInfo()
    {
        // Pen-specific fields follow POINTER_INFO (96 bytes)
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_PEN_INFO>("penFlags").ToInt32(), Is.EqualTo(96));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_PEN_INFO>("penMask").ToInt32(), Is.EqualTo(100));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_PEN_INFO>("pressure").ToInt32(), Is.EqualTo(104));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_PEN_INFO>("rotation").ToInt32(), Is.EqualTo(108));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_PEN_INFO>("tiltX").ToInt32(), Is.EqualTo(112));
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_PEN_INFO>("tiltY").ToInt32(), Is.EqualTo(116));
    }

    #endregion

    #region POINTER_TOUCH_INFO Layout Tests

    [Test]
    public void POINTER_TOUCH_INFO_ShouldHaveCorrectSize()
    {
        // POINTER_TOUCH_INFO = POINTER_INFO (96) + touchFlags (4) + touchMask (4) + rcContact (16) + rcContactRaw (16) + orientation (4) + pressure (4)
        // = 96 + 48 = 144 bytes
        int actualSize = Marshal.SizeOf<InputInjection.POINTER_TOUCH_INFO>();
        Assert.That(actualSize, Is.EqualTo(144));
    }

    [Test]
    public void POINTER_TOUCH_INFO_PointerInfoOffset_ShouldBeZero()
    {
        // pointerInfo should be at the start of the struct
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_TOUCH_INFO>("pointerInfo").ToInt32(), Is.EqualTo(0));
    }

    #endregion

    #region POINTER_TYPE_INFO Layout Tests (Critical Union Alignment)

    [Test]
    public void POINTER_TYPE_INFO_PEN_TypeOffset_ShouldBeZero()
    {
        // type field at offset 0
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_TYPE_INFO_PEN>("type").ToInt32(), Is.EqualTo(0));
    }

    [Test]
    public void POINTER_TYPE_INFO_PEN_PenInfoOffset_ShouldBeEight()
    {
        // CRITICAL: penInfo must be at offset 8, not offset 4!
        // Windows SDK POINTER_TYPE_INFO has: type (4 bytes) + 4 bytes padding + union at offset 8
        // This was the bug that caused coordinate corruption.
        int offset = Marshal.OffsetOf<InputInjection.POINTER_TYPE_INFO_PEN>("penInfo").ToInt32();
        Assert.That(offset, Is.EqualTo(8), "penInfo MUST be at offset 8 to match Windows SDK union alignment");
    }

    [Test]
    public void POINTER_TYPE_INFO_TOUCH_TypeOffset_ShouldBeZero()
    {
        // type field at offset 0
        Assert.That(Marshal.OffsetOf<InputInjection.POINTER_TYPE_INFO_TOUCH>("type").ToInt32(), Is.EqualTo(0));
    }

    [Test]
    public void POINTER_TYPE_INFO_TOUCH_TouchInfoOffset_ShouldBeEight()
    {
        // CRITICAL: touchInfo must be at offset 8, not offset 4!
        // Same alignment requirement as pen - the union starts at offset 8.
        int offset = Marshal.OffsetOf<InputInjection.POINTER_TYPE_INFO_TOUCH>("touchInfo").ToInt32();
        Assert.That(offset, Is.EqualTo(8), "touchInfo MUST be at offset 8 to match Windows SDK union alignment");
    }

    [Test]
    public void POINTER_TYPE_INFO_PEN_ShouldHaveCorrectSize()
    {
        // POINTER_TYPE_INFO_PEN = type (4) + padding (4) + POINTER_PEN_INFO (120) = 128 bytes
        int actualSize = Marshal.SizeOf<InputInjection.POINTER_TYPE_INFO_PEN>();
        Assert.That(actualSize, Is.EqualTo(128));
    }

    [Test]
    public void POINTER_TYPE_INFO_TOUCH_ShouldHaveCorrectSize()
    {
        // POINTER_TYPE_INFO_TOUCH = type (4) + padding (4) + POINTER_TOUCH_INFO (144) = 152 bytes
        int actualSize = Marshal.SizeOf<InputInjection.POINTER_TYPE_INFO_TOUCH>();
        Assert.That(actualSize, Is.EqualTo(152));
    }

    #endregion

    #region Coordinate Serialization Tests

    /// <summary>
    /// HIMETRIC conversion constant: 1 inch = 2540 HIMETRIC units
    /// At 96 DPI: HIMETRIC = Pixel * (2540 / 96) ≈ Pixel * 26.458
    /// </summary>
    private const double HIMETRIC_PER_PIXEL_96DPI = 2540.0 / 96.0;

    private static int ToHimetric(int pixel) => (int)(pixel * HIMETRIC_PER_PIXEL_96DPI);

    [TestCase(100, 100)]
    [TestCase(200, 300)]
    [TestCase(500, 423)]
    [TestCase(1920, 1080)]
    public void PenStroke_CoordinatesShouldSerializeCorrectly(int x, int y)
    {
        // Create a pen down event
        var typeInfo = new InputInjection.POINTER_TYPE_INFO_PEN
        {
            type = InputInjection.PT_PEN,
            penInfo = new InputInjection.POINTER_PEN_INFO
            {
                pointerInfo = new InputInjection.POINTER_INFO
                {
                    pointerType = (int)InputInjection.PT_PEN,
                    pointerId = 1,
                    pointerFlags = (int)(InputInjection.POINTER_FLAG_DOWN | InputInjection.POINTER_FLAG_INRANGE | InputInjection.POINTER_FLAG_INCONTACT),
                    ptPixelLocation = new InputInjection.POINT { x = x, y = y },
                    ptHimetricLocation = new InputInjection.POINT { x = ToHimetric(x), y = ToHimetric(y) },
                    ptPixelLocationRaw = new InputInjection.POINT { x = x, y = y },
                    ptHimetricLocationRaw = new InputInjection.POINT { x = ToHimetric(x), y = ToHimetric(y) }
                },
                penMask = (int)InputInjection.PEN_MASK_PRESSURE,
                pressure = 512
            }
        };

        // Marshal to bytes and back
        int size = Marshal.SizeOf<InputInjection.POINTER_TYPE_INFO_PEN>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(typeInfo, ptr, false);
            var roundTripped = Marshal.PtrToStructure<InputInjection.POINTER_TYPE_INFO_PEN>(ptr);

            // Verify coordinates survived the round trip
            Assert.That(roundTripped.penInfo.pointerInfo.ptPixelLocation.x, Is.EqualTo(x));
            Assert.That(roundTripped.penInfo.pointerInfo.ptPixelLocation.y, Is.EqualTo(y));
            Assert.That(roundTripped.penInfo.pointerInfo.ptHimetricLocation.x, Is.EqualTo(ToHimetric(x)));
            Assert.That(roundTripped.penInfo.pointerInfo.ptHimetricLocation.y, Is.EqualTo(ToHimetric(y)));
            Assert.That(roundTripped.penInfo.pressure, Is.EqualTo(512u));
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [TestCase(200, 200)]
    [TestCase(400, 300)]
    public void TouchDrag_CoordinatesShouldSerializeCorrectly(int x, int y)
    {
        var typeInfo = new InputInjection.POINTER_TYPE_INFO_TOUCH
        {
            type = InputInjection.PT_TOUCH,
            touchInfo = new InputInjection.POINTER_TOUCH_INFO
            {
                pointerInfo = new InputInjection.POINTER_INFO
                {
                    pointerType = (int)InputInjection.PT_TOUCH,
                    pointerId = 20,
                    pointerFlags = (int)(InputInjection.POINTER_FLAG_DOWN | InputInjection.POINTER_FLAG_INRANGE | InputInjection.POINTER_FLAG_INCONTACT | InputInjection.POINTER_FLAG_CONFIDENCE),
                    ptPixelLocation = new InputInjection.POINT { x = x, y = y },
                    ptHimetricLocation = new InputInjection.POINT { x = ToHimetric(x), y = ToHimetric(y) },
                    ptPixelLocationRaw = new InputInjection.POINT { x = x, y = y },
                    ptHimetricLocationRaw = new InputInjection.POINT { x = ToHimetric(x), y = ToHimetric(y) }
                },
                touchFlags = 0,
                touchMask = 0,
                rcContact = new InputInjection.RECT { left = x - 5, top = y - 5, right = x + 5, bottom = y + 5 },
                rcContactRaw = new InputInjection.RECT { left = x - 5, top = y - 5, right = x + 5, bottom = y + 5 },
                pressure = 512
            }
        };

        // Marshal to bytes and back
        int size = Marshal.SizeOf<InputInjection.POINTER_TYPE_INFO_TOUCH>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(typeInfo, ptr, false);
            var roundTripped = Marshal.PtrToStructure<InputInjection.POINTER_TYPE_INFO_TOUCH>(ptr);

            // Verify coordinates survived the round trip
            Assert.That(roundTripped.touchInfo.pointerInfo.ptPixelLocation.x, Is.EqualTo(x));
            Assert.That(roundTripped.touchInfo.pointerInfo.ptPixelLocation.y, Is.EqualTo(y));
            Assert.That(roundTripped.touchInfo.pointerInfo.ptHimetricLocation.x, Is.EqualTo(ToHimetric(x)));
            Assert.That(roundTripped.touchInfo.pointerInfo.ptHimetricLocation.y, Is.EqualTo(ToHimetric(y)));
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    #endregion

    #region Raw Byte Verification Tests

    [Test]
    public void POINTER_TYPE_INFO_PEN_ByteLayout_ShouldMatchExpected()
    {
        // Create a known struct and verify bytes at critical offsets
        var typeInfo = new InputInjection.POINTER_TYPE_INFO_PEN
        {
            type = InputInjection.PT_PEN, // 3
            penInfo = new InputInjection.POINTER_PEN_INFO
            {
                pointerInfo = new InputInjection.POINTER_INFO
                {
                    pointerType = (int)InputInjection.PT_PEN, // 3
                    pointerId = 0xDEADBEEF,
                    ptPixelLocation = new InputInjection.POINT { x = 0x12345678, y = unchecked((int)0x9ABCDEF0) }
                }
            }
        };

        int size = Marshal.SizeOf<InputInjection.POINTER_TYPE_INFO_PEN>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(typeInfo, ptr, false);
            byte[] bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, size);

            // Verify type at offset 0 (little-endian: 03 00 00 00)
            Assert.That(bytes[0], Is.EqualTo(0x03), "type byte 0");
            Assert.That(bytes[1], Is.EqualTo(0x00), "type byte 1");
            Assert.That(bytes[2], Is.EqualTo(0x00), "type byte 2");
            Assert.That(bytes[3], Is.EqualTo(0x00), "type byte 3");

            // Bytes 4-7 should be padding (could be anything, but typically zero)

            // Verify pointerType at offset 8 (inside penInfo.pointerInfo)
            Assert.That(bytes[8], Is.EqualTo(0x03), "pointerType at offset 8");  // PT_PEN = 3
            Assert.That(bytes[9], Is.EqualTo(0x00));
            Assert.That(bytes[10], Is.EqualTo(0x00));
            Assert.That(bytes[11], Is.EqualTo(0x00));

            // Verify pointerId at offset 12 (0xDEADBEEF in little-endian: EF BE AD DE)
            Assert.That(bytes[12], Is.EqualTo(0xEF), "pointerId byte 0");
            Assert.That(bytes[13], Is.EqualTo(0xBE), "pointerId byte 1");
            Assert.That(bytes[14], Is.EqualTo(0xAD), "pointerId byte 2");
            Assert.That(bytes[15], Is.EqualTo(0xDE), "pointerId byte 3");

            // Verify ptPixelLocation.x at offset 40 (8 + 32) = 0x12345678
            Assert.That(bytes[40], Is.EqualTo(0x78), "ptPixelLocation.x byte 0");
            Assert.That(bytes[41], Is.EqualTo(0x56), "ptPixelLocation.x byte 1");
            Assert.That(bytes[42], Is.EqualTo(0x34), "ptPixelLocation.x byte 2");
            Assert.That(bytes[43], Is.EqualTo(0x12), "ptPixelLocation.x byte 3");

            // Verify ptPixelLocation.y at offset 44 = 0x9ABCDEF0
            Assert.That(bytes[44], Is.EqualTo(0xF0), "ptPixelLocation.y byte 0");
            Assert.That(bytes[45], Is.EqualTo(0xDE), "ptPixelLocation.y byte 1");
            Assert.That(bytes[46], Is.EqualTo(0xBC), "ptPixelLocation.y byte 2");
            Assert.That(bytes[47], Is.EqualTo(0x9A), "ptPixelLocation.y byte 3");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    #endregion

    #region Golden Path Event Sequence Tests

    [Test]
    public void PenStrokeSequence_ShouldHaveCorrectFlags()
    {
        // Based on Gemini's golden path analysis
        uint pointerId = 10;
        int startX = 100, startY = 100;
        int endX = 100, endY = 110;

        // Frame 1: PEN DOWN
        var downFlags = InputInjection.POINTER_FLAG_NEW |
                        InputInjection.POINTER_FLAG_INRANGE |
                        InputInjection.POINTER_FLAG_INCONTACT |
                        InputInjection.POINTER_FLAG_DOWN |
                        InputInjection.POINTER_FLAG_PRIMARY;

        var penDown = new InputInjection.POINTER_TYPE_INFO_PEN
        {
            type = InputInjection.PT_PEN,
            penInfo = new InputInjection.POINTER_PEN_INFO
            {
                pointerInfo = new InputInjection.POINTER_INFO
                {
                    pointerType = (int)InputInjection.PT_PEN,
                    pointerId = pointerId,
                    pointerFlags = (int)downFlags,
                    ptPixelLocation = new InputInjection.POINT { x = startX, y = startY },
                    ptHimetricLocation = new InputInjection.POINT { x = ToHimetric(startX), y = ToHimetric(startY) }
                },
                penMask = (int)InputInjection.PEN_MASK_PRESSURE,
                pressure = 512
            }
        };

        // Verify DOWN has NEW flag
        Assert.That(penDown.penInfo.pointerInfo.pointerFlags & (int)InputInjection.POINTER_FLAG_NEW, Is.Not.EqualTo(0),
            "DOWN event should have NEW flag");
        Assert.That(penDown.penInfo.pointerInfo.pointerFlags & (int)InputInjection.POINTER_FLAG_INCONTACT, Is.Not.EqualTo(0),
            "DOWN event should have INCONTACT flag");

        // Frame 2: PEN MOVE (UPDATE)
        var updateFlags = InputInjection.POINTER_FLAG_INRANGE |
                          InputInjection.POINTER_FLAG_INCONTACT |
                          InputInjection.POINTER_FLAG_UPDATE |
                          InputInjection.POINTER_FLAG_PRIMARY;

        var penMove = new InputInjection.POINTER_TYPE_INFO_PEN
        {
            type = InputInjection.PT_PEN,
            penInfo = new InputInjection.POINTER_PEN_INFO
            {
                pointerInfo = new InputInjection.POINTER_INFO
                {
                    pointerType = (int)InputInjection.PT_PEN,
                    pointerId = pointerId,
                    pointerFlags = (int)updateFlags,
                    ptPixelLocation = new InputInjection.POINT { x = startX, y = startY + 5 },
                    ptHimetricLocation = new InputInjection.POINT { x = ToHimetric(startX), y = ToHimetric(startY + 5) }
                },
                penMask = (int)InputInjection.PEN_MASK_PRESSURE,
                pressure = 1024
            }
        };

        // Verify UPDATE does NOT have NEW flag
        Assert.That(penMove.penInfo.pointerInfo.pointerFlags & (int)InputInjection.POINTER_FLAG_NEW, Is.EqualTo(0),
            "UPDATE event should NOT have NEW flag");
        Assert.That(penMove.penInfo.pointerInfo.pointerFlags & (int)InputInjection.POINTER_FLAG_UPDATE, Is.Not.EqualTo(0),
            "UPDATE event should have UPDATE flag");

        // Frame 3: PEN UP
        var upFlags = InputInjection.POINTER_FLAG_INRANGE |
                      InputInjection.POINTER_FLAG_UP |
                      InputInjection.POINTER_FLAG_PRIMARY;

        var penUp = new InputInjection.POINTER_TYPE_INFO_PEN
        {
            type = InputInjection.PT_PEN,
            penInfo = new InputInjection.POINTER_PEN_INFO
            {
                pointerInfo = new InputInjection.POINTER_INFO
                {
                    pointerType = (int)InputInjection.PT_PEN,
                    pointerId = pointerId,
                    pointerFlags = (int)upFlags,
                    ptPixelLocation = new InputInjection.POINT { x = endX, y = endY },
                    ptHimetricLocation = new InputInjection.POINT { x = ToHimetric(endX), y = ToHimetric(endY) }
                },
                penMask = (int)InputInjection.PEN_MASK_PRESSURE,
                pressure = 0
            }
        };

        // Verify UP does NOT have INCONTACT
        Assert.That(penUp.penInfo.pointerInfo.pointerFlags & (int)InputInjection.POINTER_FLAG_INCONTACT, Is.EqualTo(0),
            "UP event should NOT have INCONTACT flag");
        Assert.That(penUp.penInfo.pointerInfo.pointerFlags & (int)InputInjection.POINTER_FLAG_UP, Is.Not.EqualTo(0),
            "UP event should have UP flag");
    }

    [Test]
    public void TouchDragSequence_ShouldHaveConfidenceFlag()
    {
        // Touch events require CONFIDENCE flag for WPF Manipulation events
        uint pointerId = 20;

        var downFlags = InputInjection.POINTER_FLAG_NEW |
                        InputInjection.POINTER_FLAG_INRANGE |
                        InputInjection.POINTER_FLAG_INCONTACT |
                        InputInjection.POINTER_FLAG_DOWN |
                        InputInjection.POINTER_FLAG_PRIMARY |
                        InputInjection.POINTER_FLAG_CONFIDENCE;

        var touchDown = new InputInjection.POINTER_TYPE_INFO_TOUCH
        {
            type = InputInjection.PT_TOUCH,
            touchInfo = new InputInjection.POINTER_TOUCH_INFO
            {
                pointerInfo = new InputInjection.POINTER_INFO
                {
                    pointerType = (int)InputInjection.PT_TOUCH,
                    pointerId = pointerId,
                    pointerFlags = (int)downFlags,
                    ptPixelLocation = new InputInjection.POINT { x = 200, y = 200 },
                    ptHimetricLocation = new InputInjection.POINT { x = ToHimetric(200), y = ToHimetric(200) }
                },
                rcContact = new InputInjection.RECT { left = 195, top = 195, right = 205, bottom = 205 }
            }
        };

        // Verify CONFIDENCE flag is set
        Assert.That(touchDown.touchInfo.pointerInfo.pointerFlags & (int)InputInjection.POINTER_FLAG_CONFIDENCE, Is.Not.EqualTo(0),
            "Touch DOWN should have CONFIDENCE flag for WPF Manipulation events");
    }

    #endregion
}
