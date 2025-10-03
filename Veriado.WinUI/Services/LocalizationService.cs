using System;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.ApplicationModel.Resources;
using Veriado.WinUI.Localization;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

/// <summary>
/// Provides access to localized resources backed by the WinAppSDK resource manager.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private readonly ILogger<LocalizationService>? _logger;
    private readonly ResourceManager _resourceManager;
    private readonly ResourceMap _resourceMap;
    private readonly IReadOnlyList<CultureInfo> _supportedCultures;
    private CultureInfo _currentCulture;
    private readonly object _gate = new();

    public LocalizationService(ILogger<LocalizationService>? logger = null)
    {
        _logger = logger;
        _resourceManager = new ResourceManager();
        _resourceMap = _resourceManager.MainResourceMap;
        _supportedCultures = CultureHelper.GetSupportedCultures(_resourceMap);
        _currentCulture = CultureHelper.DetermineInitialCulture(_supportedCultures);
        CultureHelper.ApplyCulture(_currentCulture);
    }

    public event EventHandler<CultureInfo>? CultureChanged;

    public CultureInfo CurrentCulture
    {
        get
        {
            lock (_gate)
            {
                return _currentCulture;
            }
        }
    }

    public IReadOnlyList<CultureInfo> SupportedCultures => _supportedCultures;

    public bool TrySetCulture(CultureInfo culture)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        if (!_supportedCultures.Contains(culture, CultureHelper.CultureComparer))
        {
            _logger?.LogWarning("Requested culture {Culture} is not part of the supported culture list.", culture);
            return false;
        }

        CultureInfo previous;
        lock (_gate)
        {
            if (CultureHelper.CultureComparer.Equals(_currentCulture, culture))
            {
                return false;
            }

            previous = _currentCulture;
            _currentCulture = culture;
        }

        CultureHelper.ApplyCulture(culture);
        _logger?.LogInformation("Application culture changed from {PreviousCulture} to {CurrentCulture}.", previous, culture);
        CultureChanged?.Invoke(this, culture);
        return true;
    }

    public string GetString(string resourceKey) => GetStringCore(resourceKey, CurrentCulture);

    public string GetString(string resourceKey, params object?[] arguments)
    {
        var raw = GetStringCore(resourceKey, CurrentCulture);

        if (arguments is null || arguments.Length == 0)
        {
            return raw;
        }

        return string.Format(CurrentCulture, raw, arguments);
    }

    private string GetStringCore(string resourceKey, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            throw new ArgumentException("Resource key cannot be null or whitespace.", nameof(resourceKey));
        }

        var context = _resourceManager.CreateResourceContext();
        CultureHelper.ApplyCulture(context, culture);

        try
        {
            var candidate = _resourceMap.GetValue(resourceKey, context);
            if (candidate is not null)
            {
                var value = candidate.ValueAsString;
                return string.IsNullOrEmpty(value) ? resourceKey : value;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to resolve resource {ResourceKey} for culture {Culture}.", resourceKey, culture);
        }

        return resourceKey;
    }
}