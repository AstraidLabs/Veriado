using Veriado.Domain.Metadata;

namespace Veriado.Application.Files.Commands;

/// <summary>
/// Represents a patch instruction for extended metadata.
/// </summary>
/// <param name="Key">The property key.</param>
/// <param name="Value">The metadata value to assign, or <see langword="null"/> to remove the key.</param>
public sealed record MetadataPatch(PropertyKey Key, MetadataValue? Value)
{
    /// <summary>
    /// Gets a value indicating whether the patch removes the metadata entry.
    /// </summary>
    public bool IsRemoval => Value is null;
}
