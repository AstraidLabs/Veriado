using System;
using System.Collections.Generic;
using System.Linq;

namespace Veriado.Domain.Metadata;

/// <summary>
/// Represents a discriminated union for metadata values supporting common Windows property types.
/// </summary>
public readonly struct MetadataValue : IEquatable<MetadataValue>
{
    private readonly object? _value;

    private MetadataValue(MetadataValueKind kind, object? value)
    {
        Kind = kind;
        _value = value;
    }

    /// <summary>
    /// Gets the kind of metadata value represented.
    /// </summary>
    public MetadataValueKind Kind { get; }

    /// <summary>
    /// Gets an instance representing a null metadata value.
    /// </summary>
    public static MetadataValue Null { get; } = new(MetadataValueKind.Null, null);

    /// <summary>
    /// Creates a metadata value storing a string.
    /// </summary>
    /// <param name="value">The string value.</param>
    /// <returns>The created metadata value.</returns>
    public static MetadataValue FromString(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new MetadataValue(MetadataValueKind.String, value.Trim());
    }

    /// <summary>
    /// Creates a metadata value storing a string array.
    /// </summary>
    /// <param name="values">The string values.</param>
    /// <returns>The created metadata value.</returns>
    public static MetadataValue FromStringArray(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var materialized = values.Select(v =>
        {
            if (v is null)
            {
                throw new ArgumentException("String array cannot contain null values.", nameof(values));
            }

            var trimmed = v.Trim();
            if (trimmed.Length == 0)
            {
                throw new ArgumentException("String array cannot contain empty values.", nameof(values));
            }

            return trimmed;
        }).ToArray();

        return new MetadataValue(MetadataValueKind.StringArray, materialized);
    }

    /// <summary>
    /// Creates a metadata value storing an unsigned integer.
    /// </summary>
    /// <param name="value">The unsigned integer value.</param>
    /// <returns>The created metadata value.</returns>
    public static MetadataValue FromUInt(uint value) => new(MetadataValueKind.UInt32, value);

    /// <summary>
    /// Creates a metadata value storing a signed integer.
    /// </summary>
    /// <param name="value">The signed integer value.</param>
    /// <returns>The created metadata value.</returns>
    public static MetadataValue FromInt(int value) => new(MetadataValueKind.Int32, value);

    /// <summary>
    /// Creates a metadata value storing a double precision number.
    /// </summary>
    /// <param name="value">The floating point value.</param>
    /// <returns>The created metadata value.</returns>
    public static MetadataValue FromReal(double value) => new(MetadataValueKind.Double, value);

    /// <summary>
    /// Creates a metadata value storing a Boolean.
    /// </summary>
    /// <param name="value">The Boolean value.</param>
    /// <returns>The created metadata value.</returns>
    public static MetadataValue FromBool(bool value) => new(MetadataValueKind.Boolean, value);

    /// <summary>
    /// Creates a metadata value storing a GUID.
    /// </summary>
    /// <param name="value">The GUID value.</param>
    /// <returns>The created metadata value.</returns>
    public static MetadataValue FromGuid(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("GUID value must be non-empty.", nameof(value));
        }

        return new MetadataValue(MetadataValueKind.Guid, value);
    }

    /// <summary>
    /// Creates a metadata value storing a UTC file time.
    /// </summary>
    /// <param name="value">The timestamp value.</param>
    /// <returns>The created metadata value.</returns>
    public static MetadataValue FromFileTime(DateTimeOffset value)
    {
        return new MetadataValue(MetadataValueKind.FileTime, value.ToUniversalTime());
    }

    /// <summary>
    /// Creates a metadata value storing binary data.
    /// </summary>
    /// <param name="value">The binary data.</param>
    /// <returns>The created metadata value.</returns>
    public static MetadataValue FromBinary(ReadOnlySpan<byte> value)
    {
        var buffer = value.ToArray();
        return new MetadataValue(MetadataValueKind.Binary, buffer);
    }

    /// <summary>
    /// Attempts to retrieve the stored string value.
    /// </summary>
    /// <param name="value">The extracted string value.</param>
    /// <returns><see langword="true"/> if the value was retrieved; otherwise <see langword="false"/>.</returns>
    public bool TryGetString(out string? value)
    {
        if (Kind == MetadataValueKind.String)
        {
            value = (string)_value!;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve the stored string array value.
    /// </summary>
    /// <param name="value">The extracted string array value.</param>
    /// <returns><see langword="true"/> if the value was retrieved; otherwise <see langword="false"/>.</returns>
    public bool TryGetStringArray(out string[]? value)
    {
        if (Kind == MetadataValueKind.StringArray)
        {
            var array = (string[])_value!;
            value = array.ToArray();
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve the stored GUID value.
    /// </summary>
    /// <param name="value">The extracted GUID value.</param>
    /// <returns><see langword="true"/> if the value was retrieved; otherwise <see langword="false"/>.</returns>
    public bool TryGetGuid(out Guid value)
    {
        if (Kind == MetadataValueKind.Guid)
        {
            value = (Guid)_value!;
            return true;
        }

        value = Guid.Empty;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve a <see cref="DateTimeOffset"/> from the metadata value.
    /// </summary>
    /// <param name="value">The extracted timestamp.</param>
    /// <returns><see langword="true"/> if the value was retrieved; otherwise <see langword="false"/>.</returns>
    public bool TryGetFileTime(out DateTimeOffset value)
    {
        if (Kind == MetadataValueKind.FileTime)
        {
            value = (DateTimeOffset)_value!;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve the stored binary payload.
    /// </summary>
    /// <param name="value">The extracted binary payload.</param>
    /// <returns><see langword="true"/> if the value was retrieved; otherwise <see langword="false"/>.</returns>
    public bool TryGetBinary(out byte[]? value)
    {
        if (Kind == MetadataValueKind.Binary)
        {
            var buffer = (byte[])_value!;
            value = buffer.ToArray();
            return true;
        }

        value = null;
        return false;
    }

    /// <inheritdoc />
    public bool Equals(MetadataValue other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            MetadataValueKind.Null => true,
            MetadataValueKind.String => string.Equals((string)_value!, (string)other._value!, StringComparison.Ordinal),
            MetadataValueKind.StringArray => ((string[])_value!).SequenceEqual((string[])other._value!),
            MetadataValueKind.UInt32 => EqualityComparer<uint>.Default.Equals((uint)_value!, (uint)other._value!),
            MetadataValueKind.Int32 => EqualityComparer<int>.Default.Equals((int)_value!, (int)other._value!),
            MetadataValueKind.Double => EqualityComparer<double>.Default.Equals((double)_value!, (double)other._value!),
            MetadataValueKind.Boolean => EqualityComparer<bool>.Default.Equals((bool)_value!, (bool)other._value!),
            MetadataValueKind.Guid => EqualityComparer<Guid>.Default.Equals((Guid)_value!, (Guid)other._value!),
            MetadataValueKind.FileTime => EqualityComparer<DateTimeOffset>.Default.Equals((DateTimeOffset)_value!, (DateTimeOffset)other._value!),
            MetadataValueKind.Binary => ((byte[])_value!).SequenceEqual((byte[])other._value!),
            _ => false,
        };
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is MetadataValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Kind switch
        {
            MetadataValueKind.Null => Kind.GetHashCode(),
            MetadataValueKind.String => HashCode.Combine(Kind, (string)_value!),
            MetadataValueKind.StringArray => ((string[])_value!).Aggregate(Kind.GetHashCode(), HashCode.Combine),
            MetadataValueKind.UInt32 => HashCode.Combine(Kind, (uint)_value!),
            MetadataValueKind.Int32 => HashCode.Combine(Kind, (int)_value!),
            MetadataValueKind.Double => HashCode.Combine(Kind, (double)_value!),
            MetadataValueKind.Boolean => HashCode.Combine(Kind, (bool)_value!),
            MetadataValueKind.Guid => HashCode.Combine(Kind, (Guid)_value!),
            MetadataValueKind.FileTime => HashCode.Combine(Kind, (DateTimeOffset)_value!),
            MetadataValueKind.Binary => ((byte[])_value!).Aggregate(Kind.GetHashCode(), HashCode.Combine),
            _ => Kind.GetHashCode(),
        };
    }
}

/// <summary>
/// Enumerates the supported metadata value kinds.
/// </summary>
public enum MetadataValueKind
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
    /// Represents an unsigned 32-bit integer.
    /// </summary>
    UInt32,

    /// <summary>
    /// Represents a signed 32-bit integer.
    /// </summary>
    Int32,

    /// <summary>
    /// Represents a double-precision floating point number.
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
    /// Represents a file time stored as a UTC <see cref="DateTimeOffset"/>.
    /// </summary>
    FileTime,

    /// <summary>
    /// Represents binary data.
    /// </summary>
    Binary,
}
