using Microsoft.Extensions.DependencyInjection;
using System;
using Veriado.WinUI.Views;

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
        services.AddSingleton<MainWindow>();
        services.AddTransient<FiltersNavPane>();
        services.AddTransient<FilesView>();
        services.AddTransient<FileDetailView>();
        services.AddTransient<Func<FileDetailView>>(sp => () => sp.GetRequiredService<FileDetailView>());
        services.AddTransient<ImportView>();
        services.AddTransient<SettingsView>();

        return services;
    }
}
