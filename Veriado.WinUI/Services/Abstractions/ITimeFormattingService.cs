namespace Veriado.WinUI.Services.Abstractions;

/// <summary>
/// Provides helpers for consistent time localization and formatting.
/// </summary>
public interface ITimeFormattingService
{
    /// <summary>
    /// Converts the specified UTC timestamp to the local time zone.
    /// </summary>
    DateTimeOffset ToLocal(DateTimeOffset value);

    /// <summary>
    /// Formats a timestamp using the current culture.
    /// </summary>
    string Format(DateTimeOffset value);

    /// <summary>
    /// Formats an optional timestamp using the current culture, returning an en dash if missing.
    /// </summary>
    string FormatOrDash(DateTimeOffset? value);

    /// <summary>
    /// Decomposes a timestamp to its local date component and time of day.
    /// </summary>
    (DateTimeOffset Date, TimeSpan TimeOfDay) Split(DateTimeOffset value);

    /// <summary>
    /// Composes a timestamp from a local date and time of day and returns the UTC representation.
    /// </summary>
    DateTimeOffset ComposeUtc(DateTimeOffset localDate, TimeSpan timeOfDay);
}
