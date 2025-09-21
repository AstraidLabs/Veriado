using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Extracts textual content from OpenDocument text documents (ODT).
/// </summary>
internal sealed class OdtTextExtractor
{
    private const int MaxElements = 10_000;
    private const int MaxCharacters = 200_000;

    public ValueTask<string?> TryExtractAsync(Stream content, string mime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!string.Equals(mime, "application/vnd.oasis.opendocument.text", StringComparison.OrdinalIgnoreCase))
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
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            var entry = archive.GetEntry("content.xml");
            if (entry is null)
            {
                return ValueTask.FromResult<string?>(null);
            }

            using var entryStream = entry.Open();
            using var reader = XmlReader.Create(entryStream, new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                Async = false,
            });

            var builder = new StringBuilder();
            var elements = 0;

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.NodeType == XmlNodeType.Text)
                {
                    elements++;
                    if (elements > MaxElements)
                    {
                        break;
                    }

                    AppendWithLimit(builder, reader.Value, MaxCharacters);
                    if (builder.Length >= MaxCharacters)
                    {
                        break;
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    if (IsParagraphElement(reader))
                    {
                        EnsureParagraphBreak(builder);
                    }
                    else if (IsLineBreakElement(reader))
                    {
                        builder.AppendLine();
                    }
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

    private static bool IsParagraphElement(XmlReader reader)
    {
        return reader.NodeType == XmlNodeType.Element && reader.LocalName is "p" or "h" or "list-item";
    }

    private static bool IsLineBreakElement(XmlReader reader)
    {
        return reader.NodeType == XmlNodeType.Element && reader.LocalName is "line-break";
    }

    private static void EnsureParagraphBreak(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        if (builder[^1] != '\n')
        {
            builder.AppendLine();
        }
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
