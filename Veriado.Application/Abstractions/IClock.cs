using System;

namespace Veriado.Appl.Abstractions;

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
