using Veriado.Contracts.Import;
using Veriado.WinUI.Models.Import;

namespace Veriado.WinUI.ViewModels.Import;

public sealed class ErrorDetailViewModel
{
    public ErrorDetailViewModel(ImportError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        Error = error;
        Item = new ImportErrorItem(error);
    }

    public ImportError Error { get; }

    public ImportErrorItem Item { get; }

    public string Title => Item.FileName;

    public string Message => Item.ErrorMessage;

    public string? Code => Item.Code;

    public string? Suggestion => Item.Suggestion;

    public string? FilePath => Item.FilePath;

    public string FormattedTimestamp => Item.FormattedTimestamp;

    public string? StackTrace => Item.StackTrace;

    public bool HasStackTrace => Item.HasStackTrace;

    public bool HasSuggestion => Item.HasSuggestion;

    public bool HasCode => Item.HasCode;

    public bool HasFilePath => Item.HasFilePath;

    public ImportErrorSeverity Severity => Item.Severity;

    public string SeverityText => Severity switch
    {
        ImportErrorSeverity.Fatal => "Fatální chyba",
        ImportErrorSeverity.Warning => "Varování",
        _ => "Chyba",
    };
}
