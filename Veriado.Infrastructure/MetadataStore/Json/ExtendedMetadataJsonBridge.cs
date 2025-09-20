using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Veriado.Domain.Metadata;

namespace Veriado.Infrastructure.MetadataStore.Json;

/// <summary>
/// Provides serialization helpers that convert <see cref="ExtendedMetadata"/> instances to a compact JSON representation and back.
/// </summary>
internal static class ExtendedMetadataJsonBridge
{
    private static readonly FieldInfo MetadataValueField = typeof(MetadataValue)
        .GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("MetadataValue internal layout has changed.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    /// <summary>
    /// Serializes the provided metadata instance into a JSON string.
    /// </summary>
    /// <param name="metadata">The metadata value.</param>
    /// <returns>The JSON representation.</returns>
    public static string Serialize(ExtendedMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var entries = metadata.AsEnumerable()
            .Select(pair => MetadataEntry.From(pair.Key, pair.Value))
            .ToArray();
        return JsonSerializer.Serialize(entries, SerializerOptions);
    }

    /// <summary>
    /// Deserializes a JSON payload into an <see cref="ExtendedMetadata"/> instance.
    /// </summary>
    /// <param name="json">The JSON payload.</param>
    /// <returns>The constructed metadata instance.</returns>
    public static ExtendedMetadata Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ExtendedMetadata.Empty;
        }

        var entries = JsonSerializer.Deserialize<IReadOnlyList<MetadataEntry>>(json, SerializerOptions) ?? Array.Empty<MetadataEntry>();
        var builder = ExtendedMetadata.Empty.ToBuilder();
        foreach (var entry in entries)
        {
            var key = new PropertyKey(entry.FormatId, entry.PropertyId);
            builder.Set(key, entry.ToMetadataValue());
        }

        return builder.Build();
    }

    private static object? ExtractRawValue(MetadataValue value) => MetadataValueField.GetValue(value);

    private sealed record MetadataEntry
    {
        public required Guid FormatId { get; init; }

        public required int PropertyId { get; init; }

        public required MetadataValueKind Kind { get; init; }

        public string? StringValue { get; init; }

        public string[]? StringArrayValue { get; init; }

        public uint? UIntValue { get; init; }

        public int? IntValue { get; init; }

        public double? DoubleValue { get; init; }

        public bool? BoolValue { get; init; }

        public Guid? GuidValue { get; init; }

        public DateTimeOffset? FileTimeValue { get; init; }

        public byte[]? BinaryValue { get; init; }

        public static MetadataEntry From(PropertyKey key, MetadataValue value)
        {
            var raw = ExtractRawValue(value);
            return value.Kind switch
            {
                MetadataValueKind.Null => new MetadataEntry
                {
                    FormatId = key.FormatId,
                    PropertyId = key.PropertyId,
                    Kind = MetadataValueKind.Null,
                },
                MetadataValueKind.String => new MetadataEntry
                {
                    FormatId = key.FormatId,
                    PropertyId = key.PropertyId,
                    Kind = MetadataValueKind.String,
                    StringValue = raw as string,
                },
                MetadataValueKind.StringArray => new MetadataEntry
                {
                    FormatId = key.FormatId,
                    PropertyId = key.PropertyId,
                    Kind = MetadataValueKind.StringArray,
                    StringArrayValue = raw is string[] array ? array.ToArray() : Array.Empty<string>(),
                },
                MetadataValueKind.UInt32 => new MetadataEntry
                {
                    FormatId = key.FormatId,
                    PropertyId = key.PropertyId,
                    Kind = MetadataValueKind.UInt32,
                    UIntValue = raw is uint uintValue ? uintValue : null,
                },
                MetadataValueKind.Int32 => new MetadataEntry
                {
                    FormatId = key.FormatId,
                    PropertyId = key.PropertyId,
                    Kind = MetadataValueKind.Int32,
                    IntValue = raw is int intValue ? intValue : null,
                },
                MetadataValueKind.Double => new MetadataEntry
                {
                    FormatId = key.FormatId,
                    PropertyId = key.PropertyId,
                    Kind = MetadataValueKind.Double,
                    DoubleValue = raw is double doubleValue ? doubleValue : null,
                },
                MetadataValueKind.Boolean => new MetadataEntry
                {
                    FormatId = key.FormatId,
                    PropertyId = key.PropertyId,
                    Kind = MetadataValueKind.Boolean,
                    BoolValue = raw is bool boolValue ? boolValue : null,
                },
                MetadataValueKind.Guid => new MetadataEntry
                {
                    FormatId = key.FormatId,
                    PropertyId = key.PropertyId,
                    Kind = MetadataValueKind.Guid,
                    GuidValue = raw is Guid guidValue ? guidValue : null,
                },
                MetadataValueKind.FileTime => new MetadataEntry
                {
                    FormatId = key.FormatId,
                    PropertyId = key.PropertyId,
                    Kind = MetadataValueKind.FileTime,
                    FileTimeValue = raw is DateTimeOffset dto ? dto : null,
                },
                MetadataValueKind.Binary => new MetadataEntry
                {
                    FormatId = key.FormatId,
                    PropertyId = key.PropertyId,
                    Kind = MetadataValueKind.Binary,
                    BinaryValue = raw is byte[] bytes ? bytes.ToArray() : Array.Empty<byte>(),
                },
                _ => throw new InvalidOperationException($"Unsupported metadata kind '{value.Kind}'."),
            };
        }

        public MetadataValue ToMetadataValue()
        {
            return Kind switch
            {
                MetadataValueKind.Null => MetadataValue.Null,
                MetadataValueKind.String => StringValue is null ? MetadataValue.Null : MetadataValue.FromString(StringValue),
                MetadataValueKind.StringArray => StringArrayValue is null
                    ? MetadataValue.Null
                    : MetadataValue.FromStringArray(StringArrayValue),
                MetadataValueKind.UInt32 => UIntValue.HasValue ? MetadataValue.FromUInt(UIntValue.Value) : MetadataValue.Null,
                MetadataValueKind.Int32 => IntValue.HasValue ? MetadataValue.FromInt(IntValue.Value) : MetadataValue.Null,
                MetadataValueKind.Double => DoubleValue.HasValue ? MetadataValue.FromReal(DoubleValue.Value) : MetadataValue.Null,
                MetadataValueKind.Boolean => BoolValue.HasValue ? MetadataValue.FromBool(BoolValue.Value) : MetadataValue.Null,
                MetadataValueKind.Guid => GuidValue.HasValue ? MetadataValue.FromGuid(GuidValue.Value) : MetadataValue.Null,
                MetadataValueKind.FileTime => FileTimeValue.HasValue ? MetadataValue.FromFileTime(FileTimeValue.Value) : MetadataValue.Null,
                MetadataValueKind.Binary => BinaryValue is { Length: > 0 }
                    ? MetadataValue.FromBinary(BinaryValue)
                    : MetadataValue.Null,
                _ => MetadataValue.Null,
            };
        }
    }
}
