using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Extracts textual content from PDF documents using UglyToad.PdfPig.
/// </summary>
internal sealed class PdfTextExtractor
{
    private const int MaxPages = 500;
    private const int MaxCharacters = 200_000;

    public ValueTask<string?> TryExtractAsync(Stream content, string mime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!string.Equals(mime, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult<string?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!content.CanSeek)
        {
            content = CreateMemoryStream(content, cancellationToken);
        }

        content.Seek(0, SeekOrigin.Begin);

        try
        {
            using var document = PdfDocument.Open(content, ParsingOptions.LenientParsing);
            var builder = new StringBuilder();
            var processedPages = 0;

            foreach (Page page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                processedPages++;
                if (processedPages > MaxPages)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(page.Text))
                {
                    continue;
                }

                AppendWithLimit(builder, page.Text, MaxCharacters);
                if (builder.Length >= MaxCharacters)
                {
                    break;
                }

                builder.AppendLine();
            }

            if (builder.Length == 0)
            {
                return ValueTask.FromResult<string?>(null);
            }

            return ValueTask.FromResult<string?>(builder.ToString());
        }
        catch
        {
            return ValueTask.FromResult<string?>(null);
        }
    }

    private static MemoryStream CreateMemoryStream(Stream source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var memory = new MemoryStream();
        source.CopyTo(memory);
        memory.Seek(0, SeekOrigin.Begin);
        return memory;
    }

    private static void AppendWithLimit(StringBuilder builder, string text, int limit)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var remaining = limit - builder.Length;
        if (remaining <= 0)
        {
            return;
        }

        if (text.Length <= remaining)
        {
            builder.Append(text);
        }
        else
        {
            builder.Append(text.AsSpan(0, remaining));
        }
    }
}
