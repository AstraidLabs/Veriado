using System.Globalization;

namespace Veriado.WinUI.Models.Import;

public sealed class ImportLogItem
{
    public ImportLogItem(DateTimeOffset timestamp, string title, string message, string status, string? detail = null)
    {
        Timestamp = timestamp;
        Title = title;
        Message = message;
        Status = status;
        Detail = detail;
    }

    public DateTimeOffset Timestamp { get; }

    public string Title { get; }

    public string Message { get; }

    public string Status { get; }

    public string? Detail { get; }

    public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    public string StatusDisplay
    {
        get
        {
            var status = string.IsNullOrWhiteSpace(Status) ? "info" : Status;
            return status.ToLowerInvariant() switch
            {
                "success" => "Úspěch",
                "warning" => "Varování",
                "error" => "Chyba",
                "info" => "Informace",
                _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(status),
            };
        }
    }

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}
