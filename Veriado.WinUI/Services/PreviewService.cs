using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Services.Files;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class PreviewService : IPreviewService
{
    private const int SnippetLength = 512;
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
        if (content is null || content.Content is null || content.Content.Length == 0)
        {
            return null;
        }

        var snippet = Encoding.UTF8.GetString(content.Content, 0, Math.Min(content.Content.Length, SnippetLength));
        var result = new PreviewResult(snippet, null);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
        return result;
    }
}
