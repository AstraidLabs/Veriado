using System;
using AutoMapper;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Veriado.Mapping.AC;
using Veriado.Mapping.Profiles;
using Veriado.Appl.UseCases.Files.Validation;
using Veriado.Appl.UseCases.Files.RenameFile;
using Veriado.Appl.UseCases.Files.ApplySystemMetadata;
using Veriado.Appl.UseCases.Files.SetFileValidity;
using Veriado.Appl.UseCases.Files.UpdateFileMetadata;
using Veriado.Appl.UseCases.Files.CreateFile;
using Veriado.Appl.UseCases.Files.ReplaceFileContent;
using Veriado.Appl.UseCases.Files.SetFileReadOnly;
using Veriado.Appl.UseCases.Files.ClearFileValidity;

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
                CommonValueConverters.Register(cfg);
                cfg.AddProfile<FileReadProfiles>();
                cfg.AddProfile<FileWriteProfiles>();
                cfg.AddProfile<SearchProfiles>();
            });

            configuration.AssertConfigurationIsValid();
            configuration.CompileMappings();
            return configuration;
        });

        services.AddSingleton<IMapper>(provider =>
        {
            var configuration = provider.GetRequiredService<MapperConfiguration>();
            return configuration.CreateMapper(provider.GetService);
        });

        services.AddTransient<IValidator<CreateFileCommand>, CreateFileCommandValidator>();
        services.AddTransient<IValidator<ReplaceFileContentCommand>, ReplaceFileContentCommandValidator>();
        services.AddTransient<IValidator<UpdateFileMetadataCommand>, UpdateFileMetadataCommandValidator>();
        services.AddTransient<IValidator<RenameFileCommand>, RenameFileCommandValidator>();
        services.AddTransient<IValidator<SetFileValidityCommand>, SetFileValidityCommandValidator>();
        services.AddTransient<IValidator<ClearFileValidityCommand>, ClearFileValidityCommandValidator>();
        services.AddTransient<IValidator<ApplySystemMetadataCommand>, ApplySystemMetadataCommandValidator>();
        services.AddTransient<IValidator<SetFileReadOnlyCommand>, SetFileReadOnlyCommandValidator>();

        services.AddTransient<WriteMappingPipeline>();

        return services;
    }
}
