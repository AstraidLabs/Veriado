using System;

namespace Veriado.Contracts.Files;

/// <summary>
/// Represents the payload required to clear the validity information of a file.
/// </summary>
public sealed class ClearValidityRequest
{
    /// <summary>
    /// Gets or sets the identifier of the file to update.
    /// </summary>
    public Guid FileId { get; init; }
}
