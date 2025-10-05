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
using Veriado.Infrastructure.Persistence.Interceptors;
using Veriado.Infrastructure.Repositories;
using Veriado.Infrastructure.Search;
using Veriado.Infrastructure.Search.Outbox;
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
            SqlitePragmaHelper.ApplyAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        }

        if (configuration is not null)
        {
            var indexingSection = configuration.GetSection("Search").GetSection("Indexing");
            var retryBudget = indexingSection.GetValue<int?>("RetryBudget");
            if (retryBudget.HasValue)
            {
                options.RetryBudget = retryBudget.Value;
            }
        }

        services.AddSingleton(options);
        services.AddSingleton<InfrastructureInitializationState>();
        services.AddSingleton<ISearchTelemetry, SearchTelemetry>();
        var sqlitePragmaInterceptor = new SqlitePragmaInterceptor();
        services.AddSingleton<SqlitePragmaInterceptor>(sqlitePragmaInterceptor);
        services.AddSingleton<ISqliteConnectionFactory, PooledSqliteConnectionFactory>();

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
        services.AddSingleton(sp => sp.GetRequiredService<SearchOptions>().Parse);
        services.AddSingleton(sp => sp.GetRequiredService<SearchOptions>().Trigram);
        services.AddSingleton(sp => sp.GetRequiredService<SearchOptions>().Facets);
        services.AddSingleton(sp => sp.GetRequiredService<SearchOptions>().Synonyms);
        services.AddSingleton(sp => sp.GetRequiredService<SearchOptions>().Suggestions);
        services.AddSingleton(sp => sp.GetRequiredService<SearchOptions>().Spell);
        services.AddSingleton<IOptions<AnalyzerOptions>>(sp => Options.Create(sp.GetRequiredService<SearchOptions>().Analyzer));
        services.AddSingleton<IOptions<SearchScoreOptions>>(sp => Options.Create(sp.GetRequiredService<SearchOptions>().Score));
        services.AddSingleton<IOptions<SearchParseOptions>>(sp => Options.Create(sp.GetRequiredService<SearchOptions>().Parse));
        services.AddSingleton<IOptions<TrigramIndexOptions>>(sp => Options.Create(sp.GetRequiredService<SearchOptions>().Trigram));

        services.AddSingleton<IAnalyzerFactory, AnalyzerFactory>();

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
        services.AddSingleton<SuggestionMaintenanceService>();
        services.AddSingleton<OutboxDrainService>();
        services.AddSingleton<LuceneIndexManager>();
        services.AddSingleton<LuceneSearchIndexer>();
        services.AddSingleton<ISearchIndexer>(sp => sp.GetRequiredService<LuceneSearchIndexer>());
        services.AddSingleton<ISearchIndexCoordinator, SearchIndexCoordinator>();
        services.AddSingleton<IDatabaseMaintenanceService, SqliteDatabaseMaintenanceService>();
        services.AddSingleton<LuceneSearchQueryService>();
        services.AddSingleton<ISearchQueryService>(sp => sp.GetRequiredService<LuceneSearchQueryService>());
        services.AddSingleton<ISearchHistoryService, SearchHistoryService>();
        services.AddSingleton<ISearchFavoritesService, SearchFavoritesService>();
        services.AddSingleton<ISynonymProvider, SynonymService>();
        services.AddSingleton<IFacetService, FacetService>();
        services.AddSingleton<ISearchSuggestionService, SuggestionService>();
        services.AddSingleton<ISpellSuggestionService, SpellSuggestionService>();
        services.AddSingleton<IFulltextIntegrityService, FulltextIntegrityService>();
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
        if (options.SearchIndexingMode == SearchIndexingMode.Outbox)
        {
            services.AddHostedService<OutboxWorker>();
        }

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
