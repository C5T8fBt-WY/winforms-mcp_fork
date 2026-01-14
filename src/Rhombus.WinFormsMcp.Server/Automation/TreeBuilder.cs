using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Builds XML representation of UI element trees with configurable depth,
/// heuristic pruning, and token budget enforcement.
/// </summary>
public class TreeBuilder
{
    private readonly TreeBuilderOptions _options;
    private int _tokenCount;
    private int _elementCount;

    // Approximate tokens per element (for budget estimation)
    private const int TokensPerElement = 25;

    public TreeBuilder(TreeBuilderOptions? options = null)
    {
        _options = options ?? new TreeBuilderOptions();
    }

    /// <summary>
    /// Build an XML tree from the given root element.
    /// </summary>
    public TreeBuildResult BuildTree(AutomationElement root)
    {
        _tokenCount = 0;
        _elementCount = 0;

        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = true
        });

        var dpiScale = GetDpiScaleFactor();
        var timestamp = DateTime.UtcNow.ToString("o");

        writer.WriteStartElement("tree");
        writer.WriteAttributeString("dpi_scale_factor", dpiScale.ToString("F2"));
        writer.WriteAttributeString("timestamp", timestamp);

        // Build the tree recursively
        BuildElementXml(writer, root, 0);

        // Add metadata after traversal
        writer.WriteEndElement(); // tree
        writer.Flush();

        // Update token count attribute (approximate)
        var xml = sb.ToString();
        _tokenCount = EstimateTokens(xml);

        // Re-build with token count if needed
        sb.Clear();
        using var finalWriter = XmlWriter.Create(sb, new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = true
        });

        finalWriter.WriteStartElement("tree");
        finalWriter.WriteAttributeString("dpi_scale_factor", dpiScale.ToString("F2"));
        finalWriter.WriteAttributeString("token_count", _tokenCount.ToString());
        finalWriter.WriteAttributeString("element_count", _elementCount.ToString());
        finalWriter.WriteAttributeString("timestamp", timestamp);

        _elementCount = 0; // Reset for final build
        BuildElementXml(finalWriter, root, 0);

        finalWriter.WriteEndElement();
        finalWriter.Flush();

        return new TreeBuildResult
        {
            Xml = sb.ToString(),
            TokenCount = _tokenCount,
            ElementCount = _elementCount,
            DpiScaleFactor = dpiScale,
            Timestamp = timestamp,
            Truncated = _tokenCount > _options.MaxTokenBudget
        };
    }

    private void BuildElementXml(XmlWriter writer, AutomationElement element, int depth)
    {
        // Check depth limit
        if (depth > _options.MaxDepth)
            return;

        // Check token budget
        if (_elementCount * TokensPerElement > _options.MaxTokenBudget)
            return;

        // Apply heuristic filters
        if (!ShouldIncludeElement(element))
            return;

        _elementCount++;

        // Get element properties safely
        var controlType = GetControlTypeName(element);
        var automationId = SafeGetProperty(() => element.AutomationId) ?? "";
        var name = SafeGetProperty(() => element.Name) ?? "";
        var className = SafeGetProperty(() => element.ClassName) ?? "";
        var isEnabled = SafeGetProperty(() => element.IsEnabled, true);
        var isOffscreen = SafeGetProperty(() => element.IsOffscreen, false);

        // Get bounding rectangle
        var bounds = SafeGetBounds(element);

        // Write element
        writer.WriteStartElement(controlType.ToLowerInvariant());

        if (!string.IsNullOrEmpty(automationId))
            writer.WriteAttributeString("automationId", automationId);

        if (!string.IsNullOrEmpty(name))
            writer.WriteAttributeString("name", TruncateString(name, 100));

        if (!string.IsNullOrEmpty(className))
            writer.WriteAttributeString("className", className);

        writer.WriteAttributeString("isEnabled", isEnabled.ToString().ToLowerInvariant());

        if (isOffscreen)
            writer.WriteAttributeString("isOffscreen", "true");

        if (bounds != null)
        {
            writer.WriteAttributeString("bounds",
                $"{bounds.Value.X},{bounds.Value.Y},{bounds.Value.Width},{bounds.Value.Height}");
        }

        // Get children
        try
        {
            var children = element.FindAllChildren();
            foreach (var child in children)
            {
                BuildElementXml(writer, child, depth + 1);
            }
        }
        catch
        {
            // Ignore errors getting children
        }

        writer.WriteEndElement();
    }

    private bool ShouldIncludeElement(AutomationElement element)
    {
        try
        {
            // Skip invisible elements unless explicitly requested
            if (!_options.IncludeInvisible)
            {
                var isOffscreen = SafeGetProperty(() => element.IsOffscreen, false);
                if (isOffscreen)
                    return false;
            }

            // Skip internal WPF parts (PART_*)
            if (_options.SkipInternalParts)
            {
                var automationId = SafeGetProperty(() => element.AutomationId);
                if (automationId?.StartsWith("PART_", StringComparison.OrdinalIgnoreCase) == true)
                    return false;
            }

            // Skip disabled containers with no enabled descendants
            if (_options.SkipDisabledContainers)
            {
                var isEnabled = SafeGetProperty(() => element.IsEnabled, true);
                if (!isEnabled)
                {
                    // Check if any child is enabled
                    if (!HasEnabledDescendant(element))
                        return false;
                }
            }

            return true;
        }
        catch
        {
            return true; // Include on error
        }
    }

    private bool HasEnabledDescendant(AutomationElement element)
    {
        try
        {
            var children = element.FindAllChildren();
            foreach (var child in children)
            {
                if (SafeGetProperty(() => child.IsEnabled, false))
                    return true;

                if (HasEnabledDescendant(child))
                    return true;
            }
        }
        catch { }

        return false;
    }

    private static string GetControlTypeName(AutomationElement element)
    {
        try
        {
            return element.ControlType.ToString();
        }
        catch
        {
            return "Unknown";
        }
    }

    private static T? SafeGetProperty<T>(Func<T> getter, T? defaultValue = default)
    {
        try
        {
            return getter();
        }
        catch
        {
            return defaultValue;
        }
    }

    private static System.Drawing.Rectangle? SafeGetBounds(AutomationElement element)
    {
        try
        {
            var rect = element.BoundingRectangle;
            if (rect.Width > 0 && rect.Height > 0)
            {
                return new System.Drawing.Rectangle(
                    (int)rect.X,
                    (int)rect.Y,
                    (int)rect.Width,
                    (int)rect.Height
                );
            }
        }
        catch { }

        return null;
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value.Substring(0, maxLength - 3) + "...";
    }

    private static int EstimateTokens(string text)
    {
        // Rough estimation: ~4 characters per token
        return text.Length / 4;
    }

    /// <summary>
    /// Get the system DPI scale factor.
    /// </summary>
    public static double GetDpiScaleFactor()
    {
        try
        {
            var dpi = GetDpiForSystem();
            return dpi / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}

/// <summary>
/// Options for tree building.
/// </summary>
public class TreeBuilderOptions
{
    /// <summary>
    /// Maximum depth to traverse (default: 3).
    /// </summary>
    public int MaxDepth { get; set; } = 3;

    /// <summary>
    /// Maximum token budget for the output (default: 5000).
    /// </summary>
    public int MaxTokenBudget { get; set; } = 5000;

    /// <summary>
    /// Include invisible/offscreen elements (default: false).
    /// </summary>
    public bool IncludeInvisible { get; set; } = false;

    /// <summary>
    /// Skip elements with AutomationId starting with "PART_" (default: true).
    /// </summary>
    public bool SkipInternalParts { get; set; } = true;

    /// <summary>
    /// Skip disabled containers with no enabled descendants (default: true).
    /// </summary>
    public bool SkipDisabledContainers { get; set; } = true;

    /// <summary>
    /// Optional PID to filter windows by process (default: null = all).
    /// </summary>
    public int? FilterByPid { get; set; } = null;
}

/// <summary>
/// Result of building a UI tree.
/// </summary>
public class TreeBuildResult
{
    /// <summary>
    /// The XML representation of the tree.
    /// </summary>
    public required string Xml { get; init; }

    /// <summary>
    /// Estimated token count of the XML.
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// Number of elements in the tree.
    /// </summary>
    public int ElementCount { get; init; }

    /// <summary>
    /// DPI scale factor at time of capture.
    /// </summary>
    public double DpiScaleFactor { get; init; }

    /// <summary>
    /// ISO 8601 timestamp of when tree was captured.
    /// </summary>
    public required string Timestamp { get; init; }

    /// <summary>
    /// Whether the tree was truncated due to token budget.
    /// </summary>
    public bool Truncated { get; init; }
}
