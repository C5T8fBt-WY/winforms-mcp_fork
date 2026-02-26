namespace C5T8fBtWY.WinFormsMcp.Server.Interop;

/// <summary>
/// Win32 API constants for pointer input injection.
/// These constants are used by touch, pen, and mouse input injection code.
/// </summary>
public static class Win32Constants
{
    #region Pointer Types

    /// <summary>Pointer type: Touch input.</summary>
    public const uint PT_TOUCH = 0x00000002;

    /// <summary>Pointer type: Pen/stylus input.</summary>
    public const uint PT_PEN = 0x00000003;

    #endregion

    #region Pointer Flags

    /// <summary>No flags.</summary>
    public const uint POINTER_FLAG_NONE = 0x00000000;

    /// <summary>New pointer (first contact).</summary>
    public const uint POINTER_FLAG_NEW = 0x00000001;

    /// <summary>Pointer is in range (hovering or in contact).</summary>
    public const uint POINTER_FLAG_INRANGE = 0x00000002;

    /// <summary>Pointer is in contact with the digitizer.</summary>
    public const uint POINTER_FLAG_INCONTACT = 0x00000004;

    /// <summary>First (primary) button is pressed.</summary>
    public const uint POINTER_FLAG_FIRSTBUTTON = 0x00000010;

    /// <summary>Second button is pressed.</summary>
    public const uint POINTER_FLAG_SECONDBUTTON = 0x00000020;

    /// <summary>Third button is pressed.</summary>
    public const uint POINTER_FLAG_THIRDBUTTON = 0x00000040;

    /// <summary>Fourth button is pressed.</summary>
    public const uint POINTER_FLAG_FOURTHBUTTON = 0x00000080;

    /// <summary>Fifth button is pressed.</summary>
    public const uint POINTER_FLAG_FIFTHBUTTON = 0x00000100;

    /// <summary>This is the primary pointer.</summary>
    public const uint POINTER_FLAG_PRIMARY = 0x00002000;

    /// <summary>Input is confident (not accidental).</summary>
    public const uint POINTER_FLAG_CONFIDENCE = 0x00004000;

    /// <summary>Pointer was canceled.</summary>
    public const uint POINTER_FLAG_CANCELED = 0x00008000;

    /// <summary>Pointer down event.</summary>
    public const uint POINTER_FLAG_DOWN = 0x00010000;

    /// <summary>Pointer update event.</summary>
    public const uint POINTER_FLAG_UPDATE = 0x00020000;

    /// <summary>Pointer up event.</summary>
    public const uint POINTER_FLAG_UP = 0x00040000;

    /// <summary>Wheel input.</summary>
    public const uint POINTER_FLAG_WHEEL = 0x00080000;

    /// <summary>Horizontal wheel input.</summary>
    public const uint POINTER_FLAG_HWHEEL = 0x00100000;

    /// <summary>Capture changed.</summary>
    public const uint POINTER_FLAG_CAPTURECHANGED = 0x00200000;

    /// <summary>Has transform.</summary>
    public const uint POINTER_FLAG_HASTRANSFORM = 0x00400000;

    #endregion

    #region Touch Feedback Modes

    /// <summary>Default touch feedback.</summary>
    public const uint TOUCH_FEEDBACK_DEFAULT = 0x1;

    /// <summary>Indirect touch feedback (shows at injected location).</summary>
    public const uint TOUCH_FEEDBACK_INDIRECT = 0x2;

    /// <summary>No touch feedback.</summary>
    public const uint TOUCH_FEEDBACK_NONE = 0x3;

    #endregion

    #region Touch Mask Flags

    /// <summary>No touch mask.</summary>
    public const uint TOUCH_MASK_NONE = 0x00000000;

    /// <summary>Contact area is valid.</summary>
    public const uint TOUCH_MASK_CONTACTAREA = 0x00000001;

    /// <summary>Orientation is valid.</summary>
    public const uint TOUCH_MASK_ORIENTATION = 0x00000002;

    /// <summary>Pressure is valid.</summary>
    public const uint TOUCH_MASK_PRESSURE = 0x00000004;

    #endregion

    #region Pen Flags

    /// <summary>No pen flags.</summary>
    public const uint PEN_FLAG_NONE = 0x00000000;

    /// <summary>Barrel button is pressed.</summary>
    public const uint PEN_FLAG_BARREL = 0x00000001;

    /// <summary>Pen is inverted (eraser end).</summary>
    public const uint PEN_FLAG_INVERTED = 0x00000002;

    /// <summary>Eraser mode.</summary>
    public const uint PEN_FLAG_ERASER = 0x00000004;

    #endregion

    #region Pen Mask Flags

    /// <summary>No pen mask.</summary>
    public const uint PEN_MASK_NONE = 0x00000000;

    /// <summary>Pressure is valid.</summary>
    public const uint PEN_MASK_PRESSURE = 0x00000001;

    /// <summary>Rotation is valid.</summary>
    public const uint PEN_MASK_ROTATION = 0x00000002;

    /// <summary>Tilt X is valid.</summary>
    public const uint PEN_MASK_TILT_X = 0x00000004;

    /// <summary>Tilt Y is valid.</summary>
    public const uint PEN_MASK_TILT_Y = 0x00000008;

    #endregion

    #region Pointer Feedback Modes

    /// <summary>Default pointer feedback.</summary>
    public const uint POINTER_FEEDBACK_DEFAULT = 0x1;

    /// <summary>Indirect pointer feedback.</summary>
    public const uint POINTER_FEEDBACK_INDIRECT = 0x2;

    /// <summary>No pointer feedback.</summary>
    public const uint POINTER_FEEDBACK_NONE = 0x3;

    #endregion

    #region System Metrics

    /// <summary>Screen width.</summary>
    public const int SM_CXSCREEN = 0;

    /// <summary>Screen height.</summary>
    public const int SM_CYSCREEN = 1;

    /// <summary>Virtual screen X origin.</summary>
    public const int SM_XVIRTUALSCREEN = 76;

    /// <summary>Virtual screen Y origin.</summary>
    public const int SM_YVIRTUALSCREEN = 77;

    /// <summary>Logical pixels per inch (X).</summary>
    public const int LOGPIXELSX = 88;

    /// <summary>Logical pixels per inch (Y).</summary>
    public const int LOGPIXELSY = 90;

    #endregion

    #region ShowWindow Commands

    /// <summary>Minimize window.</summary>
    public const int SW_MINIMIZE = 2;

    /// <summary>Maximize window.</summary>
    public const int SW_MAXIMIZE = 3;

    /// <summary>Restore window.</summary>
    public const int SW_RESTORE = 9;

    #endregion
}
