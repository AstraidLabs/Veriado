using System;
using AutoMapper;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Veriado.Application.Files.Commands;
using Veriado.Application.Files.Validation;
using Veriado.Mapping.AC;
using Veriado.Mapping.Profiles;

namespace Veriado.Mapping.DependencyInjection;

/// <summary>
/// Provides dependency injection helpers for mapping infrastructure.
/// </summary>
public static class MappingServiceCollectionExtensions
{
    /// <summary>
    /// Registers mapping profiles, validators and the write mapping pipeline.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddVeriadoMapping(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(provider =>
        {
            var configuration = new MapperConfiguration(cfg =>
            {
                cfg.AddProfile<FileReadProfiles>();
                cfg.AddProfile<MetadataProfiles>();
                cfg.AddProfile<FileWriteProfiles>();
            });
#if DEBUG
            configuration.AssertConfigurationIsValid();
#endif
            return configuration;
        });

        services.AddSingleton<IMapper>(provider =>
        {
            var configuration = provider.GetRequiredService<MapperConfiguration>();
            return configuration.CreateMapper(provider.GetService);
        });

        services.AddTransient<IValidator<CreateFileCommand>, CreateFileRequestValidator>();
        services.AddTransient<IValidator<ReplaceContentCommand>, ReplaceContentRequestValidator>();
        services.AddTransient<IValidator<UpdateMetadataCommand>, UpdateMetadataRequestValidator>();
        services.AddTransient<IValidator<RenameFileCommand>, RenameFileRequestValidator>();
        services.AddTransient<IValidator<SetValidityCommand>, SetValidityRequestValidator>();
        services.AddTransient<IValidator<ClearValidityCommand>, ClearValidityRequestValidator>();

        services.AddTransient<WriteMappingPipeline>();

        return services;
    }
}
