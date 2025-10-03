using System;
using System.Globalization;

namespace Veriado.WinUI.Services.Abstractions;

/// <summary>
/// Provides access to localized resources and exposes the current application culture.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Occurs when the current culture changes.
    /// </summary>
    event EventHandler<CultureInfo>? CultureChanged;

    /// <summary>
    /// Gets the culture currently used by the application.
    /// </summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>
    /// Gets the cultures supported by the application.
    /// </summary>
    IReadOnlyList<CultureInfo> SupportedCultures { get; }

    /// <summary>
    /// Attempts to change the current application culture.
    /// </summary>
    /// <param name="culture">The culture to apply.</param>
    /// <returns><see langword="true"/> if the culture was changed; otherwise <see langword="false"/>.</returns>
    bool TrySetCulture(CultureInfo culture);

    /// <summary>
    /// Gets a localized string for the specified resource key using the current culture.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <returns>The localized string if found; otherwise, the key itself.</returns>
    string GetString(string resourceKey);

    /// <summary>
    /// Gets a localized string formatted with the supplied arguments using the current culture.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="arguments">Arguments used to format the resource string.</param>
    /// <returns>The formatted localized string.</returns>
    string GetString(string resourceKey, params object?[] arguments);
}