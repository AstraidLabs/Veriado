using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Extracts textual content from PPTX presentation slides.
/// </summary>
internal sealed class PptxTextExtractor
{
    private const int MaxSlides = 500;
    private const int MaxTextElements = 10_000;
    private const int MaxCharacters = 200_000;

    public ValueTask<string?> TryExtractAsync(Stream content, string mime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!string.Equals(mime, "application/vnd.openxmlformats-officedocument.presentationml.presentation", StringComparison.OrdinalIgnoreCase))
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
            using var presentation = PresentationDocument.Open(content, false);
            var slideParts = presentation.PresentationPart?.SlideParts;
            if (slideParts is null)
            {
                return ValueTask.FromResult<string?>(null);
            }

            var builder = new StringBuilder();
            var processedSlides = 0;
            var processedElements = 0;

            foreach (var slidePart in slideParts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                processedSlides++;
                if (processedSlides > MaxSlides)
                {
                    break;
                }

                var texts = slidePart.Slide?.Descendants<A.Text>();
                if (texts is null)
                {
                    continue;
                }

                foreach (var text in texts)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(text.Text))
                    {
                        continue;
                    }

                    processedElements++;
                    if (processedElements > MaxTextElements)
                    {
                        break;
                    }

                    AppendWithLimit(builder, text.Text.Trim(), MaxCharacters);
                    if (builder.Length >= MaxCharacters)
                    {
                        break;
                    }

                    builder.AppendLine();
                }

                if (builder.Length >= MaxCharacters || processedElements > MaxTextElements)
                {
                    break;
                }
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
