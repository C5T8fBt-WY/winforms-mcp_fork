using FlaUI.Core.AutomationElements;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Detects UI state changes by comparing tree hashes before and after interactions.
/// </summary>
public class StateChangeDetector
{
    private readonly TreeHasher _hasher = new();

    /// <summary>
    /// Capture the current state of a UI tree for later comparison.
    /// </summary>
    public TreeSnapshot CaptureSnapshot(AutomationElement root)
    {
        return _hasher.CaptureSnapshot(root);
    }

    /// <summary>
    /// Compare two snapshots and generate a diff summary.
    /// </summary>
    public StateChangeResult CompareSnapshots(TreeSnapshot before, TreeSnapshot after)
    {
        if (before.Hash == after.Hash)
        {
            return new StateChangeResult
            {
                StateChanged = false,
                DiffSummary = "No changes detected."
            };
        }

        // Generate diff summary by comparing element sets
        var added = new List<string>();
        var removed = new List<string>();
        var modified = new List<string>();

        // Find removed elements (in before but not in after)
        foreach (var kvp in before.Elements)
        {
            if (!after.Elements.TryGetValue(kvp.Key, out var afterElement))
            {
                removed.Add(FormatElement(kvp.Value));
            }
            else if (kvp.Value.StateHash != afterElement.StateHash)
            {
                // Same element key but different state
                var changes = GetStateChanges(kvp.Value, afterElement);
                if (changes.Count > 0)
                    modified.Add($"{FormatElement(kvp.Value)} ({string.Join(", ", changes)})");
            }
        }

        // Find added elements (in after but not in before)
        foreach (var kvp in after.Elements)
        {
            if (!before.Elements.ContainsKey(kvp.Key))
            {
                added.Add(FormatElement(kvp.Value));
            }
        }

        var parts = new List<string>();
        if (added.Count > 0)
            parts.Add($"Added: {added.Count} ({string.Join(", ", added)})");
        if (removed.Count > 0)
            parts.Add($"Removed: {removed.Count} ({string.Join(", ", removed)})");
        if (modified.Count > 0)
            parts.Add($"Modified: {modified.Count} ({string.Join(", ", modified)})");

        return new StateChangeResult
        {
            StateChanged = true,
            AddedCount = added.Count,
            RemovedCount = removed.Count,
            ModifiedCount = modified.Count,
            DiffSummary = parts.Count > 0 ? string.Join(". ", parts) + "." : "Structure changed but no specific differences identified."
        };
    }

    private static string FormatElement(ElementInfo element)
    {
        if (!string.IsNullOrEmpty(element.AutomationId))
            return $"{element.ControlType}[{element.AutomationId}]";
        if (!string.IsNullOrEmpty(element.Name))
            return $"{element.ControlType}['{element.Name}']";
        return element.ControlType;
    }

    private static List<string> GetStateChanges(ElementInfo before, ElementInfo after)
    {
        var changes = new List<string>();

        if (before.IsEnabled != after.IsEnabled)
            changes.Add($"IsEnabled={after.IsEnabled}");
        if (before.IsOffscreen != after.IsOffscreen)
            changes.Add($"IsOffscreen={after.IsOffscreen}");
        if (before.Value != after.Value)
            changes.Add($"Value='{after.Value ?? ""}'");

        return changes;
    }
}

/// <summary>
/// Computes hashes of UI trees for change detection.
/// </summary>
public class TreeHasher
{
    private const int MaxDepth = 5;

    /// <summary>
    /// Capture a snapshot of the UI tree including hash and element information.
    /// </summary>
    public TreeSnapshot CaptureSnapshot(AutomationElement root)
    {
        var elements = new Dictionary<string, ElementInfo>();
        var sb = new StringBuilder();

        TraverseTree(root, 0, sb, elements, "");

        var hash = ComputeHash(sb.ToString());

        return new TreeSnapshot
        {
            Hash = hash,
            Elements = elements,
            CapturedAt = DateTime.UtcNow
        };
    }

    private void TraverseTree(AutomationElement element, int depth, StringBuilder sb, Dictionary<string, ElementInfo> elements, string path)
    {
        if (depth > MaxDepth)
            return;

        try
        {
            var automationId = SafeGet(() => element.AutomationId) ?? "";
            var name = SafeGet(() => element.Name) ?? "";
            var controlType = SafeGet(() => element.ControlType.ToString()) ?? "Unknown";
            var isEnabled = SafeGet(() => element.IsEnabled, true);
            var isOffscreen = SafeGet(() => element.IsOffscreen, false);
            var value = GetValue(element);

            // Generate unique key for this element
            var key = GenerateElementKey(path, controlType, automationId, name);

            // Add to hash input
            sb.AppendLine($"{depth}|{controlType}|{automationId}|{name}|{isEnabled}|{isOffscreen}|{value}");

            // Create element info
            var info = new ElementInfo
            {
                AutomationId = automationId,
                Name = name,
                ControlType = controlType,
                IsEnabled = isEnabled,
                IsOffscreen = isOffscreen,
                Value = value,
                StateHash = ComputeHash($"{isEnabled}|{isOffscreen}|{value}")
            };

            // Add to elements dict (handle duplicates by appending index)
            var finalKey = key;
            var index = 1;
            while (elements.ContainsKey(finalKey))
            {
                finalKey = $"{key}#{index++}";
            }
            elements[finalKey] = info;

            // Traverse children
            var children = SafeGet(() => element.FindAllChildren());
            if (children != null)
            {
                var childIndex = 0;
                foreach (var child in children)
                {
                    TraverseTree(child, depth + 1, sb, elements, $"{finalKey}/{childIndex++}");
                }
            }
        }
        catch
        {
            // Skip elements that throw exceptions
        }
    }

    private static string GenerateElementKey(string path, string controlType, string automationId, string name)
    {
        if (!string.IsNullOrEmpty(automationId))
            return $"{path}/{controlType}[{automationId}]";
        if (!string.IsNullOrEmpty(name))
            return $"{path}/{controlType}['{TruncateName(name)}']";
        return $"{path}/{controlType}";
    }

    private static string TruncateName(string name)
    {
        if (name.Length <= 20)
            return name;
        return name.Substring(0, 17) + "...";
    }

    private static string? GetValue(AutomationElement element)
    {
        try
        {
            var patterns = element.Patterns;
            if (patterns.Value.IsSupported)
                return patterns.Value.Pattern.Value.Value;
            if (patterns.Toggle.IsSupported)
                return patterns.Toggle.Pattern.ToggleState.Value.ToString();
            if (patterns.Selection.IsSupported)
            {
                var selected = patterns.Selection.Pattern.Selection.Value;
                if (selected != null && selected.Length > 0)
                    return string.Join(",", Array.ConvertAll(selected, e => e.Name ?? ""));
            }
        }
        catch { }
        return null;
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes).Substring(0, 16); // Truncate for readability
    }

    private static T? SafeGet<T>(Func<T> getter, T? defaultValue = default)
    {
        try { return getter(); }
        catch { return defaultValue; }
    }
}

/// <summary>
/// A snapshot of the UI tree at a point in time.
/// </summary>
public class TreeSnapshot
{
    /// <summary>
    /// Hash of the tree structure and state.
    /// </summary>
    public required string Hash { get; init; }

    /// <summary>
    /// Dictionary of element keys to element info.
    /// </summary>
    public required Dictionary<string, ElementInfo> Elements { get; init; }

    /// <summary>
    /// When the snapshot was captured.
    /// </summary>
    public DateTime CapturedAt { get; init; }
}

/// <summary>
/// Information about a single element for diff purposes.
/// </summary>
public class ElementInfo
{
    public string? AutomationId { get; init; }
    public string? Name { get; init; }
    public required string ControlType { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsOffscreen { get; init; }
    public string? Value { get; init; }
    public required string StateHash { get; init; }
}

/// <summary>
/// Result of comparing two tree snapshots.
/// </summary>
public class StateChangeResult
{
    /// <summary>
    /// Whether any state change was detected.
    /// </summary>
    public bool StateChanged { get; init; }

    /// <summary>
    /// Number of elements added.
    /// </summary>
    public int AddedCount { get; init; }

    /// <summary>
    /// Number of elements removed.
    /// </summary>
    public int RemovedCount { get; init; }

    /// <summary>
    /// Number of elements with modified state.
    /// </summary>
    public int ModifiedCount { get; init; }

    /// <summary>
    /// Human-readable summary of changes.
    /// </summary>
    public required string DiffSummary { get; init; }
}
