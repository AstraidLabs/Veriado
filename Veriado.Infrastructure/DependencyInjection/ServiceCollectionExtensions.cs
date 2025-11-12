using System;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Veriado.Appl.Abstractions;
using Veriado.Application.Import;
using Veriado.Infrastructure.Events;
using Veriado.Infrastructure.Events.Handlers;
using Veriado.Infrastructure.Hosting;
using Veriado.Infrastructure.Import;
using Veriado.Infrastructure.Idempotency;
using Veriado.Infrastructure.Integrity;
using Veriado.Infrastructure.Maintenance;
using Veriado.Infrastructure.Lifecycle;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Connections;
using Veriado.Infrastructure.Persistence.Interceptors;
using Veriado.Infrastructure.Repositories;
using Veriado.Infrastructure.Search;
using Veriado.Infrastructure.Time;
using Veriado.Infrastructure.Storage;
using Veriado.Domain.Primitives;
using Veriado.Domain.Search.Events;
using Veriado.Appl.Pipeline.Idempotency;
using Veriado.Appl.Search;
using ApplicationClock = Veriado.Appl.Abstractions.IClock;
using DomainClock = Veriado.Domain.Primitives.IClock;

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

        var optionsBuilder = services.AddOptions<InfrastructureOptions>();
        if (configuration is not null)
        {
            optionsBuilder.Bind(configuration.GetSection("Infrastructure"));
        }

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        optionsBuilder.PostConfigure(options =>
        {
            var designOverride = Environment.GetEnvironmentVariable("VERIADO_DESIGNTIME_DB_PATH");
            var resolver = new SqlitePathResolver(options.DbPath, designOverride);
            options.DbPath = resolver.Resolve(SqliteResolutionScenario.Runtime);
        });

        optionsBuilder.PostConfigure<ILoggerFactory>((opts, loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(InfrastructureOptions));
            logger.LogInformation(
                "Infrastructure configured for database {DatabasePath} (full-text available: {IsFulltextAvailable}).",
                opts.DbPath,
                opts.IsFulltextAvailable);
        });

        optionsBuilder.ValidateDataAnnotations();
        optionsBuilder.ValidateOnStart();

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<InfrastructureOptions>>().Value);
        services.AddSingleton<ISqlitePathResolver>(sp =>
        {
            var optionsMonitor = sp.GetRequiredService<IOptions<InfrastructureOptions>>();
            var logger = sp.GetRequiredService<ILogger<SqlitePathResolver>>();
            var designOverride = Environment.GetEnvironmentVariable("VERIADO_DESIGNTIME_DB_PATH");
            return new SqlitePathResolver(optionsMonitor.Value.DbPath, designOverride, logger);
        });
        services.AddSingleton<IConnectionStringProvider>(sp =>
        {
            var optionsMonitor = sp.GetRequiredService<IOptions<InfrastructureOptions>>();
            var pathResolver = sp.GetRequiredService<ISqlitePathResolver>();
            var logger = sp.GetRequiredService<ILogger<SqliteConnectionStringProvider>>();
            return new SqliteConnectionStringProvider(optionsMonitor, pathResolver, logger);
        });
        services.AddSingleton<InfrastructureInitializationState>();
        services.AddSingleton<PauseTokenSource>();
        services.AddSingleton<AppLifecycleHostedService>();
        services.AddSingleton<IAppLifecycleService>(static sp => sp.GetRequiredService<AppLifecycleHostedService>());
        services.AddSingleton<IHostedService>(static sp => sp.GetRequiredService<AppLifecycleHostedService>());
        services.AddSingleton<ISearchTelemetry, SearchTelemetry>();
        services.AddSingleton<SqlitePragmaInterceptor>();
        services.AddSingleton<ISqliteConnectionFactory, PooledSqliteConnectionFactory>();
        services.AddHealthChecks()
            .AddCheck<SqlitePragmaHealthCheck>("sqlite_pragmas")
            .AddCheck<SqliteFulltextSchemaHealthCheck>("FTS5SchemaConsistent");

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

        services.AddSingleton<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddSingleton<DomainEventsInterceptor>();
        services.AddSingleton<ISearchIndexCoordinator, SearchIndexCoordinator>();
        services.AddSingleton<IDomainEventHandler<SearchReindexRequested>, SearchReindexRequestedHandler>();

        services.AddDbContext<AppDbContext>((sp, builder) =>
        {
            var connectionProvider = sp.GetRequiredService<IConnectionStringProvider>();
            builder.UseSqlite(connectionProvider.ConnectionString, sqlite => sqlite.CommandTimeout(30));
            builder.AddInterceptors(
                sp.GetRequiredService<SqlitePragmaInterceptor>(),
                sp.GetRequiredService<DomainEventsInterceptor>());
        });
        services.AddDbContextFactory<AppDbContext>((sp, builder) =>
        {
            var connectionProvider = sp.GetRequiredService<IConnectionStringProvider>();
            builder.UseSqlite(connectionProvider.ConnectionString, sqlite => sqlite.CommandTimeout(30));
            builder.AddInterceptors(
                sp.GetRequiredService<SqlitePragmaInterceptor>(),
                sp.GetRequiredService<DomainEventsInterceptor>());
        });

        services.AddDbContextPool<ReadOnlyDbContext>((sp, builder) =>
        {
            var connectionProvider = sp.GetRequiredService<IConnectionStringProvider>();
            builder.UseSqlite(connectionProvider.ConnectionString, sqlite => sqlite.CommandTimeout(30));
            builder.AddInterceptors(sp.GetRequiredService<SqlitePragmaInterceptor>());
        }, poolSize: 256);
        services.AddDbContextFactory<ReadOnlyDbContext>((sp, builder) =>
        {
            var connectionProvider = sp.GetRequiredService<IConnectionStringProvider>();
            builder.UseSqlite(connectionProvider.ConnectionString, sqlite => sqlite.CommandTimeout(30));
            builder.AddInterceptors(sp.GetRequiredService<SqlitePragmaInterceptor>());
        });

        services.TryAddSingleton<SystemClock>();
        services.TryAddSingleton<ApplicationClock>(sp => sp.GetRequiredService<SystemClock>());
        services.TryAddSingleton<DomainClock>(sp => sp.GetRequiredService<SystemClock>());
        services.AddSingleton<SuggestionMaintenanceService>();
        services.AddSingleton<IDatabaseMaintenanceService, SqliteDatabaseMaintenanceService>();
        services.AddSingleton<LocalFileStorage>();
        services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<LocalFileStorage>());
        services.AddSingleton<IStorageWriter>(sp => sp.GetRequiredService<LocalFileStorage>());

        services.AddSingleton<ISearchQueryService, SqliteFts5QueryService>();
        services.AddSingleton<ISearchHistoryService, SearchHistoryService>();
        services.AddSingleton<ISearchFavoritesService, SearchFavoritesService>();
        services.AddSingleton<IFacetService, FacetService>();
        services.AddSingleton<ISearchSuggestionService, SuggestionService>();
        services.AddSingleton<IFulltextIntegrityService, FulltextIntegrityService>();
        services.AddSingleton<IIndexAuditor, IndexAuditor>();
        services.AddSingleton<ISearchIndexSignatureCalculator, SearchIndexSignatureCalculator>();
        services.AddSingleton<INeedsReindexEvaluator, NeedsReindexEvaluator>();
        services.AddSingleton<AuditEventProjector>();
        services.AddSingleton<IIdempotencyStore, SqliteIdempotencyStore>();

        services.AddScoped<ISearchProjectionScope, SearchProjectionScopeEf>();
        services.AddScoped<IFileSearchProjection, SearchProjectionService>();
        services.AddScoped<IFileImportWriter, FileImportService>();
        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IFilePersistenceUnitOfWork, EfFilePersistenceUnitOfWork>();
        services.AddTransient<IFactoryFilePersistenceUnitOfWork, FactoryFilePersistenceUnitOfWork>();
        services.AddScoped<IReadOnlyFileContextFactory, ReadOnlyFileContextFactory>();
        services.AddScoped<IFileReadRepository, FileReadRepository>();
        services.AddSingleton<IDiagnosticsRepository, DiagnosticsRepository>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, InfrastructureInitializationHostedService>());
        services.AddHostedService<IdempotencyCleanupWorker>();
        services.AddHostedService<IndexAuditBackgroundService>();

        return services;
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
            var loggerFactory = scopedProvider.GetRequiredService<ILoggerFactory>();
            var connectionProvider = scopedProvider.GetRequiredService<IConnectionStringProvider>();

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

            var migrationLogger = loggerFactory.CreateLogger("EfMigrations");
            var appliedMigrations = (await dbContext.Database
                    .GetAppliedMigrationsAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToList();
            var pendingMigrations = (await dbContext.Database
                    .GetPendingMigrationsAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToList();

            migrationLogger.LogInformation(
                "EF migrations status before apply: applied={AppliedCount}, pending={PendingCount}",
                appliedMigrations.Count,
                pendingMigrations.Count);

            if (pendingMigrations.Count > 0)
            {
                migrationLogger.LogInformation(
                    "Applying pending migrations: {Pending}",
                    string.Join(", ", pendingMigrations));
                await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                migrationLogger.LogInformation("Database schema already up to date; skipping EF migration execution.");
            }

            if (dbContext.Database.IsSqlite())
            {
                await using (var connection = connectionProvider.CreateConnection())
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    var pragmaLogger = loggerFactory.CreateLogger("SqlitePragmaBootstrap");
                    await SqlitePragmaHelper
                        .ApplyAsync(connection, pragmaLogger, cancellationToken)
                        .ConfigureAwait(false);
                }

                var schemaLogger = loggerFactory.CreateLogger("FulltextSchemaBootstrap");
                var sqliteConnection = (SqliteConnection)dbContext.Database.GetDbConnection();
                var shouldCloseConnection = sqliteConnection.State != ConnectionState.Open;

                if (shouldCloseConnection)
                {
                    await sqliteConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    await SqliteFulltextSchemaManager
                        .EnsureUnifiedSchemaAsync(sqliteConnection, schemaLogger, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    if (shouldCloseConnection)
                    {
                        await sqliteConnection.CloseAsync().ConfigureAwait(false);
                    }
                }
            }

            var detectorLogger = loggerFactory.CreateLogger("FulltextBootstrap");
            await SqliteFulltextSupportDetector
                .DetectAsync(options, connectionProvider, detectorLogger, cancellationToken)
                .ConfigureAwait(false);

            await RunDevelopmentFulltextGuardAsync(scopedProvider, dbContext, cancellationToken).ConfigureAwait(false);
            await dbContext.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await StartupIntegrityCheck.EnsureConsistencyAsync(scopedProvider, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        logger?.LogInformation(
            "InitializeInfrastructureAsync invoked by {Caller} for database {DatabasePath}. RanInitialization: {RanInitialization}",
            callerName ?? "unknown",
            options.DbPath,
            ranInitialization);
    }

    private static async Task RunDevelopmentFulltextGuardAsync(
        IServiceProvider scopedProvider,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var environment = scopedProvider.GetService<IHostEnvironment>();
        if (environment?.IsDevelopment() != true)
        {
            return;
        }

        if (!dbContext.Database.IsSqlite())
        {
            return;
        }

        var loggerFactory = scopedProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("FulltextDevGuard");
        var options = scopedProvider.GetRequiredService<InfrastructureOptions>();

        var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var inspection = await SqliteFulltextSchemaInspector
                .InspectAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            var snapshot = inspection.Snapshot;
            SqliteFulltextSupport.UpdateSchemaSnapshot(snapshot);

            logger.LogInformation(
                "Development FTS guard inspected search_document_fts (contentless={IsContentless}) with columns {Columns}, search_document columns {DocumentColumns} and triggers {Triggers}.",
                snapshot.IsContentless,
                string.Join(", ", snapshot.FtsColumns),
                string.Join(", ", snapshot.SearchDocumentColumns),
                string.Join(", ", snapshot.Triggers.Keys));

            if (!inspection.IsValid)
            {
                var reason = inspection.FailureReason ?? "Unknown FTS schema mismatch";
                logger.LogError(
                    "FTS schema mismatch detected in development environment: {Reason}. Disabling full-text indexing for this session.",
                    reason);
                options.IsFulltextAvailable = false;
                options.FulltextAvailabilityError = reason;
                SqliteFulltextSupport.Update(false, reason);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }
}
