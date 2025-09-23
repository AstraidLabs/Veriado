using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Services.Abstractions;
using Veriado.Services.Files;

namespace Veriado.Services;

public sealed class PreviewService : IPreviewService
{
    private const int SnippetLength = 512;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private readonly IFileContentService _contentService;
    private readonly ICacheService _cache;

    public PreviewService(IFileContentService contentService, ICacheService cache)
    {
        _contentService = contentService ?? throw new ArgumentNullException(nameof(contentService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<PreviewResult?> GetPreviewAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"preview::{fileId}";
        if (_cache.TryGetValue(cacheKey, out PreviewResult? cached) && cached is not null)
        {
            return cached;
        }

        var content = await _contentService.GetContentAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return null;
        }

        var snippet = TryGetSnippet(content) ?? BuildFallback(content);
        var result = new PreviewResult(snippet, null);
        _cache.Set(cacheKey, result, CacheDuration);
        return result;
    }

    private static string? TryGetSnippet(Contracts.Files.FileContentResponseDto content)
    {
        if (content.Content is null || content.Content.Length == 0)
        {
            return null;
        }

        if (!IsTextual(content.Mime))
        {
            return null;
        }

        try
        {
            var length = Math.Min(content.Content.Length, SnippetLength);
            var snippet = Encoding.UTF8.GetString(content.Content, 0, length);
            return snippet;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsTextual(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return false;
        }

        if (mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return mime.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mime.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || mime.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase)
            || mime.Equals("application/x-javascript", StringComparison.OrdinalIgnoreCase)
            || mime.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
            || mime.Equals("application/sql", StringComparison.OrdinalIgnoreCase)
            || mime.Equals("application/rss+xml", StringComparison.OrdinalIgnoreCase)
            || mime.Equals("application/atom+xml", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFallback(Contracts.Files.FileContentResponseDto content)
    {
        var mime = content.Mime;
        if (!string.IsNullOrWhiteSpace(mime))
        {
            if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return "🖼️ Náhled obrázku bude dostupný po otevření.";
            }

            if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            {
                return "🎞️ Video soubor";
            }

            if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            {
                return "🎧 Audio soubor";
            }

            if (mime.Contains("zip", StringComparison.OrdinalIgnoreCase) || mime.Contains("compressed", StringComparison.OrdinalIgnoreCase))
            {
                return "🗜️ Archivovaný obsah";
            }

            if (mime.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                return "📄 Dokument PDF";
            }

            if (mime.StartsWith("application/vnd.openxmlformats-officedocument", StringComparison.OrdinalIgnoreCase)
                || mime.StartsWith("application/msword", StringComparison.OrdinalIgnoreCase)
                || mime.StartsWith("application/vnd.ms-", StringComparison.OrdinalIgnoreCase))
            {
                return "📄 Dokument Office";
            }

            if (mime.StartsWith("application/vnd.oasis.opendocument", StringComparison.OrdinalIgnoreCase))
            {
                return "📄 Dokument OpenDocument";
            }
        }

        var extension = string.IsNullOrWhiteSpace(content.Extension)
            ? string.Empty
            : $".{content.Extension}";

        return string.Format(CultureInfo.CurrentCulture, "📄 {0}{1}", content.Name, extension);
    }
}
