using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Abstractions;
using Veriado.Domain.Files;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Coordinates specialised text extractors based on MIME type and enforces infrastructure limits.
/// </summary>
internal sealed class CompositeTextExtractor : ITextExtractor
{
    private readonly InfrastructureOptions _options;
    private readonly PlainTextExtractor _plainText;
    private readonly PdfTextExtractor _pdf;
    private readonly DocxTextExtractor _docx;
    private readonly PptxTextExtractor _pptx;
    private readonly XlsxTextExtractor _xlsx;
    private readonly OdtTextExtractor _odt;
    private readonly OdpTextExtractor _odp;
    private readonly OdsTextExtractor _ods;

    public CompositeTextExtractor(
        InfrastructureOptions options,
        PlainTextExtractor plainText,
        PdfTextExtractor pdf,
        DocxTextExtractor docx,
        PptxTextExtractor pptx,
        XlsxTextExtractor xlsx,
        OdtTextExtractor odt,
        OdpTextExtractor odp,
        OdsTextExtractor ods)
    {
        _options = options;
        _plainText = plainText;
        _pdf = pdf;
        _docx = docx;
        _pptx = pptx;
        _xlsx = xlsx;
        _odt = odt;
        _odp = odp;
        _ods = ods;
    }

    public async Task<string?> ExtractTextAsync(FileEntity file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();

        var bytes = file.Content.Bytes;
        if (_options.MaxContentBytes.HasValue && bytes.LongLength > _options.MaxContentBytes.Value)
        {
            return null;
        }

        using var stream = new MemoryStream(bytes, writable: false);
        var mime = file.Mime.Value;
        string? text = null;

        try
        {
            if (string.Equals(mime, "application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                text = await _pdf.TryExtractAsync(stream, mime, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(mime, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase))
            {
                text = await _docx.TryExtractAsync(stream, mime, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(mime, "application/vnd.openxmlformats-officedocument.presentationml.presentation", StringComparison.OrdinalIgnoreCase))
            {
                text = await _pptx.TryExtractAsync(stream, mime, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(mime, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", StringComparison.OrdinalIgnoreCase))
            {
                text = await _xlsx.TryExtractAsync(stream, mime, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(mime, "application/vnd.oasis.opendocument.text", StringComparison.OrdinalIgnoreCase))
            {
                text = await _odt.TryExtractAsync(stream, mime, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(mime, "application/vnd.oasis.opendocument.presentation", StringComparison.OrdinalIgnoreCase))
            {
                text = await _odp.TryExtractAsync(stream, mime, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(mime, "application/vnd.oasis.opendocument.spreadsheet", StringComparison.OrdinalIgnoreCase))
            {
                text = await _ods.TryExtractAsync(stream, mime, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            text = null;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        stream.Seek(0, SeekOrigin.Begin);
        text = await _plainText.TryExtractAsync(stream, mime, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
