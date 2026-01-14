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
