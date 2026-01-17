namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Result of an expand/collapse operation.
/// </summary>
public class ExpandCollapseResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? PreviousState { get; init; }
    public string? CurrentState { get; init; }
}

/// <summary>
/// Direction for scrolling.
/// </summary>
public enum ScrollDirection
{
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// Amount to scroll.
/// </summary>
public enum ScrollAmount
{
    SmallDecrement,
    LargeDecrement
}

/// <summary>
/// Result of a scroll operation.
/// </summary>
public class ScrollResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public double HorizontalScrollPercent { get; init; }
    public double VerticalScrollPercent { get; init; }
    public bool HorizontalChanged { get; init; }
    public bool VerticalChanged { get; init; }
}

/// <summary>
/// Detailed state of an element.
/// </summary>
public class ElementStateResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    // Basic properties
    public string? AutomationId { get; init; }
    public string? Name { get; init; }
    public string? ClassName { get; init; }
    public string? ControlType { get; init; }

    // State properties
    public bool IsEnabled { get; init; }
    public bool IsOffscreen { get; init; }
    public bool IsKeyboardFocusable { get; init; }
    public bool HasKeyboardFocus { get; init; }

    // Bounding rectangle
    public BoundingRectInfo? BoundingRect { get; set; }

    // Value pattern
    public string? Value { get; set; }
    public bool? IsReadOnly { get; set; }

    // Toggle pattern
    public string? ToggleState { get; set; }

    // Selection pattern
    public bool? IsSelected { get; set; }

    // Range value pattern (slider, progress bar)
    public double? RangeValue { get; set; }
    public double? RangeMinimum { get; set; }
    public double? RangeMaximum { get; set; }

    // DPI info
    public double DpiScaleFactor { get; set; } = 1.0;
}

/// <summary>
/// Bounding rectangle information.
/// </summary>
public class BoundingRectInfo
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

/// <summary>
/// Search criteria used to find an element. Used for self-healing element relocation.
/// </summary>
public class ElementSearchCriteria
{
    /// <summary>
    /// AutomationId used to find the element (most stable identifier).
    /// </summary>
    public string? AutomationId { get; init; }

    /// <summary>
    /// Name used to find the element.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// ClassName used to find the element.
    /// </summary>
    public string? ClassName { get; init; }

    /// <summary>
    /// ControlType of the element.
    /// </summary>
    public string? ControlType { get; init; }

    /// <summary>
    /// Last known bounding rectangle (for heuristic matching).
    /// </summary>
    public System.Drawing.Rectangle? LastKnownBounds { get; init; }

    /// <summary>
    /// Original element ID in the cache (for reference).
    /// </summary>
    public string? OriginalElementId { get; init; }
}

/// <summary>
/// Result of attempting to relocate a stale element.
/// </summary>
public class RelocateResult
{
    /// <summary>
    /// Whether relocation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The relocated element if successful.
    /// </summary>
    public FlaUI.Core.AutomationElements.AutomationElement? RelocatedElement { get; set; }

    /// <summary>
    /// Which criteria matched the relocated element.
    /// </summary>
    public string? MatchedBy { get; set; }

    /// <summary>
    /// Human-readable message about the relocation attempt.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Suggestions for recovery if relocation failed.
    /// </summary>
    public string[]? Suggestions { get; set; }
}

/// <summary>
/// Result of getting element at a screen coordinate.
/// Used for native grounding - verifying what's actually at a visual coordinate.
/// </summary>
public class ElementAtPointResult
{
    /// <summary>
    /// Whether an element was found at the point.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if lookup failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// AutomationId of the element (if available).
    /// </summary>
    public string? AutomationId { get; init; }

    /// <summary>
    /// Name of the element.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// ControlType of the element (Button, Edit, etc.).
    /// </summary>
    public string? ControlType { get; init; }

    /// <summary>
    /// RuntimeId as dot-separated string - the UIA identity for tracking.
    /// </summary>
    public string? RuntimeId { get; init; }

    /// <summary>
    /// Process ID of the element's owning process.
    /// </summary>
    public int? Pid { get; init; }

    /// <summary>
    /// Name of the process that owns this element.
    /// </summary>
    public string? ProcessName { get; init; }

    /// <summary>
    /// ClassName of the element.
    /// </summary>
    public string? ClassName { get; init; }

    /// <summary>
    /// Native window handle (HWND) in hex format.
    /// </summary>
    public string? NativeWindowHandle { get; init; }

    /// <summary>
    /// Bounding rectangle of the element.
    /// </summary>
    public BoundingRectInfo? BoundingRect { get; init; }
}
