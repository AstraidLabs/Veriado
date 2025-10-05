using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Veriado.Domain.Metadata;

namespace Veriado.Domain.Search;

/// <summary>
/// Represents the document projected to the search index.
/// </summary>
public sealed record SearchDocument(
    Guid FileId,
    string Title,
    string Mime,
    string? Author,
    string FileName,
    long SizeBytes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc,
    string? MetadataJson,
    string? MetadataText = null,
    string? ContentHash = null)
{
    /// <summary>
    /// Gets serializer options used when emitting the metadata JSON payload.
    /// </summary>
    public static JsonSerializerOptions MetadataSerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Serialises the supplied metadata using the canonical options.
    /// </summary>
    /// <param name="metadata">The metadata model.</param>
    /// <returns>The JSON payload.</returns>
    public static string SerializeMetadata(SearchDocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return JsonSerializer.Serialize(metadata, MetadataSerializerOptions);
    }

    /// <summary>
    /// Builds a compact human-readable metadata summary suitable for indexing and display.
    /// </summary>
    /// <param name="metadata">The structured metadata model.</param>
    /// <returns>The condensed metadata text or <see langword="null"/> when unavailable.</returns>
    public static string? BuildMetadataText(SearchDocumentMetadata metadata)
        => MetadataTextFormatter.BuildSummary(metadata);

    /// <summary>
    /// Builds a compact human-readable metadata summary from a JSON payload.
    /// </summary>
    /// <param name="metadataJson">The JSON payload previously produced by <see cref="SerializeMetadata"/>.</param>
    /// <returns>The condensed metadata text or <see langword="null"/> when unavailable.</returns>
    public static string? BuildMetadataText(string? metadataJson)
        => MetadataTextFormatter.BuildSummary(metadataJson);
}

/// <summary>
/// Represents structured metadata associated with a search document.
/// </summary>
/// <param name="FileName">The logical file name.</param>
/// <param name="Extension">The file extension without a dot.</param>
/// <param name="Mime">The MIME type.</param>
/// <param name="Title">The user-facing title, if available.</param>
/// <param name="Author">The document author, if available.</param>
/// <param name="SizeBytes">The file size in bytes.</param>
/// <param name="System">The system metadata snapshot.</param>
public sealed record SearchDocumentMetadata(
    string FileName,
    string Extension,
    string Mime,
    string? Title,
    string? Author,
    long SizeBytes,
    SearchDocumentSystemMetadata System);

/// <summary>
/// Represents file-system specific metadata included in the search document.
/// </summary>
/// <param name="Attributes">The file attribute flags.</param>
/// <param name="Owner">The owning security identifier, if known.</param>
/// <param name="HardLinkCount">The number of hard links, if known.</param>
/// <param name="AlternateDataStreamCount">The number of alternate data streams, if known.</param>
/// <param name="CreatedUtc">The creation timestamp in UTC.</param>
/// <param name="LastWriteUtc">The last write timestamp in UTC.</param>
/// <param name="LastAccessUtc">The last access timestamp in UTC.</param>
public sealed record SearchDocumentSystemMetadata(
    FileAttributesFlags Attributes,
    string? Owner,
    uint? HardLinkCount,
    uint? AlternateDataStreamCount,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastWriteUtc,
    DateTimeOffset LastAccessUtc);
