using System;
using AutoMapper;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Veriado.Application.UseCases.Files.ApplySystemMetadata;
using Veriado.Application.UseCases.Files.ClearFileValidity;
using Veriado.Application.UseCases.Files.CreateFile;
using Veriado.Application.UseCases.Files.ReplaceFileContent;
using Veriado.Application.UseCases.Files.RenameFile;
using Veriado.Application.UseCases.Files.SetExtendedMetadata;
using Veriado.Application.UseCases.Files.SetFileReadOnly;
using Veriado.Application.UseCases.Files.SetFileValidity;
using Veriado.Application.UseCases.Files.UpdateFileMetadata;
using Veriado.Application.UseCases.Files.Validation;
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

        services.AddTransient<IValidator<CreateFileCommand>, CreateFileCommandValidator>();
        services.AddTransient<IValidator<ReplaceFileContentCommand>, ReplaceFileContentCommandValidator>();
        services.AddTransient<IValidator<UpdateFileMetadataCommand>, UpdateFileMetadataCommandValidator>();
        services.AddTransient<IValidator<RenameFileCommand>, RenameFileCommandValidator>();
        services.AddTransient<IValidator<SetFileValidityCommand>, SetFileValidityCommandValidator>();
        services.AddTransient<IValidator<ClearFileValidityCommand>, ClearFileValidityCommandValidator>();
        services.AddTransient<IValidator<ApplySystemMetadataCommand>, ApplySystemMetadataCommandValidator>();
        services.AddTransient<IValidator<SetExtendedMetadataCommand>, SetExtendedMetadataCommandValidator>();
        services.AddTransient<IValidator<SetFileReadOnlyCommand>, SetFileReadOnlyCommandValidator>();

        services.AddTransient<WriteMappingPipeline>();

        return services;
    }
}
