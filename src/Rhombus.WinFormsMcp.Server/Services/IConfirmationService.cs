using System.Text.Json;

namespace Rhombus.WinFormsMcp.Server.Services;

/// <summary>
/// Interface for managing pending confirmations for destructive actions.
/// Confirmations have a timeout and can only be used once.
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// Create a new pending confirmation for a destructive action.
    /// </summary>
    /// <param name="action">The action type requiring confirmation.</param>
    /// <param name="description">Human-readable description of the action.</param>
    /// <param name="target">Optional target of the action.</param>
    /// <param name="parameters">Optional parameters for the action.</param>
    /// <returns>The created pending confirmation with token.</returns>
    PendingConfirmation Create(string action, string description, string? target, JsonElement? parameters);

    /// <summary>
    /// Get and remove a pending confirmation by token.
    /// Returns null if not found or expired.
    /// </summary>
    /// <param name="token">The confirmation token.</param>
    /// <returns>The pending confirmation, or null if not found or expired.</returns>
    PendingConfirmation? Consume(string token);

    /// <summary>
    /// Get the count of pending confirmations.
    /// </summary>
    int Count { get; }
}
