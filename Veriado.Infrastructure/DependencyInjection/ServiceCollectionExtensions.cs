using System;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Veriado.Infrastructure.Concurrency;
using Veriado.Infrastructure.Events;
using Veriado.Infrastructure.Idempotency;
using Veriado.Infrastructure.Integrity;
using Veriado.Infrastructure.Maintenance;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Interceptors;
using Veriado.Infrastructure.Repositories;
using Veriado.Infrastructure.Search;
using Veriado.Infrastructure.Time;
using Veriado.Domain.Primitives;
using Veriado.Appl.Pipeline.Idempotency;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods to register infrastructure services in the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Action<InfrastructureOptions>? configure = null)
    {
        return AddInfrastructureInternal(services, configuration: null, configure);
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, Action<InfrastructureOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return AddInfrastructureInternal(services, configuration, configure);
    }

    private static IServiceCollection AddInfrastructureInternal(
        IServiceCollection services,
        IConfiguration? configuration,
        Action<InfrastructureOptions>? configure)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var options = new InfrastructureOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.DbPath))
        {
            var dataDirectory = ResolveDefaultDataDirectory();
            options.DbPath = Path.Combine(dataDirectory, "veriado.db");
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
            SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
        }

        SqliteFulltextSupportDetector.Detect(options);

        services.AddSingleton(options);
        services.AddSingleton<InfrastructureInitializationState>();
        services.AddSingleton<ISearchTelemetry, SearchTelemetry>();
        services.AddSingleton<SqlitePragmaInterceptor>();
        services.AddSingleton<ISqliteConnectionFactory, PooledSqliteConnectionFactory>();
        services.AddHealthChecks()
            .AddCheck<SqlitePragmaHealthCheck>("sqlite_pragmas")
            .AddCheck<FtsDlqHealthCheck>("fts_write_ahead_dlq");

        var searchOptions = services.AddOptions<SearchOptions>();
        if (configuration is not null)
        {
            searchOptions.Bind(configuration.GetSection("Search"));
        }
        else
        {
            searchOptions.Configure(_ => { });
        }

        searchOptions.PostConfigure(options =>
        {
            options.Analyzer ??= new AnalyzerOptions();
            if (string.IsNullOrWhiteSpace(options.Analyzer.DefaultProfile))
            {
                options.Analyzer.DefaultProfile = "cs";
            }
        });

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SearchOptions>>().Value);
        services.AddSingleton(sp => sp.GetRequiredService<SearchOptions>().Analyzer);
        services.AddSingleton(sp => sp.GetRequiredService<SearchOptions>().Score);
        services.AddSingleton(sp => sp.GetRequiredService<SearchOptions>().Facets);
        services.AddSingleton(sp => sp.GetRequiredService<SearchOptions>().Suggestions);
        services.AddSingleton<IOptions<AnalyzerOptions>>(sp => Options.Create(sp.GetRequiredService<SearchOptions>().Analyzer));
        services.AddSingleton<IOptions<SearchScoreOptions>>(sp => Options.Create(sp.GetRequiredService<SearchOptions>().Score));

        services.AddSingleton<IAnalyzerFactory, AnalyzerFactory>();

        services.AddDbContextPool<AppDbContext>((sp, builder) =>
        {
            builder.UseSqlite(options.ConnectionString, sqlite => sqlite.CommandTimeout(30));
            builder.AddInterceptors(sp.GetRequiredService<SqlitePragmaInterceptor>());
        }, poolSize: 128);
        services.AddDbContextFactory<AppDbContext>((sp, builder) =>
        {
            builder.UseSqlite(options.ConnectionString, sqlite => sqlite.CommandTimeout(30));
            builder.AddInterceptors(sp.GetRequiredService<SqlitePragmaInterceptor>());
        });

        services.AddDbContextPool<ReadOnlyDbContext>((sp, builder) =>
        {
            builder.UseSqlite(options.ConnectionString, sqlite => sqlite.CommandTimeout(30));
            builder.AddInterceptors(sp.GetRequiredService<SqlitePragmaInterceptor>());
        }, poolSize: 256);
        services.AddDbContextFactory<ReadOnlyDbContext>((sp, builder) =>
        {
            builder.UseSqlite(options.ConnectionString, sqlite => sqlite.CommandTimeout(30));
            builder.AddInterceptors(sp.GetRequiredService<SqlitePragmaInterceptor>());
        });

        services.TryAddSingleton<IClock, SystemClock>();

        services.AddSingleton<IWriteQueue, WriteQueue>();
        services.AddSingleton<SuggestionMaintenanceService>();
        services.AddSingleton<SqliteFts5Indexer>();
        services.AddSingleton<ISearchIndexer>(sp => sp.GetRequiredService<SqliteFts5Indexer>());
        services.AddSingleton<ISearchIndexCoordinator, SqliteSearchIndexCoordinator>();
        services.AddSingleton<IIndexQueue, IndexQueue>();
        services.AddSingleton<IDatabaseMaintenanceService, SqliteDatabaseMaintenanceService>();

        services.AddSingleton<ISearchQueryService, SqliteFts5QueryService>();
        services.AddSingleton<FtsWriteAheadService>();
        services.AddSingleton<IFtsDlqMonitor>(sp => sp.GetRequiredService<FtsWriteAheadService>());
        services.AddSingleton<ISearchHistoryService, SearchHistoryService>();
        services.AddSingleton<ISearchFavoritesService, SearchFavoritesService>();
        services.AddSingleton<IFacetService, FacetService>();
        services.AddSingleton<ISearchSuggestionService, SuggestionService>();
        services.AddSingleton<IFulltextIntegrityService, FulltextIntegrityService>();
        services.AddSingleton<IIndexAuditor, IndexAuditor>();
        services.AddSingleton<ISearchIndexSignatureCalculator, SearchIndexSignatureCalculator>();
        services.AddSingleton<INeedsReindexEvaluator, NeedsReindexEvaluator>();
        services.AddSingleton<IEventPublisher, AuditEventPublisher>();
        services.AddSingleton<IIdempotencyStore, SqliteIdempotencyStore>();

        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IReadOnlyFileContextFactory, ReadOnlyFileContextFactory>();
        services.AddScoped<IFileReadRepository, FileReadRepository>();
        services.AddSingleton<IDiagnosticsRepository, DiagnosticsRepository>();

        services.AddHostedService<WriteWorker>();
        services.AddHostedService<IdempotencyCleanupWorker>();
        services.AddHostedService<IndexAuditBackgroundService>();

        return services;
    }

    private static string ResolveDefaultDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "Veriado");
        }

        return Path.Combine(AppContext.BaseDirectory, "veriado-data");
    }

    public static async Task InitializeInfrastructureAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default, [CallerMemberName] string? callerName = null)
    {
        var options = serviceProvider.GetRequiredService<InfrastructureOptions>();
        var state = serviceProvider.GetRequiredService<InfrastructureInitializationState>();
        var logger = serviceProvider.GetService<ILogger<InfrastructureInitializationState>>();

        var ranInitialization = await state.EnsureInitializedAsync(async () =>
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var scopedProvider = scope.ServiceProvider;
            var dbContext = scopedProvider.GetRequiredService<AppDbContext>();

            var providerName = dbContext.Database.ProviderName;
            if (string.IsNullOrWhiteSpace(providerName)
                || !providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Veriado infrastructure requires Microsoft.Data.Sqlite as the EF Core provider but '{providerName ?? "<null>"}' was configured.");
            }

            if (dbContext.Database.IsSqlite())
            {
                await dbContext.EnsureSqliteMigrationsLockClearedAsync(cancellationToken).ConfigureAwait(false);

                var needsBaseline = await dbContext.NeedsSqliteMigrationsHistoryBaselineAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (needsBaseline)
                {
                    await dbContext.EnsureSqliteMigrationsHistoryBaselinedAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            await dbContext.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await StartupIntegrityCheck.EnsureConsistencyAsync(scopedProvider, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        logger?.LogInformation(
            "InitializeInfrastructureAsync invoked by {Caller} for database {DatabasePath}. RanInitialization: {RanInitialization}",
            callerName ?? "unknown",
            options.DbPath,
            ranInitialization);
    }
}
