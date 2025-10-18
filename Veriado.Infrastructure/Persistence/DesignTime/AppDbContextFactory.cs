using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;
using Veriado.Infrastructure.Persistence.Connections;
using Veriado.Infrastructure.Persistence.Interceptors;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Persistence.DesignTime;

/// <summary>
/// Provides a design-time factory for creating <see cref="AppDbContext"/> instances.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <inheritdoc />
    public AppDbContext CreateDbContext(string[] args)
    {
        var infrastructureOptions = BuildInfrastructureOptions();
        var pragmaInterceptor = new SqlitePragmaInterceptor(NullLogger<SqlitePragmaInterceptor>.Instance);
        var connectionProvider = SqliteConnectionStringProvider.CreateDesignTimeProvider(
            infrastructureOptions,
            NullLogger<SqliteConnectionStringProvider>.Instance);

        var builder = new DbContextOptionsBuilder<AppDbContext>();
        builder.UseSqlite(connectionProvider.ConnectionString, sqlite => sqlite.CommandTimeout(30));
        builder.AddInterceptors(pragmaInterceptor);

        SqliteFulltextSupportDetector.Detect(infrastructureOptions, connectionProvider);

        return new AppDbContext(builder.Options, infrastructureOptions, NullLogger<AppDbContext>.Instance);
    }

    private static InfrastructureOptions BuildInfrastructureOptions()
    {
        var options = new InfrastructureOptions();
        var resolver = new SqlitePathResolver(options.DbPath);
        options.DbPath = resolver.Resolve(SqliteResolutionScenario.DesignTime);
        return options;
    }
}
