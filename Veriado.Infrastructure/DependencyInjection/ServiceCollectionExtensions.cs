using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Veriado.Application.Abstractions;
using Veriado.Application.Search.Abstractions;
using Veriado.Infrastructure.Concurrency;
using Veriado.Infrastructure.Integrity;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Interceptors;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Infrastructure.Repositories;
using Veriado.Infrastructure.Search;
using Veriado.Infrastructure.Search.Outbox;
using Veriado.Domain.Primitives;

namespace Veriado.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods to register infrastructure services in the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Action<InfrastructureOptions>? configure = null)
    {
        var options = new InfrastructureOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.DbPath))
        {
            options.DbPath = Path.Combine(AppContext.BaseDirectory, "veriado.db");
        }

        var directory = Path.GetDirectoryName(options.DbPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = options.DbPath,
            Cache = SqliteCacheMode.Shared,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };
        options.ConnectionString = connectionStringBuilder.ConnectionString;

        services.AddSingleton(options);
        services.AddSingleton<SqlitePragmaInterceptor>();

        services.AddDbContextPool<AppDbContext>((serviceProvider, builder) =>
        {
            builder.UseSqlite(options.ConnectionString, sqlite => sqlite.CommandTimeout(30));
            builder.AddInterceptors(serviceProvider.GetRequiredService<SqlitePragmaInterceptor>());
        }, poolSize: 128);

        services.AddDbContextPool<ReadOnlyDbContext>((serviceProvider, builder) =>
        {
            builder.UseSqlite(options.ConnectionString, sqlite => sqlite.CommandTimeout(30));
            builder.AddInterceptors(serviceProvider.GetRequiredService<SqlitePragmaInterceptor>());
        }, poolSize: 256);

        services.AddSingleton<IWriteQueue, WriteQueue>();
        services.AddSingleton<ISearchIndexer, SqliteFts5Indexer>();
        services.AddSingleton<ISearchQueryService, SqliteFts5QueryService>();
        services.AddSingleton<ISearchHistoryService, SearchHistoryService>();
        services.AddSingleton<ISearchFavoritesService, SearchFavoritesService>();
        services.AddSingleton<ITextExtractor, TextExtractor>();
        services.AddSingleton<IFulltextIntegrityService, FulltextIntegrityService>();
        services.AddSingleton<IEventPublisher, NullEventPublisher>();

        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IReadOnlyFileContextFactory, ReadOnlyFileContextFactory>();
        services.AddScoped<FileReadRepository>();

        services.AddHostedService<WriteWorker>();
        if (options.FtsIndexingMode == FtsIndexingMode.Outbox)
        {
            services.AddHostedService<OutboxWorker>();
        }

        return services;
    }

    public static async Task InitializeInfrastructureAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var scopedProvider = scope.ServiceProvider;
        var dbContext = scopedProvider.GetRequiredService<AppDbContext>();
        await dbContext.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await StartupIntegrityCheck.EnsureConsistencyAsync(scopedProvider, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class NullEventPublisher : IEventPublisher
{
    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
