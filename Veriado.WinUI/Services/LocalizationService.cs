using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly ISettingsService _settingsService;
    private readonly ResourceManager _resourceManager;
    private readonly ResourceMap _resourceMap;
    private readonly IReadOnlyList<CultureInfo> _supportedCultures;
    private CultureInfo _currentCulture;
    private readonly object _gate = new();

    public LocalizationService(ISettingsService settingsService, ILogger<LocalizationService>? logger = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger;
        _resourceManager = new ResourceManager();
        _resourceMap = _resourceManager.MainResourceMap;
        _supportedCultures = CultureHelper.GetSupportedCultures(_resourceMap);
        _currentCulture = CultureHelper.DetermineInitialCulture(_supportedCultures);
        CultureHelper.ApplyCulture(_currentCulture);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
            var preferredCulture = LocalizationConfiguration.NormalizeCulture(settings.Language);

            if (!_supportedCultures.Contains(preferredCulture, CultureHelper.CultureComparer))
            {
                preferredCulture = _supportedCultures[0];
            }

            var changed = false;

            lock (_gate)
            {
                if (!CultureHelper.CultureComparer.Equals(_currentCulture, preferredCulture))
                {
                    _currentCulture = preferredCulture;
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            CultureHelper.ApplyCulture(preferredCulture);
            _logger?.LogInformation("Application culture initialized to {Culture}.", preferredCulture);
            CultureChanged?.Invoke(this, preferredCulture);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to initialize localization settings.");
        }
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
        _ = PersistCultureAsync(culture);
        return true;
    }

 

    public string GetString(string resourceKey, params object?[] arguments)
    {
        return GetString(resourceKey, null, arguments);
    }

    public string GetString(string resourceKey, string? defaultValue = null, params object?[] arguments)
    {
        var template = GetStringCore(resourceKey, CurrentCulture);

        if (string.Equals(template, resourceKey, StringComparison.Ordinal) && defaultValue is not null)
        {
            template = defaultValue;
        }

        if (arguments is { Length: > 0 })
        {
            try
            {
                return string.Format(CurrentCulture, template, arguments);
            }
            catch (FormatException ex)
            {
                _logger?.LogWarning(ex, "Failed to format resource {ResourceKey} with supplied arguments.", resourceKey);
            }
        }

        return template;
    }

    private async Task PersistCultureAsync(CultureInfo culture)
    {
        try
        {
            await _settingsService.UpdateAsync(settings => settings.Language = culture.Name).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist culture {Culture} to settings.", culture);
        }
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

    public string GetString(string resourceKey)
    {
        return GetString(resourceKey, null, Array.Empty<object?>());
    }
}