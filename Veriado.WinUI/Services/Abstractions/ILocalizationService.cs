using System.Globalization;

namespace Veriado.WinUI.Services.Abstractions;

public interface ILocalizationService
{
    event EventHandler<CultureInfo>? CultureChanged;

    CultureInfo CurrentCulture { get; }

    IReadOnlyList<CultureInfo> SupportedCultures { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SetCultureAsync(CultureInfo culture, CancellationToken cancellationToken = default);

    string GetString(string resourceKey, string? defaultValue = null, params object?[] arguments);
}
