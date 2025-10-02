using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using Veriado.WinUI.Localization;

namespace Veriado.WinUI.Services;

public sealed class LocalizationService : ILocalizationService
{
    private readonly ISettingsService _settingsService;
    private readonly ResourceManager _resourceManager = new();
    private readonly ResourceMap? _resourceMap;
    private readonly ResourceContext _resourceContext;
    private readonly IReadOnlyList<CultureInfo> _supportedCultures;
    private CultureInfo _currentCulture;

    public LocalizationService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _resourceMap = _resourceManager.MainResourceMap.TryGetSubtree("Resources");
        _resourceContext = _resourceManager.CreateResourceContext();
        _supportedCultures = LocalizationConfiguration.SupportedCultures;
        _currentCulture = LocalizationConfiguration.NormalizeCulture(CultureInfo.CurrentUICulture);
        UpdateCulture(_currentCulture, force: true);
    }

    public event EventHandler<CultureInfo>? CultureChanged;

    public CultureInfo CurrentCulture => _currentCulture;

    public IReadOnlyList<CultureInfo> SupportedCultures => _supportedCultures;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
        var culture = LocalizationConfiguration.NormalizeCulture(settings.Language);
        await ApplyCultureAsync(culture, persist: false, force: true, cancellationToken).ConfigureAwait(false);
    }

    public Task SetCultureAsync(CultureInfo culture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return ApplyCultureAsync(culture, persist: true, force: false, cancellationToken);
    }

    public string GetString(string resourceKey, string? defaultValue = null, params object?[] arguments)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            throw new ArgumentException("Resource key must be provided.", nameof(resourceKey));
        }

        var template = TryGetString(resourceKey) ?? defaultValue ?? resourceKey;
        if (arguments is { Length: > 0 })
        {
            try
            {
                return string.Format(_currentCulture, template, arguments);
            }
            catch (FormatException)
            {
                // Ignore formatting errors to prevent crashing the UI when resources are misconfigured.
            }
        }

        return template;
    }

    private async Task ApplyCultureAsync(CultureInfo culture, bool persist, bool force, CancellationToken cancellationToken)
    {
        var normalized = LocalizationConfiguration.NormalizeCulture(culture);
        var hasChanged = !string.Equals(normalized.Name, _currentCulture.Name, StringComparison.OrdinalIgnoreCase);

        if (hasChanged || force)
        {
            UpdateCulture(normalized, force);
            if (hasChanged)
            {
                CultureChanged?.Invoke(this, normalized);
            }
        }

        if (persist)
        {
            await _settingsService.UpdateAsync(settings => settings.Language = normalized.Name, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private void UpdateCulture(CultureInfo culture, bool force)
    {
        if (!force && string.Equals(_currentCulture.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentCulture = culture;
        CultureHelper.ApplyCulture(culture);
        _resourceContext.QualifierValues["Language"] = culture.Name;
    }

    private string? TryGetString(string resourceKey)
    {
        if (_resourceMap is null)
        {
            return null;
        }

        var candidate = _resourceMap.TryGetValue(resourceKey, _resourceContext);
        return candidate?.ValueAsString;
    }
}
