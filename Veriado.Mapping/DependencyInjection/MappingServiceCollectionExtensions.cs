using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Veriado.Appl.UseCases.Files.ApplySystemMetadata;
using Veriado.Appl.UseCases.Files.ClearFileValidity;
using Veriado.Appl.UseCases.Files.CreateFile;
using Veriado.Appl.UseCases.Files.RenameFile;
using Veriado.Appl.UseCases.Files.ReplaceFileContent;
using Veriado.Appl.UseCases.Files.SetFileReadOnly;
using Veriado.Appl.UseCases.Files.SetFileValidity;
using Veriado.Appl.UseCases.Files.UpdateFileMetadata;
using Veriado.Appl.UseCases.Files.Validation;
using Veriado.Mapping.AC;
using Veriado.Mapping.Profiles;

namespace Veriado.Mapping.DependencyInjection;

public static class MappingServiceCollectionExtensions
{
    public static IServiceCollection AddVeriadoMapping(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // AutoMapper konfigurace + registrace profilů
        services.AddAutoMapper((sp, cfg) =>
        {
            // Pokud používáš licencování v AM 15+, odkomentuj a měj jistotu, že typ podporuje tento property:
            // cfg.LicenseKey = "<tvůj_licenční_klíč>";

            CommonValueConverters.Register(cfg);
            cfg.AddProfile<FileReadProfiles>();
            cfg.AddProfile<FileWriteProfiles>();
            cfg.AddProfile<SearchProfiles>();
        }, typeof(FileReadProfiles).Assembly);

        // (Volitelné) Validace konfigurace po startu
        services.AddHostedService(sp =>
            new MapperValidationHostedService(
                sp.GetRequiredService<AutoMapper.IConfigurationProvider>()));

        // FluentValidation registrace (explicitně – máš je už hotové)
        services.AddTransient<IValidator<CreateFileCommand>, CreateFileCommandValidator>();
        services.AddTransient<IValidator<ReplaceFileContentCommand>, ReplaceFileContentCommandValidator>();
        services.AddTransient<IValidator<UpdateFileMetadataCommand>, UpdateFileMetadataCommandValidator>();
        services.AddTransient<IValidator<RenameFileCommand>, RenameFileCommandValidator>();
        services.AddTransient<IValidator<SetFileValidityCommand>, SetFileValidityCommandValidator>();
        services.AddTransient<IValidator<ClearFileValidityCommand>, ClearFileValidityCommandValidator>();
        services.AddTransient<IValidator<ApplySystemMetadataCommand>, ApplySystemMetadataCommandValidator>();
        services.AddTransient<IValidator<SetFileReadOnlyCommand>, SetFileReadOnlyCommandValidator>();

        // tvoje pipeline (ponechávám dle tvého projektu)
        services.AddTransient<WriteMappingPipeline>();

        return services;
    }

    private sealed class MapperValidationHostedService : IHostedService
    {
        private readonly AutoMapper.IConfigurationProvider _config;

        public MapperValidationHostedService(AutoMapper.IConfigurationProvider config)
            => _config = config;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _config.AssertConfigurationIsValid();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
