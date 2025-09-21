using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Veriado.Domain.Metadata;

namespace Veriado.Infrastructure.MetadataStore.Kv;

/// <summary>
/// Provides conversions between domain <see cref="ExtendedMetadata"/> values and <see cref="ExtMetadataEntry"/> rows.
/// </summary>
internal static class ExtMetadataMapper
{
    private static readonly JsonSerializerOptions ArraySerializerOptions = new(JsonSerializerDefaults.General);
    private static readonly FieldInfo MetadataValueField = typeof(MetadataValue).GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("MetadataValue internal layout has changed.");

    public static IReadOnlyList<ExtMetadataEntry> ToEntries(Guid fileId, ExtendedMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var entries = new List<ExtMetadataEntry>();

        foreach (var pair in metadata.AsEnumerable())
        {
            var entry = new ExtMetadataEntry
            {
                FileId = fileId,
                FormatId = pair.Key.FormatId,
                PropertyId = pair.Key.PropertyId,
                Kind = pair.Value.Kind,
            };

            var raw = MetadataValueField.GetValue(pair.Value);
            switch (pair.Value.Kind)
            {
                case MetadataValueKind.Null:
                    break;
                case MetadataValueKind.String:
                    entry.TextValue = raw as string;
                    break;
                case MetadataValueKind.StringArray:
                    entry.TextValue = JsonSerializer.Serialize(raw as string[] ?? Array.Empty<string>(), ArraySerializerOptions);
                    break;
                case MetadataValueKind.UInt32:
                case MetadataValueKind.Int32:
                case MetadataValueKind.Double:
                case MetadataValueKind.Boolean:
                    entry.TextValue = Convert.ToString(raw, CultureInfo.InvariantCulture);
                    break;
                case MetadataValueKind.Guid:
                    entry.TextValue = raw is Guid guid && guid != Guid.Empty
                        ? guid.ToString("D", CultureInfo.InvariantCulture)
                        : null;
                    break;
                case MetadataValueKind.FileTime:
                    if (raw is DateTimeOffset dto)
                    {
                        entry.TextValue = dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                    }
                    break;
                case MetadataValueKind.Binary:
                    entry.BinaryValue = raw is byte[] bytes && bytes.Length > 0 ? bytes.ToArray() : null;
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported metadata kind '{pair.Value.Kind}'.");
            }

            entries.Add(entry);
        }

        return entries;
    }

    public static ExtendedMetadata FromEntries(IEnumerable<ExtMetadataEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var builder = ExtendedMetadata.Empty.ToBuilder();

        foreach (var entry in entries)
        {
            var key = new PropertyKey(entry.FormatId, entry.PropertyId);
            var value = entry.Kind switch
            {
                MetadataValueKind.Null => MetadataValue.Null,
                MetadataValueKind.String => string.IsNullOrEmpty(entry.TextValue)
                    ? MetadataValue.Null
                    : MetadataValue.FromString(entry.TextValue),
                MetadataValueKind.StringArray => DeserializeStringArray(entry.TextValue),
                MetadataValueKind.UInt32 => TryParseUInt(entry.TextValue, out var uintValue)
                    ? MetadataValue.FromUInt(uintValue)
                    : MetadataValue.Null,
                MetadataValueKind.Int32 => TryParseInt(entry.TextValue, out var intValue)
                    ? MetadataValue.FromInt(intValue)
                    : MetadataValue.Null,
                MetadataValueKind.Double => TryParseDouble(entry.TextValue, out var doubleValue)
                    ? MetadataValue.FromReal(doubleValue)
                    : MetadataValue.Null,
                MetadataValueKind.Boolean => TryParseBool(entry.TextValue, out var boolValue)
                    ? MetadataValue.FromBool(boolValue)
                    : MetadataValue.Null,
                MetadataValueKind.Guid => Guid.TryParse(entry.TextValue, out var guidValue) && guidValue != Guid.Empty
                    ? MetadataValue.FromGuid(guidValue)
                    : MetadataValue.Null,
                MetadataValueKind.FileTime => TryParseDateTime(entry.TextValue, out var dtoValue)
                    ? MetadataValue.FromFileTime(dtoValue)
                    : MetadataValue.Null,
                MetadataValueKind.Binary => entry.BinaryValue is { Length: > 0 } binary
                    ? MetadataValue.FromBinary(binary)
                    : MetadataValue.Null,
                _ => MetadataValue.Null,
            };

            builder.Set(key, value);
        }

        return builder.Build();
    }

    private static MetadataValue DeserializeStringArray(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return MetadataValue.Null;
        }

        try
        {
            var values = JsonSerializer.Deserialize<string[]>(payload, ArraySerializerOptions) ?? Array.Empty<string>();
            return MetadataValue.FromStringArray(values);
        }
        catch (JsonException)
        {
            return MetadataValue.Null;
        }
    }

    private static bool TryParseUInt(string? text, out uint value)
        => uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParseInt(string? text, out int value)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDouble(string? text, out double value)
        => double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

    private static bool TryParseBool(string? text, out bool value)
        => bool.TryParse(text, out value);

    private static bool TryParseDateTime(string? text, out DateTimeOffset value)
        => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
}
