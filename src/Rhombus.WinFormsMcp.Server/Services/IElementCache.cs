using FlaUI.Core.AutomationElements;

namespace Rhombus.WinFormsMcp.Server.Services;

/// <summary>
/// Interface for caching UI Automation elements with staleness detection.
/// Elements are cached with auto-generated IDs (elem_1, elem_2, etc.).
/// </summary>
public interface IElementCache
{
    /// <summary>
    /// Cache an element and return its auto-generated ID (elem_N).
    /// </summary>
    /// <param name="element">The AutomationElement to cache.</param>
    /// <returns>The cache ID assigned to the element.</returns>
    string Cache(AutomationElement element);

    /// <summary>
    /// Get cached element by ID, or null if not found.
    /// </summary>
    /// <param name="elementId">The cache ID (e.g., "elem_1").</param>
    /// <returns>The cached element, or null if not found.</returns>
    AutomationElement? Get(string elementId);

    /// <summary>
    /// Remove a single cached element.
    /// </summary>
    /// <param name="elementId">The cache ID to remove.</param>
    void Clear(string elementId);

    /// <summary>
    /// Remove all cached elements.
    /// </summary>
    void ClearAll();

    /// <summary>
    /// Check if an element reference is stale (no longer valid in the UI tree).
    /// </summary>
    /// <param name="elementId">The cache ID to check.</param>
    /// <returns>True if the element is stale or not found, false if valid.</returns>
    bool IsStale(string elementId);

    /// <summary>
    /// Get the total number of cached elements.
    /// </summary>
    int Count { get; }
}
