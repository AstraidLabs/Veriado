using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Abstractions;
using Veriado.Domain.Files;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides basic text extraction for plain-text based MIME types.
/// </summary>
internal sealed class TextExtractor : ITextExtractor
{
    private static readonly HashSet<string> PlainTextMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/csv",
        "application/json",
    };

    public Task<string?> ExtractTextAsync(FileEntity file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();

        var mime = file.Mime.Value;
        if (!PlainTextMimeTypes.Contains(mime))
        {
            return Task.FromResult<string?>(null);
        }

        var text = Encoding.UTF8.GetString(file.Content.Bytes);
        return Task.FromResult<string?>(text);
    }
}
