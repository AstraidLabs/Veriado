using System;

namespace Veriado.Contracts.Files;

/// <summary>
/// Represents a single sort specification for file grid queries.
/// </summary>
public sealed record FileSortSpecDto
{
    /// <summary>
    /// Gets the field name to sort by.
    /// </summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the sort direction is descending.
    /// </summary>
    public bool Descending { get; init; }
        = false;
}
