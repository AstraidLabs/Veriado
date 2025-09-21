using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Extracts textual content from XLSX spreadsheets.
/// </summary>
internal sealed class XlsxTextExtractor
{
    private const int MaxSheets = 64;
    private const int MaxRowsPerSheet = 2_000;
    private const int MaxColumnsPerRow = 64;
    private const int MaxCells = 50_000;
    private const int MaxCharacters = 200_000;

    public ValueTask<string?> TryExtractAsync(Stream content, string mime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!string.Equals(mime, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", StringComparison.OrdinalIgnoreCase))
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
            using var document = SpreadsheetDocument.Open(content, false);
            var workbookPart = document.WorkbookPart;
            if (workbookPart is null)
            {
                return ValueTask.FromResult<string?>(null);
            }

            var sharedStrings = LoadSharedStrings(workbookPart);
            var builder = new StringBuilder();
            var processedCells = 0;
            var processedSheets = 0;

            foreach (var worksheetPart in workbookPart.WorksheetParts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                processedSheets++;
                if (processedSheets > MaxSheets)
                {
                    break;
                }

                var sheetData = worksheetPart.Worksheet?.GetFirstChild<SheetData>();
                if (sheetData is null)
                {
                    continue;
                }

                var processedRows = 0;

                foreach (var row in sheetData.Elements<Row>())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    processedRows++;
                    if (processedRows > MaxRowsPerSheet)
                    {
                        break;
                    }

                    var processedColumns = 0;
                    var rowBuilder = new StringBuilder();

                    foreach (var cell in row.Elements<Cell>())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        processedColumns++;
                        if (processedColumns > MaxColumnsPerRow)
                        {
                            break;
                        }

                        var text = GetCellText(cell, sharedStrings);
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        processedCells++;
                        if (processedCells > MaxCells)
                        {
                            break;
                        }

                        AppendWithLimit(rowBuilder, text.Trim(), MaxCharacters);
                        if (rowBuilder.Length >= MaxCharacters)
                        {
                            break;
                        }

                        rowBuilder.Append('\t');
                    }

                    if (rowBuilder.Length > 0)
                    {
                        builder.Append(rowBuilder.ToString().TrimEnd('\t'));
                        builder.AppendLine();
                    }

                    if (builder.Length >= MaxCharacters || processedCells > MaxCells)
                    {
                        break;
                    }
                }

                if (builder.Length >= MaxCharacters || processedCells > MaxCells)
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

    private static IReadOnlyList<string> LoadSharedStrings(WorkbookPart workbookPart)
    {
        var table = workbookPart.SharedStringTablePart?.SharedStringTable;
        if (table is null)
        {
            return Array.Empty<string>();
        }

        return table.Elements<SharedStringItem>()
            .Select(item => item.InnerText ?? string.Empty)
            .ToArray();
    }

    private static string? GetCellText(Cell cell, IReadOnlyList<string> sharedStrings)
    {
        if (cell.CellValue is null)
        {
            return null;
        }

        var raw = cell.CellValue.InnerText;
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        if (cell.DataType is null)
        {
            return raw;
        }

        return cell.DataType.Value switch
        {
            CellValues.SharedString => TryResolveSharedString(raw, sharedStrings),
            CellValues.Boolean => raw == "1" ? "TRUE" : "FALSE",
            CellValues.Date => raw,
            CellValues.Number => raw,
            CellValues.InlineString => cell.InnerText,
            _ => raw,
        };
    }

    private static string? TryResolveSharedString(string raw, IReadOnlyList<string> sharedStrings)
    {
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return raw;
        }

        if (index < 0 || index >= sharedStrings.Count)
        {
            return raw;
        }

        return sharedStrings[index];
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
