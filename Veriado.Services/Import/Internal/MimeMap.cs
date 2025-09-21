using System;
using System.Collections.Generic;

namespace Veriado.Services.Import.Internal;

/// <summary>
/// Provides a simple file extension to MIME type mapping for folder imports.
/// </summary>
internal static class MimeMap
{
    private static readonly IReadOnlyDictionary<string, string> _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["txt"] = "text/plain",
        ["md"] = "text/markdown",
        ["csv"] = "text/csv",
        ["json"] = "application/json",
        ["xml"] = "application/xml",
        ["pdf"] = "application/pdf",
        ["doc"] = "application/msword",
        ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ["xls"] = "application/vnd.ms-excel",
        ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ["ppt"] = "application/vnd.ms-powerpoint",
        ["pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["gif"] = "image/gif",
        ["bmp"] = "image/bmp",
        ["tif"] = "image/tiff",
        ["tiff"] = "image/tiff",
        ["rtf"] = "application/rtf",
        ["zip"] = "application/zip",
        ["7z"] = "application/x-7z-compressed",
        ["rar"] = "application/vnd.rar",
        ["eml"] = "message/rfc822",
    };

    /// <summary>
    /// Gets the MIME type for the supplied extension, falling back to <c>application/octet-stream</c>.
    /// </summary>
    /// <param name="extension">The extension without a leading dot.</param>
    /// <returns>The matching MIME type.</returns>
    public static string GetMimeType(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "application/octet-stream";
        }

        var normalized = extension.TrimStart('.');
        if (_map.TryGetValue(normalized, out var mime))
        {
            return mime;
        }

        return string.Concat("application/", normalized);
    }
}
