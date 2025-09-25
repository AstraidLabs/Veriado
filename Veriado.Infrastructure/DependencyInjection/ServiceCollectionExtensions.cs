using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Veriado.Infrastructure.Concurrency;
using Veriado.Infrastructure.Events;
using Veriado.Infrastructure.Idempotency;
using Veriado.Infrastructure.Integrity;
using Veriado.Infrastructure.Maintenance;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Interceptors;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Infrastructure.Repositories;
using Veriado.Infrastructure.Search;
using Veriado.Infrastructure.Search.Outbox;
using Veriado.Infrastructure.Time;
using Veriado.Domain.Primitives;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Search.Abstractions;
using Veriado.Appl.Pipeline.Idempotency;

namespace Veriado.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods to register infrastructure services in the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Action<InfrastructureOptions>? configure = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

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

        if (!File.Exists(options.DbPath))
        {
            using var connection = new SqliteConnection(options.ConnectionString);
            connection.Open();
        }

        services.AddSingleton(options);
        var sqlitePragmaInterceptor = new SqlitePragmaInterceptor();
        services.AddSingleton<SqlitePragmaInterceptor>(sqlitePragmaInterceptor);

        services.AddDbContextPool<AppDbContext>(ConfigureDbContext, poolSize: 128);
        services.AddDbContextFactory<AppDbContext>(ConfigureDbContext);

        services.AddDbContextPool<ReadOnlyDbContext>(ConfigureDbContext, poolSize: 256);
        services.AddDbContextFactory<ReadOnlyDbContext>(ConfigureDbContext);

        void ConfigureDbContext(DbContextOptionsBuilder builder)
        {
            builder.UseSqlite(options.ConnectionString, sqlite => sqlite.CommandTimeout(30));
            builder.AddInterceptors(sqlitePragmaInterceptor);
        }

        services.TryAddSingleton<IClock, SystemClock>();

        services.AddSingleton<IWriteQueue, WriteQueue>();
        services.AddSingleton<ISearchIndexer, SqliteFts5Indexer>();
        services.AddSingleton<ISearchIndexCoordinator, SqliteSearchIndexCoordinator>();
        services.AddSingleton<IDatabaseMaintenanceService, SqliteDatabaseMaintenanceService>();
        services.AddSingleton<ISearchQueryService, SqliteFts5QueryService>();
        services.AddSingleton<ISearchHistoryService, SearchHistoryService>();
        services.AddSingleton<ISearchFavoritesService, SearchFavoritesService>();
        services.AddSingleton<PlainTextExtractor>();
        services.AddSingleton<PdfTextExtractor>();
        services.AddSingleton<DocxTextExtractor>();
        services.AddSingleton<PptxTextExtractor>();
        services.AddSingleton<XlsxTextExtractor>();
        services.AddSingleton<OdtTextExtractor>();
        services.AddSingleton<OdpTextExtractor>();
        services.AddSingleton<OdsTextExtractor>();
        services.AddSingleton<ITextExtractor, CompositeTextExtractor>();
        services.AddSingleton<IFulltextIntegrityService, FulltextIntegrityService>();
        services.AddSingleton<IEventPublisher, AuditEventPublisher>();
        services.AddSingleton<IIdempotencyStore, SqliteIdempotencyStore>();

        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IReadOnlyFileContextFactory, ReadOnlyFileContextFactory>();
        services.AddScoped<IFileReadRepository, FileReadRepository>();
        services.AddSingleton<IDiagnosticsRepository, DiagnosticsRepository>();

        services.AddHostedService<WriteWorker>();
        services.AddHostedService<IdempotencyCleanupWorker>();
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
