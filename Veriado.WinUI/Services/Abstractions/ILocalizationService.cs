using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.WinUI.Services.Abstractions;

/// <summary>
/// Provides access to localized resources and exposes the current application culture.
/// </summary>
public interface ILocalizationService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Gets a localized string using the current culture and formats it with optional arguments.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="defaultValue">The default value to use if the resource is missing.</param>
    /// <param name="arguments">Optional formatting arguments.</param>
    /// <returns>The localized string or the provided default value.</returns>
    string GetString(string resourceKey, string? defaultValue = null, params object?[] arguments);
}