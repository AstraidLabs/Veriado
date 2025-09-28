namespace Veriado.Contracts.Files;

/// <summary>
/// Represents the payload required to replace the binary content of an existing file.
/// </summary>
public sealed class ReplaceContentRequest
{
    /// <summary>
    /// Gets or sets the identifier of the file to update.
    /// </summary>
    public Guid FileId { get; init; }

    /// <summary>
    /// Gets or sets the new binary content of the file.
    /// </summary>
    public byte[] Content { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets an optional maximum content length constraint.
    /// </summary>
    public int? MaxContentLength { get; init; }
}
