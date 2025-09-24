using System;
using Microsoft.Extensions.DependencyInjection;
using Veriado.Appl.DependencyInjection;
using Veriado.Contracts.Search.Abstractions;
using Veriado.Services.Diagnostics;
using Veriado.Services.Files;
using Veriado.Services.Import;
using Veriado.Services.Maintenance;
using Veriado.Services.Search;

namespace Veriado.Services.DependencyInjection;

/// <summary>
/// Provides dependency injection helpers for the services layer.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the orchestration services consumed by hosts such as WinUI or APIs.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// Hosts must invoke <c>InitializeInfrastructureAsync()</c> during startup to ensure the database is ready.
    /// </remarks>
    public static IServiceCollection AddVeriadoServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddApplication();

        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IFileQueryService, FileQueryService>();
        services.AddScoped<IFileOperationsService, FileOperationsService>();
        services.AddScoped<IFileContentService, FileContentService>();
        services.AddScoped<IMaintenanceService, MaintenanceService>();
        services.AddScoped<IHealthService, HealthService>();
        services.AddSingleton<ISearchFacade, SearchFacade>();

        return services;
    }
}
