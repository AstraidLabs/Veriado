using System;
using System.Globalization;

namespace Veriado.WinUI.Models.Import;

public sealed class ImportLogItem
{
    public ImportLogItem(DateTimeOffset timestamp, string title, string message, string status)
    {
        Timestamp = timestamp;
        Title = title;
        Message = message;
        Status = status;
    }

    public DateTimeOffset Timestamp { get; }

    public string Title { get; }

    public string Message { get; }

    public string Status { get; }

    public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
}
