using System;
using System.Globalization;
using System.Text.Json;
using Veriado.Domain.Metadata;

namespace Veriado.Domain.Search;

/// <summary>
/// Provides helpers for converting structured metadata into compact, human-readable text.
/// </summary>
public static class MetadataTextFormatter
{
    private static readonly (FileAttributesFlags Flag, string DisplayName)[] AttributeDisplayNames =
    {
        (FileAttributesFlags.ReadOnly, "Read-only"),
        (FileAttributesFlags.Hidden, "Hidden"),
        (FileAttributesFlags.System, "System"),
        (FileAttributesFlags.Directory, "Directory"),
        (FileAttributesFlags.Archive, "Archive"),
        (FileAttributesFlags.Device, "Device"),
        (FileAttributesFlags.Temporary, "Temporary"),
        (FileAttributesFlags.SparseFile, "Sparse"),
        (FileAttributesFlags.ReparsePoint, "Reparse"),
        (FileAttributesFlags.Compressed, "Compressed"),
        (FileAttributesFlags.Offline, "Offline"),
        (FileAttributesFlags.NotContentIndexed, "Not indexed"),
        (FileAttributesFlags.Encrypted, "Encrypted"),
        (FileAttributesFlags.IntegrityStream, "Integrity stream"),
        (FileAttributesFlags.NoScrubData, "No scrub data"),
    };

    /// <summary>
    /// Builds a compact metadata summary from a structured metadata instance.
    /// </summary>
    /// <param name="metadata">The structured metadata.</param>
    /// <returns>The condensed metadata text or <see langword="null"/> when unavailable.</returns>
    public static string? BuildSummary(SearchDocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var parts = new List<string>(6);

        if (metadata.System.Owner is { Length: > 0 } owner)
        {
            parts.Add($"Owner: {owner}");
        }

        if (metadata.SizeBytes > 0)
        {
            parts.Add($"Size: {FormatSize(metadata.SizeBytes)}");
        }

        var attributeSummary = BuildAttributeSummary(metadata.System.Attributes);
        if (attributeSummary is { Length: > 0 })
        {
            parts.Add($"Attributes: {attributeSummary}");
        }

        if (metadata.System.HardLinkCount is { } hardLinks && hardLinks > 1)
        {
            parts.Add($"Hard links: {hardLinks}");
        }

        if (metadata.System.AlternateDataStreamCount is { } ads && ads > 0)
        {
            parts.Add($"Alternate streams: {ads}");
        }

        if (metadata.Title is { Length: > 0 } title && !string.Equals(title, metadata.FileName, StringComparison.Ordinal))
        {
            parts.Add($"Title: {title}");
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return string.Join(" • ", parts);
    }

    /// <summary>
    /// Builds a metadata summary from a JSON payload.
    /// </summary>
    /// <param name="metadataJson">The metadata JSON payload.</param>
    /// <returns>The condensed metadata text or <see langword="null"/> when unavailable.</returns>
    public static string? BuildSummary(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<SearchDocumentMetadata>(metadataJson, SearchDocument.MetadataSerializerOptions);
            return metadata is null ? null : BuildSummary(metadata);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatSize(long sizeBytes)
    {
        const long OneKb = 1024L;
        if (sizeBytes < OneKb)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{sizeBytes} B");
        }

        var units = new[] { "KB", "MB", "GB", "TB", "PB" };
        double value = sizeBytes;
        var unitIndex = 0;
        while (value >= OneKb && unitIndex < units.Length - 1)
        {
            value /= OneKb;
            unitIndex++;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", value, units[unitIndex]);
    }

    private static string? BuildAttributeSummary(FileAttributesFlags attributes)
    {
        if (attributes == FileAttributesFlags.None || attributes == FileAttributesFlags.Normal)
        {
            return null;
        }

        var list = new List<string>(AttributeDisplayNames.Length);
        foreach (var (flag, displayName) in AttributeDisplayNames)
        {
            if ((attributes & flag) == flag)
            {
                list.Add(displayName);
            }
        }

        return list.Count == 0 ? null : string.Join(", ", list);
    }
}
