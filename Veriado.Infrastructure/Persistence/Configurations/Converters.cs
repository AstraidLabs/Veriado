using System;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Veriado.Domain.Metadata;
using Veriado.Domain.Search;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.MetadataStore.Json;

namespace Veriado.Infrastructure.Persistence.Configurations;

/// <summary>
/// Provides reusable value converters and comparers for EF Core mappings.
/// </summary>
internal static class Converters
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static readonly ValueConverter<Guid, byte[]> GuidToBlob = new(
        guid => guid.ToByteArray(),
        blob => new Guid(blob));

    public static readonly ValueConverter<FileName, string> FileNameToString = new(
        name => name.Value,
        value => FileName.From(value));

    public static readonly ValueConverter<FileExtension, string> FileExtensionToString = new(
        ext => ext.Value,
        value => FileExtension.From(value));

    public static readonly ValueConverter<MimeType, string> MimeTypeToString = new(
        mime => mime.Value,
        value => MimeType.From(value));

    public static readonly ValueConverter<FileHash, string> FileHashToString = new(
        hash => hash.Value,
        value => FileHash.From(value));

    public static readonly ValueConverter<ByteSize, long> ByteSizeToLong = new(
        size => size.Value,
        value => ByteSize.From(value));

    public static readonly ValueConverter<UtcTimestamp, string> UtcTimestampToString = new(
        timestamp => timestamp.Value.ToString("O", CultureInfo.InvariantCulture),
        value => UtcTimestamp.From(DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));

    public static readonly ValueConverter<UtcTimestamp?, string?> NullableUtcTimestampToString = new(
        timestamp => timestamp.HasValue
            ? timestamp.Value.Value.ToString("O", CultureInfo.InvariantCulture)
            : null,
        value => value == null
            ? null
            : UtcTimestamp.From(DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));

    public static readonly ValueConverter<DateTimeOffset, string> DateTimeOffsetToString = new(
        value => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    public static readonly ValueConverter<DateTimeOffset?, string?> NullableDateTimeOffsetToString = new(
        value => value.HasValue
            ? value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            : null,
        text => text == null
            ? null
            : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    public static readonly ValueConverter<FileAttributesFlags, int> FileAttributesToInt = new(
        flags => (int)flags,
        value => (FileAttributesFlags)value);

    public static readonly ValueConverter<ExtendedMetadata, string> ExtendedMetadataToJson = new(
        metadata => ExtendedMetadataJsonBridge.Serialize(metadata),
        json => ExtendedMetadataJsonBridge.Deserialize(json));

    public static readonly ValueConverter<Fts5Policy, string> FtsPolicyToJson = new(
        policy => JsonSerializer.Serialize(policy, JsonOptions),
        json => string.IsNullOrWhiteSpace(json)
            ? Fts5Policy.Default
            : JsonSerializer.Deserialize<Fts5Policy>(json, JsonOptions) ?? Fts5Policy.Default);

    public static readonly ValueComparer<ExtendedMetadata> ExtendedMetadataComparer = new(
        (left, right) => left.Equals(right),
        value => value.GetHashCode(),
        value => ExtendedMetadataJsonBridge.Deserialize(ExtendedMetadataJsonBridge.Serialize(value)));
}
