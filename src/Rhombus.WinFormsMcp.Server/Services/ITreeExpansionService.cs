namespace C5T8fBtWY.WinFormsMcp.Server.Services;

/// <summary>
/// Interface for tracking which elements are marked for expansion in the UI tree.
/// The tree builder will expand marked elements regardless of depth limit.
/// </summary>
public interface ITreeExpansionService
{
    /// <summary>
    /// Mark an element for expansion.
    /// </summary>
    /// <param name="elementKey">AutomationId or Name of the element.</param>
    void Mark(string elementKey);

    /// <summary>
    /// Check if an element is marked for expansion.
    /// </summary>
    /// <param name="elementKey">AutomationId or Name of the element.</param>
    /// <returns>True if the element is marked for expansion.</returns>
    bool IsMarked(string elementKey);

    /// <summary>
    /// Get all elements marked for expansion.
    /// </summary>
    /// <returns>A read-only collection of marked element keys.</returns>
    IReadOnlyCollection<string> GetAll();

    /// <summary>
    /// Clear the expansion mark for a specific element.
    /// </summary>
    /// <param name="elementKey">AutomationId or Name of the element.</param>
    void Clear(string elementKey);

    /// <summary>
    /// Clear all expansion marks.
    /// </summary>
    void ClearAll();

    /// <summary>
    /// Get the count of marked elements.
    /// </summary>
    int Count { get; }
}
