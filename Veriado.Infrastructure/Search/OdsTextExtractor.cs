using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Extracts textual content from OpenDocument spreadsheet files (ODS).
/// </summary>
internal sealed class OdsTextExtractor
{
    private const int MaxRows = 2_000;
    private const int MaxCells = 50_000;
    private const int MaxCharacters = 200_000;

    public ValueTask<string?> TryExtractAsync(Stream content, string mime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!string.Equals(mime, "application/vnd.oasis.opendocument.spreadsheet", StringComparison.OrdinalIgnoreCase))
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
            var processedRows = 0;
            var processedCells = 0;
            var atRowStart = true;

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (IsRowElement(reader))
                    {
                        processedRows++;
                        if (processedRows > MaxRows)
                        {
                            break;
                        }

                        if (builder.Length > 0)
                        {
                            builder.AppendLine();
                        }

                        atRowStart = true;
                    }
                    else if (IsCellElement(reader))
                    {
                        if (!atRowStart)
                        {
                            builder.Append('\t');
                        }

                        atRowStart = false;
                        processedCells++;
                        if (processedCells > MaxCells)
                        {
                            break;
                        }
                    }
                    else if (IsLineBreakElement(reader))
                    {
                        builder.AppendLine();
                        atRowStart = true;
                    }
                }
                else if (reader.NodeType == XmlNodeType.Text)
                {
                    AppendWithLimit(builder, reader.Value, MaxCharacters);
                    if (builder.Length >= MaxCharacters)
                    {
                        break;
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

    private static bool IsRowElement(XmlReader reader)
    {
        return reader.NodeType == XmlNodeType.Element && reader.LocalName is "table-row";
    }

    private static bool IsCellElement(XmlReader reader)
    {
        return reader.NodeType == XmlNodeType.Element && reader.LocalName is "table-cell";
    }

    private static bool IsLineBreakElement(XmlReader reader)
    {
        return reader.NodeType == XmlNodeType.Element && reader.LocalName is "line-break";
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
