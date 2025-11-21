using Microsoft.Extensions.DependencyInjection;

namespace Veriado.WinUI.DependencyInjection;

/// <summary>
/// Provides dependency injection helpers for WinUI-specific components.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the WinUI shell window and associated views.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddWinUiShell(this IServiceCollection services)
    {
        services.AddTransient<Views.Shell.MainShell>();
        services.AddTransient<Views.Files.FilesPage>();
        services.AddTransient<Views.Import.ImportPage>();
        services.AddTransient<Views.Storage.StorageManagementPage>();
        services.AddTransient<Views.Settings.SettingsPage>();

        return services;
    }
}
