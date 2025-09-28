using AutoMapper;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
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

        // AutoMapper 15.x – vestavìné DI rozšíøení (bez deprecated balíèku)
        services.AddAutoMapper(cfg =>
        {
            // Pokud používáš licencování v AM 15+, odkomentuj:
            cfg.LicenseKey = "eyJhbGciOiJSUzI1NiIsImtpZCI6Ikx1Y2t5UGVubnlTb2Z0d2FyZUxpY2Vuc2VLZXkvYmJiMTNhY2I1OTkwNGQ4OWI0Y2IxYzg1ZjA4OGNjZjkiLCJ0eXAiOiJKV1QifQ.eyJpc3MiOiJodHRwczovL2x1Y2t5cGVubnlzb2Z0d2FyZS5jb20iLCJhdWQiOiJMdWNreVBlbm55U29mdHdhcmUiLCJleHAiOiIxNzkwNTUzNjAwIiwiaWF0IjoiMTc1OTA4MTEyNyIsImFjY291bnRfaWQiOiIwMTk5NTY2YTJlNDc3ODYxYjcwZGM0MzlhMjk4ODFkMyIsImN1c3RvbWVyX2lkIjoiY3RtXzAxazViNnF0NHR5ZDMxZ3N6eHJtemhnMnJ0Iiwic3ViX2lkIjoiLSIsImVkaXRpb24iOiIwIiwidHlwZSI6IjIifQ.AIwqOazIsxmdI-TtobavHD_HIU7xdRJI_oQZre1ATByMJuzDJfDRFGCyWkbDMvl8Vs6gWnTqqBj0Xuro4OlUpySdjjlIvbHJuSHSSY8-q4TfaPeMp1HIQmxw2hSJtKYa2STJ9a2D6yDKYe4xifM0OHrPhk0vxn63uCNWijfAdMEFm5LoMRHD7vnhDP-8R6RlqKt6c7tAJcKybRM0iRJ1MA94c__OSymob_kYwmE6xKWttPO2crigpEjRokenDjFymaFQKa-47KD70C3RvMQInuC2oaIfN-zIy_eq6NgMxB5sq4wZrR5HlKt8hGAncUt_90SluL_0rK7RWP9GhG74-A";

            CommonValueConverters.Register(cfg);
            cfg.AddProfile<FileReadProfiles>();
            cfg.AddProfile<FileWriteProfiles>();
            cfg.AddProfile<SearchProfiles>();
        }, typeof(FileReadProfiles).Assembly);

        // (Volitelné) Validace konfigurace po startu
        services.AddHostedService(sp =>
            new MapperValidationHostedService(
                sp.GetRequiredService<AutoMapper.IConfigurationProvider>()));

        // FluentValidation registrace
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

    private sealed class MapperValidationHostedService : IHostedService
    {
        private readonly AutoMapper.IConfigurationProvider _config;

        public MapperValidationHostedService(AutoMapper.IConfigurationProvider config) => _config = config;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _config.AssertConfigurationIsValid();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
