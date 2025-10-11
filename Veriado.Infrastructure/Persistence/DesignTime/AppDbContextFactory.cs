using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;
using Veriado.Infrastructure.Persistence.Interceptors;

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
        var connectionString = infrastructureOptions.ConnectionString
            ?? throw new InvalidOperationException("The infrastructure connection string was not initialized.");

        var builder = new DbContextOptionsBuilder<AppDbContext>();
        builder.UseSqlite(connectionString, sqlite => sqlite.CommandTimeout(30));
        builder.AddInterceptors(pragmaInterceptor);

        return new AppDbContext(builder.Options, infrastructureOptions, NullLogger<AppDbContext>.Instance);
    }

    private static InfrastructureOptions BuildInfrastructureOptions()
    {
        var options = new InfrastructureOptions();

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
        SqliteFulltextSupportDetector.Detect(options);
        return options;
    }
}
