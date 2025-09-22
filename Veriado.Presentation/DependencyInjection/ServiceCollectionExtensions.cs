using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Veriado.Presentation.Mapping;

namespace Veriado.Presentation.DependencyInjection;

public static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddPresentationModels(this IServiceCollection services)
    {
        services.AddAutoMapper(cfg =>
        {
            cfg.AddProfile<FilesContractsToPresentationProfile>();
            cfg.AddProfile<SearchContractsToPresentationProfile>();
            cfg.AddProfile<ImportContractsToPresentationProfile>();
        }, typeof(PresentationServiceCollectionExtensions).Assembly);

        return services;
    }
}
