using System;
using Microsoft.Extensions.DependencyInjection;
using Veriado.Presentation.Converters;
using Veriado.Presentation.ViewModels;
using Veriado.Presentation.ViewModels.Files;

namespace Veriado.Presentation.DependencyInjection;

/// <summary>
/// Provides registration helpers for the presentation layer.
/// </summary>
public static class PresentationViewModelServiceCollectionExtensions
{
    /// <summary>
    /// Registers presentation services, view models and helpers.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <returns>The original service collection for chaining.</returns>
    public static IServiceCollection AddWinUiPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ShellViewModel>();
        services.AddTransient<SearchBarViewModel>();
        services.AddTransient<FileFiltersViewModel>();
        services.AddTransient<SortStateViewModel>();
        services.AddTransient<FavoritesViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<FilesGridViewModel>();
        services.AddTransient<FileDetailViewModel>();
        services.AddTransient<ImportViewModel>();

        services.AddSingleton<BoolToSeverityConverter>();
        services.AddSingleton<NavigationSelectionConverter>();

        return services;
    }
}
