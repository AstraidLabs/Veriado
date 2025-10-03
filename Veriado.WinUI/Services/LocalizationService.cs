using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class LocalizationService : ILocalizationService
{
    private static readonly CultureInfo DefaultCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly IReadOnlyList<CultureInfo> SupportedCultureList =
        Array.AsReadOnly(new[] { DefaultCulture });

    private readonly ResourceManager _resourceManager = new();
    private readonly ResourceMap? _resourceMap;
    private readonly ResourceContext _resourceContext;

    public LocalizationService(ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);

        _resourceMap = _resourceManager.MainResourceMap.TryGetSubtree("Resources");
        _resourceContext = new ResourceContext(_resourceManager);
        _resourceContext.QualifierValues["Language"] = DefaultCulture.Name;
    }

    public event EventHandler<CultureInfo>? CultureChanged;

    public CultureInfo CurrentCulture => DefaultCulture;

    public IReadOnlyList<CultureInfo> SupportedCultures => SupportedCultureList;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task SetCultureAsync(CultureInfo culture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return Task.CompletedTask;
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
                return string.Format(DefaultCulture, template, arguments);
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
}
