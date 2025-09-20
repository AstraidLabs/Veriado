using System;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Provides access to wall-clock time in UTC.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets the current UTC timestamp.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
