using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class LocalizationService : ILocalizationService
{
    private static readonly CultureInfo DefaultCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly IReadOnlyList<CultureInfo> SupportedCultureList =
        Array.AsReadOnly(new[] { DefaultCulture });

    private readonly ISettingsService _settingsService;
    private readonly ResourceManager _resourceManager = new();
    private readonly ResourceMap? _resourceMap;
    private ResourceContext _resourceContext;

    private CultureInfo _currentCulture = DefaultCulture;
    private bool _isInitialized;

    public LocalizationService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        _resourceMap = _resourceManager.MainResourceMap.TryGetSubtree("Resources");
        _resourceContext = _resourceManager.CreateResourceContext();
        UpdateQualifierLanguage(DefaultCulture.Name);
        ApplyCultureToThread(DefaultCulture);
    }

    public event EventHandler<CultureInfo>? CultureChanged;

    public CultureInfo CurrentCulture => _currentCulture;

    public IReadOnlyList<CultureInfo> SupportedCultures => SupportedCultureList;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        var settings = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
        var desiredCulture = TryGetCulture(settings.Language) ?? DefaultCulture;

        await ApplyCultureAsync(desiredCulture, persist: false, cancellationToken).ConfigureAwait(false);

        _isInitialized = true;
    }

    public Task SetCultureAsync(CultureInfo culture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return ApplyCultureAsync(culture, persist: true, cancellationToken);
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
                // Ignore formatting errors to avoid crashing the UI when resources are misconfigured.
            }
        }

        return template;
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

    private async Task ApplyCultureAsync(CultureInfo culture, bool persist, CancellationToken cancellationToken)
    {
        var resolved = ResolveCulture(culture);
        if (string.Equals(resolved.Name, _currentCulture.Name, StringComparison.OrdinalIgnoreCase))
        {
            if (persist)
            {
                await PersistCultureAsync(resolved, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        _currentCulture = resolved;

        UpdateQualifierLanguage(resolved.Name);
        ApplyCultureToThread(resolved);
        ResetResourceContexts();

        if (persist)
        {
            await PersistCultureAsync(resolved, cancellationToken).ConfigureAwait(false);
        }

        CultureChanged?.Invoke(this, _currentCulture);
    }

    private CultureInfo ResolveCulture(CultureInfo culture)
    {
        foreach (var supported in SupportedCultureList)
        {
            if (string.Equals(supported.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
            {
                return supported;
            }
        }

        try
        {
            return CultureInfo.GetCultureInfo(culture.Name);
        }
        catch (CultureNotFoundException)
        {
            return DefaultCulture;
        }
    }

    private static CultureInfo? TryGetCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private Task PersistCultureAsync(CultureInfo culture, CancellationToken cancellationToken)
    {
        return _settingsService.UpdateAsync(settings => settings.Language = culture.Name, cancellationToken);
    }

    private void UpdateQualifierLanguage(string language)
    {
        SetQualifierLanguage(_resourceContext, language);
    }

    private static void ApplyCultureToThread(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private void ResetResourceContexts()
    {
        _resourceContext = _resourceManager.CreateResourceContext();
        SetQualifierLanguage(_resourceContext, _currentCulture.Name);
    }

    private static void SetQualifierLanguage(ResourceContext context, string language)
    {
        if (context.QualifierValues.ContainsKey("Language"))
        {
            context.QualifierValues["Language"] = language;
        }
        else
        {
            context.QualifierValues.Add("Language", language);
        }
    }
}
