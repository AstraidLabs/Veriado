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

    /// <summary>
    /// Gets or sets an optional optimistic concurrency token based on the file version.
    /// </summary>
    public int? ExpectedVersion { get; init; }
}
