using System;
using Microsoft.Extensions.DependencyInjection;
using Veriado.WinUI.ViewModels;

namespace Veriado.WinUI.DependencyInjection;

/// <summary>
/// Registers WinUI view models with the dependency injection container.
/// </summary>
public static class ViewModelServiceCollectionExtensions
{
    /// <summary>
    /// Adds the WinUI view models to the provided service collection.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <returns>The original service collection for chaining.</returns>
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<FilesGridViewModel>();
        services.AddSingleton<FileDetailViewModel>();
        services.AddSingleton<ImportViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services;
    }
}
