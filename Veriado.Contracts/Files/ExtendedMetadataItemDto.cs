using System;
using System.Collections.Generic;

namespace Veriado.Contracts.Files;

/// <summary>
/// Represents a metadata key/value pair originating from an external consumer.
/// </summary>
public sealed class ExtendedMetadataItemDto
{
    /// <summary>
    /// Gets or sets the property set format identifier.
    /// </summary>
    public Guid FormatId { get; init; }

    /// <summary>
    /// Gets or sets the property identifier within the property set.
    /// </summary>
    public int PropertyId { get; init; }

    /// <summary>
    /// Gets or sets the metadata value payload.
    /// </summary>
    public MetadataValueDto? Value { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the metadata value should be removed.
    /// </summary>
    public bool Remove { get; init; }
}

/// <summary>
/// Represents a discriminated union describing a metadata value payload.
/// </summary>
public sealed class MetadataValueDto
{
    /// <summary>
    /// Gets or sets the value kind represented by the DTO.
    /// </summary>
    public MetadataValueDtoKind Kind { get; init; }

    /// <summary>
    /// Gets or sets a string value when <see cref="Kind"/> equals <see cref="MetadataValueDtoKind.String"/>.
    /// </summary>
    public string? StringValue { get; init; }

    /// <summary>
    /// Gets or sets a string array when <see cref="Kind"/> equals <see cref="MetadataValueDtoKind.StringArray"/>.
    /// </summary>
    public IReadOnlyList<string>? StringArrayValue { get; init; }

    /// <summary>
    /// Gets or sets an unsigned 32-bit integer when the kind equals <see cref="MetadataValueDtoKind.UInt32"/>.
    /// </summary>
    public uint? UInt32Value { get; init; }

    /// <summary>
    /// Gets or sets a signed 32-bit integer when the kind equals <see cref="MetadataValueDtoKind.Int32"/>.
    /// </summary>
    public int? Int32Value { get; init; }

    /// <summary>
    /// Gets or sets a double precision floating point value when the kind equals <see cref="MetadataValueDtoKind.Double"/>.
    /// </summary>
    public double? DoubleValue { get; init; }

    /// <summary>
    /// Gets or sets a Boolean value when the kind equals <see cref="MetadataValueDtoKind.Boolean"/>.
    /// </summary>
    public bool? BooleanValue { get; init; }

    /// <summary>
    /// Gets or sets a GUID value when the kind equals <see cref="MetadataValueDtoKind.Guid"/>.
    /// </summary>
    public Guid? GuidValue { get; init; }

    /// <summary>
    /// Gets or sets a timestamp when the kind equals <see cref="MetadataValueDtoKind.FileTime"/>.
    /// </summary>
    public DateTimeOffset? FileTimeValue { get; init; }

    /// <summary>
    /// Gets or sets the binary payload when the kind equals <see cref="MetadataValueDtoKind.Binary"/>.
    /// </summary>
    public byte[]? BinaryValue { get; init; }
}

/// <summary>
/// Enumerates the supported metadata value kinds for DTO exchanges.
/// </summary>
public enum MetadataValueDtoKind
{
    /// <summary>
    /// Represents a null value.
    /// </summary>
    Null,

    /// <summary>
    /// Represents a string value.
    /// </summary>
    String,

    /// <summary>
    /// Represents an array of strings.
    /// </summary>
    StringArray,

    /// <summary>
    /// Represents an unsigned 32-bit integer value.
    /// </summary>
    UInt32,

    /// <summary>
    /// Represents a signed 32-bit integer value.
    /// </summary>
    Int32,

    /// <summary>
    /// Represents a double precision floating point value.
    /// </summary>
    Double,

    /// <summary>
    /// Represents a Boolean value.
    /// </summary>
    Boolean,

    /// <summary>
    /// Represents a GUID value.
    /// </summary>
    Guid,

    /// <summary>
    /// Represents a UTC timestamp stored as a file time.
    /// </summary>
    FileTime,

    /// <summary>
    /// Represents a binary payload.
    /// </summary>
    Binary,
}
