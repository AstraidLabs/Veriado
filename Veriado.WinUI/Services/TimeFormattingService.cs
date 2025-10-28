using System.Globalization;

namespace Veriado.WinUI.Services;

/// <summary>
/// Implements formatting helpers for localized timestamps.
/// </summary>
public sealed class TimeFormattingService : ITimeFormattingService
{
    public DateTimeOffset ToLocal(DateTimeOffset value)
    {
        return value.ToLocalTime();
    }

    public string Format(DateTimeOffset value)
    {
        return ToLocal(value).ToString("g", CultureInfo.CurrentCulture);
    }

    public string FormatOrDash(DateTimeOffset? value)
    {
        return value.HasValue ? Format(value.Value) : "–";
    }

    public (DateTimeOffset Date, TimeSpan TimeOfDay) Split(DateTimeOffset value)
    {
        var local = ToLocal(value);
        return (new DateTimeOffset(local.Date, local.Offset), local.TimeOfDay);
    }

    public DateTimeOffset ComposeUtc(DateTimeOffset localDate, TimeSpan timeOfDay)
    {
        var composed = new DateTimeOffset(
            localDate.Year,
            localDate.Month,
            localDate.Day,
            timeOfDay.Hours,
            timeOfDay.Minutes,
            timeOfDay.Seconds,
            localDate.Offset);
        return composed.ToUniversalTime();
    }
}
