using System;

namespace Rhombus.WinFormsMcp.Server.Abstractions;

/// <summary>
/// Abstraction for time-related operations to enable testing.
/// Production code uses SystemTimeProvider, tests can inject mock implementations.
/// </summary>
public interface ITimeProvider
{
    /// <summary>
    /// Gets the current UTC date and time.
    /// </summary>
    DateTime UtcNow { get; }
}

/// <summary>
/// Default implementation that uses the system clock.
/// </summary>
public class SystemTimeProvider : ITimeProvider
{
    /// <summary>
    /// Singleton instance for production use.
    /// </summary>
    public static readonly SystemTimeProvider Instance = new();

    /// <summary>
    /// Gets the current UTC date and time from the system clock.
    /// </summary>
    public DateTime UtcNow => DateTime.UtcNow;
}
