using System;
using Veriado.Domain.Metadata;

namespace Veriado.Infrastructure.MetadataStore.Kv;

/// <summary>
/// Represents a persisted metadata entry stored in the key/value metadata table.
/// </summary>
public sealed class ExtMetadataEntry
{
    /// <summary>
    /// Gets or sets the identifier of the parent file.
    /// </summary>
    public Guid FileId { get; set; }
        = Guid.Empty;

    /// <summary>
    /// Gets or sets the property set format identifier.
    /// </summary>
    public Guid FormatId { get; set; }
        = Guid.Empty;

    /// <summary>
    /// Gets or sets the property identifier within the property set.
    /// </summary>
    public int PropertyId { get; set; }
        = 0;

    /// <summary>
    /// Gets or sets the metadata kind represented by this entry.
    /// </summary>
    public MetadataValueKind Kind { get; set; }
        = MetadataValueKind.Null;

    /// <summary>
    /// Gets or sets the textual representation of the value, if any.
    /// </summary>
    public string? TextValue { get; set; }
        = null;

    /// <summary>
    /// Gets or sets the binary representation of the value, if any.
    /// </summary>
    public byte[]? BinaryValue { get; set; }
        = null;
}
