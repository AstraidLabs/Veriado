using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Veriado.Appl.Pipeline;
using Veriado.Appl.UseCases.Queries.FileGrid;
using Veriado.Appl.Pipeline.Idempotency;

namespace Veriado.Appl.DependencyInjection;

/// <summary>
/// Provides extension methods to register the application layer services.
/// </summary>
public static class ApplicationServicesExtensions
{
    /// <summary>
    /// Registers MediatR handlers, pipeline behaviors and supporting services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services, Action<ApplicationOptions>? configure = null)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var options = new ApplicationOptions();
        configure?.Invoke(options);

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        services.AddScoped<IRequestContext>(_ => AmbientRequestContext.Current);

        RegisterValidators(services, assembly);

        services.TryAddSingleton(new ImportPolicy(options.MaxContentLengthBytes));
        services.TryAddSingleton(new ValidityReminderPolicy(options.ValidityReminderLeadTime));
        services.TryAddSingleton(new FileGridQueryOptions
        {
            MaxPageSize = options.MaxGridPageSize,
            MaxCandidateResults = options.MaxFulltextCandidates,
        });
        services.AddTransient<FileGridQueryValidator>();

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }

    private static void RegisterValidators(IServiceCollection services, Assembly assembly)
    {
        var validatorInterface = typeof(IRequestValidator<>);
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            var validatorInterfaces = type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == validatorInterface);
            foreach (var serviceType in validatorInterfaces)
            {
                services.AddTransient(serviceType, type);
            }
        }
    }
}

/// <summary>
/// Options used to configure application layer services.
/// </summary>
public sealed class ApplicationOptions
{
    /// <summary>
    /// Gets or sets the optional maximum file content size in bytes accepted by commands.
    /// </summary>
    public int? MaxContentLengthBytes { get; set; }

    /// <summary>
    /// Gets or sets the lead time before expiration used for reminder queries.
    /// </summary>
    public TimeSpan ValidityReminderLeadTime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets or sets the maximum allowed page size for grid queries.
    /// </summary>
    public int MaxGridPageSize { get; set; } = 200;

    /// <summary>
    /// Gets or sets the maximum number of Lucene candidates retrieved before applying filters.
    /// </summary>
    public int MaxFulltextCandidates { get; set; } = 2_000;
}
