using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides plain-text extraction for UTF and legacy encoded files.
/// </summary>
internal sealed class PlainTextExtractor
{
    private const int MaxCharacters = 200_000;

    private static readonly HashSet<string> PlainTextMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/csv",
        "application/json",
        "application/xml",
        "text/xml",
    };

    public ValueTask<string?> TryExtractAsync(Stream content, string mime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!IsTextMime(mime))
        {
            return ValueTask.FromResult<string?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!content.CanSeek)
        {
            content = CreateMemoryStream(content, cancellationToken);
        }

        content.Seek(0, SeekOrigin.Begin);

        using var reader = new StreamReader(
            content,
            encoding: Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);

        var builder = new StringBuilder();
        var buffer = new char[4096];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            var remaining = MaxCharacters - builder.Length;
            if (remaining <= 0)
            {
                break;
            }

            var toAppend = Math.Min(remaining, read);
            builder.Append(buffer, 0, toAppend);
            if (toAppend < read)
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

    private static MemoryStream CreateMemoryStream(Stream source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var memory = new MemoryStream();
        source.CopyTo(memory);
        memory.Seek(0, SeekOrigin.Begin);
        return memory;
    }

    private static bool IsTextMime(string mime)
    {
        if (PlainTextMimeTypes.Contains(mime))
        {
            return true;
        }

        if (mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (mime.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) || mime.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
